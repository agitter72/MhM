using System.Security.Claims;
using MhM.UI.Data;
using MhM.UI.Data.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace MhM.UI.Components.Pages;
public partial class Moderation
{
    [Inject] protected IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;
    [Inject] protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    protected List<ModerationRow> listingReports=[]; protected List<ModerationRow> userReports=[];
    protected string selectedListingReason=string.Empty, selectedUserReason=string.Empty, listingSearch=string.Empty, userSearch=string.Empty, currentIdentityId=string.Empty;
    protected string? message; protected bool isError;
    protected IEnumerable<ModerationRow> FilteredListingReports => listingReports.Where(x => (string.IsNullOrWhiteSpace(selectedListingReason) || x.Report.Reason == selectedListingReason) && (string.IsNullOrWhiteSpace(listingSearch) || x.Listing is not null && (x.Listing.Title.Contains(listingSearch, StringComparison.CurrentCultureIgnoreCase) || x.Listing.Requester.DisplayName.Contains(listingSearch, StringComparison.CurrentCultureIgnoreCase) || x.Listing.City.Contains(listingSearch, StringComparison.CurrentCultureIgnoreCase))));
    protected IEnumerable<ModerationRow> FilteredUserReports => userReports.Where(x => (string.IsNullOrWhiteSpace(selectedUserReason) || x.Report.Reason == selectedUserReason) && (string.IsNullOrWhiteSpace(userSearch) || x.User is not null && (x.User.DisplayName.Contains(userSearch, StringComparison.CurrentCultureIgnoreCase) || x.User.Username.Contains(userSearch, StringComparison.CurrentCultureIgnoreCase))));
    protected override async Task OnInitializedAsync() => await ReloadAsync();
    private async Task ReloadAsync()
    {
        currentIdentityId=(await AuthenticationStateProvider.GetAuthenticationStateAsync()).User.FindFirstValue(ClaimTypes.NameIdentifier)??string.Empty;
        await using var db=await DbFactory.CreateDbContextAsync();
        var reports=await db.ContentReports.Include(x=>x.ReporterUser).Where(x=>x.Status!=ReportStatus.Erledigt&&x.Status!=ReportStatus.Abgelehnt).OrderBy(x=>x.CreatedUtc).ToListAsync();
        var listingIds=reports.Where(x=>x.TargetType==ReportTargetType.Auftrag).Select(x=>x.TargetId).Distinct().ToList();
        var listings=await db.Listings.AsNoTracking().Include(x=>x.Requester).Where(x=>listingIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id);
        listingReports=reports.Where(x=>x.TargetType==ReportTargetType.Auftrag).Select(x=>new ModerationRow(x,listings.GetValueOrDefault(x.TargetId),null)).ToList();
        var userIds=reports.Where(x=>x.TargetType==ReportTargetType.Nutzer).Select(x=>x.TargetId).Distinct().ToList();
        var users=await db.AppUsers.AsNoTracking().Where(x=>userIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id);
        userReports=reports.Where(x=>x.TargetType==ReportTargetType.Nutzer).Select(x=>new ModerationRow(x,null,users.GetValueOrDefault(x.TargetId))).ToList();
    }
    protected async Task ResolveReportAsync(ContentReport report)=>await ExecuteAsync(async()=>{await using var db=await DbFactory.CreateDbContextAsync();var entity=await db.ContentReports.FirstAsync(x=>x.Id==report.Id);entity.Status=ReportStatus.Erledigt;entity.ResolvedUtc=DateTime.UtcNow;entity.ResolvedByIdentityUserId=currentIdentityId;AddAudit(db,"ReportResolved",entity.Id.ToString(),$"{entity.TargetType}:{entity.TargetId}");await db.SaveChangesAsync();},"Meldung wurde erledigt.");
    protected async Task DeleteListingAsync(ModerationRow row)
    {
        var reason=row.DeleteReason.Trim();if(reason.Length==0){message="Die Begründung für den Auftraggeber ist ein Pflichtfeld.";isError=true;return;}
        await ExecuteAsync(async()=>{await using var db=await DbFactory.CreateDbContextAsync();var listing=await db.Listings.FirstOrDefaultAsync(x=>x.Id==row.Report.TargetId)??throw new InvalidOperationException();listing.Status=ListingStatus.Storniert;db.UserNotifications.Add(new UserNotification{Type=UserNotificationType.Moderation,RecipientUserId=listing.RequesterId,ListingId=listing.Id,Title="Auftrag durch Moderation entfernt",Content=$"Dein Auftrag ‚{listing.Title}‘ wurde entfernt. Begründung: {reason}",LinkUrl=$"/auftraege/{listing.Id}"});var related=await db.ContentReports.Where(x=>x.TargetType==ReportTargetType.Auftrag&&x.TargetId==listing.Id&&x.Status!=ReportStatus.Erledigt&&x.Status!=ReportStatus.Abgelehnt).ToListAsync();foreach(var report in related){report.Status=ReportStatus.Erledigt;report.ResolvedUtc=DateTime.UtcNow;report.ResolvedByIdentityUserId=currentIdentityId;}AddAudit(db,"ListingDeleted",listing.Id.ToString(),reason,"Listing");await db.SaveChangesAsync();},"Der Auftrag wurde entfernt und der Auftraggeber benachrichtigt.");
    }
    private async Task ExecuteAsync(Func<Task> action,string success){try{await action();message=success;isError=false;await ReloadAsync();}catch{message="Die Aktion konnte nicht ausgeführt werden.";isError=true;}}
    private void AddAudit(MhMDbContext db,string action,string targetId,string details,string targetType="Moderation")=>db.AdminAuditLogs.Add(new AdminAuditLog{ActorIdentityUserId=currentIdentityId,Action=action,TargetType=targetType,TargetId=targetId,Details=details});
    protected sealed class ModerationRow(ContentReport report,Listing? listing,AppUser? user){public ContentReport Report{get;}=report;public Listing? Listing{get;}=listing;public AppUser? User{get;}=user;public bool ShowDeleteForm{get;set;}public string DeleteReason{get;set;}=string.Empty;}
}
