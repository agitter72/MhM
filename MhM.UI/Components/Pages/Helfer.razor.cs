using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using MhM.UI.Data;
using MhM.UI.Data.Models;
using MhM.UI.Localization;

namespace MhM.UI.Components.Pages;
public partial class Helfer : IAsyncDisposable
{
    private const int BatchSize=10;
    [Inject] protected IDbContextFactory<MhMDbContext> DbFactory{get;set;}=default!;
    [Inject] protected UiLocalizer T{get;set;}=default!;
    [Inject] protected IJSRuntime JS{get;set;}=default!;
    [CascadingParameter] protected Task<AuthenticationState> AuthenticationStateTask{get;set;}=default!;
    protected List<HelperProfile>? items; protected AppUser? currentUser; protected HelperProfile? currentHelperProfile; protected string search=string.Empty; protected int visibleCount=BatchSize; protected ElementReference loadMoreSentinel;
    private DotNetObjectReference<Helfer>? dotNetReference;
    private readonly Dictionary<Guid,HelperStats> stats=[];
    protected readonly record struct HelperStats(double? Rating,int ReviewCount,int AcceptedCount);
    protected HelperStats GetStats(Guid userId)=>stats.TryGetValue(userId,out var value)?value:default;
    protected static IReadOnlyList<string> GetSkills(HelperProfile profile)=>profile.Skills.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
    private IEnumerable<HelperProfile> FilteredItems=>items is null?[]:string.IsNullOrWhiteSpace(search)?items:items.Where(x=>x.User.DisplayName.Contains(search,StringComparison.CurrentCultureIgnoreCase)||x.User.Username.Contains(search,StringComparison.CurrentCultureIgnoreCase)||x.User.City.Contains(search,StringComparison.CurrentCultureIgnoreCase)||x.Skills.Contains(search,StringComparison.CurrentCultureIgnoreCase));
    protected int FilteredCount=>FilteredItems.Count(); protected List<HelperProfile> VisibleItems=>FilteredItems.Take(visibleCount).ToList(); protected bool HasMoreItems=>visibleCount<FilteredCount;
    protected override async Task OnInitializedAsync()=>await LoadAsync();
    protected override async Task OnAfterRenderAsync(bool firstRender){if(HasMoreItems){dotNetReference??=DotNetObjectReference.Create(this);await JS.InvokeVoidAsync("infiniteScroll.observe",loadMoreSentinel,dotNetReference);}}
    protected void FilterHelpers(ChangeEventArgs args){search=args.Value?.ToString()??string.Empty;visibleCount=BatchSize;}
    [JSInvokable] public Task LoadMoreAsync(){visibleCount=Math.Min(visibleCount+BatchSize,FilteredCount);StateHasChanged();return Task.CompletedTask;}
    private async Task LoadAsync()
    {
        await using var db=await DbFactory.CreateDbContextAsync();
        items=await db.HelperProfiles.AsNoTracking().Include(x=>x.User).Where(x=>x.User.Role==UserRole.Helfer).OrderBy(x=>x.User.City).ThenBy(x=>x.User.DisplayName).ToListAsync();
        await LoadStatsAsync(db,items.Select(x=>x.UserId).ToList());
        var authState=await AuthenticationStateTask;if(authState.User.Identity?.IsAuthenticated!=true)return;var identityId=authState.User.FindFirstValue(ClaimTypes.NameIdentifier);if(string.IsNullOrWhiteSpace(identityId))return;
        currentUser=await db.AppUsers.Include(x=>x.HelperProfile).FirstOrDefaultAsync(x=>x.IdentityUserId==identityId);currentHelperProfile=currentUser?.HelperProfile;
    }

    private async Task LoadStatsAsync(MhMDbContext db,List<Guid> helperIds)
    {
        stats.Clear();
        if(helperIds.Count==0)return;
        var visibilityCutoff=DateTime.UtcNow.AddDays(-14);
        var reviews=await db.Reviews.AsNoTracking()
            .Where(x=>helperIds.Contains(x.RevieweeId)
                &&(x.CreatedUtc<=visibilityCutoff||db.Reviews.Any(other=>other.ListingId==x.ListingId&&other.ReviewerId==x.RevieweeId&&other.RevieweeId==x.ReviewerId))
                &&db.ListingApplications.Any(a=>a.ListingId==x.ListingId&&a.ApplicantId==x.RevieweeId&&a.Status==ListingApplicationStatus.Angenommen))
            .GroupBy(x=>x.RevieweeId)
            .Select(g=>new{UserId=g.Key,Average=g.Average(x=>(double)x.Stars),Count=g.Count()})
            .ToListAsync();
        var accepted=await db.ListingApplications.AsNoTracking()
            .Where(x=>helperIds.Contains(x.ApplicantId)&&x.Status==ListingApplicationStatus.Angenommen)
            .GroupBy(x=>x.ApplicantId)
            .Select(g=>new{UserId=g.Key,Count=g.Count()})
            .ToListAsync();
        var acceptedByUser=accepted.ToDictionary(x=>x.UserId,x=>x.Count);
        var reviewsByUser=reviews.ToDictionary(x=>x.UserId,x=>(x.Average,x.Count));
        foreach(var id in helperIds.Distinct())
        {
            var hasReviews=reviewsByUser.TryGetValue(id,out var review);
            stats[id]=new HelperStats(hasReviews?review.Average:null,hasReviews?review.Count:0,acceptedByUser.GetValueOrDefault(id));
        }
    }

    public async ValueTask DisposeAsync(){if(dotNetReference is null)return;try{await JS.InvokeVoidAsync("infiniteScroll.disconnect");}catch(JSDisconnectedException){}dotNetReference.Dispose();}
}
