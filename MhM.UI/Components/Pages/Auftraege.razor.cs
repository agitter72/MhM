using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MhM.UI.Data;
using MhM.UI.Localization;
using MhM.UI.Models;

namespace MhM.UI.Components.Pages;

public partial class Auftraege
{
    [Inject]
    protected MhMDbContext Db { get; set; } = default!;

    [Inject]
    protected UiLocalizer T { get; set; } = default!;

    protected List<Listing>? items;

    protected override async Task OnInitializedAsync()
    {
        items = await Db.Listings
            .Include(x => x.Category)
            .Include(x => x.Requester)
            .Where(x => x.Status == ListingStatus.Offen)
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync();
    }
}