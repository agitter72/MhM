using MhM.UI.Components;
using MhM.UI.Data;
using MhM.UI.Localization;
using MhM.UI.Models;
using MhM.UI.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Console/debug logging works in local development, containers and App Service without
// requiring write access to the Windows Event Log.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

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

// DbContextFactory for Blazor Server best practices - use this in new/refactored code
var connectionString = builder.Configuration.GetConnectionString("MhM")
    ?? throw new InvalidOperationException("Connection string 'MhM' was not found.");

// Register Azure credential for Managed Identity authentication
var credential = new Azure.Identity.DefaultAzureCredential();

builder.Services.AddDbContextFactory<MhMDbContext>(options =>
{
    if (connectionString.StartsWith("Server=tcp:"))
    {
        // Use Azure SQL with Managed Identity - set up token provider
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            // Add connection resiliency
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        });

        // Configure Azure AD token provider for the connection
        options.AddInterceptors(new AzureAdAuthenticationDbConnectionInterceptor(credential));
    }
    else
    {
        // Use standard SQL Server connection
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        });
    }

    options.LogTo(_ => { }, LogLevel.None)
        .ConfigureWarnings(x =>
        {
            //x.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning); //zum Erkennen von Skip/Take ohne OrderBy-Errors
        });
    //.LogTo(Console.WriteLine, new[] { DbLoggerCategory.Database.Command.Name }, LogLevel.Debug)
    //.EnableSensitiveDataLogging()
}, ServiceLifetime.Singleton);

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

// Register HttpClient for geocoding service
builder.Services.AddHttpClient<IGeocodingService, GoogleGeocodingService>(client =>
{
    client.BaseAddress = new Uri("https://maps.googleapis.com/maps/api/");
});

builder.Services.Configure<ListingImageSettings>(
    builder.Configuration.GetSection("ListingImages"));
builder.Services.AddScoped<IListingImageService, ListingImageService>(sp =>
    new ListingImageService(
        sp.GetRequiredService<IDbContextFactory<MhMDbContext>>(),
        sp.GetRequiredService<IConfiguration>()
          .GetSection("ListingImages")
          .Get<ListingImageSettings>() ?? new ListingImageSettings()));

builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IMatchingService, MatchingService>();

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
        await context.Database.MigrateAsync();
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

    if (user == null)
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

app.MapPost("/account/profile", async (
    PersonalProfileUpdate profile,
    ClaimsPrincipal principal,
    IDbContextFactory<MhMDbContext> dbFactory,
    ILookupNormalizer normalizer,
    SignInManager<ApplicationIdentityUser> signInManager) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(profile, new ValidationContext(profile), validationResults, validateAllProperties: true))
    {
        return Results.Json(
            new ProfileUpdateResult(false, validationResults[0].ErrorMessage ?? "Bitte die Eingaben prüfen."),
            statusCode: StatusCodes.Status400BadRequest);
    }

    var identityUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrWhiteSpace(identityUserId))
    {
        return Results.Json(
            new ProfileUpdateResult(false, "Die Anmeldung ist abgelaufen. Bitte erneut anmelden."),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!InternationalPhone.TryNormalize(profile.Phone, out var normalizedPhone))
    {
        return Results.Json(
            new ProfileUpdateResult(false, "Bitte eine gültige internationale Telefonnummer eingeben."),
            statusCode: StatusCodes.Status400BadRequest);
    }

    await using var db = await dbFactory.CreateDbContextAsync();
    var identityUser = await db.Users.FirstOrDefaultAsync(x => x.Id == identityUserId);
    if (identityUser is null)
    {
        return Results.Json(new ProfileUpdateResult(false, "Benutzerkonto nicht gefunden."), statusCode: 404);
    }

    var originalEmail = identityUser.Email;
    var appUser = await db.AppUsers.FirstOrDefaultAsync(x => x.Email == originalEmail);
    if (appUser is null)
    {
        return Results.Json(new ProfileUpdateResult(false, "AppUser-Profil nicht gefunden."), statusCode: 404);
    }

    var email = profile.Email.Trim();
    var normalizedEmail = normalizer.NormalizeEmail(email);
    var emailInUse = await db.Users.AnyAsync(x => x.Id != identityUserId && x.NormalizedEmail == normalizedEmail)
        || await db.AppUsers.AnyAsync(x => x.Id != appUser.Id && x.Email == email);
    if (emailInUse)
    {
        return Results.Json(
            new ProfileUpdateResult(false, "Diese E-Mail-Adresse wird bereits verwendet."),
            statusCode: StatusCodes.Status409Conflict);
    }

    var firstName = profile.FirstName.Trim();
    var lastName = profile.LastName.Trim();
    var displayName = $"{firstName} {lastName}".Trim();

    identityUser.FirstName = firstName;
    identityUser.LastName = lastName;
    identityUser.Email = email;
    identityUser.NormalizedEmail = normalizedEmail;
    identityUser.UserName = email;
    identityUser.NormalizedUserName = normalizer.NormalizeName(email);
    identityUser.PhoneNumber = normalizedPhone;
    identityUser.SecurityStamp = Guid.NewGuid().ToString();
    identityUser.ConcurrencyStamp = Guid.NewGuid().ToString();

    appUser.DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName;
    appUser.Email = email;
    appUser.Phone = normalizedPhone;
    appUser.PostalCode = profile.PostalCode.Trim();
    appUser.City = profile.City.Trim();

    await db.SaveChangesAsync();
    await signInManager.RefreshSignInAsync(identityUser);

    return Results.Json(new ProfileUpdateResult(true, "Persönliche Daten wurden gespeichert.", normalizedPhone));
}).RequireAuthorization();

app.MapGet("/api/listing-images/{id:guid}", async (Guid id, IDbContextFactory<MhMDbContext> dbFactory) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var image = await db.ListingImages.FindAsync(id);
    if (image is null) return Results.NotFound();
    return Results.File(image.Data, image.ContentType, image.FileName);
});//.RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
