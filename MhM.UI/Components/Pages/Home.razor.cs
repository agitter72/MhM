using Microsoft.AspNetCore.Components;
using MhM.UI.Localization;

namespace MhM.UI.Components.Pages;

public partial class Home
{
    [Inject]
    protected UiLocalizer T { get; set; } = default!;
}