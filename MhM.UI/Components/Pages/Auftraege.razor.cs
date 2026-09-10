using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using MhM.UI.Data;
using MhM.UI.Localization;
using MhM.UI.Data.Models;

namespace MhM.UI.Components.Pages;

public partial class Auftraege : IAsyncDisposable
{
    private const int LoadBatchSize = 10;

    [Inject]
    protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected UiLocalizer T { get; set; } = default!;

    [Inject]
    protected IJSRuntime JS { get; set; } = default!;

    [Inject]
    protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    [SupplyParameterFromQuery(Name = "radiusKm")]
    public double? RadiusKm { get; set; }

    [SupplyParameterFromQuery(Name = "q")]
    public string? Query { get; set; }

    [SupplyParameterFromQuery(Name = "city")]
    public string? City { get; set; }

    [SupplyParameterFromQuery(Name = "min")]
    public decimal? BudgetMinFilter { get; set; }

    [SupplyParameterFromQuery(Name = "max")]
    public decimal? BudgetMaxFilter { get; set; }

    [SupplyParameterFromQuery(Name = "k")]
    public int[]? CategoryIds { get; set; }

    protected List<Listing>? items;
    protected List<Category> categories = [];
    protected HashSet<int> selectedCategoryIds = [];
    protected Dictionary<Guid, double> distancesKmByListingId = [];

    protected readonly Dictionary<Guid, ListingApplicationInputModel> applicationModels = [];
    protected readonly HashSet<Guid> appliedListingIds = [];
    protected Guid? currentUserId;
    protected bool currentUserCanApply;
    protected Guid? applyingListingId;
    protected string? applyError;
    protected string? applySuccess;
    protected int totalItems;
    protected bool loadingMore;
    protected ElementReference loadMoreSentinel;

    private List<Listing> filteredItems = [];
    private DotNetObjectReference<Auftraege>? dotNetReference;

    protected bool HasMoreItems => items is not null && items.Count < totalItems;

    protected bool IsGeoSearchActive => Latitude.HasValue && Longitude.HasValue;
    protected double EffectiveRadiusKm => RadiusKm is > 0 ? RadiusKm.Value : 50d;

    // durch:
    [SupplyParameterFromQuery(Name = "comp")]
    public string? Compensation { get; set; }

    protected CompensationType? CompensationFilter
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Compensation))
            {
                return null;
            }

            if (int.TryParse(Compensation, out var intValue) &&
                Enum.IsDefined(typeof(CompensationType), intValue))
            {
                return (CompensationType)intValue;
            }

            return null;
        }
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (HasMoreItems)
        {
            dotNetReference ??= DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("infiniteScroll.observe", loadMoreSentinel, dotNetReference);
        }

        if (firstRender && !Latitude.HasValue && !Longitude.HasValue)
        {
            var location = await JS.InvokeAsync<BrowserLocationDto?>("browserLocation.getCurrent");
            if (location is not null)
            {
                Latitude = location.Latitude;
                Longitude = location.Longitude;

                await LoadListingsAsync();
                StateHasChanged();
            }
        }
    }

    private sealed class BrowserLocationDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    protected override async Task OnParametersSetAsync()
    {
        await LoadListingsAsync();
    }

    private async Task LoadListingsAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();

        categories = await db.Categories
            .OrderBy(x => x.Name)
            .ToListAsync();

        selectedCategoryIds = (CategoryIds ?? [])
            .Distinct()
            .ToHashSet();

        var listingQuery = db.Listings
            .Include(x => x.Category)
            .Include(x => x.Requester)
            .Include(x => x.Images)
            .Where(x => x.Status == ListingStatus.Offen)
            .AsQueryable();

        if (selectedCategoryIds.Count > 0)
        {
            listingQuery = listingQuery.Where(x => selectedCategoryIds.Contains(x.CategoryId));
        }

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var term = Query.Trim();
            listingQuery = listingQuery.Where(x =>
                x.Title.Contains(term) ||
                x.Description.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(City))
        {
            var locationTerm = City.Trim();
            listingQuery = listingQuery.Where(x =>
                x.City.Contains(locationTerm) ||
                x.PostalCode.Contains(locationTerm));
        }

        if (CompensationFilter.HasValue)
        {
            listingQuery = listingQuery.Where(x => x.CompensationType == CompensationFilter.Value);
        }

        if (BudgetMinFilter.HasValue)
        {
            var min = BudgetMinFilter.Value;
            listingQuery = listingQuery.Where(x => (x.BudgetMax ?? x.BudgetMin ?? decimal.MaxValue) >= min);
        }

        if (BudgetMaxFilter.HasValue)
        {
            var max = BudgetMaxFilter.Value;
            listingQuery = listingQuery.Where(x => (x.BudgetMin ?? x.BudgetMax ?? 0m) <= max);
        }

        var filteredListings = await listingQuery
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync();

        await LoadCurrentUserAsync(db);

        distancesKmByListingId.Clear();

        if (!IsGeoSearchActive)
        {
            filteredItems = filteredListings;
        }
        else
        {
            var centerLat = Latitude!.Value;
            var centerLon = Longitude!.Value;
            var radius = EffectiveRadiusKm;

            filteredItems = filteredListings
                .Where(x => x.Latitude.HasValue && x.Longitude.HasValue)
                .Select(x => new
                {
                    Listing = x,
                    DistanceKm = CalculateDistanceKm(centerLat, centerLon, x.Latitude!.Value, x.Longitude!.Value)
                })
                .Where(x => x.DistanceKm <= radius)
                .OrderBy(x => x.DistanceKm)
                .Select(x =>
                {
                    distancesKmByListingId[x.Listing.Id] = x.DistanceKm;
                    return x.Listing;
                })
                .ToList();
        }

        totalItems = filteredItems.Count;
        items = filteredItems
            .Take(LoadBatchSize)
            .ToList();

        SeedApplicationModels();
    }

    [JSInvokable]
    public Task LoadMoreAsync()
    {
        if (loadingMore || items is null || !HasMoreItems)
        {
            return Task.CompletedTask;
        }

        loadingMore = true;
        var nextCount = Math.Min(items.Count + LoadBatchSize, totalItems);
        items = filteredItems.Take(nextCount).ToList();

        foreach (var listing in items)
        {
            applicationModels.TryAdd(listing.Id, new ListingApplicationInputModel
            {
                CompensationType = listing.CompensationType
            });
        }

        loadingMore = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (dotNetReference is null)
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("infiniteScroll.disconnect");
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone; there is no observer left to clean up.
        }

        dotNetReference.Dispose();
    }

    protected string? GetDistanceText(Guid listingId)
        => distancesKmByListingId.TryGetValue(listingId, out var d) ? $"{d:N1} km" : null;

    protected ListingApplicationInputModel GetApplicationModel(Guid listingId)
    {
        if (!applicationModels.TryGetValue(listingId, out var model))
        {
            model = new ListingApplicationInputModel();
            applicationModels[listingId] = model;
        }

        return model;
    }

    protected bool CanApplyToListing(Listing listing)
        => currentUserCanApply
           && currentUserId.HasValue
           && listing.Status == ListingStatus.Offen
           && listing.RequesterId != currentUserId.Value;

    protected bool HasApplied(Guid listingId) => appliedListingIds.Contains(listingId);

    protected string GetApplyFormName(Guid listingId) => $"apply-listing-{listingId:N}";

    protected async Task ApplyAsync(Guid listingId)
    {
        if (applyingListingId.HasValue)
            return;

        applyError = null;
        applySuccess = null;

        if (!currentUserCanApply || !currentUserId.HasValue)
        {
            applyError = "Nur angemeldete Helfer können sich bewerben.";
            return;
        }

        if (appliedListingIds.Contains(listingId))
        {
            applyError = "Du hast dich bereits auf diesen Auftrag beworben.";
            return;
        }

        var model = GetApplicationModel(listingId);
        applyingListingId = listingId;

        try
        {
            await using var db = await DbFactory.CreateDbContextAsync();

            var listing = await db.Listings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == listingId);

            if (listing is null)
            {
                applyError = "Auftrag nicht gefunden.";
                return;
            }

            if (listing.Status != ListingStatus.Offen)
            {
                applyError = "Auftrag ist nicht mehr offen.";
                return;
            }

            if (listing.RequesterId == currentUserId.Value)
            {
                applyError = "Du kannst dich nicht auf deinen eigenen Auftrag bewerben.";
                return;
            }

            var exists = await db.ListingApplications
                .AnyAsync(x => x.ListingId == listingId && x.ApplicantId == currentUserId.Value);

            if (exists)
            {
                appliedListingIds.Add(listingId);
                applyError = "Du hast dich bereits auf diesen Auftrag beworben.";
                return;
            }

            db.ListingApplications.Add(new ListingApplication
            {
                ListingId = listingId,
                ApplicantId = currentUserId.Value,
                Message = model.Message.Trim(),
                ProposedPrice = model.ProposedPrice,
                CompensationType = model.CompensationType,
                Status = ListingApplicationStatus.Eingereicht,
                CreatedUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            appliedListingIds.Add(listingId);
            Navigation.NavigateTo("/mein?tab=applications&status=eingereicht");
            return;
        }
        finally
        {
            applyingListingId = null;
        }
    }

    private async Task LoadCurrentUserAsync(MhMDbContext db)
    {
        currentUserId = null;
        currentUserCanApply = false;
        appliedListingIds.Clear();

        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var principal = authState.User;

        if (principal.Identity?.IsAuthenticated != true)
            return;

        var email = principal.Identity.Name?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return;

        var user = await db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Email == email);

        if (user is null)
            return;

        currentUserId = user.Id;
        currentUserCanApply = user.Role == UserRole.Helfer;

        if (!currentUserCanApply)
            return;

        var existing = await db.ListingApplications
            .AsNoTracking()
            .Where(x => x.ApplicantId == user.Id)
            .Select(x => x.ListingId)
            .ToListAsync();

        foreach (var listingId in existing)
        {
            appliedListingIds.Add(listingId);
        }
    }

    private void SeedApplicationModels()
    {
        applicationModels.Clear();

        if (items is null)
            return;

        foreach (var listing in items)
        {
            applicationModels[listing.Id] = new ListingApplicationInputModel
            {
                CompensationType = listing.CompensationType
            };
        }
    }

    private static double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371d;

        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(DegreesToRadians(lat1)) *
                Math.Cos(DegreesToRadians(lat2)) *
                Math.Pow(Math.Sin(dLon / 2), 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusKm * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    protected sealed class ListingApplicationInputModel
    {
        [Required(ErrorMessage = "Bitte eine Nachricht eingeben.")]
        [StringLength(1500, MinimumLength = 10, ErrorMessage = "Die Nachricht muss zwischen 10 und 1500 Zeichen lang sein.")]
        public string Message { get; set; } = string.Empty;

        [Range(typeof(decimal), "0", "999999", ErrorMessage = "Bitte einen gültigen Preis eingeben.")]
        public decimal? ProposedPrice { get; set; }

        public CompensationType CompensationType { get; set; } = CompensationType.Bezahlung;
    }
}
