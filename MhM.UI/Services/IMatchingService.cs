using MhM.UI.Data.Models;

namespace MhM.UI.Services;

public sealed record MatchCandidate(
    Guid ApplicantId,
    string ApplicantName,
    decimal? ProposedPrice,
    double? DistanceKm,
    double TotalScore,
    double DistanceScore,
    double RatingScore,
    double PriceScore,
    double ReliabilityScore,
    double VerificationScore);

public interface IMatchingService
{
    Task<IReadOnlyList<MatchCandidate>> GetTopCandidatesForListingAsync(
        Guid listingId,
        int top = 5,
        CancellationToken cancellationToken = default);

    Task AssignListingAsync(
        Guid listingId,
        Guid helperUserId,
        CancellationToken cancellationToken = default);

    Task CompleteListingByRequesterAsync(
        Guid listingId,
        Guid requesterUserId,
        CancellationToken cancellationToken = default);
}