using MhM.UI.Data;
using MhM.UI.Data.Models;
using MhM.UI.Localization;
using MhM.UI.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

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

    protected override async Task OnParametersSetAsync()
    {
        isLoading = true;
        applyError = null;
        applySuccess = null;
        reviewError = null;
        reviewSuccess = null;

        await using var db = await DbFactory.CreateDbContextAsync();

        item = await db.Listings
            .Include(x => x.Category)
            .Include(x => x.Requester)
            .Include(x => x.Images)
            .FirstOrDefaultAsync(x => x.Id == ListingId);

        if (item is not null)
        {
            applicationCount = await db.ListingApplications
                .CountAsync(x => x.ListingId == item.Id);
        }

        await LoadCurrentUserAsync(db);
        await LoadReviewStateAsync(db);
        await LoadSubmittedReviewsAsync(db);

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

            appliedListingIds.Add(item.Id);
            applicationCount++;
            Navigation.NavigateTo("/mein?tab=applications&status=eingereicht");
        }
        finally
        {
            isApplying = false;
        }
    }

    protected Task SubmitHelperReviewAsync(ReviewSubmission submission)
        => SaveReviewAsync(submission, ReviewTarget.Helper);

    protected Task SubmitRequesterReviewAsync(ReviewSubmission submission)
        => SaveReviewAsync(submission, ReviewTarget.Requester);

    private async Task SaveReviewAsync(ReviewSubmission submission, ReviewTarget target)
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
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == item.Id);

        if (listing is null)
        {
            reviewError = "Auftrag nicht gefunden.";
            return;
        }

        if (listing.Status != ListingStatus.Abgeschlossen)
        {
            reviewError = "Bewertungen sind erst nach Auftragsbeendigung möglich.";
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
            existing.Stars = stars;
            existing.CreatedUtc = DateTime.UtcNow;

            existing.CategoryRatings.Clear();
            foreach (var category in categories)
            {
                existing.CategoryRatings.Add(new ReviewCategoryRating
                {
                    ReviewId = existing.Id,
                    CategoryKey = category.Key,
                    Stars = submission.Ratings[category.Key]
                });
            }
        }

        await db.SaveChangesAsync();

        if (target == ReviewTarget.Helper)
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

    private async Task LoadReviewStateAsync(MhMDbContext db)
    {
        canReviewHelper = false;
        canReviewRequester = false;
        hasReviewedHelper = false;
        hasReviewedRequester = false;
        acceptedHelperId = null;

        if (item is null || !currentUserId.HasValue || item.Status != ListingStatus.Abgeschlossen)
            return;

        acceptedHelperId = await db.ListingApplications
            .AsNoTracking()
            .Where(x => x.ListingId == item.Id && x.Status == ListingApplicationStatus.Angenommen)
            .Select(x => (Guid?)x.ApplicantId)
            .FirstOrDefaultAsync();

        if (!acceptedHelperId.HasValue)
            return;

        canReviewHelper = currentUserId.Value == item.RequesterId;
        canReviewRequester = currentUserId.Value == acceptedHelperId.Value;

        if (canReviewHelper)
        {
            hasReviewedHelper = await db.Reviews
                .AsNoTracking()
                .AnyAsync(x =>
                    x.ListingId == item.Id &&
                    x.ReviewerId == currentUserId.Value &&
                    x.RevieweeId == acceptedHelperId.Value);
        }

        if (canReviewRequester)
        {
            hasReviewedRequester = await db.Reviews
                .AsNoTracking()
                .AnyAsync(x =>
                    x.ListingId == item.Id &&
                    x.ReviewerId == currentUserId.Value &&
                    x.RevieweeId == item.RequesterId);
        }
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

    private static string ToStars(int stars)
    {
        var safeStars = Math.Clamp(stars, 0, 5);
        return new string('★', safeStars) + new string('☆', 5 - safeStars);
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