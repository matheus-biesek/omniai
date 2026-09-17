using Microsoft.EntityFrameworkCore;
using Shared.Data;
using StackExchange.Redis;

namespace Tests.Integration;

// Testes de integracao usam o Postgres/Redis do docker compose, mas num banco proprio
// ("omniai_tests") e em streams Redis com prefixo "test:" - nunca tocam nos dados do ambiente.
// Rodar so os unitarios: dotnet test --filter "Category!=Integration"
public sealed class DatabaseFixture : IAsyncLifetime
{
    public static readonly string WriteConnectionString =
        Environment.GetEnvironmentVariable("OMNIAI_TEST_WRITE_DB")
        ?? "Host=localhost;Port=5432;Database=omniai_tests;Username=omniai;Password=omniai_dev_password";

    public static readonly string ReplicaConnectionString =
        Environment.GetEnvironmentVariable("OMNIAI_TEST_READ_DB")
        ?? "Host=localhost;Port=5433;Database=omniai_tests;Username=omniai;Password=omniai_dev_password";

    public static readonly string RedisConnectionString =
        Environment.GetEnvironmentVariable("OMNIAI_TEST_REDIS") ?? "localhost:6379";

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public WriteDbContext CreateWriteContext() =>
        new(new DbContextOptionsBuilder<WriteDbContext>().UseNpgsql(WriteConnectionString).Options);

    // ReadDbContext apontando pro PRIMARY - usado nos testes de agregacao pra nao depender do atraso
    // de replicacao. O teste de replicacao usa ReplicaConnectionString explicitamente.
    public ReadDbContext CreateReadContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<ReadDbContext>().UseNpgsql(connectionString ?? WriteConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using (var ctx = CreateWriteContext())
        {
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.MigrateAsync();
        }

        Redis = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
    }

    public async Task ResetAsync()
    {
        await using var ctx = CreateWriteContext();
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE usage_records, usage_event_logs, api_keys, projects RESTART IDENTITY CASCADE");
    }

    public async Task DisposeAsync()
    {
        await Redis.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Integration";
}
