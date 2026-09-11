using MhM.UI.Data;
using MhM.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace MhM.UI.Components.Layout;

public partial class MainLayout : IAsyncDisposable
{
    [Inject]
    protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected INotificationService NotificationService { get; set; } = default!;

    [Inject]
    protected IJSRuntime JS { get; set; } = default!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    protected readonly List<UserNotificationListItem> notificationItems = [];
    protected int unreadNotificationCount;
    protected bool isNotificationCenterOpen;
    protected ElementReference notificationCenterElement;

    private Guid? currentAppUserId;
    private PeriodicTimer? notificationRefreshTimer;
    private CancellationTokenSource? notificationRefreshCts;
    private Task? notificationRefreshTask;
    private bool navigationSubscribed;
    private DotNetObjectReference<MainLayout>? notificationOutsideClickReference;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        Navigation.LocationChanged += HandleLocationChanged;
        navigationSubscribed = true;

        await JS.InvokeVoidAsync("appNotifications.requestPermission");
        await RefreshNotificationsAsync();
        await StartNotificationPollingAsync();
        await InvokeAsync(StateHasChanged);
    }

    protected async Task ToggleNotificationCenterAsync()
    {
        isNotificationCenterOpen = !isNotificationCenterOpen;

        if (isNotificationCenterOpen)
        {
            await RefreshNotificationsAsync();
            notificationOutsideClickReference ??= DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("popover.registerOutsideClick", notificationCenterElement, notificationOutsideClickReference);
        }
        else
        {
            await JS.InvokeVoidAsync("popover.unregisterOutsideClick");
        }
    }

    protected async Task CloseNotificationCenterAsync()
    {
        isNotificationCenterOpen = false;
        await JS.InvokeVoidAsync("popover.unregisterOutsideClick");
    }

    [JSInvokable]
    public Task CloseNotificationCenterFromOutsideAsync()
    {
        if (!isNotificationCenterOpen)
        {
            return Task.CompletedTask;
        }

        isNotificationCenterOpen = false;
        return InvokeAsync(StateHasChanged);
    }

    protected static string GetNotificationTypeLabel(UserNotificationListItem notification)
        => notification.Type switch
        {
            Data.Models.UserNotificationType.Auftragsvergabe => "Vergabe",
            Data.Models.UserNotificationType.ChatNachricht => "Chat",
            _ => "Info"
        };

    protected static string FormatNotificationTimestamp(DateTime createdUtc)
        => createdUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    private async Task RefreshNotificationsAsync(CancellationToken cancellationToken = default)
    {
        if (!await TryLoadCurrentUserAsync(cancellationToken))
        {
            notificationItems.Clear();
            unreadNotificationCount = 0;
            return;
        }

        var recipientUserId = currentAppUserId!.Value;
        var recentNotifications = await NotificationService.GetRecentNotificationsAsync(recipientUserId, 12, cancellationToken);
        var undeliveredNotifications = await NotificationService.GetUndeliveredNotificationsAsync(recipientUserId, 12, cancellationToken);
        unreadNotificationCount = await NotificationService.GetUnreadCountAsync(recipientUserId, cancellationToken);

        notificationItems.Clear();
        notificationItems.AddRange(recentNotifications);

        if (undeliveredNotifications.Count == 0)
        {
            return;
        }

        foreach (var notification in undeliveredNotifications)
        {
            await JS.InvokeVoidAsync(
                "appNotifications.showIfAllowed",
                notification.Title,
                notification.Content,
                Navigation.ToAbsoluteUri(notification.LinkUrl).ToString(),
                notification.Id.ToString("N"));
        }

        await NotificationService.MarkAsDeliveredAsync(
            recipientUserId,
            undeliveredNotifications.Select(x => x.Id).ToArray(),
            cancellationToken);
    }

    private async Task<bool> TryLoadCurrentUserAsync(CancellationToken cancellationToken)
    {
        currentAppUserId = null;

        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var principal = authState.User;

        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var email = principal.Identity.Name?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        await using var db = await DbFactory.CreateDbContextAsync(cancellationToken);
        currentAppUserId = await db.AppUsers
            .AsNoTracking()
            .Where(x => x.Email == email)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return currentAppUserId.HasValue;
    }

    private async Task StartNotificationPollingAsync()
    {
        await StopNotificationPollingAsync();

        notificationRefreshCts = new CancellationTokenSource();
        notificationRefreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        notificationRefreshTask = RunNotificationPollingAsync(notificationRefreshTimer, notificationRefreshCts.Token);
    }

    private async Task RunNotificationPollingAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(async () =>
                {
                    await RefreshNotificationsAsync(cancellationToken);
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void HandleLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        isNotificationCenterOpen = false;
        _ = InvokeAsync(async () =>
        {
            await JS.InvokeVoidAsync("popover.unregisterOutsideClick");
            await RefreshNotificationsAsync();
            StateHasChanged();
        });
    }

    private async Task StopNotificationPollingAsync()
    {
        var timer = notificationRefreshTimer;
        var cts = notificationRefreshCts;
        var task = notificationRefreshTask;

        notificationRefreshTimer = null;
        notificationRefreshCts = null;
        notificationRefreshTask = null;

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

    public async ValueTask DisposeAsync()
    {
        if (navigationSubscribed)
        {
            Navigation.LocationChanged -= HandleLocationChanged;
            navigationSubscribed = false;
        }

        if (notificationOutsideClickReference is not null)
        {
            try
            {
                await JS.InvokeVoidAsync("popover.unregisterOutsideClick");
            }
            catch (JSDisconnectedException)
            {
            }

            notificationOutsideClickReference.Dispose();
            notificationOutsideClickReference = null;
        }

        await StopNotificationPollingAsync();
    }
}
