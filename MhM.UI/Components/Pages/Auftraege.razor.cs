using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MhM.UI.Data;
using MhM.UI.Localization;
using MhM.UI.Data.Models;

namespace MhM.UI.Components.Pages;

public partial class Auftraege
{
    [Inject]
    protected MhMDbContext Db { get; set; } = default!;

    [Inject]
    protected UiLocalizer T { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "lat")]
    public double? Latitude { get; set; }

    [SupplyParameterFromQuery(Name = "lon")]
    public double? Longitude { get; set; }

    [SupplyParameterFromQuery(Name = "radiusKm")]
    public double? RadiusKm { get; set; }

    protected List<Listing>? items;
    protected Dictionary<Guid, double> distancesKmByListingId = [];

    protected bool IsGeoSearchActive => Latitude.HasValue && Longitude.HasValue;
    protected double EffectiveRadiusKm => RadiusKm is > 0 ? RadiusKm.Value : 25d;

    protected override async Task OnParametersSetAsync()
    {
        var openListings = await Db.Listings
            .Include(x => x.Category)
            .Include(x => x.Requester)
            .Include(x => x.Images)
            .Where(x => x.Status == ListingStatus.Offen)
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync();

        distancesKmByListingId.Clear();

        if (!IsGeoSearchActive)
        {
            items = openListings;
            return;
        }

        var centerLat = Latitude!.Value;
        var centerLon = Longitude!.Value;
        var radius = EffectiveRadiusKm;

        items = openListings
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

    protected string? GetDistanceText(Guid listingId)
        => distancesKmByListingId.TryGetValue(listingId, out var d) ? $"{d:N1} km" : null;

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
}