namespace MhM.UI.Services;

public interface IGeocodingService
{
    Task<(double Latitude, double Longitude)?> TryGeocodeAsync(
        string postalCode,
        string city,
        CancellationToken cancellationToken = default);
}