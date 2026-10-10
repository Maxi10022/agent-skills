using BlazorBlueprint.Components;
using BlazorBlueprint.Primitives.Services;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace YourApp.Tests.Testing.Bases;

// Replace the BlazorBlueprint registration, BbPortalHost and ToastService with your component library's
public abstract class ComponentTestBase : BunitContext
{
    private bool _servicesInitialized;

    // The per-test data catalog, drop it if the project has none
    public TestDataCollection Data { get; } = new(TestFaker.ForCurrentTest());

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected ToastService Toasts => Services.GetRequiredService<ToastService>();

    // Runs once, right before the first render, so a test can still register services in Arrange
    protected virtual void InitializeComponentServices() => Services.AddBlazorBlueprintComponents();

    public override IRenderedComponent<TComponent> Render<TComponent>(RenderFragment renderFragment)
    {
        EnsureComponentServices();
        return base.Render<TComponent>(WithPortalHost(renderFragment));
    }

    public override IRenderedComponent<ContainerFragment> Render(RenderFragment renderFragment)
    {
        EnsureComponentServices();
        return base.Render(WithPortalHost(renderFragment));
    }

    private void EnsureComponentServices()
    {
        if (_servicesInitialized) return;

        _servicesInitialized = true;
        InitializeComponentServices();

        // Both resolve services, so they come after every registration
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo(rendererName: "Server", isInteractive: true));
    }

    private static RenderFragment WithPortalHost(RenderFragment content) => builder =>
    {
        builder.OpenComponent<BbPortalHost>(0);
        builder.CloseComponent();
        content(builder);
    };
}
