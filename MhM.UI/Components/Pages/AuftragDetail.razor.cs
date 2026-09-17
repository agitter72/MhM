using MhM.UI.Data;
using MhM.UI.Data.Models;
using MhM.UI.Localization;
using MhM.UI.Models;
using MhM.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MhM.UI.Components.Pages;

public partial class AuftragDetail
{
    [Parameter]
    public Guid ListingId { get; set; }

    [Inject]
    protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected UiLocalizer T { get; set; } = default!;

    [Inject]
    protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    [Inject]
    protected IMatchingService MatchingService { get; set; } = default!;

    protected bool canRequesterCompleteAndReview;
    protected bool requesterCompletionBlockedByPreferredDate;
    protected bool canStartWork;
    protected bool canReportCompletion;
    protected bool canReportProblem;
    protected bool canManageListing;
    protected bool currentUserIsAdmin;
    protected string? trustMessage;
    protected bool showListingReportForm;
    protected string selectedListingReportReason = string.Empty;
    protected string listingReportDetails = string.Empty;
    protected bool showAdminDeleteForm;
    protected string adminDeleteReason = string.Empty;

    // Methoden anpassen
    protected Task SubmitHelperReviewAsync(ReviewSubmission submission)
        => SaveReviewAsync(submission, ReviewTarget.Helper, completeListingIfAllowed: true);

    protected Task SubmitRequesterReviewAsync(ReviewSubmission submission)
        => SaveReviewAsync(submission, ReviewTarget.Requester, completeListingIfAllowed: false);

    private async Task SaveReviewAsync(
        ReviewSubmission submission,
        ReviewTarget target,
        bool completeListingIfAllowed)
    {
        reviewError = null;
        reviewSuccess = null;

        if (item is null || !currentUserId.HasValue)
        {
            reviewError = "Bewertung konnte nicht gespeichert werden.";
            return;
        }

        await using var db = await DbFactory.CreateDbContextAsync();

        var listing = await db.Listings
            .FirstOrDefaultAsync(x => x.Id == item.Id);

        if (listing is null)
        {
            reviewError = "Auftrag nicht gefunden.";
            return;
        }

        var acceptedId = await db.ListingApplications
            .AsNoTracking()
            .Where(x => x.ListingId == listing.Id && x.Status == ListingApplicationStatus.Angenommen)
            .Select(x => (Guid?)x.ApplicantId)
            .FirstOrDefaultAsync();

        if (!acceptedId.HasValue)
        {
            reviewError = "Es wurde kein angenommener Helfer gefunden.";
            return;
        }

        var reviewerId = currentUserId.Value;
        var preferredDateReached = IsPreferredDateReached(listing.PreferredDateUtc);

        var canCompleteListingNow =
            completeListingIfAllowed &&
            target == ReviewTarget.Helper &&
            reviewerId == listing.RequesterId &&
            listing.Status == ListingStatus.AbschlussGemeldet &&
            preferredDateReached;

        if (listing.Status != ListingStatus.Abgeschlossen && !canCompleteListingNow)
        {
            if (target == ReviewTarget.Helper &&
                reviewerId == listing.RequesterId &&
                listing.Status == ListingStatus.AbschlussGemeldet &&
                !preferredDateReached)
            {
                reviewError = $"Der Auftrag kann erst ab {FormatDateLocal(listing.PreferredDateUtc)} beendet und bewertet werden.";
            }
            else
            {
                reviewError = "Bewertungen sind erst nach Auftragsbeendigung möglich.";
            }

            return;
        }

        Guid revieweeId;
        IReadOnlyList<ReviewCategory> categories;

        switch (target)
        {
            case ReviewTarget.Helper:
                if (reviewerId != listing.RequesterId)
                {
                    reviewError = "Nur der Auftraggeber kann den Helfer bewerten.";
                    return;
                }

                revieweeId = acceptedId.Value;
                categories = _helperReviewCategories;
                break;

            case ReviewTarget.Requester:
                if (reviewerId != acceptedId.Value)
                {
                    reviewError = "Nur der angenommene Helfer kann den Auftraggeber bewerten.";
                    return;
                }

                revieweeId = listing.RequesterId;
                categories = _requesterReviewCategories;
                break;

            default:
                reviewError = "Ungültiger Bewertungstyp.";
                return;
        }

        if (reviewerId == revieweeId)
        {
            reviewError = "Selbstbewertung ist nicht erlaubt.";
            return;
        }

        if (categories.Any(c => !submission.Ratings.TryGetValue(c.Key, out var v) || v is < 1 or > 5))
        {
            reviewError = "Bitte alle Bewertungskategorien mit 1 bis 5 Sternen ausfüllen.";
            return;
        }

        var stars = Math.Clamp((int)Math.Round(submission.AverageRating, MidpointRounding.AwayFromZero), 1, 5);

        var existing = await db.Reviews
            .Include(x => x.CategoryRatings)
            .FirstOrDefaultAsync(x =>
                x.ListingId == listing.Id &&
                x.ReviewerId == reviewerId &&
                x.RevieweeId == revieweeId);

        if (existing is null)
        {
            existing = new Review
            {
                ListingId = listing.Id,
                ReviewerId = reviewerId,
                RevieweeId = revieweeId,
                Stars = stars,
                CreatedUtc = DateTime.UtcNow
            };

            foreach (var category in categories)
            {
                existing.CategoryRatings.Add(new ReviewCategoryRating
                {
                    CategoryKey = category.Key,
                    Stars = submission.Ratings[category.Key]
                });
            }

            db.Reviews.Add(existing);
        }
        else
        {
            reviewError = "Eine abgegebene Bewertung kann nur durch den Support geändert werden.";
            return;
        }

        if (canCompleteListingNow)
        {
            try
            {
                await MatchingService.CompleteListingByRequesterAsync(listing.Id, reviewerId);
                await db.Entry(listing).ReloadAsync();
            }
            catch (InvalidOperationException ex)
            {
                reviewError = ex.Message;
                return;
            }
        }

        await db.SaveChangesAsync();

        if (item is not null)
        {
            item.Status = listing.Status;
        }

        await LoadReviewStateAsync(db);
        await LoadSubmittedReviewsAsync(db);

        if (canCompleteListingNow)
        {
            hasReviewedHelper = true;
            reviewSuccess = "Auftrag wurde beendet und der Helfer erfolgreich bewertet.";
        }
        else if (target == ReviewTarget.Helper)
        {
            hasReviewedHelper = true;
            reviewSuccess = "Die Helfer-Bewertung wurde gespeichert.";
        }
        else
        {
            hasReviewedRequester = true;
            reviewSuccess = "Die Auftraggeber-Bewertung wurde gespeichert.";
        }
    }

    private async Task LoadReviewStateAsync(MhMDbContext db)
    {
        canReviewHelper = false;
        canReviewRequester = false;
        hasReviewedHelper = false;
        hasReviewedRequester = false;
        canRequesterCompleteAndReview = false;
        requesterCompletionBlockedByPreferredDate = false;
        canStartWork = false;
        canReportCompletion = false;
        canReportProblem = false;
        acceptedHelperId = null;

        if (item is null || !currentUserId.HasValue)
            return;

        acceptedHelperId = await db.ListingApplications
            .AsNoTracking()
            .Where(x => x.ListingId == item.Id && x.Status == ListingApplicationStatus.Angenommen)
            .Select(x => (Guid?)x.ApplicantId)
            .FirstOrDefaultAsync();

        if (!acceptedHelperId.HasValue)
            return;

        var isRequester = currentUserId.Value == item.RequesterId;
        var isAcceptedHelper = currentUserId.Value == acceptedHelperId.Value;
        canStartWork = isAcceptedHelper && item.Status == ListingStatus.Vergeben;
        canReportCompletion = isAcceptedHelper && item.Status == ListingStatus.InDurchfuehrung;
        canReportProblem = (isRequester || isAcceptedHelper) && item.Status is ListingStatus.Vergeben or ListingStatus.InDurchfuehrung or ListingStatus.AbschlussGemeldet;

        if (isRequester)
        {
            hasReviewedHelper = await db.Reviews
                .AsNoTracking()
                .AnyAsync(x =>
                    x.ListingId == item.Id &&
                    x.ReviewerId == currentUserId.Value &&
                    x.RevieweeId == acceptedHelperId.Value);
        }

        if (isAcceptedHelper)
        {
            hasReviewedRequester = await db.Reviews
                .AsNoTracking()
                .AnyAsync(x =>
                    x.ListingId == item.Id &&
                    x.ReviewerId == currentUserId.Value &&
                    x.RevieweeId == item.RequesterId);
        }

        var preferredDateReached = IsPreferredDateReached(item.PreferredDateUtc);

        canRequesterCompleteAndReview =
            item.Status == ListingStatus.AbschlussGemeldet &&
            isRequester &&
            preferredDateReached &&
            !hasReviewedHelper;

        requesterCompletionBlockedByPreferredDate =
            item.Status == ListingStatus.AbschlussGemeldet &&
            isRequester &&
            !preferredDateReached;

        canReviewHelper = item.Status == ListingStatus.Abgeschlossen && isRequester;
        canReviewRequester = item.Status == ListingStatus.Abgeschlossen && isAcceptedHelper;
    }

    private static bool IsPreferredDateReached(DateTime? preferredDateUtc)
        => !preferredDateUtc.HasValue || DateTime.UtcNow >= preferredDateUtc.Value;

    private static string FormatDateLocal(DateTime? utcDate)
        => utcDate?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "-";
    private static readonly IReadOnlyList<ReviewCategory> _helperReviewCategories =
    [
        new("quality", "Qualität der Arbeit"),
        new("reliability", "Zuverlässigkeit"),
        new("appearance", "Persönliches Auftreten des Helfers"),
        new("speed", "Zügigkeit der Umsetzung")
    ];

    private static readonly IReadOnlyList<ReviewCategory> _requesterReviewCategories =
    [
        new("quality", "Qualität der Zusammenarbeit"),
        new("reliability", "Zuverlässigkeit"),
        new("appearance", "Persönliches Auftreten des Auftraggebers"),
        new("speed", "Zügigkeit der Rückmeldungen")
    ];

    protected Listing? item;
    protected bool isLoading = true;
    protected int applicationCount;

    protected readonly Dictionary<Guid, ListingApplicationInputModel> applicationModels = [];
    protected readonly HashSet<Guid> appliedListingIds = [];
    protected Guid? currentUserId;
    protected bool currentUserCanApply;
    protected bool isApplying;
    protected string? applyError;
    protected string? applySuccess;

    protected bool canReviewHelper;
    protected bool canReviewRequester;
    protected bool hasReviewedHelper;
    protected bool hasReviewedRequester;
    protected string? reviewError;
    protected string? reviewSuccess;

    private Guid? acceptedHelperId;

    protected ReviewViewModel? helperReviewFromRequester;
    protected ReviewViewModel? requesterReviewFromHelper;
    protected AssignmentAgreement? agreement;

    protected override async Task OnParametersSetAsync()
    {
        isLoading = true;
        applyError = null;
        applySuccess = null;
        reviewError = null;
        reviewSuccess = null;
        showListingReportForm = false;
        selectedListingReportReason = string.Empty;
        listingReportDetails = string.Empty;
        showAdminDeleteForm = false;
        adminDeleteReason = string.Empty;

        await using var db = await DbFactory.CreateDbContextAsync();

        item = await db.Listings
            .Include(x => x.Category)
            .Include(x => x.Requester)
            .Include(x => x.Images)
            .FirstOrDefaultAsync(x => x.Id == ListingId);

        await LoadCurrentUserAsync(db);
        canManageListing = item is not null && currentUserId.HasValue && (item.RequesterId == currentUserId || currentUserIsAdmin);
        if (item?.Status is ListingStatus.Entwurf or ListingStatus.Storniert && !canManageListing)
            item = null;

        if (item is not null && canManageListing)
        {
            applicationCount = await db.ListingApplications
                .CountAsync(x => x.ListingId == item.Id);
        }

        await LoadReviewStateAsync(db);
        agreement = item is not null && currentUserId.HasValue && (canManageListing || currentUserId == acceptedHelperId)
            ? await db.AssignmentAgreements.AsNoTracking().FirstOrDefaultAsync(x => x.ListingId == item.Id)
            : null;
        await LoadSubmittedReviewsAsync(db);
        await LoadChatStateAsync(db);
        await EnsureListingNotificationsReadAsync();
        await RestartChatRefreshLoopAsync();

        //SeedApplicationModel();
        isLoading = false;
    }

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

    protected static string FormatBudget(Listing listing)
    {
        if (listing.BudgetMin.HasValue && listing.BudgetMax.HasValue)
        {
            return $"{listing.BudgetMin:N0}–{listing.BudgetMax:N0} €";
        }

        if (listing.BudgetMin.HasValue)
        {
            return $"Ab {listing.BudgetMin:N0} €";
        }

        if (listing.BudgetMax.HasValue)
        {
            return $"Bis {listing.BudgetMax:N0} €";
        }

        return "Nach Absprache";
    }

    protected static string GetInitials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0
            ? "?"
            : string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    protected bool HasApplied(Guid listingId) => appliedListingIds.Contains(listingId);

    protected string GetApplyFormName(Guid listingId) => $"apply-listing-{listingId:N}";

    protected async Task ApplyAsync()
    {
        if (item is null || isApplying)
            return;

        applyError = null;
        applySuccess = null;

        if (!currentUserCanApply || !currentUserId.HasValue)
        {
            applyError = "Nur angemeldete Helfer können sich bewerben.";
            return;
        }

        if (appliedListingIds.Contains(item.Id))
        {
            applyError = "Du hast dich bereits auf diesen Auftrag beworben.";
            return;
        }

        var model = GetApplicationModel(item.Id);
        isApplying = true;

        try
        {
            await using var db = await DbFactory.CreateDbContextAsync();

            var listing = await db.Listings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == item.Id);

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
                .AnyAsync(x => x.ListingId == item.Id && x.ApplicantId == currentUserId.Value);

            var blocked = await db.UserBlocks.AnyAsync(x =>
                (x.BlockingUserId == currentUserId.Value && x.BlockedUserId == listing.RequesterId) ||
                (x.BlockingUserId == listing.RequesterId && x.BlockedUserId == currentUserId.Value));
            if (blocked)
            {
                applyError = "Eine Blockierung verhindert neue Kontakte zwischen diesen Konten.";
                return;
            }

            if (exists)
            {
                appliedListingIds.Add(item.Id);
                applyError = "Du hast dich bereits auf diesen Auftrag beworben.";
                return;
            }

            db.ListingApplications.Add(new ListingApplication
            {
                ListingId = item.Id,
                ApplicantId = currentUserId.Value,
                Message = model.Message.Trim(),
                ProposedPrice = model.ProposedPrice,
                CompensationType = model.CompensationType,
                Status = ListingApplicationStatus.Eingereicht,
                CreatedUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            await NotificationService.CreateApplicationNotificationAsync(
                listing.Id,
                listing.RequesterId,
                currentUserId.Value,
                currentUserDisplayName ?? "Ein Helfer",
                listing.Title);

            appliedListingIds.Add(item.Id);
            applicationCount++;
            Navigation.NavigateTo("/einstellungen?tab=applications&status=eingereicht");
        }
        finally
        {
            isApplying = false;
        }
    }

    private async Task LoadCurrentUserAsync(MhMDbContext db)
    {
        currentUserId = null;
        currentUserDisplayName = null;
        currentUserCanApply = false;
        appliedListingIds.Clear();

        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var principal = authState.User;
        currentUserIsAdmin = principal.IsInRole(PlatformRoles.Admin);

        if (principal.Identity?.IsAuthenticated != true)
            return;

        var identityUserId = principal.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(identityUserId))
            return;

        var user = await db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdentityUserId == identityUserId);

        if (user is null)
            return;

        currentUserId = user.Id;
        currentUserDisplayName = user.DisplayName;
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

    protected async Task StartWorkAsync() => await ChangeHelperStatusAsync(start: true);
    protected async Task ReportCompletionAsync() => await ChangeHelperStatusAsync(start: false);

    private async Task ChangeHelperStatusAsync(bool start)
    {
        if (!currentUserId.HasValue || item is null) return;
        try
        {
            if (start) await MatchingService.StartListingAsync(item.Id, currentUserId.Value);
            else await MatchingService.ReportCompletionAsync(item.Id, currentUserId.Value);
            item.Status = start ? ListingStatus.InDurchfuehrung : ListingStatus.AbschlussGemeldet;
            await using var db = await DbFactory.CreateDbContextAsync();
            await LoadReviewStateAsync(db);
            trustMessage = start ? "Durchführung wurde gestartet." : "Abschluss wurde gemeldet. Der Auftraggeber kann ihn nun bestätigen.";
        }
        catch (Exception) { trustMessage = "Der Statuswechsel konnte nicht ausgeführt werden."; }
    }

    protected async Task ReportListingAsync()
    {
        if (!currentUserId.HasValue || item is null) return;
        if (!ReportReasons.Listing.Contains(selectedListingReportReason))
        {
            trustMessage = "Bitte wähle einen Meldegrund aus.";
            return;
        }
        if (selectedListingReportReason == ReportReasons.Other && string.IsNullOrWhiteSpace(listingReportDetails))
        {
            trustMessage = "Bitte beschreibe bei ‚Sonstiges‘, was das Problem ist.";
            return;
        }
        await using var db = await DbFactory.CreateDbContextAsync();
        var duplicate = await db.ContentReports.AnyAsync(x => x.ReporterUserId == currentUserId && x.TargetType == ReportTargetType.Auftrag && x.TargetId == item.Id && x.Status != ReportStatus.Erledigt && x.Status != ReportStatus.Abgelehnt);
        if (!duplicate)
        {
            db.ContentReports.Add(new ContentReport
            {
                ReporterUserId = currentUserId.Value,
                TargetType = ReportTargetType.Auftrag,
                TargetId = item.Id,
                Reason = selectedListingReportReason,
                Details = selectedListingReportReason == ReportReasons.Other ? listingReportDetails.Trim() : string.Empty
            });
            await db.SaveChangesAsync();
        }
        showListingReportForm = false;
        trustMessage = duplicate ? "Du hast diesen Auftrag bereits gemeldet." : "Meldung wurde sicher an die Administration übermittelt.";
    }

    protected async Task DeleteListingAsAdminAsync()
    {
        if (!currentUserIsAdmin || item is null) return;
        var reason = adminDeleteReason.Trim();
        if (reason.Length == 0)
        {
            trustMessage = "Eine Begründung für die Löschung ist erforderlich.";
            return;
        }

        await using var db = await DbFactory.CreateDbContextAsync();
        var listing = await db.Listings.FirstOrDefaultAsync(x => x.Id == item.Id);
        if (listing is null) return;
        listing.Status = ListingStatus.Storniert;
        db.UserNotifications.Add(new UserNotification
        {
            Type = UserNotificationType.Moderation,
            RecipientUserId = listing.RequesterId,
            ListingId = listing.Id,
            Title = "Auftrag durch Moderation entfernt",
            Content = $"Dein Auftrag ‚{listing.Title}‘ wurde entfernt. Begründung: {reason}",
            LinkUrl = $"/auftraege/{listing.Id}"
        });
        var actorIdentityId = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        db.AdminAuditLogs.Add(new AdminAuditLog
        {
            ActorIdentityUserId = actorIdentityId,
            Action = "ListingDeleted",
            TargetType = "Listing",
            TargetId = listing.Id.ToString(),
            Details = reason
        });
        var openReports = await db.ContentReports.Where(x => x.TargetType == ReportTargetType.Auftrag && x.TargetId == listing.Id && x.Status != ReportStatus.Erledigt && x.Status != ReportStatus.Abgelehnt).ToListAsync();
        foreach (var report in openReports) { report.Status = ReportStatus.Erledigt; report.ResolvedUtc = DateTime.UtcNow; report.ResolvedByIdentityUserId = actorIdentityId; }
        await db.SaveChangesAsync();
        item.Status = ListingStatus.Storniert;
        showAdminDeleteForm = false;
        trustMessage = "Der Auftrag wurde entfernt und der Auftraggeber mit der Begründung benachrichtigt.";
    }

    protected async Task ReportProblemAsync()
    {
        if (!currentUserId.HasValue || item is null || !canReportProblem) return;
        await using var db = await DbFactory.CreateDbContextAsync();
        var listing = await db.Listings.FirstOrDefaultAsync(x => x.Id == item.Id);
        var helperId = await db.ListingApplications.Where(x => x.ListingId == item.Id && x.Status == ListingApplicationStatus.Angenommen).Select(x => (Guid?)x.ApplicantId).FirstOrDefaultAsync();
        if (listing is null || !helperId.HasValue || (currentUserId != listing.RequesterId && currentUserId != helperId)) return;
        if (listing.Status is not (ListingStatus.Vergeben or ListingStatus.InDurchfuehrung or ListingStatus.AbschlussGemeldet)) return;
        listing.Status = ListingStatus.ProblemGemeldet;
        db.ContentReports.Add(new ContentReport { ReporterUserId = currentUserId.Value, TargetType = ReportTargetType.Auftrag, TargetId = item.Id, Reason = "Problem im Auftragsablauf", Details = "Ein Beteiligter hat einen Konfliktfall eröffnet. Vereinbarung und Chat müssen erhalten bleiben." });
        await db.SaveChangesAsync();
        item.Status = ListingStatus.ProblemGemeldet; canReportProblem = false;
        trustMessage = "Problemfall wurde eröffnet. Vereinbarung und Chat bleiben für die Klärung erhalten.";
    }

    protected async Task ToggleRequesterBlockAsync()
    {
        if (!currentUserId.HasValue || item is null || currentUserId == item.RequesterId) return;
        await using var db = await DbFactory.CreateDbContextAsync();
        var block = await db.UserBlocks.FirstOrDefaultAsync(x => x.BlockingUserId == currentUserId && x.BlockedUserId == item.RequesterId);
        if (block is null) { db.UserBlocks.Add(new UserBlock { BlockingUserId = currentUserId.Value, BlockedUserId = item.RequesterId }); trustMessage = "Nutzer wurde blockiert. Bestehende Verträge und Konfliktchats bleiben erhalten."; }
        else { db.UserBlocks.Remove(block); trustMessage = "Blockierung wurde aufgehoben."; }
        await db.SaveChangesAsync();
    }

    private async Task LoadSubmittedReviewsAsync(MhMDbContext db)
    {
        helperReviewFromRequester = null;
        requesterReviewFromHelper = null;

        if (item is null || item.Status != ListingStatus.Abgeschlossen)
            return;

        var acceptedId = await db.ListingApplications
            .AsNoTracking()
            .Where(x => x.ListingId == item.Id && x.Status == ListingApplicationStatus.Angenommen)
            .Select(x => (Guid?)x.ApplicantId)
            .FirstOrDefaultAsync();

        if (!acceptedId.HasValue)
            return;

        var helperId = acceptedId.Value;

        var reviews = await db.Reviews
            .AsNoTracking()
            .Include(x => x.CategoryRatings)
            .Where(x => x.ListingId == item.Id &&
                        ((x.ReviewerId == item.RequesterId && x.RevieweeId == helperId) ||
                         (x.ReviewerId == helperId && x.RevieweeId == item.RequesterId)))
            .ToListAsync();

        var releaseReached = reviews.Count >= 2 || reviews.Any(x => x.CreatedUtc <= DateTime.UtcNow.AddDays(-14));
        if (!releaseReached)
            return;

        var helperReview = reviews.FirstOrDefault(x => x.ReviewerId == item.RequesterId && x.RevieweeId == helperId);
        if (helperReview is not null)
        {
            helperReviewFromRequester = BuildReviewViewModel(
                helperReview,
                "Auftraggeber",
                "Helfer",
                _helperReviewCategories);
        }

        var requesterReview = reviews.FirstOrDefault(x => x.ReviewerId == helperId && x.RevieweeId == item.RequesterId);
        if (requesterReview is not null)
        {
            requesterReviewFromHelper = BuildReviewViewModel(
                requesterReview,
                "Helfer",
                "Auftraggeber",
                _requesterReviewCategories);
        }
    }

    private static ReviewViewModel BuildReviewViewModel(
        Review review,
        string reviewerRole,
        string revieweeRole,
        IReadOnlyList<ReviewCategory> categories)
    {
        var categoryRatings = categories
            .Select(c => new ReviewCategoryViewModel(
                c.Label,
                review.CategoryRatings.FirstOrDefault(r => r.CategoryKey == c.Key)?.Stars ?? 0))
            .ToList();

        return new ReviewViewModel(
            reviewerRole,
            revieweeRole,
            review.Stars,
            review.CreatedUtc,
            categoryRatings);
    }

    private static ParsedReviewComment ParseStoredComment(string? rawComment)
    {
        if (string.IsNullOrWhiteSpace(rawComment))
        {
            return new ParsedReviewComment(null, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        }

        const string marker = "Kategorien:";
        var idx = rawComment.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (idx < 0)
        {
            return new ParsedReviewComment(rawComment.Trim(), new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        }

        var commentPart = rawComment[..idx].Trim();
        var ratingsPart = rawComment[(idx + marker.Length)..].Trim();

        var ratings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in ratingsPart.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split(':', 2, StringSplitOptions.TrimEntries);
            if (kv.Length == 2 && int.TryParse(kv[1], out var value))
            {
                ratings[kv[0]] = Math.Clamp(value, 1, 5);
            }
        }

        return new ParsedReviewComment(string.IsNullOrWhiteSpace(commentPart) ? null : commentPart, ratings);
    }

    protected sealed record ReviewCategoryViewModel(string Label, int Stars);

    protected sealed record ReviewViewModel(
        string ReviewerRole,
        string RevieweeRole,
        int Stars,
        DateTime CreatedUtc,
        IReadOnlyList<ReviewCategoryViewModel> Categories);

    private sealed record ParsedReviewComment(string? Comment, Dictionary<string, int> Ratings);

    private enum ReviewTarget
    {
        Helper,
        Requester
    }

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
