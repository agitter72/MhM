using Microsoft.AspNetCore.Components;
using MhM.UI.Data.Models;

namespace MhM.UI.Components.Shared;

public partial class ImageCarousel
{
    [Parameter, EditorRequired]
    public List<ListingImage> Images { get; set; } = [];

    /// <summary>
    /// Wird aufgerufen, wenn der Nutzer das aktuell angezeigte Bild löschen möchte.
    /// Erhält die Id des Bildes als Argument.
    /// </summary>
    [Parameter]
    public EventCallback<Guid> OnDelete { get; set; }

    private int _index = 0;

    protected override void OnParametersSet()
    {
        // Index korrigieren, falls die Bildliste kürzer geworden ist
        if (_index >= Images.Count)
            _index = Math.Max(0, Images.Count - 1);
    }

    private void Prev()
    {
        if (Images.Count == 0) return;
        _index = (_index - 1 + Images.Count) % Images.Count;
    }

    private void Next()
    {
        if (Images.Count == 0) return;
        _index = (_index + 1) % Images.Count;
    }

    private void GoTo(int index) => _index = index;

    private async Task DeleteCurrentAsync()
    {
        if (Images.Count == 0) return;
        var id = Images[_index].Id;
        await OnDelete.InvokeAsync(id);
    }
}