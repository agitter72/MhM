using MhM.UI.Components;
using MhM.UI.Data;
using MhM.UI.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder
    .Services.AddLocalization();
builder.Services.AddSingleton<UiLocalizer>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

//builder.Services.AddDbContext<MhMDbContext>(options =>
//    options.UseSqlServer(
//        builder.Configuration.GetConnectionString("MhM")
//        ?? throw new InvalidOperationException("Connection string 'MhM' was not found.")));

builder.Services.AddDbContext<MhMDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("MhM")
        ?? throw new InvalidOperationException("Connection string 'MhM' was not found.");

    var sqlConnection = new Microsoft.Data.SqlClient.SqlConnection(connectionString)
    {
        AccessToken = new Azure.Identity.DefaultAzureCredential().GetToken(
            new Azure.Core.TokenRequestContext(new[] { "https://database.windows.net/.default" })).Token
    };

    options.UseSqlServer(sqlConnection);
});

// Add ASP.NET Core Identity
builder.Services.AddIdentity<ApplicationIdentityUser, IdentityRole>(options =>
    {
        // Password settings
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;

        // Lockout settings
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;

        // User settings
        options.User.RequireUniqueEmail = true;

        // Sign-in settings
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<MhMDbContext>()
    .AddDefaultTokenProviders();

// Configure Cookie Authentication
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
    options.AccessDeniedPath = "/access-denied";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.Cookie.Name = "MhM.Auth";
    options.Cookie.IsEssential = true;
});

var supportedCultures = new[]
{
    new CultureInfo("de-DE"),
    new CultureInfo("en-US")
};

var requestLocalizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("de-DE"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
};

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// DB: Migration and Seeding
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<MhMDbContext>();
        context.Database.Migrate();
        if (!app.Environment.IsProduction())
        {
            await DbInitializer.InitializeAsync(context);
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"Fehler beim Migrieren/Seeding: {e.Message}");
    }
}
app.UseRequestLocalization(requestLocalizationOptions);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/culture/set", (string culture, string? redirectUri, HttpContext httpContext) =>
{
    var supportedCultureNames = supportedCultures
        .Select(x => x.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (!supportedCultureNames.Contains(culture))
    {
        culture = "de-DE";
    }

    var requestCulture = new RequestCulture(culture);

    httpContext.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(requestCulture),
        new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            SameSite = SameSiteMode.Lax
        });

    var target = string.IsNullOrWhiteSpace(redirectUri) || !Uri.IsWellFormedUriString(redirectUri, UriKind.Relative)
        ? "/"
        : redirectUri;

    return Results.LocalRedirect(target);
});

app.MapPost("/account/logon", async (HttpContext context, UserManager<ApplicationIdentityUser> userManager, SignInManager<ApplicationIdentityUser> signInManager) =>
{
    var form = await context.Request.ReadFromJsonAsync<MhM.UI.Models.LoginModel>();

    if (form == null)
    {
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        await context.Response.WriteAsync("No user data given");
        return;
    }

    var user = await userManager.FindByEmailAsync(form.Email);

    if (user == null )
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("User not found");
        return;
    }

    if (user.IsActive == false)
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("User not active");
        return;
    }

    var result = await signInManager.PasswordSignInAsync(user, form.Password, form.RememberMe, lockoutOnFailure: false);
    if (result.Succeeded)
    {
        await context.Response.WriteAsync("OK");
    }
    else
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Invalid credentials");
    }
});

app.MapGet("account/logoff", async (HttpContext context, SignInManager<ApplicationIdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    //await context.Response.WriteAsync("OK");
    context.Response.Redirect("/");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
