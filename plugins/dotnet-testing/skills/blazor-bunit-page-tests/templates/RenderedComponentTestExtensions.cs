using AngleSharp.Dom;
using BlazorBlueprint.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace YourApp.Tests.Testing.Extensions;

// Generic helpers for every rendered component; replace the BlazorBlueprint types with your library's
public static class RenderedComponentTestExtensions
{
    extension<TComponent>(IRenderedComponent<TComponent> cut) where TComponent : class, IComponent
    {
        public bool Settled() => cut.FindComponents<BbSpinner>().Count == 0;

        public IElement Button(string text) =>
            cut.FindAll("button")
                .First(button => button.TextContent.Trim() == text);

        public Task ClickAsync(string text) => cut.Button(text).ClickAsync(new MouseEventArgs());

        public BbInput Input(string id) =>
            cut.FindComponents<BbInput>()
                .First(input => input.Instance.Id == id)
                .Instance;

        public Task TypeAsync(string id, string value) =>
            cut.InvokeAsync(() => cut.Input(id).ValueChanged.InvokeAsync(value));

        public Task SubmitAsync() => cut.Find("form").SubmitAsync();

        // A sheet's or dialog's own close button, which reads "Close" to screen readers only
        public Task CloseDialogAsync() =>
            cut.Find("[role=dialog]")
                .QuerySelectorAll("button")
                .First(button => button.TextContent.Trim() == "Close")
                .ClickAsync(new MouseEventArgs());

        public IElement Row(string firstCell) =>
            cut.FindAll("tbody tr")
                .First(row => row.QuerySelector("td")!.TextContent.Trim() == firstCell);

        // The row's last menu button opens its dropdown, the item is picked by its text
        public async Task RowActionAsync(string firstCell, string action)
        {
            await cut.Row(firstCell)
                .QuerySelectorAll("button")
                .Last()
                .ClickAsync(new MouseEventArgs());

            var item = cut.FindComponents<BbDropdownMenuItem>()
                .First(item => item.Markup.Contains(action));

            await cut.InvokeAsync(() => item.Instance.OnClick.InvokeAsync(new MouseEventArgs()));
        }
    }
}
