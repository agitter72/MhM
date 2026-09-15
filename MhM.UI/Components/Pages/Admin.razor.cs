using System.Security.Claims;
using MhM.UI.Data;
using MhM.UI.Data.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MhM.UI.Components.Pages;

public partial class Admin
{
    [Inject] protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;
    [Inject] protected UserManager<ApplicationIdentityUser> UserManager { get; set; } = default!;
    [Inject] protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    protected List<AdminUserRow> users = [];
    protected List<AdminReportRow> listingReports = [];
    protected List<AdminReportRow> userReports = [];
    protected List<string> listingReasonOptions = [];
    protected List<string> userReasonOptions = [];
    protected string selectedListingReason = string.Empty;
    protected string selectedUserReason = string.Empty;
    protected string? message;
    protected bool isError;
    protected string currentIdentityId = string.Empty;
    protected IEnumerable<AdminReportRow> FilteredListingReports => string.IsNullOrWhiteSpace(selectedListingReason) ? listingReports : listingReports.Where(x => x.Report.Reason == selectedListingReason);
    protected IEnumerable<AdminReportRow> FilteredUserReports => string.IsNullOrWhiteSpace(selectedUserReason) ? userReports : userReports.Where(x => x.Report.Reason == selectedUserReason);

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        currentIdentityId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        await using var db = await DbFactory.CreateDbContextAsync();
        var profiles = await db.AppUsers.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync();
        users = [];
        foreach (var profile in profiles.Where(x => x.IdentityUserId != null))
        {
            var identity = await UserManager.FindByIdAsync(profile.IdentityUserId!);
            if (identity is null) continue;
            users.Add(new AdminUserRow(profile, identity, await UserManager.IsInRoleAsync(identity, PlatformRoles.Admin)));
        }
        var reports = await db.ContentReports.Include(x => x.ReporterUser)
            .Where(x => x.Status != ReportStatus.Erledigt && x.Status != ReportStatus.Abgelehnt)
            .OrderBy(x => x.CreatedUtc).ToListAsync();
        var listingIds = reports.Where(x => x.TargetType == ReportTargetType.Auftrag).Select(x => x.TargetId).Distinct().ToList();
        var targetListings = await db.Listings.AsNoTracking().Include(x => x.Requester).Where(x => listingIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        listingReports = reports.Where(x => x.TargetType == ReportTargetType.Auftrag)
            .Select(x => new AdminReportRow(x, targetListings.GetValueOrDefault(x.TargetId), null)).ToList();
        var userIds = reports.Where(x => x.TargetType == ReportTargetType.Nutzer).Select(x => x.TargetId).Distinct().ToList();
        var targetUsers = await db.AppUsers.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        userReports = reports.Where(x => x.TargetType == ReportTargetType.Nutzer)
            .Select(x => new AdminReportRow(x, null, targetUsers.GetValueOrDefault(x.TargetId))).ToList();
        listingReasonOptions = listingReports.Select(x => x.Report.Reason).Distinct().OrderBy(x => x).ToList();
        userReasonOptions = userReports.Select(x => x.Report.Reason).Distinct().OrderBy(x => x).ToList();
    }

    protected async Task SaveUserAsync(AdminUserRow row)
    {
        await ExecuteAsync(async () =>
        {
            var identity = await UserManager.FindByIdAsync(row.IdentityId) ?? throw new InvalidOperationException("Nutzer nicht gefunden.");
            await using var db = await DbFactory.CreateDbContextAsync();
            var profile = await db.AppUsers.FirstAsync(x => x.IdentityUserId == row.IdentityId);
            profile.DisplayName = Required(row.DisplayName, "Name", 120);
            profile.City = Required(row.City, "Ort", 120);
            var username = Required(row.Username, "Nutzername", 30).ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(username, "^[a-z0-9._-]+$")) throw new InvalidOperationException("Nutzername ist ungültig.");
            profile.Username = username; profile.NormalizedUsername = username.ToUpperInvariant();
            profile.Role = row.ProfileRole;
            profile.Verifications = (row.EmailVerified ? VerificationLevel.Email : 0) | (row.PhoneVerified ? VerificationLevel.Phone : 0) | (row.IdentityVerified ? VerificationLevel.Identity : 0);
            profile.IsVerified = profile.Verifications.HasFlag(VerificationLevel.Identity);
            var email = Required(row.Email, "E-Mail", 256);
            identity.Email = email; identity.NormalizedEmail = UserManager.NormalizeEmail(email); profile.Email = email;
            identity.UserName = username; identity.NormalizedUserName = UserManager.NormalizeName(username);
            var isAdminNow = await UserManager.IsInRoleAsync(identity, PlatformRoles.Admin);
            if (row.IsAdmin && !isAdminNow) EnsureSucceeded(await UserManager.AddToRoleAsync(identity, PlatformRoles.Admin));
            if (!row.IsAdmin && isAdminNow)
            {
                if (row.IdentityId == currentIdentityId) throw new InvalidOperationException("Du kannst dir die eigene Adminrolle nicht entziehen.");
                EnsureSucceeded(await UserManager.RemoveFromRoleAsync(identity, PlatformRoles.Admin));
            }
            EnsureSucceeded(await UserManager.UpdateAsync(identity));
            AddAudit(db, "UserUpdated", row.IdentityId, $"Profilrolle={row.ProfileRole}; Admin={row.IsAdmin}; Verifizierungen={profile.Verifications}");
            await db.SaveChangesAsync();
        }, "Nutzer wurde aktualisiert.");
    }

    protected async Task SetBanAsync(AdminUserRow row, bool ban)
    {
        await ExecuteAsync(async () =>
        {
            if (row.IdentityId == currentIdentityId) throw new InvalidOperationException("Du kannst dein eigenes Administratorkonto nicht sperren.");
            var identity = await UserManager.FindByIdAsync(row.IdentityId) ?? throw new InvalidOperationException("Nutzer nicht gefunden.");
            identity.IsActive = !ban;
            identity.LockoutEnd = ban ? DateTimeOffset.MaxValue : null;
            EnsureSucceeded(await UserManager.UpdateAsync(identity));
            EnsureSucceeded(await UserManager.UpdateSecurityStampAsync(identity));
            await using var db = await DbFactory.CreateDbContextAsync();
            AddAudit(db, ban ? "UserBanned" : "UserUnbanned", row.IdentityId, string.Empty);
            await db.SaveChangesAsync();
        }, ban ? "Nutzer wurde gesperrt." : "Nutzer wurde entsperrt.");
    }

    protected async Task DeleteUserAsync(AdminUserRow row)
    {
        await ExecuteAsync(async () =>
        {
            if (row.IdentityId == currentIdentityId) throw new InvalidOperationException("Du kannst dein eigenes Administratorkonto nicht löschen.");
            var identity = await UserManager.FindByIdAsync(row.IdentityId) ?? throw new InvalidOperationException("Nutzer nicht gefunden.");
            await using var db = await DbFactory.CreateDbContextAsync();
            var profile = await db.AppUsers.FirstAsync(x => x.IdentityUserId == row.IdentityId);
            AddAudit(db, "UserDeleted", row.IdentityId, "Anonymisiert; Vertrags- und Auditdaten bleiben erhalten.");
            profile.IdentityUserId = null;
            profile.DisplayName = "Gelöschter Nutzer";
            profile.Username = $"deleted-{profile.Id:N}"[..30];
            profile.NormalizedUsername = profile.Username.ToUpperInvariant();
            profile.Email = $"deleted-{profile.Id:N}@invalid.local";
            profile.Phone = null; profile.Description = string.Empty; profile.ProfileImageData = null; profile.ProfileImageContentType = null; profile.ProfileImageUpdatedUtc = null;
            profile.IsVerified = false; profile.Verifications = VerificationLevel.None;
            var separateImage = await db.ProfileImages.FirstOrDefaultAsync(x => x.UserId == profile.Id);
            if (separateImage is not null) db.ProfileImages.Remove(separateImage);
            await db.SaveChangesAsync();
            EnsureSucceeded(await UserManager.DeleteAsync(identity));
        }, "Nutzerkonto wurde gelöscht und verbleibende Vertragsdaten wurden anonymisiert.");
    }

    protected async Task ResolveReportAsync(ContentReport report)
    {
        await ExecuteAsync(async () =>
        {
            await using var db = await DbFactory.CreateDbContextAsync();
            var entity = await db.ContentReports.FirstAsync(x => x.Id == report.Id);
            entity.Status = ReportStatus.Erledigt; entity.ResolvedUtc = DateTime.UtcNow; entity.ResolvedByIdentityUserId = currentIdentityId;
            AddAudit(db, "ReportResolved", entity.Id.ToString(), $"{entity.TargetType}:{entity.TargetId}");
            await db.SaveChangesAsync();
        }, "Meldung wurde erledigt.");
    }

    protected async Task DeleteListingAsync(AdminReportRow row)
    {
        var reason = row.DeleteReason.Trim();
        if (reason.Length == 0)
        {
            message = "Die Begründung für den Auftraggeber ist ein Pflichtfeld.";
            isError = true;
            return;
        }

        await ExecuteAsync(async () =>
        {
            await using var db = await DbFactory.CreateDbContextAsync();
            var listing = await db.Listings.FirstOrDefaultAsync(x => x.Id == row.Report.TargetId)
                ?? throw new InvalidOperationException("Auftrag nicht gefunden.");
            listing.Status = ListingStatus.Storniert;
            db.UserNotifications.Add(new UserNotification
            {
                Type = UserNotificationType.Moderation,
                RecipientUserId = listing.RequesterId,
                ListingId = listing.Id,
                Title = "Auftrag durch Moderation entfernt",
                Content = $"Dein Auftrag ‚{listing.Title}‘ wurde entfernt. Begründung: {reason}",
                LinkUrl = $"/auftraege/{listing.Id}"
            });
            var relatedReports = await db.ContentReports.Where(x => x.TargetType == ReportTargetType.Auftrag && x.TargetId == listing.Id && x.Status != ReportStatus.Erledigt && x.Status != ReportStatus.Abgelehnt).ToListAsync();
            foreach (var report in relatedReports)
            {
                report.Status = ReportStatus.Erledigt;
                report.ResolvedUtc = DateTime.UtcNow;
                report.ResolvedByIdentityUserId = currentIdentityId;
            }
            AddAudit(db, "ListingDeleted", listing.Id.ToString(), reason, "Listing");
            await db.SaveChangesAsync();
        }, "Der Auftrag wurde entfernt und der Auftraggeber mit der Begründung benachrichtigt.");
    }

    private async Task ExecuteAsync(Func<Task> action, string success)
    {
        try { await action(); message = success; isError = false; await ReloadAsync(); }
        catch (Exception) { message = "Die Aktion konnte nicht ausgeführt werden. Prüfe Eingaben und Berechtigungen."; isError = true; }
    }
    private void AddAudit(MhMDbContext db, string action, string targetId, string details, string targetType = "UserManagement") => db.AdminAuditLogs.Add(new AdminAuditLog { ActorIdentityUserId = currentIdentityId, Action = action, TargetType = targetType, TargetId = targetId, Details = details });
    private static string Required(string value, string field, int max) { value = value.Trim(); if (value.Length == 0 || value.Length > max) throw new InvalidOperationException($"{field} ist ungültig."); return value; }
    private static void EnsureSucceeded(IdentityResult result) { if (!result.Succeeded) throw new InvalidOperationException("Identity-Aktion fehlgeschlagen."); }

    protected sealed class AdminUserRow
    {
        public AdminUserRow(AppUser p, ApplicationIdentityUser i, bool admin) { IdentityId=i.Id; DisplayName=p.DisplayName; Username=p.Username; Email=p.Email; City=p.City; ProfileRole=p.Role; IsAdmin=admin; IsActive=i.IsActive; EmailVerified=p.Verifications.HasFlag(VerificationLevel.Email); PhoneVerified=p.Verifications.HasFlag(VerificationLevel.Phone); IdentityVerified=p.Verifications.HasFlag(VerificationLevel.Identity); }
        public string IdentityId { get; set; } public string DisplayName { get; set; } public string Username { get; set; } public string Email { get; set; } public string City { get; set; } public UserRole ProfileRole { get; set; } public bool IsAdmin { get; set; } public bool IsActive { get; set; } public bool EmailVerified { get; set; } public bool PhoneVerified { get; set; } public bool IdentityVerified { get; set; }
    }

    protected sealed class AdminReportRow(ContentReport report, Listing? listing, AppUser? user)
    {
        public ContentReport Report { get; } = report;
        public Listing? Listing { get; } = listing;
        public AppUser? User { get; } = user;
        public bool ShowDeleteForm { get; set; }
        public string DeleteReason { get; set; } = string.Empty;
    }
}
