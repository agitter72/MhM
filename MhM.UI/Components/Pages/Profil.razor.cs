using System.Security.Claims;
using MhM.UI.Data;
using MhM.UI.Data.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace MhM.UI.Components.Pages;

public partial class Profil
{
    [Parameter] public string? Username { get; set; }
    [Inject] protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;
    [Inject] protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    protected AppUser? profile;
    protected List<Listing> listings = [];
    protected bool isLoading = true;
    protected bool isOwnProfile;
    protected Guid? currentUserId;
    protected bool showReportForm;
    protected string selectedReportReason = string.Empty;
    protected string reportDetails = string.Empty;
    protected string? reportMessage;
    protected bool reportSucceeded;
    protected double? helperRating;
    protected int helperReviewCount;
    protected int acceptedHelperListingCount;

    protected static IReadOnlyList<string> GetHelperSkills(HelperProfile helperProfile)
        => helperProfile.Skills
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    protected override async Task OnParametersSetAsync()
    {
        isLoading = true;
        profile = null;
        listings = [];
        showReportForm = false;
        selectedReportReason = string.Empty;
        reportDetails = string.Empty;
        reportMessage = null;

        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var identityUserId = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        await using var db = await DbFactory.CreateDbContextAsync();
        currentUserId = string.IsNullOrWhiteSpace(identityUserId)
            ? null
            : await db.AppUsers.Where(x => x.IdentityUserId == identityUserId).Select(x => (Guid?)x.Id).FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(Username))
        {
            if (string.IsNullOrWhiteSpace(identityUserId))
            {
                Navigation.NavigateTo("/account/login");
                return;
            }

            profile = await db.AppUsers
                .AsNoTracking()
                .Include(x => x.HelperProfile)
                .FirstOrDefaultAsync(x => x.IdentityUserId == identityUserId);
        }
        else
        {
            var normalized = Username.Trim().ToUpperInvariant();
            profile = await db.AppUsers
                .AsNoTracking()
                .Include(x => x.HelperProfile)
                .FirstOrDefaultAsync(x => x.NormalizedUsername == normalized);
        }

        if (profile is not null)
        {
            isOwnProfile = !string.IsNullOrWhiteSpace(identityUserId) && profile.IdentityUserId == identityUserId;
            var isAdmin = authState.User.IsInRole(PlatformRoles.Admin);
            if (!isOwnProfile && !isAdmin && profile.IdentityUserId is not null &&
                !await db.Users.AnyAsync(x => x.Id == profile.IdentityUserId && x.IsActive))
            {
                profile = null;
                isLoading = false;
                return;
            }
            listings = await db.Listings
                .AsNoTracking()
                .Include(x => x.Category)
                .Include(x => x.Requester)
                .Include(x => x.Images)
                .Where(x => x.RequesterId == profile.Id && x.Status != ListingStatus.Entwurf && x.Status != ListingStatus.Storniert)
                .OrderByDescending(x => x.CreatedUtc)
                .ToListAsync();

            if (profile.HelperProfile is not null)
            {
                var ratings = await db.Reviews
                    .AsNoTracking()
                    .Where(x => x.RevieweeId == profile.Id &&
                        (x.CreatedUtc <= DateTime.UtcNow.AddDays(-14) || db.Reviews.Any(other =>
                            other.ListingId == x.ListingId && other.ReviewerId == x.RevieweeId && other.RevieweeId == x.ReviewerId)) &&
                        db.ListingApplications.Any(a =>
                        a.ListingId == x.ListingId &&
                        a.ApplicantId == profile.Id &&
                        a.Status == ListingApplicationStatus.Angenommen))
                    .Select(x => x.Stars)
                    .ToListAsync();
                helperReviewCount = ratings.Count;
                helperRating = ratings.Count == 0 ? null : ratings.Average();
                acceptedHelperListingCount = await db.ListingApplications
                    .AsNoTracking()
                    .CountAsync(x => x.ApplicantId == profile.Id && x.Status == ListingApplicationStatus.Angenommen);
            }
        }

        isLoading = false;
    }

    protected async Task ReportUserAsync()
    {
        reportMessage = null;
        reportSucceeded = false;
        if (profile is null || !currentUserId.HasValue || currentUserId == profile.Id) return;
        if (!ReportReasons.User.Contains(selectedReportReason))
        {
            reportMessage = "Bitte wähle einen Meldegrund aus.";
            return;
        }
        if (selectedReportReason == ReportReasons.Other && string.IsNullOrWhiteSpace(reportDetails))
        {
            reportMessage = "Bitte beschreibe das Problem im Freitextfeld.";
            return;
        }

        await using var db = await DbFactory.CreateDbContextAsync();
        var duplicate = await db.ContentReports.AnyAsync(x => x.ReporterUserId == currentUserId &&
            x.TargetType == ReportTargetType.Nutzer && x.TargetId == profile.Id &&
            x.Status != ReportStatus.Erledigt && x.Status != ReportStatus.Abgelehnt);
        if (!duplicate)
        {
            db.ContentReports.Add(new ContentReport
            {
                ReporterUserId = currentUserId.Value,
                TargetType = ReportTargetType.Nutzer,
                TargetId = profile.Id,
                Reason = selectedReportReason,
                Details = selectedReportReason == ReportReasons.Other ? reportDetails.Trim() : string.Empty
            });
            await db.SaveChangesAsync();
        }
        reportSucceeded = true;
        reportMessage = duplicate ? "Du hast diesen Nutzer bereits gemeldet." : "Die Meldung wurde an die Administration übermittelt.";
    }
}
