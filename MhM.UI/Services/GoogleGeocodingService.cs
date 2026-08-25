using System.Text.Json;
using System.Text.Json.Serialization;

namespace MhM.UI.Services;

public sealed class GoogleGeocodingService(HttpClient httpClient, IConfiguration configuration) : IGeocodingService
{
    public async Task<(double Latitude, double Longitude)?> TryGeocodeAsync(
        string postalCode,
        string city,
        CancellationToken cancellationToken = default)
    {

        //dotnet user-secrets set "GoogleMaps:ApiKey" "YOUR_KEY" --project ".\MhM.UI\MhM.UI.csproj"
        var apiKey = configuration["GoogleMaps:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var address = $"{postalCode} {city}, Deutschland";
        var url =
            $"geocode/json?address={Uri.EscapeDataString(address)}&language=de&region=de&key={Uri.EscapeDataString(apiKey)}";

        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<GoogleGeocodeResponse>(stream, cancellationToken: cancellationToken);

        if (payload is null || payload.Status != "OK" || payload.Results.Count == 0)
        {
            return null;
        }

        var loc = payload.Results[0].Geometry.Location;
        return (loc.Lat, loc.Lng);
    }

    private sealed class GoogleGeocodeResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("results")]
        public List<GoogleGeocodeResult> Results { get; set; } = [];
    }

    private sealed class GoogleGeocodeResult
    {
        [JsonPropertyName("geometry")]
        public GoogleGeometry Geometry { get; set; } = new();
    }

    private sealed class GoogleGeometry
    {
        [JsonPropertyName("location")]
        public GoogleLocation Location { get; set; } = new();
    }

    private sealed class GoogleLocation
    {
        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lng")]
        public double Lng { get; set; }
    }
}