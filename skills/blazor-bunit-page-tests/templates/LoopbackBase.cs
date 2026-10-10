using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace YourApp.Tests.Testing.Bases;

// A real HTTP server on a free loopback port, for code that only talks to a URL
public abstract class LoopbackBase : ComponentTestBase, IAsyncLifetime
{
    private readonly WebApplication _application;

    protected LoopbackBase()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _application = builder.Build();
    }

    protected virtual void ConfigureLoopback(WebApplication app)
    {
    }

    public async ValueTask InitializeAsync()
    {
        ConfigureLoopback(_application);
        await _application.StartAsync();
    }

    protected Uri GetLoopbackUri(string path = "")
    {
        var server = _application.Services.GetRequiredService<IServer>();
        var addresses = server.Features.GetRequiredFeature<IServerAddressesFeature>();
        return new Uri(new Uri(addresses.Addresses.Single()), path);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _application.DisposeAsync();
        await base.DisposeAsyncCore();
    }
}
