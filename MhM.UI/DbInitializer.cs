using Microsoft.EntityFrameworkCore;
using MhM.UI.Models;

namespace MhM.UI.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(MhMDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        if (await db.Categories.AnyAsync())
        {
            return;
        }

        var categories = new[]
        {
            new Category { Name = "Garten & Außenbereich", Slug = "garten" },
            new Category { Name = "Umzug & Transport", Slug = "umzug" },
            new Category { Name = "Computer & Technik", Slug = "technik" },
            new Category { Name = "Haushalt & Reinigung", Slug = "haushalt" },
            new Category { Name = "Seniorenhilfe", Slug = "seniorenhilfe" }
        };

        var requester = new AppUser
        {
            DisplayName = "Familie Becker",
            Email = "familie.becker@mhm.local",
            Phone = "0170 1234567",
            PostalCode = "97070",
            City = "Würzburg",
            Role = UserRole.Privatperson
        };

        var helperUser = new AppUser
        {
            DisplayName = "Max Hilft",
            Email = "max.hilft@mhm.local",
            Phone = "0171 555444",
            PostalCode = "97074",
            City = "Würzburg",
            Role = UserRole.Helfer,
            IsVerified = true
        };

        var helperProfile = new HelperProfile
        {
            User = helperUser,
            Title = "Zuverlässige Hilfe für Garten, Möbel und PC",
            Description = "Unterstützung bei kleinen Alltagsaufgaben in der Region.",
            Skills = "Gartenarbeit, Möbelaufbau, Computerhilfe",
            HourlyRate = 25m,
            RadiusKm = 20,
            OffersBarter = true
        };

        var listing = new Listing
        {
            Requester = requester,
            Category = categories[0],
            Title = "Hecke schneiden und Gartenweg säubern",
            Description = "Für unseren kleinen Garten wird Hilfe für ca. 3 Stunden gesucht.",
            BudgetMin = 50m,
            BudgetMax = 80m,
            CompensationType = CompensationType.Beides,
            PostalCode = "97070",
            City = "Würzburg",
            Status = ListingStatus.Offen,
            PreferredDateUtc = DateTime.UtcNow.AddDays(5)
        };

        db.Categories.AddRange(categories);
        db.Users.AddRange(requester, helperUser);
        db.HelperProfiles.Add(helperProfile);
        db.Listings.Add(listing);

        await db.SaveChangesAsync();
    }
}