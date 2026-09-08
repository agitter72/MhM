using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using MhM.UI.Data;
using MhM.UI.Data.Models;
using MhM.UI.Localization;

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

    protected override async Task OnParametersSetAsync()
    {
        isLoading = true;
        applyError = null;
        applySuccess = null;

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
        SeedApplicationModel();
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
            applySuccess = "Bewerbung erfolgreich gesendet.";
            applicationCount++;

            model.Message = string.Empty;
            model.ProposedPrice = null;
        }
        finally
        {
            isApplying = false;
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

    private void SeedApplicationModel()
    {
        applicationModels.Clear();

        if (item is null)
            return;

        applicationModels[item.Id] = new ListingApplicationInputModel
        {
            CompensationType = item.CompensationType
        };
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