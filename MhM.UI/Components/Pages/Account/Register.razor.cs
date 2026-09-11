using MhM.UI.Data;
using MhM.UI.Data.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using System.ComponentModel.DataAnnotations;
using MhM.UI.Models;

namespace MhM.UI.Components.Pages.Account;

public partial class Register
{
    [Inject] private UserManager<ApplicationIdentityUser> UserManager { get; set; } = default!;
    [Inject] private SignInManager<ApplicationIdentityUser> SignInManager { get; set; } = default!;
    [Inject] private IDbContextFactory<MhMDbContext> DbFactory { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private readonly RegisterInputModel model = new();
    private bool isBusy;
    private string? errorMessage;

    private async Task RegisterAsync()
    {
        if (isBusy) return;

        isBusy = true;
        errorMessage = null;

        try
        {
            if (!UsernameRules.TryNormalize(model.Username, out var username))
            {
                errorMessage = "Der Nutzername ist ungültig.";
                return;
            }

            if (await UserManager.FindByNameAsync(username) is not null)
            {
                errorMessage = "Dieser Nutzername ist bereits vergeben.";
                return;
            }

            var identityUser = new ApplicationIdentityUser
            {
                UserName = username,
                Email = model.Email.Trim(),
                FirstName = model.FirstName.Trim(),
                LastName = model.LastName.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await UserManager.CreateAsync(identityUser, model.Password);
            if (!result.Succeeded)
            {
                errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
                return;
            }

            try
            {
                var appUser = new AppUser
                {
                    IdentityUserId = identityUser.Id,
                    DisplayName = $"{model.FirstName.Trim()} {model.LastName.Trim()}".Trim(),
                    Username = username,
                    NormalizedUsername = username.ToUpperInvariant(),
                    Email = model.Email.Trim(),
                    PostalCode = model.PostalCode.Trim(),
                    City = model.City.Trim(),
                    Role = UserRole.Privatperson
                };

                await using var db = await DbFactory.CreateDbContextAsync();
                db.AppUsers.Add(appUser);
                await db.SaveChangesAsync();
            }
            catch
            {
                await UserManager.DeleteAsync(identityUser);
                errorMessage = "Registrierung fehlgeschlagen. Bitte erneut versuchen.";
                return;
            }

            var loginRequestResult = await JS.InvokeAsync<bool>("requestLogin", "/account/logon", username, model.Password);
            if (loginRequestResult)
            {
                Nav.NavigateTo("/", forceLoad: true);
                return;
            }

            errorMessage = "Ungültige Anmeldedaten.";
        }
        finally
        {
            isBusy = false;
        }
    }

    private sealed class RegisterInputModel
    {
        [Required, MaxLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string LastName { get; set; } = string.Empty;

        [Username]
        public string Username { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string PostalCode { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        public string City { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
