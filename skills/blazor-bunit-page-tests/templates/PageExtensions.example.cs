using BlazorBlueprint.Components;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace YourApp.Tests.Customers.Support;

// <Slice>/Support/<Page>Extensions; every helper of a slice extends the same type, so names stay unique
public static class CustomersPageExtensions
{
    extension(IRenderedComponent<ContainerFragment> cut)
    {
        public IReadOnlyList<string> Names() =>
        [
            .. cut.FindAll("tbody tr td:first-child")
                .Select(cell => cell.TextContent.Trim())
        ];

        public IReadOnlyList<string> Cells(string name) =>
        [
            .. cut.Row(name)
                .QuerySelectorAll("td")
                .Select(cell => cell.TextContent.Trim())
        ];

        public bool PaneOpen() => cut.FindComponent<BbSheet>().Instance.Open is true;

        public Task SearchAsync(string query) =>
            cut.InvokeAsync(() => cut.FindComponent<BbInputGroupInput>().Instance.ValueChanged.InvokeAsync(query));

        public async Task AddTagAsync(string tag)
        {
            await cut.Find("#customer-tags").InputAsync(new ChangeEventArgs { Value = tag });
            await cut.Find("#customer-tags").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }

        // Held, not awaited, an awaited confirm deadlocks the test before the dialog can be clicked
        public async Task<Task> OpenDeleteDialogAsync(string name)
        {
            var deleteTask = cut.RowActionAsync(name, "Delete customer");
            await cut.WaitForStateAsync(() => cut.Markup.Contains("Delete customer?"), Timeouts.Render);
            return deleteTask;
        }
    }
}
