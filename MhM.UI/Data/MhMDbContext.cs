using MhM.UI.Data.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MhM.UI.Data;

public class MhMDbContext : IdentityDbContext<ApplicationIdentityUser>
{
    public MhMDbContext(DbContextOptions<MhMDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<HelperProfile> HelperProfiles => Set<HelperProfile>();
    public DbSet<ProfileImage> ProfileImages => Set<ProfileImage>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingImage> ListingImages => Set<ListingImage>();   // NEU
    public DbSet<ListingApplication> ListingApplications => Set<ListingApplication>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewCategoryRating> ReviewCategoryRatings => Set<ReviewCategoryRating>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var users = modelBuilder.Entity<AppUser>();
        users.ToTable("Users");
        users.HasKey(x => x.Id);
        users.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        users.Property(x => x.IdentityUserId).HasMaxLength(450);
        users.Property(x => x.Username).HasMaxLength(30).IsRequired();
        users.Property(x => x.NormalizedUsername).HasMaxLength(30).IsRequired();
        users.Property(x => x.Description).HasMaxLength(500).IsRequired();
        users.Property(x => x.Email).HasMaxLength(256).IsRequired();
        users.Property(x => x.Phone).HasMaxLength(50);
        users.Property(x => x.PostalCode).HasMaxLength(20).IsRequired();
        users.Property(x => x.City).HasMaxLength(120).IsRequired();
        users.HasIndex(x => x.Email).IsUnique();
        users.HasIndex(x => x.IdentityUserId).IsUnique().HasFilter("[IdentityUserId] IS NOT NULL");
        users.HasIndex(x => x.NormalizedUsername).IsUnique();

        var profileImages = modelBuilder.Entity<ProfileImage>();
        profileImages.ToTable("ProfileImages");
        profileImages.HasKey(x => x.Id);
        profileImages.Property(x => x.ContentType).HasMaxLength(50).IsRequired();
        profileImages.Property(x => x.Data).IsRequired();
        profileImages.HasIndex(x => x.UserId).IsUnique();
        profileImages.HasOne(x => x.User)
            .WithOne(x => x.ProfileImage)
            .HasForeignKey<ProfileImage>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        var helperProfiles = modelBuilder.Entity<HelperProfile>();
        helperProfiles.ToTable("HelperProfiles");
        helperProfiles.HasKey(x => x.Id);
        helperProfiles.Property(x => x.Title).HasMaxLength(160).IsRequired();
        helperProfiles.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        helperProfiles.Property(x => x.Skills).HasMaxLength(1000);
        helperProfiles.Property(x => x.HourlyRate).HasPrecision(10, 2);
        helperProfiles.HasOne(x => x.User)
            .WithOne(x => x.HelperProfile)
            .HasForeignKey<HelperProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        var categories = modelBuilder.Entity<Category>();
        categories.ToTable("Categories");
        categories.HasKey(x => x.Id);
        categories.Property(x => x.Name).HasMaxLength(100).IsRequired();
        categories.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        categories.HasIndex(x => x.Slug).IsUnique();

        var listings = modelBuilder.Entity<Listing>();
        listings.ToTable("Listings");
        listings.HasKey(x => x.Id);
        listings.Property(x => x.Title).HasMaxLength(160).IsRequired();
        listings.Property(x => x.Description).HasMaxLength(3000).IsRequired();
        listings.Property(x => x.BudgetMin).HasPrecision(10, 2);
        listings.Property(x => x.BudgetMax).HasPrecision(10, 2);
        listings.Property(x => x.PostalCode).HasMaxLength(20).IsRequired();
        listings.Property(x => x.City).HasMaxLength(120).IsRequired();
        listings.HasOne(x => x.Requester)
            .WithMany(x => x.Listings)
            .HasForeignKey(x => x.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);
        listings.HasOne(x => x.Category)
            .WithMany(x => x.Listings)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // NEU: ListingImages
        var images = modelBuilder.Entity<ListingImage>();
        images.ToTable("ListingImages");
        images.HasKey(x => x.Id);
        images.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        images.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        images.Property(x => x.Data).IsRequired();
        images.HasOne(x => x.Listing)
            .WithMany(x => x.Images)
            .HasForeignKey(x => x.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

        var applications = modelBuilder.Entity<ListingApplication>();
        applications.ToTable("ListingApplications");
        applications.HasKey(x => x.Id);
        applications.Property(x => x.Message).HasMaxLength(1500).IsRequired();
        applications.Property(x => x.ProposedPrice).HasPrecision(10, 2);

        applications.Property(x => x.Status)
            .HasConversion<int>()
            .HasDefaultValue(ListingApplicationStatus.Eingereicht)
            .IsRequired();

        applications.HasIndex(x => new { x.ListingId, x.ApplicantId }).IsUnique();

        applications.HasOne(x => x.Listing)
            .WithMany(x => x.Applications)
            .HasForeignKey(x => x.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

        applications.HasOne(x => x.Applicant)
            .WithMany()
            .HasForeignKey(x => x.ApplicantId)
            .OnDelete(DeleteBehavior.Restrict);

        var conversations = modelBuilder.Entity<Conversation>();
        conversations.ToTable("Conversations");
        conversations.HasKey(x => x.Id);
        conversations.HasIndex(x => new { x.ListingId, x.RequesterId, x.HelperId }).IsUnique();
        conversations.HasOne(x => x.Listing)
            .WithMany(x => x.Conversations)
            .HasForeignKey(x => x.ListingId)
            .OnDelete(DeleteBehavior.Cascade);
        conversations.HasOne(x => x.Requester)
            .WithMany()
            .HasForeignKey(x => x.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);
        conversations.HasOne(x => x.Helper)
            .WithMany()
            .HasForeignKey(x => x.HelperId)
            .OnDelete(DeleteBehavior.Restrict);

        var messages = modelBuilder.Entity<Message>();
        messages.ToTable("Messages");
        messages.HasKey(x => x.Id);
        messages.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        messages.HasIndex(x => new { x.ConversationId, x.SentUtc });
        messages.HasIndex(x => new { x.RecipientUserId, x.ReadUtc, x.DeliveredUtc });
        messages.HasOne(x => x.Conversation)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        messages.HasOne(x => x.SenderUser)
            .WithMany()
            .HasForeignKey(x => x.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);
        messages.HasOne(x => x.RecipientUser)
            .WithMany()
            .HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        var notifications = modelBuilder.Entity<UserNotification>();
        notifications.ToTable("UserNotifications");
        notifications.HasKey(x => x.Id);
        notifications.Property(x => x.Type)
            .HasConversion<int>()
            .IsRequired();
        notifications.Property(x => x.Title).HasMaxLength(160).IsRequired();
        notifications.Property(x => x.Content).HasMaxLength(2000).IsRequired();
        notifications.Property(x => x.LinkUrl).HasMaxLength(512).IsRequired();
        notifications.HasIndex(x => new { x.RecipientUserId, x.ReadUtc, x.DeliveredUtc, x.CreatedUtc });
        notifications.HasIndex(x => x.MessageId)
            .IsUnique()
            .HasFilter("[MessageId] IS NOT NULL");
        notifications.HasOne(x => x.RecipientUser)
            .WithMany()
            .HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);
        notifications.HasOne(x => x.SenderUser)
            .WithMany()
            .HasForeignKey(x => x.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);
        notifications.HasOne(x => x.Listing)
            .WithMany()
            .HasForeignKey(x => x.ListingId)
            .OnDelete(DeleteBehavior.Restrict);
        notifications.HasOne(x => x.Conversation)
            .WithMany(x => x.Notifications)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.SetNull);
        notifications.HasOne(x => x.Message)
            .WithMany(x => x.Notifications)
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Restrict);

        var reviews = modelBuilder.Entity<Review>();
        reviews.ToTable("Reviews");
        reviews.HasKey(x => x.Id);
        reviews.ToTable(t => t.HasCheckConstraint("CK_Reviews_Stars", "[Stars] >= 1 AND [Stars] <= 5"));

        reviews.HasOne(x => x.Listing)
            .WithMany()
            .HasForeignKey(x => x.ListingId)
            .OnDelete(DeleteBehavior.Cascade);
        reviews.HasOne(x => x.Reviewer)
            .WithMany()
            .HasForeignKey(x => x.ReviewerId)
            .OnDelete(DeleteBehavior.Restrict);
        reviews.HasOne(x => x.Reviewee)
            .WithMany()
            .HasForeignKey(x => x.RevieweeId)
            .OnDelete(DeleteBehavior.Restrict);

        var reviewCategoryRatings = modelBuilder.Entity<ReviewCategoryRating>();
        reviewCategoryRatings.ToTable("ReviewCategoryRatings");
        reviewCategoryRatings.HasKey(x => x.Id);
        reviewCategoryRatings.Property(x => x.CategoryKey).HasMaxLength(64).IsRequired();
        reviewCategoryRatings.Property(x => x.Stars).IsRequired();
        reviewCategoryRatings.ToTable(t => t.HasCheckConstraint("CK_ReviewCategoryRatings_Stars", "[Stars] >= 1 AND [Stars] <= 5"));
        reviewCategoryRatings.HasIndex(x => new { x.ReviewId, x.CategoryKey }).IsUnique();

        reviewCategoryRatings.HasOne(x => x.Review)
            .WithMany(x => x.CategoryRatings)
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ApplicationIdentityUser>(entity =>
        {
            entity.Property(u => u.FirstName).HasMaxLength(100);
            entity.Property(u => u.LastName).HasMaxLength(100);
            entity.HasIndex(u => u.Email).IsUnique();
        });
    }
}
