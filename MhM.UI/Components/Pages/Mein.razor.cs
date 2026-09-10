using System.ComponentModel.DataAnnotations;
using MhM.UI.Data;
using MhM.UI.Data.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MhM.UI.Components.Pages;

public partial class Mein
{
    [Inject] protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;
    [Inject] protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] protected UserManager<ApplicationIdentityUser> UserManager { get; set; } = default!;
    [Inject] protected SignInManager<ApplicationIdentityUser> SignInManager { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "tab")]
    public string? TabQuery { get; set; }

    [SupplyParameterFromQuery(Name = "status")]
    public string? StatusQuery { get; set; }

    protected bool isLoading = true;

    protected List<Listing> myOwnListings = [];
    protected List<Listing> myHelperListings = [];

    protected List<MyApplicationRow> myApplications = [];

    protected PersonalDataModel personalData = new();
    protected HelperDataModel helperData = new();
    protected PasswordChangeModel passwordData = new();
    // In der Klasse `Mein` ergänzen (z. B. bei den anderen UI-States)
    protected ListingApplicationStatus? selectedApplicationStatusFilter;

    protected int applicationsCountAll => myApplications.Count;
    protected int applicationsCountEingereicht => myApplications.Count(x => x.Status == ListingApplicationStatus.Eingereicht);
    protected int applicationsCountAngenommen => myApplications.Count(x => x.Status == ListingApplicationStatus.Angenommen);
    protected int applicationsCountAbgelehnt => myApplications.Count(x => x.Status == ListingApplicationStatus.Abgelehnt);

    protected IReadOnlyList<MyApplicationRow> filteredApplications =>
        selectedApplicationStatusFilter is null
            ? myApplications
            : myApplications.Where(x => x.Status == selectedApplicationStatusFilter.Value).ToList();

    protected bool savingPersonal;
    protected bool savingHelper;
    protected bool savingPassword;

    protected string? personalError;
    protected string? personalSuccess;
    protected string? helperError;
    protected string? helperSuccess;
    protected string? passwordError;
    protected string? passwordSuccess;

    private string? currentIdentityUserId;
    private Guid? currentAppUserId;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    protected override void OnParametersSet()
    {
        if (TryParseTab(TabQuery, out var tab))
        {
            activeTab = tab;
        }

        if (activeTab == MeinTab.Applications && TryParseStatus(StatusQuery, out var status))
        {
            selectedApplicationStatusFilter = status;
        }
        else if (activeTab != MeinTab.Applications)
        {
            selectedApplicationStatusFilter = null;
        }
    }

    private async Task LoadAsync()
    {
        isLoading = true;

        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var principal = authState.User;
        var identityUser = await UserManager.GetUserAsync(principal);

        if (identityUser is null)
        {
            isLoading = false;
            return;
        }

        currentIdentityUserId = identityUser.Id;

        await using var db = await DbFactory.CreateDbContextAsync();

        var appUser = await db.AppUsers
            .Include(x => x.HelperProfile)
            .FirstOrDefaultAsync(x => x.Email == identityUser.Email);

        if (appUser is null)
        {
            isLoading = false;
            personalError = "Kein AppUser-Profil gefunden.";
            return;
        }

        currentAppUserId = appUser.Id;

        personalData = new PersonalDataModel
        {
            FirstName = identityUser.FirstName ?? string.Empty,
            LastName = identityUser.LastName ?? string.Empty,
            Email = identityUser.Email ?? appUser.Email,
            Phone = identityUser.PhoneNumber ?? appUser.Phone ?? string.Empty,
            PostalCode = appUser.PostalCode,
            City = appUser.City
        };

        helperData = new HelperDataModel
        {
            Title = appUser.HelperProfile?.Title ?? $"{appUser.DisplayName} hilft vor Ort",
            Description = appUser.HelperProfile?.Description ?? string.Empty,
            Skills = appUser.HelperProfile?.Skills ?? string.Empty,
            HourlyRate = appUser.HelperProfile?.HourlyRate,
            RadiusKm = appUser.HelperProfile?.RadiusKm ?? 15,
            OffersBarter = appUser.HelperProfile?.OffersBarter ?? true
        };

        myOwnListings = await db.Listings
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Requester)
            .Include(x => x.Images)
            .Where(x => x.RequesterId == appUser.Id)
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync();

        myHelperListings = await db.ListingApplications
            .AsNoTracking()
            .Where(x => x.ApplicantId == appUser.Id && x.Status == ListingApplicationStatus.Angenommen)
            .Include(x => x.Listing).ThenInclude(x => x.Category)
            .Include(x => x.Listing).ThenInclude(x => x.Requester)
            .Include(x => x.Listing).ThenInclude(x => x.Images)
            .Select(x => x.Listing)
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync();

        myApplications = await db.ListingApplications
            .AsNoTracking()
            .Where(x => x.ApplicantId == appUser.Id)
            .Include(x => x.Listing)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new MyApplicationRow(
                x.ListingId,
                x.Listing.Title,
                x.Listing.PostalCode,
                x.Listing.City,
                x.Status,
                x.ProposedPrice,
                x.CreatedUtc))
            .ToListAsync();

        isLoading = false;
    }

    protected async Task SavePersonalDataAsync()
    {
        if (savingPersonal || string.IsNullOrWhiteSpace(currentIdentityUserId) || !currentAppUserId.HasValue)
            return;

        savingPersonal = true;
        personalError = null;
        personalSuccess = null;

        try
        {
            var identityUser = await UserManager.FindByIdAsync(currentIdentityUserId);
            if (identityUser is null)
            {
                personalError = "Benutzerkonto nicht gefunden.";
                return;
            }

            await using var db = await DbFactory.CreateDbContextAsync();
            var appUser = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == currentAppUserId.Value);
            if (appUser is null)
            {
                personalError = "AppUser-Profil nicht gefunden.";
                return;
            }

            identityUser.FirstName = personalData.FirstName.Trim();
            identityUser.LastName = personalData.LastName.Trim();
            identityUser.Email = personalData.Email.Trim();
            identityUser.UserName = personalData.Email.Trim();
            identityUser.PhoneNumber = string.IsNullOrWhiteSpace(personalData.Phone) ? null : personalData.Phone.Trim();

            var updateResult = await UserManager.UpdateAsync(identityUser);
            if (!updateResult.Succeeded)
            {
                personalError = string.Join(" ", updateResult.Errors.Select(x => x.Description));
                return;
            }

            appUser.DisplayName = $"{personalData.FirstName} {personalData.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(appUser.DisplayName))
            {
                appUser.DisplayName = personalData.Email.Trim();
            }

            appUser.Email = personalData.Email.Trim();
            appUser.Phone = string.IsNullOrWhiteSpace(personalData.Phone) ? null : personalData.Phone.Trim();
            appUser.PostalCode = personalData.PostalCode.Trim();
            appUser.City = personalData.City.Trim();

            await db.SaveChangesAsync();
            //await SignInManager.RefreshSignInAsync(identityUser);

            personalSuccess = "Persönliche Daten wurden gespeichert.";
        }
        catch (Exception ex)
        {
            personalError = $"Speichern fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            savingPersonal = false;
        }
    }

    protected async Task SaveHelperDataAsync()
    {
        if (savingHelper || !currentAppUserId.HasValue)
            return;

        savingHelper = true;
        helperError = null;
        helperSuccess = null;

        try
        {
            await using var db = await DbFactory.CreateDbContextAsync();

            var appUser = await db.AppUsers
                .Include(x => x.HelperProfile)
                .FirstOrDefaultAsync(x => x.Id == currentAppUserId.Value);

            if (appUser is null)
            {
                helperError = "AppUser-Profil nicht gefunden.";
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

            profile.Title = helperData.Title.Trim();
            profile.Description = helperData.Description.Trim();
            profile.Skills = helperData.Skills.Trim();
            profile.HourlyRate = helperData.HourlyRate;
            profile.RadiusKm = helperData.RadiusKm;
            profile.OffersBarter = helperData.OffersBarter;

            appUser.Role = UserRole.Helfer;

            await db.SaveChangesAsync();
            helperSuccess = "Helferdaten wurden gespeichert.";
        }
        catch (Exception ex)
        {
            helperError = $"Speichern fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            savingHelper = false;
        }
    }

    protected async Task ChangePasswordAsync()
    {
        if (savingPassword || string.IsNullOrWhiteSpace(currentIdentityUserId))
            return;

        savingPassword = true;
        passwordError = null;
        passwordSuccess = null;

        try
        {
            var identityUser = await UserManager.FindByIdAsync(currentIdentityUserId);
            if (identityUser is null)
            {
                passwordError = "Benutzerkonto nicht gefunden.";
                return;
            }

            var result = await UserManager.ChangePasswordAsync(
                identityUser,
                passwordData.CurrentPassword,
                passwordData.NewPassword);

            if (!result.Succeeded)
            {
                passwordError = string.Join(" ", result.Errors.Select(x => x.Description));
                return;
            }

            await SignInManager.RefreshSignInAsync(identityUser);
            passwordData = new PasswordChangeModel();
            passwordSuccess = "Passwort wurde erfolgreich geändert.";
        }
        catch (Exception ex)
        {
            passwordError = $"Passwortänderung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            savingPassword = false;
        }
    }

    protected MeinTab activeTab = MeinTab.OwnListings;

    protected void SetTab(MeinTab tab)
    {
        activeTab = tab;

        if (tab != MeinTab.Applications)
        {
            selectedApplicationStatusFilter = null;
        }

        SyncUrlFromState();
    }

    protected void SetApplicationFilter(ListingApplicationStatus? status)
    {
        activeTab = MeinTab.Applications;
        selectedApplicationStatusFilter = status;
        SyncUrlFromState();
    }

    private void SyncUrlFromState()
    {
        var target = BuildMeinUrl(activeTab, selectedApplicationStatusFilter);
        var currentRelative = "/" + Navigation.ToBaseRelativePath(Navigation.Uri);

        if (!string.Equals(currentRelative, target.TrimStart('/').Insert(0, "/"), StringComparison.OrdinalIgnoreCase))
        {
            Navigation.NavigateTo(target, replace: true);
        }
    }

    private static string BuildMeinUrl(MeinTab tab, ListingApplicationStatus? status)
    {
        var url = $"/mein?tab={ToTabQuery(tab)}";

        if (tab == MeinTab.Applications && status.HasValue)
        {
            url += $"&status={ToStatusQuery(status.Value)}";
        }

        return url;
    }

    private static string ToTabQuery(MeinTab tab) => tab switch
    {
        MeinTab.OwnListings => "own",
        MeinTab.HelperListings => "helper",
        MeinTab.Applications => "applications",
        MeinTab.HelperProfile => "helper-profile",
        MeinTab.PersonalData => "personal",
        MeinTab.Password => "password",
        _ => "own"
    };

    private static bool TryParseTab(string? value, out MeinTab tab)
    {
        tab = MeinTab.OwnListings;

        switch (value?.Trim().ToLowerInvariant())
        {
            case "own":
                tab = MeinTab.OwnListings;
                return true;
            case "helper":
                tab = MeinTab.HelperListings;
                return true;
            case "applications":
                tab = MeinTab.Applications;
                return true;
            case "helper-profile":
                tab = MeinTab.HelperProfile;
                return true;
            case "personal":
                tab = MeinTab.PersonalData;
                return true;
            case "password":
                tab = MeinTab.Password;
                return true;
            default:
                return false;
        }
    }

    private static string ToStatusQuery(ListingApplicationStatus status) => status switch
    {
        ListingApplicationStatus.Eingereicht => "eingereicht",
        ListingApplicationStatus.Angenommen => "angenommen",
        ListingApplicationStatus.Abgelehnt => "abgelehnt",
        _ => "eingereicht"
    };

    private static bool TryParseStatus(string? value, out ListingApplicationStatus status)
    {
        status = ListingApplicationStatus.Eingereicht;

        switch (value?.Trim().ToLowerInvariant())
        {
            case "eingereicht":
                status = ListingApplicationStatus.Eingereicht;
                return true;
            case "angenommen":
                status = ListingApplicationStatus.Angenommen;
                return true;
            case "abgelehnt":
                status = ListingApplicationStatus.Abgelehnt;
                return true;
            default:
                return false;
        }
    }

    protected enum MeinTab
    {
        OwnListings,
        HelperListings,
        Applications,
        HelperProfile,
        PersonalData,
        Password
    }

    protected static string GetApplicationStatusText(ListingApplicationStatus status) => status switch
    {
        ListingApplicationStatus.Eingereicht => "Eingereicht",
        ListingApplicationStatus.Angenommen => "Angenommen",
        ListingApplicationStatus.Abgelehnt => "Abgelehnt",
        _ => status.ToString()
    };

    protected static string GetApplicationStatusClass(ListingApplicationStatus status) => status switch
    {
        ListingApplicationStatus.Eingereicht => "mein-status-badge status-eingereicht",
        ListingApplicationStatus.Angenommen => "mein-status-badge status-angenommen",
        ListingApplicationStatus.Abgelehnt => "mein-status-badge status-abgelehnt",
        _ => "mein-status-badge"
    };

    protected sealed record MyApplicationRow(
        Guid ListingId,
        string Title,
        string PostalCode,
        string City,
        ListingApplicationStatus Status,
        decimal? ProposedPrice,
        DateTime CreatedUtc);

    protected sealed class PersonalDataModel
    {
        [Required, MaxLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string LastName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Phone { get; set; }

        [Required, MaxLength(20)]
        public string PostalCode { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        public string City { get; set; } = string.Empty;
    }

    protected sealed class HelperDataModel
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

    protected sealed class PasswordChangeModel
    {
        [Required, DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), MinLength(8)]
        public string NewPassword { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Compare(nameof(NewPassword))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}