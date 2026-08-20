namespace MhM.UI.Models;

public enum UserRole
{
    Privatperson = 1,
    Helfer = 2,
    Unternehmen = 3
}

public enum CompensationType
{
    Bezahlung = 1,
    Tausch = 2,
    Beides = 3
}

public enum ListingStatus
{
    Entwurf = 1,
    Offen = 2,
    InBearbeitung = 3,
    Abgeschlossen = 4,
    Storniert = 5
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public UserRole Role { get; set; } = UserRole.Privatperson;
    public bool IsVerified { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public HelperProfile? HelperProfile { get; set; }
    public ICollection<Listing> Listings { get; set; } = [];
}

public sealed class HelperProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Skills { get; set; } = string.Empty;
    public decimal? HourlyRate { get; set; }
    public int RadiusKm { get; set; } = 15;
    public bool OffersBarter { get; set; } = true;

    public AppUser User { get; set; } = default!;
}

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    public ICollection<Listing> Listings { get; set; } = [];
}

public sealed class Listing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequesterId { get; set; }
    public int CategoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? BudgetMin { get; set; }
    public decimal? BudgetMax { get; set; }
    public CompensationType CompensationType { get; set; } = CompensationType.Bezahlung;
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime? PreferredDateUtc { get; set; }
    public ListingStatus Status { get; set; } = ListingStatus.Offen;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public AppUser Requester { get; set; } = default!;
    public Category Category { get; set; } = default!;
    public ICollection<ListingApplication> Applications { get; set; } = [];
    public ICollection<Conversation> Conversations { get; set; } = [];
}

public sealed class ListingApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid ApplicantId { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal? ProposedPrice { get; set; }
    public CompensationType CompensationType { get; set; } = CompensationType.Bezahlung;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public Listing Listing { get; set; } = default!;
    public AppUser Applicant { get; set; } = default!;
}

public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid RequesterId { get; set; }
    public Guid HelperId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public Listing Listing { get; set; } = default!;
    public AppUser Requester { get; set; } = default!;
    public AppUser Helper { get; set; } = default!;
    public ICollection<Message> Messages { get; set; } = [];
}

public sealed class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid SenderUserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime SentUtc { get; set; } = DateTime.UtcNow;

    public Conversation Conversation { get; set; } = default!;
    public AppUser SenderUser { get; set; } = default!;
}

public sealed class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid ReviewerId { get; set; }
    public Guid RevieweeId { get; set; }
    public int Stars { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public Listing Listing { get; set; } = default!;
    public AppUser Reviewer { get; set; } = default!;
    public AppUser Reviewee { get; set; } = default!;
}