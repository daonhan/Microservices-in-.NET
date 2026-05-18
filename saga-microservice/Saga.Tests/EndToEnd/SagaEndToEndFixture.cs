using Testcontainers.MsSql;

namespace Saga.Tests.EndToEnd;

/// <summary>
/// Spins a fresh SQL Server container per test class so the saga's DB schema, outbox,
/// transition audit, and state machine are exercised against a clean, isolated instance.
/// Tagged via the <c>[Trait("Category", "EndToEnd")]</c> on test classes so the suite is
/// opt-in via <c>dotnet test --filter Category=EndToEnd</c>.
/// </summary>
public sealed class SagaEndToEndFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public SagaEndToEndWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        Factory = new SagaEndToEndWebApplicationFactory(_sqlContainer.GetConnectionString());
        await Factory.InitializeAsync();
        // Touch the server to force host startup (and migrations).
        _ = Factory.Server;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _sqlContainer.DisposeAsync();
    }
}
