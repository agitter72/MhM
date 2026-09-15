using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using MhM.UI.Data;
using MhM.UI.Localization;
using MhM.UI.Data.Models;
using MhM.UI.Services;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace MhM.UI.Components.Pages;

public partial class Auftrag
{
    [Parameter]
    public Guid? Id { get; set; }

    [Inject]
    protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    [Inject]
    protected UiLocalizer T { get; set; } = default!;

    [Inject]
    protected IGeocodingService Geocoding { get; set; } = default!;

    [Inject]
    protected IListingImageService ImageService { get; set; } = default!;

    [Inject]
    protected IConfiguration Configuration { get; set; } = default!;
    [Inject] protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    protected readonly ListingFormModel model = new();
    protected List<Category> categories = [];
    protected List<AppUser> requesters = [];
    protected List<ListingImage> existingImages = [];
    protected List<PendingListingImage> pendingImages = [];
    protected bool isLoading = true;
    protected string? loadError;
    protected string? saveError;
    protected string? imageUploadError;
    private Guid currentUserId;
    protected bool isAdmin;

    protected int maxImages =>
        Configuration.GetSection("ListingImages").GetValue<int?>("MaxCount") ?? 20;

    protected bool IsEditMode => Id.HasValue;
    protected string CurrentFormName => IsEditMode ? "edit-listing-form" : "create-listing-form";
    protected IEnumerable<ListingStatus> EditableStatuses => model.Status is ListingStatus.Entwurf or ListingStatus.Offen
        ? [ListingStatus.Entwurf, ListingStatus.Offen]
        : [model.Status];

    protected override async Task OnParametersSetAsync()
    {
        isLoading = true;
        loadError = null;
        saveError = null;
        imageUploadError = null;
        pendingImages = [];

        await using var db = await DbFactory.CreateDbContextAsync();
        var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        var identityId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        isAdmin = principal.IsInRole(PlatformRoles.Admin);
        var currentUser = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.IdentityUserId == identityId);
        if (currentUser is null)
        {
            loadError = "Dein Plattformprofil konnte nicht geladen werden.";
            isLoading = false;
            return;
        }
        currentUserId = currentUser.Id;

        categories = await db.Categories
            .OrderBy(x => x.Name)
            .ToListAsync();

        if (IsEditMode)
        {
            var listing = await db.Listings.FirstOrDefaultAsync(x => x.Id == Id!.Value);

            if (listing is null)
            {
                loadError = T["TaskEdit.LoadError"];
                isLoading = false;
                return;
            }
            if (!isAdmin && listing.RequesterId != currentUserId)
            {
                loadError = "Du darfst diesen Auftrag nicht bearbeiten.";
                isLoading = false;
                return;
            }

            model.Id = listing.Id;
            model.RequesterId = listing.RequesterId;
            model.CategoryId = listing.CategoryId;
            model.Title = listing.Title;
            model.Description = listing.Description;
            model.BudgetMin = listing.BudgetMin;
            model.BudgetMax = listing.BudgetMax;
            model.CompensationType = listing.CompensationType;
            model.PostalCode = listing.PostalCode;
            model.City = listing.City;
            model.Status = listing.Status;
            model.PreferredDateLocal = listing.PreferredDateUtc?.ToLocalTime();

            existingImages = await ImageService.GetImagesAsync(Id!.Value);
        }
        else
        {
            model.Id = null;
            model.RequesterId = currentUserId;
            model.CategoryId = categories.FirstOrDefault()?.Id ?? 0;
            model.Title = string.Empty;
            model.Description = string.Empty;
            model.BudgetMin = null;
            model.BudgetMax = null;
            model.CompensationType = CompensationType.Beides;
            model.PostalCode = string.Empty;
            model.City = string.Empty;
            model.Status = ListingStatus.Entwurf;
            model.PreferredDateLocal = DateTime.Today.AddDays(3);
            existingImages = [];
        }

        isLoading = false;
    }

    protected async Task OnImagesSelectedAsync(InputFileChangeEventArgs e)
    {
        imageUploadError = null;
        var remaining = maxImages - existingImages.Count - pendingImages.Count;
        var selectedFiles = e.GetMultipleFiles(maxImages);
        if (selectedFiles.Count > remaining)
        {
            imageUploadError = $"Du kannst noch höchstens {remaining} Bild(er) hinzufügen.";
            return;
        }
        var files = selectedFiles;

        foreach (var file in files)
        {
            if (file.Size > 5 * 1024 * 1024 || file.ContentType is not ("image/jpeg" or "image/png"))
            {
                imageUploadError = "Erlaubt sind ausschließlich JPEG- und PNG-Bilder mit maximal 5 MB.";
                break;
            }

            if (IsEditMode)
            {
                using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
                var (success, error) = await ImageService.AddImageAsync(
                    Id!.Value, currentUserId, isAdmin, file.Name, file.ContentType, stream);
                if (!success) { imageUploadError = error; break; }
            }
            else
            {
                await using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                var bytes = memory.ToArray();
                if (!ProfileImageSecurity.TryValidate(bytes, out var detectedContentType, out var validationError) ||
                    !ProfileImageSecurity.TryRemoveMetadata(bytes, detectedContentType, out var sanitized))
                {
                    imageUploadError = string.IsNullOrWhiteSpace(validationError) ? "Das Bild konnte nicht sicher verarbeitet werden." : validationError;
                    break;
                }
                pendingImages.Add(new PendingListingImage(Guid.NewGuid(), file.Name, detectedContentType, sanitized));
            }
        }

        if (IsEditMode)
            existingImages = await ImageService.GetImagesAsync(Id!.Value);
    }

    protected void RemovePendingImage(Guid id) => pendingImages.RemoveAll(x => x.Id == id);

    protected async Task DeleteImageAsync(Guid imageId)
    {
        await ImageService.DeleteImageAsync(imageId, currentUserId, isAdmin);
        existingImages = await ImageService.GetImagesAsync(Id!.Value);
    }

    protected async Task SaveAsync()
    {
        saveError = null;

        if (model.BudgetMin.HasValue && model.BudgetMax.HasValue && model.BudgetMin > model.BudgetMax)
        {
            saveError = T["TaskEdit.BudgetRangeError"];
            return;
        }

        double? latitude = null;
        double? longitude = null;
        var maybeCoords = await Geocoding.TryGeocodeAsync(model.PostalCode.Trim(), model.City.Trim());
        if (maybeCoords.HasValue)
        {
            latitude = Math.Round(maybeCoords.Value.Latitude, 6);
            longitude = Math.Round(maybeCoords.Value.Longitude, 6);
        }

        await using var db = await DbFactory.CreateDbContextAsync();

        Listing entity;
        if (IsEditMode)
        {
            entity = await db.Listings.FirstAsync(x => x.Id == Id!.Value);
            if (!isAdmin && entity.RequesterId != currentUserId)
            {
                saveError = "Du darfst diesen Auftrag nicht bearbeiten.";
                return;
            }
            if (!isAdmin && entity.Status is not ListingStatus.Entwurf and not ListingStatus.Offen)
            {
                saveError = "Ein vergebener Auftrag kann nicht mehr einseitig geändert werden.";
                return;
            }
        }
        else
        {
            entity = new Listing { CreatedUtc = DateTime.UtcNow };
            await db.Listings.AddAsync(entity);
        }

        entity.RequesterId = IsEditMode ? entity.RequesterId : currentUserId;
        entity.CategoryId = model.CategoryId;
        entity.Title = model.Title.Trim();
        entity.Description = model.Description.Trim();
        entity.BudgetMin = model.BudgetMin;
        entity.BudgetMax = model.BudgetMax;
        entity.CompensationType = model.CompensationType;
        entity.PostalCode = model.PostalCode.Trim();
        entity.City = model.City.Trim();
        if (!IsEditMode || !isAdmin || entity.Status is ListingStatus.Entwurf or ListingStatus.Offen)
            entity.Status = model.Status is ListingStatus.Entwurf or ListingStatus.Offen ? model.Status : ListingStatus.Entwurf;
        entity.PreferredDateUtc = model.PreferredDateLocal?.ToUniversalTime();
        entity.Latitude = latitude;
        entity.Longitude = longitude;

        await db.SaveChangesAsync();

        foreach (var pending in pendingImages)
        {
            using var stream = new MemoryStream(pending.Data, writable: false);
            var (success, error) = await ImageService.AddImageAsync(
                entity.Id, currentUserId, isAdmin, pending.FileName, pending.ContentType, stream);
            if (!success)
            {
                saveError = $"Der Auftrag wurde gespeichert, aber ein Bild konnte nicht hinzugefügt werden: {error}";
                return;
            }
        }

        Navigation.NavigateTo($"/auftraege/{entity.Id}");
    }

    protected sealed record PendingListingImage(Guid Id, string FileName, string ContentType, byte[] Data);

    protected sealed class ListingFormModel
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "Bitte einen Auftraggeber auswählen.")]
        public Guid? RequesterId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Bitte eine Kategorie auswählen.")]
        public int CategoryId { get; set; }

        [Required(ErrorMessage = "Bitte einen Titel eingeben.")]
        [StringLength(160, MinimumLength = 5, ErrorMessage = "Der Titel muss zwischen 5 und 160 Zeichen lang sein.")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte eine Beschreibung eingeben.")]
        [StringLength(3000, MinimumLength = 20, ErrorMessage = "Die Beschreibung muss zwischen 20 und 3000 Zeichen lang sein.")]
        public string Description { get; set; } = string.Empty;

        [Range(typeof(decimal), "0", "999999", ErrorMessage = "Bitte ein gültiges Mindestbudget eingeben.")]
        public decimal? BudgetMin { get; set; }

        [Range(typeof(decimal), "0", "999999", ErrorMessage = "Bitte ein gültiges Maximalbudget eingeben.")]
        public decimal? BudgetMax { get; set; }

        public CompensationType CompensationType { get; set; } = CompensationType.Beides;

        [Required(ErrorMessage = "Bitte eine PLZ eingeben.")]
        [StringLength(20, ErrorMessage = "Die PLZ ist zu lang.")]
        public string PostalCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte einen Ort eingeben.")]
        [StringLength(120, ErrorMessage = "Der Ort ist zu lang.")]
        public string City { get; set; } = string.Empty;

        public DateTime? PreferredDateLocal { get; set; }

        public ListingStatus Status { get; set; } = ListingStatus.Offen;
    }
}
