using MhM.UI.Data;
using MhM.UI.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MhM.UI.Services;

public sealed class ListingImageSettings
{
    public int MaxCount { get; set; } = 20;
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024; // 5 MB
}

public interface IListingImageService
{
    Task<List<ListingImage>> GetImagesAsync(Guid listingId);
    Task<(bool Success, string? Error)> AddImageAsync(Guid listingId, Guid actingUserId, bool isAdmin, string fileName, string contentType, Stream data);
    Task DeleteImageAsync(Guid imageId, Guid actingUserId, bool isAdmin);
}

public sealed class ListingImageService(
    IDbContextFactory<MhMDbContext> dbFactory,
    ListingImageSettings settings) : IListingImageService
{
    private static readonly HashSet<string> AllowedContentTypes =
        ["image/jpeg", "image/png"];

    public async Task<List<ListingImage>> GetImagesAsync(Guid listingId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return await db.ListingImages
            .Where(x => x.ListingId == listingId)
            .OrderBy(x => x.UploadedUtc)
            .ToListAsync();
    }

    public async Task<(bool Success, string? Error)> AddImageAsync(
        Guid listingId, Guid actingUserId, bool isAdmin, string fileName, string contentType, Stream data)
    {
        if (contentType is null || !AllowedContentTypes.Contains(contentType.ToLowerInvariant()))
            return (false, "Ungültiger Dateityp. Erlaubt sind ausschließlich JPEG und PNG.");

        if (data.Length > settings.MaxFileSizeBytes)
            return (false, $"Die Datei ist zu groß. Maximal {settings.MaxFileSizeBytes / 1024 / 1024} MB erlaubt.");

        await using var db = await dbFactory.CreateDbContextAsync();

        var listing = await db.Listings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == listingId);
        if (listing is null)
            return (false, "Auftrag nicht gefunden.");
        if (!isAdmin && listing.RequesterId != actingUserId)
            return (false, "Du darfst die Bilder dieses Auftrags nicht verändern.");
        if (listing.Status is not ListingStatus.Entwurf and not ListingStatus.Offen)
            return (false, "Bilder können nach der Vergabe nicht mehr verändert werden.");

        var count = await db.ListingImages.CountAsync(x => x.ListingId == listingId);
        if (count >= settings.MaxCount)
            return (false, $"Maximal {settings.MaxCount} Bilder pro Auftrag erlaubt.");

        using var ms = new MemoryStream();
        await data.CopyToAsync(ms);
        var bytes = ms.ToArray();
        if (!ProfileImageSecurity.TryValidate(bytes, out var detectedContentType, out var validationError) ||
            !ProfileImageSecurity.TryRemoveMetadata(bytes, detectedContentType, out var sanitized))
            return (false, validationError.Length == 0 ? "Das Bild konnte nicht sicher verarbeitet werden." : validationError);

        db.ListingImages.Add(new ListingImage
        {
            ListingId = listingId,
            FileName = $"{Guid.NewGuid():N}{(detectedContentType == "image/png" ? ".png" : ".jpg")}",
            ContentType = detectedContentType,
            Data = sanitized,
            UploadedUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task DeleteImageAsync(Guid imageId, Guid actingUserId, bool isAdmin)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var image = await db.ListingImages.Include(x => x.Listing).FirstOrDefaultAsync(x => x.Id == imageId);
        if (image is not null)
        {
            if (!isAdmin && image.Listing.RequesterId != actingUserId)
                throw new UnauthorizedAccessException("Du darfst dieses Bild nicht löschen.");
            if (image.Listing.Status is not ListingStatus.Entwurf and not ListingStatus.Offen)
                throw new InvalidOperationException("Bilder können nach der Vergabe nicht mehr verändert werden.");
            db.ListingImages.Remove(image);
            await db.SaveChangesAsync();
        }
    }
}
