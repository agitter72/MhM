using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MhM.UI.Data;
using MhM.UI.Localization;
using MhM.UI.Data.Models;

namespace MhM.UI.Components.Pages;

public partial class Helfer
{
    [Inject]
    protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected UiLocalizer T { get; set; } = default!;

    protected List<HelperProfile>? items;

    protected override async Task OnInitializedAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();

        items = await db.HelperProfiles
            .Include(x => x.User)
            .OrderBy(x => x.User.City)
            .ThenBy(x => x.User.DisplayName)
            .ToListAsync();
    }
}