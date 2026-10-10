using Alba;
using Alba.Security;
using JasperFx.CommandLine;
using JasperFx.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace YourApp.Tests.Testing.Fixtures;

// Adapt the connection string name and the stubbed external services to the app
public abstract class FixtureBase(PostgresServer server) : IAsyncLifetime
{
    public IAlbaHost Host { get; private set; } = null!;

    public virtual async ValueTask InitializeAsync()
    {
        // A database of its own on the shared server
        var connectionString = await server.CreateDatabaseAsync(GetType().Name);

        JasperFxEnvironment.AutoStartHost = true;
        Host = await AlbaHost.For<Program>(builder =>
            {
                // Dummy keys, no test reaches a cloud service
                builder.UseSetting("ExternalService:ApiKey", "test");

                builder.UseSetting("ConnectionStrings:Marten", connectionString);

                builder.ConfigureServices(services =>
                {
                    services.RunWolverineInSoloMode();
                    ConfigureStubs(services);

                    // A paused queue would hold every shutdown for the default 30 seconds
                    services.ConfigureWolverine(options => options.Durability.DrainTimeout = 2.Seconds());
                });

                Configure(builder);
            },
            new AuthenticationStub()
        );
    }

    protected virtual void ConfigureStubs(IServiceCollection services)
    {
        // Replace clients of external services with deterministic stubs here
    }

    protected virtual void Configure(IWebHostBuilder builder)
    {
        // Runs last, so a derived fixture's settings win over the defaults above
    }

    public virtual async ValueTask DisposeAsync()
    {
        // The database goes with the shared server at the end of the run
        await Host.DisposeAsync();
    }
}

// One fixture and one collection per slice, both in <Slice>/Support/
public class DefaultFixture(PostgresServer server) : FixtureBase(server);

[CollectionDefinition(nameof(DefaultTestCollection))]
public class DefaultTestCollection : ICollectionFixture<DefaultFixture>;
