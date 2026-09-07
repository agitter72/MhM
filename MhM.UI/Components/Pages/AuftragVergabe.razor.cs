using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MhM.UI.Data;
using MhM.UI.Data.Models;
using MhM.UI.Services;

namespace MhM.UI.Components.Pages;

public partial class AuftragVergabe
{
    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected IMatchingService MatchingService { get; set; } = default!;

    protected readonly List<MatchCandidate> candidates = [];
    protected bool isLoading = true;
    protected bool isAssigning;
    protected bool canAssign;
    protected string? error;
    protected string? success;
    protected string listingTitle = string.Empty;
    protected string listingStatusText = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        isLoading = true;
        error = null;
        success = null;
        candidates.Clear();

        await using var db = await DbFactory.CreateDbContextAsync();

        var listing = await db.Listings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == Id);

        if (listing is null)
        {
            error = "Auftrag nicht gefunden.";
            isLoading = false;
            return;
        }

        listingTitle = listing.Title;
        listingStatusText = listing.Status.ToString();
        canAssign = listing.Status is ListingStatus.Offen or ListingStatus.InBearbeitung;

        var top = await MatchingService.GetTopCandidatesForListingAsync(Id, 5);
        candidates.AddRange(top);

        isLoading = false;
    }

    protected async Task AssignAsync(Guid helperUserId)
    {
        if (!canAssign || isAssigning)
            return;

        isAssigning = true;
        error = null;
        success = null;

        try
        {
            await MatchingService.AssignListingAsync(Id, helperUserId);
            success = "Auftrag wurde erfolgreich vergeben.";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            isAssigning = false;
        }
    }
}