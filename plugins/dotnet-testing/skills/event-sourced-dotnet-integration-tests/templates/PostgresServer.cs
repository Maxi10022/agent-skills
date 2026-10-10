using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(YourApp.Tests.Testing.Fixtures.PostgresServer))]

namespace YourApp.Tests.Testing.Fixtures;

// xUnit v3 assembly fixture; adapt the image to the one production runs (with the extensions the app needs)
public sealed class PostgresServer : IAsyncLifetime
{
    private readonly SemaphoreSlim _creating = new(1, 1);

    // Durability is pointless for a throwaway server, and every host keeps its own connection pool
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .WithDatabase("app")
        .WithUsername("admin")
        .WithPassword("secure")
        .WithCommand(
            "-c", "max_connections=1000",
            "-c", "fsync=off",
            "-c", "synchronous_commit=off",
            "-c", "full_page_writes=off")
        .Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    // CREATE DATABASE copies template1 under a lock, so fixtures starting together create theirs one at a time
    public async Task<string> CreateDatabaseAsync(string prefix)
    {
        var name = $"{prefix[..Math.Min(prefix.Length, 24)]}_{Guid.CreateVersion7():N}".ToLowerInvariant();

        await _creating.WaitAsync();
        try
        {
            await using var connection = new NpgsqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _creating.Release();
        }

        var database = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,
            IncludeErrorDetail = true
        };

        return database.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        _creating.Dispose();
        await _container.DisposeAsync();
    }
}
