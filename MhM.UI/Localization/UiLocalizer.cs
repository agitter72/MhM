using System.Globalization;

namespace MhM.UI.Localization;

public sealed class UiLocalizer
{
    private static readonly IReadOnlyDictionary<string, Translation> Texts = new Dictionary<string, Translation>
    {
        ["App.Title"] = new("Menschen helfen Menschen", "People Helping People"),
        ["General.Dashboard"] = new("Dashboard", "Dashboard"),
        ["General.Loading"] = new("Lade Daten...", "Loading data..."),
        ["General.Save"] = new("Speichern", "Save"),
        ["General.Cancel"] = new("Abbrechen", "Cancel"),
        ["General.Edit"] = new("Bearbeiten", "Edit"),
        ["General.Yes"] = new("Ja", "Yes"),
        ["General.No"] = new("Nein", "No"),
        ["General.Select"] = new("Bitte auswählen", "Please select"),
        ["General.Open"] = new("Offen", "Open"),
        ["General.Region"] = new("Region", "Region"),
        ["General.Location"] = new("Standort", "Location"),
        ["General.Budget"] = new("Budget", "Budget"),
        ["General.Date"] = new("Termin", "Date"),

        ["Nav.Platform"] = new("Plattform", "Platform"),
        ["Nav.Home"] = new("Dashboard", "Dashboard"),
        ["Nav.Listings"] = new("Aufträge", "Jobs"),
        ["Nav.NewListing"] = new("Neuer Auftrag", "New job"),
        ["Nav.Helpers"] = new("Helfer", "Helpers"),

        ["Layout.SidebarSubtitle"] = new("Regionales Dashboard", "Regional dashboard"),
        ["Layout.SidebarCardTitle"] = new("Schnell Hilfe finden", "Find help quickly"),
        ["Layout.SidebarCardText"] = new("Nachbarschaftshilfe, Kleinaufträge, Tausch und Bezahlung in einer Oberfläche.", "Neighborhood help, small jobs, barter and paid support in one interface."),
        ["Layout.CreateListing"] = new("Auftrag erstellen", "Create job"),
        ["Layout.TopbarEyebrow"] = new("Community Platform", "Community Platform"),
        ["Layout.TopbarTitle"] = new("MhM Control Center", "MhM Control Center"),
        ["Layout.Workspace"] = new("Workspace", "Workspace"),

        ["Culture.German"] = new("Deutsch", "German"),
        ["Culture.English"] = new("Englisch", "English"),

        ["Home.PageTitle"] = new("Menschen helfen Menschen", "People Helping People"),
        ["Home.Eyebrow"] = new("Nachbarschaftshilfe neu gedacht", "Neighborhood help reimagined"),
        ["Home.Headline"] = new("Regionale Hilfe für Alltag, Haus und Garten.", "Regional help for everyday life, home and garden."),
        ["Home.Description"] = new("Menschen, Familien und Helfer werden direkt miteinander verbunden. Kleinaufträge, Tauschgeschäfte und bezahlte Unterstützung in einer modernen Plattform.", "People, families and helpers are connected directly. Small jobs, barter deals and paid support in one modern platform."),
        ["Home.FindHelpers"] = new("Helfer finden", "Find helpers"),
        ["Home.OpenListings"] = new("Offene Aufträge", "Open jobs"),
        ["Home.ActiveHelpers"] = new("Aktive Helfer", "Active helpers"),
        ["Home.BarterOffers"] = new("Tauschangebote", "Barter offers"),
        ["Home.LiveFocus"] = new("Live Fokus", "Live focus"),
        ["Home.TopCategories"] = new("Top-Kategorien", "Top categories"),
        ["Home.CategoryGarden"] = new("Garten & Außenbereich", "Garden & outdoors"),
        ["Home.CategoryTech"] = new("Computer & Technik", "Computers & tech"),
        ["Home.CategoryHousehold"] = new("Haushalt & Reinigung", "Household & cleaning"),
        ["Home.CategorySenior"] = new("Seniorenhilfe", "Senior support"),
        ["Home.CoreFeatures"] = new("Kernfunktionen", "Core features"),
        ["Home.PlatformModules"] = new("Plattform-Module", "Platform modules"),
        ["Home.Profiles"] = new("Profile", "Profiles"),
        ["Home.ProfilesText"] = new("Registrierung für Suchende, Helfer und regionale Anbieter.", "Registration for seekers, helpers and local providers."),
        ["Home.Listings"] = new("Aufträge", "Jobs"),
        ["Home.ListingsText"] = new("Kleinaufträge einfach anlegen, verwalten und veröffentlichen.", "Create, manage and publish small jobs easily."),
        ["Home.Chat"] = new("Chat", "Chat"),
        ["Home.ChatText"] = new("Direkte Abstimmung für Rückfragen, Termine und Details.", "Direct coordination for questions, appointments and details."),
        ["Home.Reviews"] = new("Bewertungen", "Reviews"),
        ["Home.ReviewsText"] = new("Vertrauen durch Feedback und regionale Reputation.", "Trust through feedback and local reputation."),
        ["Home.BusinessModel"] = new("Geschäftsmodell", "Business model"),
        ["Home.RevenueStreams"] = new("Erlösquellen", "Revenue streams"),
        ["Home.PremiumProfiles"] = new("Premiumprofile", "Premium profiles"),
        ["Home.PremiumProfilesText"] = new("Sichtbarkeit für Helfer und Unternehmen", "Visibility for helpers and businesses"),
        ["Home.FeaturedAds"] = new("Hervorgehobene Anzeigen", "Featured ads"),
        ["Home.FeaturedAdsText"] = new("Mehr Reichweite für dringende Aufträge", "More reach for urgent jobs"),
        ["Home.Fees"] = new("Vermittlungsgebühren", "Placement fees"),
        ["Home.FeesText"] = new("Optionale Provision auf erfolgreiche Aufträge", "Optional fee for successful jobs"),
        ["Home.VerificationAds"] = new("Verifizierungen & Werbung", "Verification & advertising"),
        ["Home.VerificationAdsText"] = new("Zusatzumsätze für regionale Präsenz", "Additional revenue for local presence"),

        ["Listings.PageTitle"] = new("Aufträge", "Jobs"),
        ["Listings.Headline"] = new("Offene Aufträge", "Open jobs"),
        ["Listings.Empty"] = new("Keine offenen Aufträge vorhanden.", "No open jobs available."),
        ["Listings.New"] = new("Neuen Auftrag erstellen", "Create new job"),
        ["Listings.Category"] = new("Kategorie", "Category"),
        ["Listings.Requester"] = new("Auftraggeber", "Requester"),
        ["Listings.Location"] = new("Ort", "Location"),
        ["Listings.Compensation"] = new("Vergütung", "Compensation"),
        ["Listings.Status"] = new("Status", "Status"),
        ["Listings.Budget"] = new("Budget", "Budget"),

        ["Helpers.PageTitle"] = new("Helfer", "Helpers"),
        ["Helpers.Headline"] = new("Helfer in der Region", "Helpers in your area"),
        ["Helpers.Empty"] = new("Keine Helferprofile vorhanden.", "No helper profiles available."),
        ["Helpers.Name"] = new("Name", "Name"),
        ["Helpers.Skills"] = new("Skills", "Skills"),
        ["Helpers.Radius"] = new("Radius", "Radius"),
        ["Helpers.HourlyRate"] = new("Stundensatz", "Hourly rate"),
        ["Helpers.Barter"] = new("Tausch möglich", "Barter available"),

        ["TaskEdit.PageTitle.New"] = new("Auftrag erstellen", "Create job"),
        ["TaskEdit.PageTitle.Edit"] = new("Auftrag bearbeiten", "Edit job"),
        ["TaskEdit.LoadError"] = new("Der Auftrag wurde nicht gefunden.", "The job was not found."),
        ["TaskEdit.HeaderEyebrow"] = new("Auftragsverwaltung", "Job management"),
        ["TaskEdit.HeaderNew"] = new("Neuen Auftrag anlegen", "Create a new job"),
        ["TaskEdit.HeaderEdit"] = new("Bestehenden Auftrag bearbeiten", "Edit existing job"),
        ["TaskEdit.HeaderText"] = new("Erfasse alle wichtigen Angaben für Sichtbarkeit, Vergütung und Region.", "Enter all important details for visibility, compensation and region."),
        ["TaskEdit.BasicData"] = new("Grunddaten", "Basic data"),
        ["TaskEdit.Details"] = new("Auftragsdetails", "Job details"),
        ["TaskEdit.Requester"] = new("Auftraggeber", "Requester"),
        ["TaskEdit.Category"] = new("Kategorie", "Category"),
        ["TaskEdit.Title"] = new("Titel", "Title"),
        ["TaskEdit.Description"] = new("Beschreibung", "Description"),
        ["TaskEdit.CompensationSection"] = new("Vergütung", "Compensation"),
        ["TaskEdit.Terms"] = new("Konditionen", "Terms"),
        ["TaskEdit.BudgetFrom"] = new("Budget von", "Budget from"),
        ["TaskEdit.BudgetTo"] = new("Budget bis", "Budget to"),
        ["TaskEdit.Compensation"] = new("Vergütung", "Compensation"),
        ["TaskEdit.Status"] = new("Status", "Status"),
        ["TaskEdit.LocationSection"] = new("Standort", "Location"),
        ["TaskEdit.RegionDate"] = new("Region & Termin", "Region & date"),
        ["TaskEdit.PostalCode"] = new("PLZ", "Postal code"),
        ["TaskEdit.City"] = new("Ort", "City"),
        ["TaskEdit.PreferredDate"] = new("Wunschtermin", "Preferred date"),
        ["TaskEdit.Preview"] = new("Vorschau", "Preview"),
        ["TaskEdit.PreviewTitle"] = new("Auftragsvorschau", "Job preview"),
        ["TaskEdit.NoTitle"] = new("Noch kein Titel", "No title yet"),
        ["TaskEdit.NoDescription"] = new("Die Beschreibung erscheint hier als Vorschau.", "The description will appear here as a preview."),
        ["TaskEdit.BudgetRangeError"] = new("Das Mindestbudget darf nicht größer als das Maximalbudget sein.", "The minimum budget cannot be greater than the maximum budget."),

        ["CompensationType.Bezahlung"] = new("Bezahlung", "Paid"),
        ["CompensationType.Tausch"] = new("Tausch", "Barter"),
        ["CompensationType.Beides"] = new("Beides", "Both"),

        ["ListingStatus.Entwurf"] = new("Entwurf", "Draft"),
        ["ListingStatus.Offen"] = new("Offen", "Open"),
        ["ListingStatus.InBearbeitung"] = new("In Bearbeitung", "In progress"),
        ["ListingStatus.Abgeschlossen"] = new("Abgeschlossen", "Completed"),
        ["ListingStatus.Storniert"] = new("Storniert", "Cancelled")
    };

    public string this[string key] => Translate(key);

    public string Enum(Enum value) => Translate($"{value.GetType().Name}.{value}");

    private string Translate(string key)
    {
        if (!Texts.TryGetValue(key, out var translation))
        {
            return key;
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? translation.En
            : translation.De;
    }

    private sealed record Translation(string De, string En);
}