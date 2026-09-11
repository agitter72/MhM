using MhM.UI.Data;
using MhM.UI.Data.Models;
using MhM.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace MhM.UI.Components.Pages;

public partial class AuftragDetail : IAsyncDisposable
{
    [Inject]
    protected INotificationService NotificationService { get; set; } = default!;

    protected readonly ChatInputModel chatInput = new();
    protected readonly List<ChatMessageViewModel> chatMessages = [];
    protected bool canUseChat;
    protected bool isSendingChatMessage;
    protected string? chatError;
    protected Guid? conversationId;
    protected string chatParticipantName = string.Empty;
    protected string? currentUserDisplayName;

    private PeriodicTimer? chatRefreshTimer;
    private CancellationTokenSource? chatRefreshCts;
    private Task? chatRefreshTask;

    protected string GetChatFormName(Guid id) => $"chat-conversation-{id:N}";

    protected bool IsOwnMessage(ChatMessageViewModel message)
        => currentUserId.HasValue && message.SenderUserId == currentUserId.Value;

    protected string GetMessageStateText(ChatMessageViewModel message)
    {
        if (message.ReadUtc.HasValue)
        {
            return $"Gelesen {message.ReadUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}";
        }

        if (message.DeliveredUtc.HasValue)
        {
            return $"Zugestellt {message.DeliveredUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}";
        }

        return "Gesendet";
    }

    private async Task LoadChatStateAsync(MhMDbContext db)
    {
        canUseChat = false;
        conversationId = null;
        chatParticipantName = string.Empty;
        chatMessages.Clear();

        if (item is null || !currentUserId.HasValue || !acceptedHelperId.HasValue)
        {
            return;
        }

        var isRequester = currentUserId.Value == item.RequesterId;
        var isAcceptedHelper = currentUserId.Value == acceptedHelperId.Value;

        if (!isRequester && !isAcceptedHelper)
        {
            return;
        }

        var conversation = await db.Conversations
            .AsNoTracking()
            .Include(x => x.Requester)
            .Include(x => x.Helper)
            .FirstOrDefaultAsync(x =>
                x.ListingId == item.Id &&
                x.RequesterId == item.RequesterId &&
                x.HelperId == acceptedHelperId.Value);

        if (conversation is null)
        {
            return;
        }

        canUseChat = true;
        conversationId = conversation.Id;
        chatParticipantName = isRequester
            ? conversation.Helper.DisplayName
            : conversation.Requester.DisplayName;

        var messages = await db.Messages
            .AsNoTracking()
            .Where(x => x.ConversationId == conversation.Id)
            .Include(x => x.SenderUser)
            .OrderBy(x => x.SentUtc)
            .ToListAsync();

        chatMessages.AddRange(messages.Select(x => new ChatMessageViewModel(
            x.Id,
            x.SenderUserId,
            x.RecipientUserId,
            x.SenderUser.DisplayName,
            x.Content,
            x.SentUtc,
            x.DeliveredUtc,
            x.ReadUtc)));
    }

    private async Task EnsureListingNotificationsReadAsync(CancellationToken cancellationToken = default)
    {
        if (item is null || !currentUserId.HasValue || !canUseChat)
        {
            return;
        }

        await NotificationService.MarkListingAsReadAsync(item.Id, currentUserId.Value, cancellationToken);
    }

    private async Task RestartChatRefreshLoopAsync()
    {
        await StopChatRefreshLoopAsync();

        if (!canUseChat || item is null || !currentUserId.HasValue)
        {
            return;
        }

        chatRefreshCts = new CancellationTokenSource();
        chatRefreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(8));
        chatRefreshTask = RunChatRefreshLoopAsync(item.Id, chatRefreshTimer, chatRefreshCts.Token);
    }

    private async Task RunChatRefreshLoopAsync(Guid listingId, PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(async () =>
                {
                    if (item?.Id != listingId || !canUseChat)
                    {
                        return;
                    }

                    await RefreshChatAsync(cancellationToken);
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshChatAsync(CancellationToken cancellationToken = default)
    {
        if (item is null)
        {
            return;
        }

        await using var db = await DbFactory.CreateDbContextAsync(cancellationToken);
        await LoadChatStateAsync(db);
        await EnsureListingNotificationsReadAsync(cancellationToken);
    }

    private async Task StopChatRefreshLoopAsync()
    {
        var timer = chatRefreshTimer;
        var cts = chatRefreshCts;
        var task = chatRefreshTask;

        chatRefreshTimer = null;
        chatRefreshCts = null;
        chatRefreshTask = null;

        if (cts is not null)
        {
            await cts.CancelAsync();
        }

        timer?.Dispose();
        cts?.Dispose();

        if (task is not null)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    protected async Task SendChatMessageAsync()
    {
        if (item is null || !currentUserId.HasValue || !conversationId.HasValue || isSendingChatMessage)
        {
            return;
        }

        var trimmedContent = chatInput.Message.Trim();
        if (string.IsNullOrWhiteSpace(trimmedContent))
        {
            chatError = "Bitte eine Nachricht eingeben.";
            return;
        }

        isSendingChatMessage = true;
        chatError = null;

        try
        {
            await using var db = await DbFactory.CreateDbContextAsync();

            var conversation = await db.Conversations
                .AsNoTracking()
                .Include(x => x.Requester)
                .Include(x => x.Helper)
                .FirstOrDefaultAsync(x => x.Id == conversationId.Value && x.ListingId == item.Id);

            if (conversation is null)
            {
                canUseChat = false;
                chatError = "Chat ist aktuell nicht verfügbar.";
                return;
            }

            if (currentUserId.Value != conversation.RequesterId && currentUserId.Value != conversation.HelperId)
            {
                chatError = "Du bist nicht Teil dieses Chats.";
                return;
            }

            var recipientUserId = currentUserId.Value == conversation.RequesterId
                ? conversation.HelperId
                : conversation.RequesterId;

            var senderName = currentUserDisplayName ??
                (currentUserId.Value == conversation.RequesterId
                    ? conversation.Requester.DisplayName
                    : conversation.Helper.DisplayName);

            var message = new Message
            {
                ConversationId = conversation.Id,
                SenderUserId = currentUserId.Value,
                RecipientUserId = recipientUserId,
                Content = trimmedContent,
                SentUtc = DateTime.UtcNow
            };

            db.Messages.Add(message);
            await db.SaveChangesAsync();

            await NotificationService.CreateChatNotificationAsync(
                item.Id,
                conversation.Id,
                message.Id,
                currentUserId.Value,
                recipientUserId,
                senderName,
                item.Title,
                trimmedContent);

            chatInput.Message = string.Empty;
            await RefreshChatAsync();
        }
        finally
        {
            isSendingChatMessage = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopChatRefreshLoopAsync();
    }

    protected sealed record ChatMessageViewModel(
        Guid Id,
        Guid SenderUserId,
        Guid RecipientUserId,
        string SenderDisplayName,
        string Content,
        DateTime SentUtc,
        DateTime? DeliveredUtc,
        DateTime? ReadUtc);

    protected sealed class ChatInputModel
    {
        [Required(ErrorMessage = "Bitte eine Nachricht eingeben.")]
        [StringLength(4000, ErrorMessage = "Die Nachricht darf maximal 4000 Zeichen lang sein.")]
        public string Message { get; set; } = string.Empty;
    }
}
