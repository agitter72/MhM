using MhM.UI.Data.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MhM.UI.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(MhMDbContext db)
    {
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

        // Create requesters
        var requesters = Enumerable.Range(1, 12)
            .Select(i => new AppUser
            {
                DisplayName = $"Anfrageperson {i}",
                Email = $"anfrage{i}@mhm.local",
                Phone = $"0170 1000{i:000}",
                PostalCode = $"970{70 + (i % 10)}",
                City = "Würzburg",
                Role = UserRole.Privatperson
            })
            .ToList();

        // Keep one helper profile for demo data
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
            Title = "Zuverlässige Hilfe für Alltag, Haushalt und Technik",
            Description = "Unterstützung bei kleinen Alltagsaufgaben in der Region.",
            Skills = "Gartenarbeit, Möbelaufbau, Computerhilfe, Transport",
            HourlyRate = 25m,
            RadiusKm = 20,
            OffersBarter = true
        };

        var titles = new[]
        {
            "Kurzfristige Hilfe gesucht",
            "Unterstützung am Wochenende",
            "Einmalige Hilfeleistung benötigt",
            "Wer kann zeitnah helfen?",
            "Dringende Unterstützung gesucht"
        };

        var descriptions = new[]
        {
            "Die Aufgabe soll in den nächsten Tagen erledigt werden.",
            "Bitte nur melden, wenn Erfahrung vorhanden ist.",
            "Werkzeug kann teilweise gestellt werden.",
            "Zeitfenster ist flexibel, Details nach Absprache.",
            "Faire Bezahlung und freundlicher Kontakt."
        };

        var random = new Random(42);
        var listings = new List<Listing>();

        for (var i = 1; i <= 50; i++)
        {
            var category = categories[random.Next(categories.Length)];
            var requester = requesters[random.Next(requesters.Count)];
            var budgetMin = random.Next(25, 120);
            var budgetMax = budgetMin + random.Next(20, 100);

            listings.Add(new Listing
            {
                Requester = requester,
                Category = category,
                Title = $"{titles[random.Next(titles.Length)]} ({category.Name}) #{i}",
                Description = descriptions[random.Next(descriptions.Length)],
                BudgetMin = budgetMin,
                BudgetMax = budgetMax,
                CompensationType = (CompensationType)random.Next(1, 4),
                PostalCode = $"970{random.Next(70, 80)}",
                City = "Würzburg",
                Status = ListingStatus.Offen,
                PreferredDateUtc = DateTime.UtcNow.AddDays(random.Next(1, 30))
            });
        }

        db.Categories.AddRange(categories);
        db.AppUsers.AddRange(requesters);
        db.AppUsers.Add(helperUser);
        db.HelperProfiles.Add(helperProfile);
        db.Listings.AddRange(listings);

        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            throw;
        }
    }
}
