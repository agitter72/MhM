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

    protected readonly List<ApplicationRow> applicationRows = [];
    protected bool isLoading = true;
    protected bool isAssigning;
    protected bool canAssign;
    protected string? error;
    protected string? success;
    protected string listingTitle = string.Empty;
    protected string listingStatusText = string.Empty;

    protected ListingApplicationStatus? selectedStatusFilter;

    protected int CountAll => applicationRows.Count;
    protected int CountEingereicht => applicationRows.Count(x => x.Status == ListingApplicationStatus.Eingereicht);
    protected int CountAngenommen => applicationRows.Count(x => x.Status == ListingApplicationStatus.Angenommen);
    protected int CountAbgelehnt => applicationRows.Count(x => x.Status == ListingApplicationStatus.Abgelehnt);

    protected IReadOnlyList<ApplicationRow> FilteredApplicationRows =>
        selectedStatusFilter is null
            ? applicationRows
            : applicationRows.Where(x => x.Status == selectedStatusFilter.Value).ToList();

    protected override async Task OnParametersSetAsync()
    {
        await ReloadAsync();
    }

    protected void SetFilter(ListingApplicationStatus? filter)
    {
        selectedStatusFilter = filter;
    }

    private async Task ReloadAsync()
    {
        isLoading = true;
        error = null;
        success = null;
        applicationRows.Clear();

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

        var applications = await db.ListingApplications
            .AsNoTracking()
            .Where(x => x.ListingId == Id)
            .Include(x => x.Applicant)
            .ToListAsync();

        var rankedCandidates = await MatchingService.GetTopCandidatesForListingAsync(Id, 50);
        var rankedByApplicantId = rankedCandidates.ToDictionary(x => x.ApplicantId, x => x);

        foreach (var app in applications)
        {
            rankedByApplicantId.TryGetValue(app.ApplicantId, out var ranked);

            applicationRows.Add(new ApplicationRow(
                ApplicantId: app.ApplicantId,
                ApplicantName: app.Applicant.DisplayName,
                ApplicantUsername: app.Applicant.Username,
                ApplicantDescription: app.Applicant.Description,
                ProfileImageUpdatedUtc: app.Applicant.ProfileImageUpdatedUtc,
                Status: app.Status,
                ProposedPrice: app.ProposedPrice,
                DistanceKm: ranked?.DistanceKm,
                TotalScore: ranked?.TotalScore,
                DistanceScore: ranked?.DistanceScore,
                RatingScore: ranked?.RatingScore,
                PriceScore: ranked?.PriceScore,
                ReliabilityScore: ranked?.ReliabilityScore,
                VerificationScore: ranked?.VerificationScore,
                CreatedUtc: app.CreatedUtc));
        }

        applicationRows.Sort((a, b) =>
        {
            var statusCompare = GetSortWeight(a.Status).CompareTo(GetSortWeight(b.Status));
            if (statusCompare != 0) return statusCompare;
            return b.CreatedUtc.CompareTo(a.CreatedUtc);
        });

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

    protected static string GetStatusText(ListingApplicationStatus status) => status switch
    {
        ListingApplicationStatus.Eingereicht => "Eingereicht",
        ListingApplicationStatus.Angenommen => "Angenommen",
        ListingApplicationStatus.Abgelehnt => "Abgelehnt",
        _ => status.ToString()
    };

    protected static string GetStatusBadgeClass(ListingApplicationStatus status) => status switch
    {
        ListingApplicationStatus.Eingereicht => "assignment-status-badge status-eingereicht",
        ListingApplicationStatus.Angenommen => "assignment-status-badge status-angenommen",
        ListingApplicationStatus.Abgelehnt => "assignment-status-badge status-abgelehnt",
        _ => "assignment-status-badge"
    };

    private static int GetSortWeight(ListingApplicationStatus status) => status switch
    {
        ListingApplicationStatus.Eingereicht => 0,
        ListingApplicationStatus.Angenommen => 1,
        ListingApplicationStatus.Abgelehnt => 2,
        _ => 99
    };

    protected sealed record ApplicationRow(
        Guid ApplicantId,
        string ApplicantName,
        string ApplicantUsername,
        string ApplicantDescription,
        DateTime? ProfileImageUpdatedUtc,
        ListingApplicationStatus Status,
        decimal? ProposedPrice,
        double? DistanceKm,
        double? TotalScore,
        double? DistanceScore,
        double? RatingScore,
        double? PriceScore,
        double? ReliabilityScore,
        double? VerificationScore,
        DateTime CreatedUtc);
}
