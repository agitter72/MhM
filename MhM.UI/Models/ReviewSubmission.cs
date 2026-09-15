namespace MhM.UI.Models;

public sealed class ReviewSubmission
{
    public Dictionary<string, int> Ratings { get; } = new(StringComparer.OrdinalIgnoreCase);

    public double AverageRating =>
        Ratings.Count == 0 ? 0 : Ratings.Values.Average();
}