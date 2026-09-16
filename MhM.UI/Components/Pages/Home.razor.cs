using Microsoft.AspNetCore.Components;
using MhM.UI.Localization;
using MhM.UI.Data;
using MhM.UI.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MhM.UI.Components.Pages;

public partial class Home
{
    [Inject]
    protected UiLocalizer T { get; set; } = default!;

    [Inject] protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;
    protected int openListings;
    protected int activeHelpers;
    protected int barterOffers;
    protected List<CategoryStat> topCategories = [];

    protected override async Task OnInitializedAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        openListings = await db.Listings.CountAsync(x => x.Status == ListingStatus.Offen);
        activeHelpers = await db.HelperProfiles.CountAsync(x => x.User.Role == UserRole.Helfer);
        barterOffers = await db.Listings.CountAsync(x => x.Status == ListingStatus.Offen && (x.CompensationType == CompensationType.Tausch || x.CompensationType == CompensationType.Beides));
        var categoryCounts = await db.Listings
            .Where(x => x.Status == ListingStatus.Offen)
            .GroupBy(x => x.Category.Name)
            .Select(group => new
            {
                Name = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name)
            .Take(4)
            .ToListAsync();

        topCategories = categoryCounts
            .Select(x => new CategoryStat(x.Name, x.Count))
            .ToList();
    }

    protected sealed record CategoryStat(string Name, int Count);
}
