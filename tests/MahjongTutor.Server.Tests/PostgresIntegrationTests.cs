using MahjongTutor.Server.Cards;
using MahjongTutor.Server.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MahjongTutor.Server.Tests;

/// <summary>
/// Real-Postgres integration test: creates a throwaway database, applies
/// migrations, round-trips a card through jsonb, drops the database.
/// Runs only when MAHJONG_TEST_DB is set (e.g. CI, or local compose DB):
/// <c>MAHJONG_TEST_DB="Host=localhost;Port=5433;Username=mahjong;Password=mahjong"</c>
/// </summary>
public class PostgresIntegrationTests
{
    private sealed class Factory(DbContextOptions<MahjongTutorDbContext> options)
        : IDbContextFactory<MahjongTutorDbContext>
    {
        public MahjongTutorDbContext CreateDbContext() => new(options);
        public Task<MahjongTutorDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(CreateDbContext());
    }

    [Fact]
    public async Task Card_roundtrips_through_jsonb()
    {
        var baseCs = Environment.GetEnvironmentVariable("MAHJONG_TEST_DB");
        if (string.IsNullOrEmpty(baseCs)) return; // no Postgres configured — env-gated

        var dbName = "mt_it_" + Guid.NewGuid().ToString("N")[..12];
        var admin = new NpgsqlConnectionStringBuilder(baseCs) { Database = "postgres" };
        await using (var conn = new NpgsqlConnection(admin.ConnectionString))
        {
            await conn.OpenAsync();
            await using var create = conn.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{dbName}\"";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var cs = new NpgsqlConnectionStringBuilder(baseCs) { Database = dbName }.ConnectionString;
            var options = new DbContextOptionsBuilder<MahjongTutorDbContext>()
                .UseNpgsql(cs).Options;
            await using (var db = new MahjongTutorDbContext(options))
                await db.Database.MigrateAsync();

            var store = new CardStore(new Factory(options));
            var id = await store.ImportJsonAsync(TestCards.Json);
            await store.SetActiveAsync(id);

            var active = await store.GetActiveAsync();
            Assert.NotNull(active);
            Assert.Equal(2, active!.AllHands.Count());

            var exported = await store.ExportJsonAsync(id);
            Assert.Equal(
                Core.Card.CardJson.Parse(TestCards.Json).AllHands.Select(h => h.Pattern),
                Core.Card.CardJson.Parse(exported).AllHands.Select(h => h.Pattern));
        }
        finally
        {
            await using var conn = new NpgsqlConnection(admin.ConnectionString);
            await conn.OpenAsync();
            await using var drop = conn.CreateCommand();
            drop.CommandText =
                $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
