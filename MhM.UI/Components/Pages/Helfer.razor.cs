using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using MhM.UI.Data;
using MhM.UI.Data.Models;
using MhM.UI.Localization;

namespace MhM.UI.Components.Pages;

public partial class Helfer
{
    [Inject]
    protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected UiLocalizer T { get; set; } = default!;

    [CascadingParameter]
    protected Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

    protected List<HelperProfile>? items;
    protected AppUser? currentUser;
    protected HelperProfile? currentHelperProfile;
    protected HelperActivationModel activation = new();

    protected bool isAuthenticated;
    protected bool isBusy;
    protected string? errorMessage;
    protected string? successMessage;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    protected async Task ActivateAsync()
    {
        if (currentUser is null)
        {
            errorMessage = T["Helpers.AppUserMissing"];
            return;
        }

        isBusy = true;
        errorMessage = null;
        successMessage = null;

        try
        {
            await using var db = await DbFactory.CreateDbContextAsync();

            var appUser = await db.AppUsers
                .Include(x => x.HelperProfile)
                .FirstOrDefaultAsync(x => x.Id == currentUser.Id);

            if (appUser is null)
            {
                errorMessage = T["Helpers.AppUserMissing"];
                return;
            }

            var profile = appUser.HelperProfile;
            if (profile is null)
            {
                profile = new HelperProfile
                {
                    UserId = appUser.Id
                };
                db.HelperProfiles.Add(profile);
            }

            profile.Title = activation.Title.Trim();
            profile.Description = activation.Description.Trim();
            profile.Skills = activation.Skills.Trim();
            profile.HourlyRate = activation.HourlyRate;
            profile.RadiusKm = activation.RadiusKm;
            profile.OffersBarter = activation.OffersBarter;

            appUser.Role = UserRole.Helfer;

            await db.SaveChangesAsync();

            await LoadAsync();
            successMessage = currentHelperProfile is null ? T["Helpers.Activated"] : T["Helpers.Updated"];
        }
        catch
        {
            errorMessage = T["Helpers.SaveFailed"];
        }
        finally
        {
            isBusy = false;
        }
    }

    private async Task LoadAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();

        items = await db.HelperProfiles
            .Include(x => x.User)
            .OrderBy(x => x.User.City)
            .ThenBy(x => x.User.DisplayName)
            .ToListAsync();

        var authState = await AuthenticationStateTask;
        isAuthenticated = authState.User.Identity?.IsAuthenticated == true;

        currentUser = null;
        currentHelperProfile = null;

        if (!isAuthenticated)
        {
            return;
        }

        var identityUserId = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(identityUserId))
        {
            return;
        }

        currentUser = await db.AppUsers
            .Include(x => x.HelperProfile)
            .FirstOrDefaultAsync(x => x.IdentityUserId == identityUserId);

        currentHelperProfile = currentUser?.HelperProfile;

        if (currentHelperProfile is not null)
        {
            activation = new HelperActivationModel
            {
                Title = currentHelperProfile.Title,
                Description = currentHelperProfile.Description,
                Skills = currentHelperProfile.Skills,
                HourlyRate = currentHelperProfile.HourlyRate,
                RadiusKm = currentHelperProfile.RadiusKm,
                OffersBarter = currentHelperProfile.OffersBarter
            };
        }
        else if (currentUser is not null)
        {
            activation = new HelperActivationModel
            {
                Title = $"{currentUser.DisplayName} hilft vor Ort",
                Description = string.Empty,
                Skills = string.Empty,
                HourlyRate = null,
                RadiusKm = 15,
                OffersBarter = true
            };
        }
    }

    protected sealed class HelperActivationModel
    {
        [Required, MaxLength(160)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Skills { get; set; } = string.Empty;

        [Range(typeof(decimal), "0", "9999.99", ParseLimitsInInvariantCulture = true)]
        public decimal? HourlyRate { get; set; }

        [Range(1, 500)]
        public int RadiusKm { get; set; } = 15;

        public bool OffersBarter { get; set; } = true;
    }
}
