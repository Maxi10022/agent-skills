using System.Security.Claims;
using BlazorBlueprint.Components;
using Bunit;
using Bunit.Rendering;
using Marten;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Wolverine;

namespace YourApp.Tests.Testing.Bases;

// The page half of the host test base; the host half (Host, Given, waits, Faulted) completes this partial class
public abstract partial class TestBase : ComponentTestBase
{
    // The section name the layout's header outlet uses, pages project their header controls into it
    private const string HeaderSection = "header";

    // A fault test holds the command back, so the fault it publishes is the only answer the page gets
    private IMessageBus? _bus;

    protected virtual Guid UserId { get; } = Guid.CreateVersion7();

    protected virtual string Username { get; } = "test-user";

    protected override void InitializeComponentServices()
    {
        base.InitializeComponentServices();

        var scope = Host.Services.CreateAsyncScope();

        AddAuthorization().SetAuthorized(Username);
        Services.AddSingleton<AuthenticationStateProvider>(new AuthenticationStateProviderStub(UserId, Username));

        Services.AddScoped<IDocumentSession>(_ =>
            Host.Services.GetRequiredService<IDocumentStore>().LightweightSession());

        Services.AddScoped<IMessageBus>(_ =>
            _bus ?? scope.ServiceProvider.GetRequiredService<IMessageBus>());

        InitializeComponentServices(scope);
    }

    // The slice's host singletons, the same instances the host forwards to
    protected virtual void InitializeComponentServices(AsyncServiceScope scope)
    {
    }

    // Call before the render, the page resolves its bus when it renders
    protected IMessageBus StubBus() => _bus = Substitute.For<IMessageBus>();

    protected static TCommand Sent<TCommand>(IMessageBus bus) =>
        (TCommand)bus.ReceivedCalls().Single().GetArguments()[0]!;

    // Beside the header outlet and the dialog provider, the page's own markup holds neither
    protected IRenderedComponent<ContainerFragment> RenderPage<TPage>() where TPage : IComponent =>
        Render(builder =>
        {
            builder.OpenComponent<SectionOutlet>(0);
            builder.AddComponentParameter(1, nameof(SectionOutlet.SectionName), HeaderSection);
            builder.CloseComponent();

            builder.OpenComponent<BbDialogProvider>(2);
            builder.CloseComponent();

            builder.OpenComponent<TPage>(3);
            builder.CloseComponent();
        });
}

public class AuthenticationStateProviderStub(Guid userId, string username) : AuthenticationStateProvider
{
    private readonly ClaimsPrincipal _user = new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username)
        ],
        authenticationType: "Test"
    ));

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_user));
}
