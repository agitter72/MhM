using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using MhM.UI.Components;
using MhM.UI.Data;
using MhM.UI.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLocalization();
builder.Services.AddSingleton<UiLocalizer>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<MhMDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("MhM")
        ?? throw new InvalidOperationException("Connection string 'MhM' was not found.")));

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
app.UseRequestLocalization(requestLocalizationOptions);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MhMDbContext>();
    await DbInitializer.InitializeAsync(db);
}

app.Run();
