using MhM.UI.Data;
using MhM.UI.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace MhM.UI.Services;

public sealed record UserNotificationListItem(
    Guid Id,
    UserNotificationType Type,
    string Title,
    string Content,
    string LinkUrl,
    DateTime CreatedUtc,
    DateTime? DeliveredUtc,
    DateTime? ReadUtc,
    string? SenderDisplayName,
    Guid ListingId,
    Guid? ConversationId,
    Guid? MessageId);

public interface INotificationService
{
    Task CreateAssignmentNotificationAsync(
        Guid listingId,
        Guid conversationId,
        Guid requesterUserId,
        Guid helperUserId,
        string requesterName,
        string listingTitle,
        CancellationToken cancellationToken = default);

    Task CreateChatNotificationAsync(
        Guid listingId,
        Guid conversationId,
        Guid messageId,
        Guid senderUserId,
        Guid recipientUserId,
        string senderName,
        string listingTitle,
        string messageContent,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserNotificationListItem>> GetRecentNotificationsAsync(
        Guid recipientUserId,
        int take = 10,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserNotificationListItem>> GetUndeliveredNotificationsAsync(
        Guid recipientUserId,
        int take = 10,
        CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default);

    Task MarkAsDeliveredAsync(
        Guid recipientUserId,
        IReadOnlyCollection<Guid> notificationIds,
        CancellationToken cancellationToken = default);

    Task MarkListingAsReadAsync(
        Guid listingId,
        Guid recipientUserId,
        CancellationToken cancellationToken = default);

    Task MarkConversationMessagesAsReadAsync(
        Guid conversationId,
        Guid recipientUserId,
        CancellationToken cancellationToken = default);
}

public sealed class NotificationService(IDbContextFactory<MhMDbContext> dbFactory) : INotificationService
{
    public async Task CreateAssignmentNotificationAsync(
        Guid listingId,
        Guid conversationId,
        Guid requesterUserId,
        Guid helperUserId,
        string requesterName,
        string listingTitle,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.UserNotifications.Add(new UserNotification
        {
            Type = UserNotificationType.Auftragsvergabe,
            RecipientUserId = helperUserId,
            SenderUserId = requesterUserId,
            ListingId = listingId,
            ConversationId = conversationId,
            Title = "Auftrag vergeben",
            Content = $"{requesterName} hat dir den Auftrag \"{listingTitle}\" vergeben.",
            LinkUrl = $"/auftraege/{listingId}",
            CreatedUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CreateChatNotificationAsync(
        Guid listingId,
        Guid conversationId,
        Guid messageId,
        Guid senderUserId,
        Guid recipientUserId,
        string senderName,
        string listingTitle,
        string messageContent,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        db.UserNotifications.Add(new UserNotification
        {
            Type = UserNotificationType.ChatNachricht,
            RecipientUserId = recipientUserId,
            SenderUserId = senderUserId,
            ListingId = listingId,
            ConversationId = conversationId,
            MessageId = messageId,
            Title = $"Neue Nachricht zu {listingTitle}",
            Content = $"{senderName}: {TrimPreview(messageContent, 140)}",
            LinkUrl = $"/auftraege/{listingId}",
            CreatedUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserNotificationListItem>> GetRecentNotificationsAsync(
        Guid recipientUserId,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await CreateBaseQuery(db, recipientUserId)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(Math.Max(1, take))
            .Select(ProjectNotification())
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserNotificationListItem>> GetUndeliveredNotificationsAsync(
        Guid recipientUserId,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await CreateBaseQuery(db, recipientUserId)
            .Where(x => x.DeliveredUtc == null)
            .OrderBy(x => x.CreatedUtc)
            .Take(Math.Max(1, take))
            .Select(ProjectNotification())
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetUnreadCountAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await CreateBaseQuery(db, recipientUserId)
            .CountAsync(x => x.ReadUtc == null, cancellationToken);
    }

    public async Task MarkAsDeliveredAsync(
        Guid recipientUserId,
        IReadOnlyCollection<Guid> notificationIds,
        CancellationToken cancellationToken = default)
    {
        if (notificationIds.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var deliveredUtc = DateTime.UtcNow;

        var notifications = await db.UserNotifications
            .Where(x => x.RecipientUserId == recipientUserId && notificationIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var messageIdsToDeliver = new HashSet<Guid>();

        foreach (var notification in notifications)
        {
            if (!notification.DeliveredUtc.HasValue)
            {
                notification.DeliveredUtc = deliveredUtc;
            }

            if (notification.MessageId.HasValue)
            {
                messageIdsToDeliver.Add(notification.MessageId.Value);
            }
        }

        if (messageIdsToDeliver.Count > 0)
        {
            var messages = await db.Messages
                .Where(x => messageIdsToDeliver.Contains(x.Id) && x.RecipientUserId == recipientUserId && x.DeliveredUtc == null)
                .ToListAsync(cancellationToken);

            foreach (var message in messages)
            {
                message.DeliveredUtc = deliveredUtc;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkListingAsReadAsync(
        Guid listingId,
        Guid recipientUserId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var notifications = await db.UserNotifications
            .Where(x => x.ListingId == listingId && x.RecipientUserId == recipientUserId && x.ReadUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            notification.DeliveredUtc ??= now;
            notification.ReadUtc = now;
        }

        var conversations = await db.Conversations
            .Where(x => x.ListingId == listingId && (x.RequesterId == recipientUserId || x.HelperId == recipientUserId))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (conversations.Count > 0)
        {
            var messages = await db.Messages
                .Where(x => conversations.Contains(x.ConversationId) && x.RecipientUserId == recipientUserId && x.ReadUtc == null)
                .ToListAsync(cancellationToken);

            foreach (var message in messages)
            {
                message.DeliveredUtc ??= now;
                message.ReadUtc = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkConversationMessagesAsReadAsync(
        Guid conversationId,
        Guid recipientUserId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var messages = await db.Messages
            .Where(x => x.ConversationId == conversationId && x.RecipientUserId == recipientUserId && x.ReadUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.DeliveredUtc ??= now;
            message.ReadUtc = now;
        }

        var notifications = await db.UserNotifications
            .Where(x => x.ConversationId == conversationId && x.RecipientUserId == recipientUserId && x.ReadUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            notification.DeliveredUtc ??= now;
            notification.ReadUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<UserNotification> CreateBaseQuery(MhMDbContext db, Guid recipientUserId)
        => db.UserNotifications
            .AsNoTracking()
            .Where(x => x.RecipientUserId == recipientUserId)
            .Include(x => x.SenderUser);

    private static Expression<Func<UserNotification, UserNotificationListItem>> ProjectNotification()
        => x => new UserNotificationListItem(
            x.Id,
            x.Type,
            x.Title,
            x.Content,
            x.LinkUrl,
            x.CreatedUtc,
            x.DeliveredUtc,
            x.ReadUtc,
            x.SenderUser != null ? x.SenderUser.DisplayName : null,
            x.ListingId,
            x.ConversationId,
            x.MessageId);

    private static string TrimPreview(string content, int maxLength)
    {
        var normalized = (content ?? string.Empty).Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 1)].TrimEnd() + "…";
    }
}
