using MhM.UI.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MhM.UI.Components.Pages.Account;

public partial class Login
{
    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private readonly LoginModel model = new();
    private bool isBusy;
    private string? errorMessage;

    public async Task HandleLogin()
    {
        errorMessage = null;
        isBusy = true;
        try
        {
            var loginRequestResult = await JS.InvokeAsync<bool>("requestLogin", "/account/logon", model.Email, model.Password, model.RememberMe);
            if (loginRequestResult)
            {
                Nav.NavigateTo("/", forceLoad: true);
            }
            else
            {
                errorMessage = "Ungültige Anmeldedaten.";
            }
        }
        catch (Exception)
        {
            errorMessage = "Anmeldung fehlgeschlagen.";
        }
        finally { isBusy = false; }
    }
}