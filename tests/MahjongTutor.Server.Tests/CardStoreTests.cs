using MahjongTutor.Core.Card;
using MahjongTutor.Server.Cards;
using MahjongTutor.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace MahjongTutor.Server.Tests;

/// <summary>CardStore logic over the EF InMemory provider (jsonb columns are just strings there).</summary>
public class CardStoreTests
{
    private sealed class Factory(DbContextOptions<MahjongTutorDbContext> options)
        : IDbContextFactory<MahjongTutorDbContext>
    {
        public MahjongTutorDbContext CreateDbContext() => new(options);
        public Task<MahjongTutorDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(CreateDbContext());
    }

    private static CardStore NewStore() => new(new Factory(
        new DbContextOptionsBuilder<MahjongTutorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options));

    [Fact]
    public async Task Import_save_get_roundtrips_card()
    {
        var store = NewStore();
        var id = await store.ImportJsonAsync(TestCards.Json);

        var card = await store.GetAsync(id);
        Assert.NotNull(card);
        Assert.Equal(2099, card!.Year);
        Assert.Equal(2, card.AllHands.Count());

        var exported = await store.ExportJsonAsync(id);
        var reimported = CardJson.Parse(exported);
        Assert.Equal(
            card.AllHands.Select(h => h.Pattern),
            reimported.AllHands.Select(h => h.Pattern));
    }

    [Fact]
    public async Task List_returns_summaries_with_hand_count()
    {
        var store = NewStore();
        await store.ImportJsonAsync(TestCards.Json);
        var list = await store.ListAsync();
        var s = Assert.Single(list);
        Assert.Equal("Persist Test Card", s.Name);
        Assert.Equal(2, s.HandCount);
        Assert.False(s.IsActive);
    }

    [Fact]
    public async Task SetActive_is_exclusive()
    {
        var store = NewStore();
        var id1 = await store.ImportJsonAsync(TestCards.Json);
        var id2 = await store.ImportJsonAsync(TestCards.Json);

        await store.SetActiveAsync(id1);
        Assert.Equal(id1, (await store.ListAsync()).Single(c => c.IsActive).Id);

        await store.SetActiveAsync(id2);
        var list = await store.ListAsync();
        Assert.Equal(id2, list.Single(c => c.IsActive).Id);
        Assert.Single(list, c => c.IsActive);
        Assert.NotNull(await store.GetActiveAsync());
    }

    [Fact]
    public async Task Save_rejects_invalid_card()
    {
        var store = NewStore();
        var bad = new MahjongCard
        {
            Year = 2099, Name = "Bad",
            Categories = [new CardCategory
            {
                Name = "C",
                Hands = [new CardHand { Id = "H", Points = 25, Pattern = "FF 11a" }] // 4 tiles
            }]
        };
        await Assert.ThrowsAsync<CardValidationException>(() => store.SaveAsync(bad));
    }

    [Fact]
    public async Task Delete_removes_card()
    {
        var store = NewStore();
        var id = await store.ImportJsonAsync(TestCards.Json);
        await store.DeleteAsync(id);
        Assert.Null(await store.GetAsync(id));
        Assert.Empty(await store.ListAsync());
    }
}
