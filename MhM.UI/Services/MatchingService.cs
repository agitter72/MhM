using MhM.UI.Data;
using MhM.UI.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace MhM.UI.Services;

public sealed class MatchingService(
    IDbContextFactory<MhMDbContext> dbFactory,
    INotificationService notificationService) : IMatchingService
{
    public async Task<IReadOnlyList<MatchCandidate>> GetTopCandidatesForListingAsync(
        Guid listingId,
        int top = 5,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var listing = await db.Listings
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == listingId, cancellationToken);

        if (listing is null || listing.Status != ListingStatus.Offen)
            return [];

        var applications = await db.ListingApplications
            .Where(x => x.ListingId == listingId && x.Status == ListingApplicationStatus.Eingereicht)
            .Include(x => x.Applicant)
                .ThenInclude(x => x.HelperProfile)
            .ToListAsync(cancellationToken);

        if (applications.Count == 0)
            return [];

        var applicantIds = applications
            .Select(x => x.ApplicantId)
            .Distinct()
            .ToList();

        var ratingByUser = await db.Reviews
            .Where(x => applicantIds.Contains(x.RevieweeId) &&
                (x.CreatedUtc <= DateTime.UtcNow.AddDays(-14) || db.Reviews.Any(other =>
                    other.ListingId == x.ListingId && other.ReviewerId == x.RevieweeId && other.RevieweeId == x.ReviewerId)))
            .GroupBy(x => x.RevieweeId)
            .Select(g => new { UserId = g.Key, AvgStars = g.Average(r => (double)r.Stars) })
            .ToDictionaryAsync(x => x.UserId, x => x.AvgStars, cancellationToken);

        var reliabilityByUser = await db.Conversations
            .Where(c => applicantIds.Contains(c.HelperId))
            .Join(
                db.Listings,
                c => c.ListingId,
                l => l.Id,
                (c, l) => new { c.HelperId, l.Status, l.Id })
            .GroupBy(x => x.HelperId)
            .Select(g => new
            {
                UserId = g.Key,
                Total = g.Select(x => x.Id).Distinct().Count(),
                Done = g.Where(x => x.Status == ListingStatus.Abgeschlossen).Select(x => x.Id).Distinct().Count()
            })
            .ToDictionaryAsync(
                x => x.UserId,
                x => x.Total == 0 ? 50d : (double)x.Done / x.Total * 100d,
                cancellationToken);

        var ranked = new List<MatchCandidate>();

        foreach (var app in applications)
        {
            if (!IsCompensationCompatible(listing.CompensationType, app.CompensationType))
                continue;

            var helper = app.Applicant;
            var profile = helper.HelperProfile;

            var distanceKm = TryGetDistanceKm(listing, helper);
            var maxRadiusKm = profile?.RadiusKm > 0 ? profile.RadiusKm : 50;

            if (distanceKm.HasValue && distanceKm.Value > maxRadiusKm)
                continue;

            var distanceScore = CalculateDistanceScore(distanceKm, maxRadiusKm);
            var ratingScore = CalculateRatingScore(ratingByUser.GetValueOrDefault(app.ApplicantId));
            var priceScore = CalculatePriceScore(listing.BudgetMin, listing.BudgetMax, app.ProposedPrice);
            var reliabilityScore = reliabilityByUser.GetValueOrDefault(app.ApplicantId, 50d);
            var verificationScore = helper.IsVerified ? 100d : 0d;

            var total =
                distanceScore * 0.35 +
                ratingScore * 0.25 +
                priceScore * 0.20 +
                reliabilityScore * 0.15 +
                verificationScore * 0.05;

            ranked.Add(new MatchCandidate(
                ApplicantId: app.ApplicantId,
                ApplicantName: helper.DisplayName,
                ProposedPrice: app.ProposedPrice,
                DistanceKm: distanceKm,
                TotalScore: Math.Round(total, 2),
                DistanceScore: Math.Round(distanceScore, 2),
                RatingScore: Math.Round(ratingScore, 2),
                PriceScore: Math.Round(priceScore, 2),
                ReliabilityScore: Math.Round(reliabilityScore, 2),
                VerificationScore: Math.Round(verificationScore, 2)));
        }

        return ranked
            .OrderByDescending(x => x.TotalScore)
            .ThenBy(x => x.DistanceKm ?? double.MaxValue)
            .Take(Math.Max(1, top))
            .ToList();
    }

    public async Task AssignListingAsync(
        Guid listingId,
        Guid helperUserId,
        Guid actingUserId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var retryDb = await dbFactory.CreateDbContextAsync(cancellationToken);
            await using var tx = await retryDb.Database.BeginTransactionAsync(cancellationToken);

            var listing = await retryDb.Listings
                .Include(x => x.Requester)
                .FirstOrDefaultAsync(x => x.Id == listingId, cancellationToken)
                ?? throw new InvalidOperationException("Auftrag nicht gefunden.");

            if (!isAdmin && listing.RequesterId != actingUserId)
                throw new UnauthorizedAccessException("Nur der Auftraggeber kann diesen Auftrag vergeben.");

            if (listing.Status != ListingStatus.Offen)
                throw new InvalidOperationException("Auftrag kann nicht vergeben werden.");

            var selectedApplication = await retryDb.ListingApplications
                .FirstOrDefaultAsync(
                    x => x.ListingId == listingId &&
                         x.ApplicantId == helperUserId &&
                         x.Status == ListingApplicationStatus.Eingereicht,
                    cancellationToken);

            if (selectedApplication is null)
                throw new InvalidOperationException("Keine eingereichte Bewerbung dieses Helfers vorhanden.");

            listing.Status = ListingStatus.Vergeben;
            selectedApplication.Status = ListingApplicationStatus.Angenommen;

            var agreementText = string.Join("|", listing.Id, listing.RequesterId, helperUserId, listing.Title,
                listing.Description, selectedApplication.ProposedPrice, selectedApplication.CompensationType,
                listing.PreferredDateUtc?.ToString("O"), listing.PostalCode, listing.City);
            retryDb.AssignmentAgreements.Add(new AssignmentAgreement
            {
                ListingId = listing.Id,
                RequesterId = listing.RequesterId,
                HelperId = helperUserId,
                Title = listing.Title,
                Description = listing.Description,
                AgreedPrice = selectedApplication.ProposedPrice,
                CompensationType = selectedApplication.CompensationType,
                PreferredDateUtc = listing.PreferredDateUtc,
                LocationSummary = $"{listing.PostalCode} {listing.City}".Trim(),
                AgreementHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(agreementText)))
            });

            var otherPendingApplications = await retryDb.ListingApplications
                .Where(x => x.ListingId == listingId &&
                            x.ApplicantId != helperUserId &&
                            x.Status == ListingApplicationStatus.Eingereicht)
                .ToListAsync(cancellationToken);

            foreach (var app in otherPendingApplications)
            {
                app.Status = ListingApplicationStatus.Abgelehnt;
            }

            var conversation = await retryDb.Conversations.FirstOrDefaultAsync(
                x => x.ListingId == listingId &&
                     x.RequesterId == listing.RequesterId &&
                     x.HelperId == helperUserId,
                cancellationToken);

            if (conversation is null)
            {
                conversation = new Conversation
                {
                    ListingId = listingId,
                    RequesterId = listing.RequesterId,
                    HelperId = helperUserId,
                    CreatedUtc = DateTime.UtcNow
                };
                retryDb.Conversations.Add(conversation);
            }

            retryDb.Messages.Add(new Message
            {
                Conversation = conversation,
                SenderUserId = listing.RequesterId,
                RecipientUserId = helperUserId,
                Content = $"Auftrag „{listing.Title}“ wurde vergeben. Bitte Details im Chat abstimmen.",
                SentUtc = DateTime.UtcNow
            });

            await retryDb.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            await notificationService.CreateAssignmentNotificationAsync(
                listingId,
                conversation.Id,
                listing.RequesterId,
                helperUserId,
                listing.Requester.DisplayName,
                listing.Title,
                cancellationToken);
        });
    }

    public async Task CompleteListingByRequesterAsync(
    Guid listingId,
    Guid requesterUserId,
    CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var retryDb = await dbFactory.CreateDbContextAsync(cancellationToken);
            await using var tx = await retryDb.Database.BeginTransactionAsync(cancellationToken);

            var listing = await retryDb.Listings
                .FirstOrDefaultAsync(x => x.Id == listingId, cancellationToken)
                ?? throw new InvalidOperationException("Auftrag nicht gefunden.");

            if (listing.RequesterId != requesterUserId)
                throw new InvalidOperationException("Nur der Auftraggeber kann den Auftrag abschließen.");

            if (listing.Status != ListingStatus.AbschlussGemeldet)
                throw new InvalidOperationException("Der Helfer muss den Abschluss zuerst melden.");

            if (listing.PreferredDateUtc.HasValue && DateTime.UtcNow < listing.PreferredDateUtc.Value)
                throw new InvalidOperationException(
                    $"Der Auftrag kann erst ab {listing.PreferredDateUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm} abgeschlossen werden.");

            var acceptedExists = await retryDb.ListingApplications
                .AnyAsync(x => x.ListingId == listingId && x.Status == ListingApplicationStatus.Angenommen, cancellationToken);

            if (!acceptedExists)
                throw new InvalidOperationException("Es wurde kein angenommener Helfer gefunden.");

            listing.Status = ListingStatus.Abgeschlossen;

            await retryDb.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });
    }


    public Task StartListingAsync(Guid listingId, Guid helperUserId, CancellationToken cancellationToken = default)
        => TransitionForHelperAsync(listingId, helperUserId, ListingStatus.Vergeben, ListingStatus.InDurchfuehrung, cancellationToken);

    public Task ReportCompletionAsync(Guid listingId, Guid helperUserId, CancellationToken cancellationToken = default)
        => TransitionForHelperAsync(listingId, helperUserId, ListingStatus.InDurchfuehrung, ListingStatus.AbschlussGemeldet, cancellationToken);

    public Task ConfirmCompletionAsync(Guid listingId, Guid requesterUserId, CancellationToken cancellationToken = default)
        => CompleteListingByRequesterAsync(listingId, requesterUserId, cancellationToken);

    private async Task TransitionForHelperAsync(Guid listingId, Guid helperUserId, ListingStatus expected, ListingStatus next, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var listing = await db.Listings.FirstOrDefaultAsync(x => x.Id == listingId, cancellationToken)
            ?? throw new InvalidOperationException("Auftrag nicht gefunden.");
        var isAccepted = await db.ListingApplications.AnyAsync(x => x.ListingId == listingId && x.ApplicantId == helperUserId && x.Status == ListingApplicationStatus.Angenommen, cancellationToken);
        if (!isAccepted)
            throw new UnauthorizedAccessException("Nur der angenommene Helfer darf diesen Schritt ausführen.");
        if (listing.Status != expected)
            throw new InvalidOperationException("Dieser Statuswechsel ist nicht zulässig.");
        listing.Status = next;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsCompensationCompatible(CompensationType listingType, CompensationType applicationType)
        => listingType == CompensationType.Beides ||
           applicationType == CompensationType.Beides ||
           listingType == applicationType;

    private static double? TryGetDistanceKm(Listing listing, AppUser helper)
    {
        if (!listing.Latitude.HasValue || !listing.Longitude.HasValue)
            return null;
        if (!helper.Latitude.HasValue || !helper.Longitude.HasValue)
            return null;

        return CalculateDistanceKm(
            listing.Latitude.Value,
            listing.Longitude.Value,
            helper.Latitude.Value,
            helper.Longitude.Value);
    }

    private static double CalculateDistanceScore(double? distanceKm, int radiusKm)
    {
        if (!distanceKm.HasValue)
            return 50d;

        var radius = Math.Max(1, radiusKm);
        if (distanceKm.Value >= radius)
            return 0d;

        return 100d * (1d - (distanceKm.Value / radius));
    }

    private static double CalculateRatingScore(double avgStars)
    {
        var stars = Math.Clamp(avgStars <= 0 ? 3.5 : avgStars, 1d, 5d);
        return (stars / 5d) * 100d;
    }

    private static double CalculatePriceScore(decimal? budgetMin, decimal? budgetMax, decimal? proposedPrice)
    {
        if (!proposedPrice.HasValue || (!budgetMin.HasValue && !budgetMax.HasValue))
            return 60d;

        var price = proposedPrice.Value;

        if (budgetMin.HasValue && budgetMax.HasValue && budgetMin <= budgetMax)
        {
            if (price >= budgetMin && price <= budgetMax)
                return 100d;

            if (price < budgetMin)
            {
                var diffRatio = budgetMin.Value == 0 ? 1m : (budgetMin.Value - price) / budgetMin.Value;
                return (double)Math.Max(0m, 100m - (diffRatio * 100m));
            }

            var upper = budgetMax.Value == 0 ? 1m : budgetMax.Value;
            var overRatio = (price - budgetMax.Value) / upper;
            return (double)Math.Max(0m, 100m - (overRatio * 100m));
        }

        if (budgetMin.HasValue && price < budgetMin.Value)
            return 70d;

        if (budgetMax.HasValue && price > budgetMax.Value)
            return 40d;

        return 80d;
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
}
