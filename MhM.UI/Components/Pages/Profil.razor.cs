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

        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var identityUserId = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        await using var db = await DbFactory.CreateDbContextAsync();

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
            listings = await db.Listings
                .AsNoTracking()
                .Include(x => x.Category)
                .Include(x => x.Requester)
                .Include(x => x.Images)
                .Where(x => x.RequesterId == profile.Id && x.Status != ListingStatus.Entwurf)
                .OrderByDescending(x => x.CreatedUtc)
                .ToListAsync();

            if (profile.HelperProfile is not null)
            {
                var ratings = await db.Reviews
                    .AsNoTracking()
                    .Where(x => x.RevieweeId == profile.Id && db.ListingApplications.Any(a =>
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
}
