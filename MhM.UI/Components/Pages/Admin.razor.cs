using System.Security.Claims;
using MhM.UI.Data;
using MhM.UI.Data.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace MhM.UI.Components.Pages;

public partial class Admin : IAsyncDisposable
{
    private const int BatchSize = 12;
    [Inject] protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;
    [Inject] protected UserManager<ApplicationIdentityUser> UserManager { get; set; } = default!;
    [Inject] protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    protected List<AdminUserRow> users = [];
    protected string userSearch = string.Empty;
    protected string? message;
    protected bool isError;
    protected string currentIdentityId = string.Empty;
    protected int visibleUserCount = BatchSize;
    protected ElementReference loadMoreSentinel;
    private DotNetObjectReference<Admin>? dotNetReference;

    private IEnumerable<AdminUserRow> FilteredUsers => string.IsNullOrWhiteSpace(userSearch) ? users : users.Where(x => x.Matches(userSearch));
    protected int FilteredUserCount => FilteredUsers.Count();
    protected List<AdminUserRow> VisibleUsers => FilteredUsers.Take(visibleUserCount).ToList();
    protected bool HasMoreUsers => visibleUserCount < FilteredUserCount;

    protected override async Task OnInitializedAsync() => await ReloadAsync();
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (HasMoreUsers) { dotNetReference ??= DotNetObjectReference.Create(this); await JS.InvokeVoidAsync("infiniteScroll.observe", loadMoreSentinel, dotNetReference); }
    }

    private async Task ReloadAsync()
    {
        var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        currentIdentityId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        await using var db = await DbFactory.CreateDbContextAsync();
        var profiles = await db.AppUsers.AsNoTracking().Where(x => x.IdentityUserId != null).OrderBy(x => x.DisplayName).ToListAsync();
        users = [];
        foreach (var profile in profiles)
        {
            var identity = await UserManager.FindByIdAsync(profile.IdentityUserId!);
            if (identity is not null) users.Add(new AdminUserRow(profile, identity, await UserManager.IsInRoleAsync(identity, PlatformRoles.Admin)));
        }
        visibleUserCount = BatchSize;
    }

    protected void FilterUsers(ChangeEventArgs args) { userSearch = args.Value?.ToString() ?? string.Empty; visibleUserCount = BatchSize; }
    [JSInvokable] public Task LoadMoreAsync() { visibleUserCount = Math.Min(visibleUserCount + BatchSize, FilteredUserCount); StateHasChanged(); return Task.CompletedTask; }

    protected async Task SaveUserAsync(AdminUserRow row)
    {
        if (!row.IsDirty || row.IsSaving) return;
        row.IsSaving = true; message = null;
        try
        {
            var identity = await UserManager.FindByIdAsync(row.IdentityId) ?? throw new InvalidOperationException();
            await using var db = await DbFactory.CreateDbContextAsync();
            var profile = await db.AppUsers.FirstAsync(x => x.IdentityUserId == row.IdentityId);
            profile.Role = row.ProfileRole;
            var preservedVerifications = profile.Verifications & ~(VerificationLevel.Email | VerificationLevel.Phone | VerificationLevel.Identity);
            profile.Verifications = preservedVerifications | (row.EmailVerified ? VerificationLevel.Email : 0) | (row.PhoneVerified ? VerificationLevel.Phone : 0) | (row.IdentityVerified ? VerificationLevel.Identity : 0);
            profile.IsVerified = profile.Verifications.HasFlag(VerificationLevel.Identity);
            var isAdminNow = await UserManager.IsInRoleAsync(identity, PlatformRoles.Admin);
            if (row.IsAdmin && !isAdminNow) EnsureSucceeded(await UserManager.AddToRoleAsync(identity, PlatformRoles.Admin));
            if (!row.IsAdmin && isAdminNow) { if (row.IdentityId == currentIdentityId) throw new InvalidOperationException(); EnsureSucceeded(await UserManager.RemoveFromRoleAsync(identity, PlatformRoles.Admin)); }
            AddAudit(db, "UserUpdated", row.IdentityId, $"Profilrolle={row.ProfileRole}; Admin={row.IsAdmin}; Verifizierungen={profile.Verifications}");
            await db.SaveChangesAsync(); row.MarkSaved(); message = $"Änderungen für {row.DisplayName} wurden gespeichert."; isError = false;
        }
        catch { message = "Die Änderungen konnten nicht gespeichert werden."; isError = true; }
        finally { row.IsSaving = false; }
    }

    protected async Task SetBanAsync(AdminUserRow row, bool ban) => await ExecuteAsync(async () =>
    {
        if (row.IdentityId == currentIdentityId) throw new InvalidOperationException();
        var identity = await UserManager.FindByIdAsync(row.IdentityId) ?? throw new InvalidOperationException();
        identity.IsActive = !ban; identity.LockoutEnd = ban ? DateTimeOffset.MaxValue : null;
        EnsureSucceeded(await UserManager.UpdateAsync(identity)); EnsureSucceeded(await UserManager.UpdateSecurityStampAsync(identity));
        await using var db = await DbFactory.CreateDbContextAsync(); AddAudit(db, ban ? "UserBlocked" : "UserUnblocked", row.IdentityId, string.Empty); await db.SaveChangesAsync();
    }, ban ? "Nutzer wurde gesperrt." : "Nutzer wurde entsperrt.");

    protected async Task DeleteUserAsync(AdminUserRow row)
    {
        if (row.IdentityId == currentIdentityId) return;
        if (!await JS.InvokeAsync<bool>("confirm", $"Konto von {row.DisplayName} (@{row.Username}) endgültig löschen? Das Konto wird gelöscht und verbleibende Vertragsdaten werden anonymisiert. Dies kann nicht rückgängig gemacht werden.")) return;
        await DeleteUserCoreAsync(row);
    }

    private async Task DeleteUserCoreAsync(AdminUserRow row) => await ExecuteAsync(async () =>
    {
        if (row.IdentityId == currentIdentityId) throw new InvalidOperationException();
        var identity = await UserManager.FindByIdAsync(row.IdentityId) ?? throw new InvalidOperationException();
        await using var db = await DbFactory.CreateDbContextAsync(); var profile = await db.AppUsers.FirstAsync(x => x.IdentityUserId == row.IdentityId);
        AddAudit(db, "UserDeleted", row.IdentityId, "Anonymisiert; Vertrags- und Auditdaten bleiben erhalten.");
        profile.IdentityUserId = null; profile.DisplayName = "Gelöschter Nutzer"; profile.Username = $"deleted-{profile.Id:N}"[..30]; profile.NormalizedUsername = profile.Username.ToUpperInvariant(); profile.Email = $"deleted-{profile.Id:N}@invalid.local"; profile.Phone = null; profile.Description = string.Empty; profile.ProfileImageData = null; profile.ProfileImageContentType = null; profile.ProfileImageUpdatedUtc = null; profile.IsVerified = false; profile.Verifications = VerificationLevel.None;
        var image = await db.ProfileImages.FirstOrDefaultAsync(x => x.UserId == profile.Id); if (image is not null) db.ProfileImages.Remove(image);
        await db.SaveChangesAsync(); EnsureSucceeded(await UserManager.DeleteAsync(identity));
    }, "Nutzerkonto wurde gelöscht und verbleibende Vertragsdaten wurden anonymisiert.");

    private async Task ExecuteAsync(Func<Task> action, string success) { try { await action(); message = success; isError = false; await ReloadAsync(); } catch { message = "Die Aktion konnte nicht ausgeführt werden. Prüfe die Berechtigungen."; isError = true; } }
    private void AddAudit(MhMDbContext db, string action, string targetId, string details) => db.AdminAuditLogs.Add(new AdminAuditLog { ActorIdentityUserId = currentIdentityId, Action = action, TargetType = "UserManagement", TargetId = targetId, Details = details });
    private static void EnsureSucceeded(IdentityResult result) { if (!result.Succeeded) throw new InvalidOperationException(); }
    public async ValueTask DisposeAsync() { if (dotNetReference is null) return; try { await JS.InvokeVoidAsync("infiniteScroll.disconnect"); } catch (JSDisconnectedException) { } dotNetReference.Dispose(); }

    protected sealed class AdminUserRow
    {
        private UserRole profileRole, savedRole; private bool isAdmin, emailVerified, phoneVerified, identityVerified, savedAdmin, savedEmail, savedPhone, savedIdentity;
        public AdminUserRow(AppUser p, ApplicationIdentityUser i, bool admin) { UserId=p.Id; IdentityId=i.Id; DisplayName=p.DisplayName; Username=p.Username; Email=p.Email; City=p.City; ProfileImageUpdatedUtc=p.ProfileImageUpdatedUtc; profileRole=p.Role; isAdmin=admin; IsActive=i.IsActive; emailVerified=p.Verifications.HasFlag(VerificationLevel.Email); phoneVerified=p.Verifications.HasFlag(VerificationLevel.Phone); identityVerified=p.Verifications.HasFlag(VerificationLevel.Identity); CaptureSnapshot(); }
        public Guid UserId { get; } public string IdentityId { get; } public string DisplayName { get; } public string Username { get; } public string Email { get; } public string City { get; } public DateTime? ProfileImageUpdatedUtc { get; } public bool IsActive { get; } public bool IsSaving { get; set; } public bool Saved { get; private set; }
        public UserRole ProfileRole { get => profileRole; set { profileRole=value; Saved=false; } } public bool IsAdmin { get => isAdmin; set { isAdmin=value; Saved=false; } } public bool EmailVerified { get => emailVerified; set { emailVerified=value; Saved=false; } } public bool PhoneVerified { get => phoneVerified; set { phoneVerified=value; Saved=false; } } public bool IdentityVerified { get => identityVerified; set { identityVerified=value; Saved=false; } }
        public bool IsDirty => profileRole != savedRole || isAdmin != savedAdmin || emailVerified != savedEmail || phoneVerified != savedPhone || identityVerified != savedIdentity;
        public bool Matches(string value) { var q=value.Trim(); return DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase) || Username.Contains(q, StringComparison.CurrentCultureIgnoreCase) || Email.Contains(q, StringComparison.CurrentCultureIgnoreCase) || City.Contains(q, StringComparison.CurrentCultureIgnoreCase); }
        public void MarkSaved() { CaptureSnapshot(); Saved=true; } private void CaptureSnapshot() { savedRole=profileRole; savedAdmin=isAdmin; savedEmail=emailVerified; savedPhone=phoneVerified; savedIdentity=identityVerified; }
    }
}
