using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using MhM.UI.Data;
using MhM.UI.Localization;
using MhM.UI.Data.Models;
using MhM.UI.Services;

namespace MhM.UI.Components.Pages;

public partial class Auftrag
{
    [Parameter]
    public Guid? Id { get; set; }

    [Inject]
    protected MhMDbContext Db { get; set; } = default!;

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

    protected readonly ListingFormModel model = new();
    protected List<Category> categories = [];
    protected List<AppUser> requesters = [];
    protected List<ListingImage> existingImages = [];
    protected bool isLoading = true;
    protected string? loadError;
    protected string? saveError;
    protected string? imageUploadError;

    protected int maxImages =>
        Configuration.GetSection("ListingImages").GetValue<int?>("MaxCount") ?? 20;

    protected bool IsEditMode => Id.HasValue;
    protected string CurrentFormName => IsEditMode ? "edit-listing-form" : "create-listing-form";

    protected override async Task OnParametersSetAsync()
    {
        isLoading = true;
        loadError = null;
        saveError = null;
        imageUploadError = null;

        categories = await Db.Categories
            .OrderBy(x => x.Name)
            .ToListAsync();

        requesters = await Db.Users
            .OrderBy(x => x.DisplayName)
            .ToListAsync();

        if (IsEditMode)
        {
            var listing = await Db.Listings.FirstOrDefaultAsync(x => x.Id == Id!.Value);

            if (listing is null)
            {
                loadError = T["TaskEdit.LoadError"];
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
            model.RequesterId = requesters.FirstOrDefault()?.Id;
            model.CategoryId = categories.FirstOrDefault()?.Id ?? 0;
            model.Title = string.Empty;
            model.Description = string.Empty;
            model.BudgetMin = null;
            model.BudgetMax = null;
            model.CompensationType = CompensationType.Beides;
            model.PostalCode = string.Empty;
            model.City = string.Empty;
            model.Status = ListingStatus.Offen;
            model.PreferredDateLocal = DateTime.Today.AddDays(3);
            existingImages = [];
        }

        isLoading = false;
    }

    protected async Task OnImagesSelectedAsync(InputFileChangeEventArgs e)
    {
        imageUploadError = null;
        var remaining = maxImages - existingImages.Count;
        var files = e.GetMultipleFiles(remaining);

        foreach (var file in files)
        {
            using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
            var (success, error) = await ImageService.AddImageAsync(
                Id!.Value, file.Name, file.ContentType, stream);

            if (!success)
            {
                imageUploadError = error;
                break;
            }
        }

        existingImages = await ImageService.GetImagesAsync(Id!.Value);
    }

    protected async Task DeleteImageAsync(Guid imageId)
    {
        await ImageService.DeleteImageAsync(imageId);
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

        // Geokoordinaten immer beim Speichern neu ermitteln
        double? latitude = null;
        double? longitude = null;
        var maybeCoords = await Geocoding.TryGeocodeAsync(model.PostalCode.Trim(), model.City.Trim());
        if (maybeCoords.HasValue)
        {
            latitude = Math.Round(maybeCoords.Value.Latitude, 6);
            longitude = Math.Round(maybeCoords.Value.Longitude, 6);
        }

        Listing entity;

        if (IsEditMode)
        {
            entity = await Db.Listings.FirstAsync(x => x.Id == Id!.Value);
        }
        else
        {
            entity = new Listing { CreatedUtc = DateTime.UtcNow };
            await Db.Listings.AddAsync(entity);
        }

        entity.RequesterId = model.RequesterId!.Value;
        entity.CategoryId = model.CategoryId;
        entity.Title = model.Title.Trim();
        entity.Description = model.Description.Trim();
        entity.BudgetMin = model.BudgetMin;
        entity.BudgetMax = model.BudgetMax;
        entity.CompensationType = model.CompensationType;
        entity.PostalCode = model.PostalCode.Trim();
        entity.City = model.City.Trim();
        entity.Status = model.Status;
        entity.PreferredDateUtc = model.PreferredDateLocal?.ToUniversalTime();
        entity.Latitude = latitude;
        entity.Longitude = longitude;

        await Db.SaveChangesAsync();
        Navigation.NavigateTo("/auftraege");
    }

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