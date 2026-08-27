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
    Task<(bool Success, string? Error)> AddImageAsync(Guid listingId, string fileName, string contentType, Stream data);
    Task DeleteImageAsync(Guid imageId);
}

public sealed class ListingImageService(
    IDbContextFactory<MhMDbContext> dbFactory,
    ListingImageSettings settings) : IListingImageService
{
    private static readonly HashSet<string> AllowedContentTypes =
        ["image/jpeg", "image/png", "image/gif", "image/bmp"];

    public async Task<List<ListingImage>> GetImagesAsync(Guid listingId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return await db.ListingImages
            .Where(x => x.ListingId == listingId)
            .OrderBy(x => x.UploadedUtc)
            .ToListAsync();
    }

    public async Task<(bool Success, string? Error)> AddImageAsync(
        Guid listingId, string fileName, string contentType, Stream data)
    {
        if (!AllowedContentTypes.Contains(contentType.ToLowerInvariant()))
            return (false, "Ungültiger Dateityp. Erlaubt: jpg, png, gif, bmp.");

        if (data.Length > settings.MaxFileSizeBytes)
            return (false, $"Die Datei ist zu groß. Maximal {settings.MaxFileSizeBytes / 1024 / 1024} MB erlaubt.");

        await using var db = await dbFactory.CreateDbContextAsync();

        var count = await db.ListingImages.CountAsync(x => x.ListingId == listingId);
        if (count >= settings.MaxCount)
            return (false, $"Maximal {settings.MaxCount} Bilder pro Auftrag erlaubt.");

        using var ms = new MemoryStream();
        await data.CopyToAsync(ms);

        db.ListingImages.Add(new ListingImage
        {
            ListingId = listingId,
            FileName = Path.GetFileName(fileName),
            ContentType = contentType.ToLowerInvariant(),
            Data = ms.ToArray(),
            UploadedUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task DeleteImageAsync(Guid imageId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var image = await db.ListingImages.FindAsync(imageId);
        if (image is not null)
        {
            db.ListingImages.Remove(image);
            await db.SaveChangesAsync();
        }
    }
}