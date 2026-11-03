using MahjongTutor.Core.Card;
using MahjongTutor.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace MahjongTutor.Server.Cards;

public sealed record CardSummary(int Id, int Year, string Name, int HandCount, bool IsActive);

/// <summary>Persistence for user-entered yearly cards.</summary>
public interface ICardStore
{
    Task<IReadOnlyList<CardSummary>> ListAsync(CancellationToken ct = default);
    Task<MahjongCard?> GetAsync(int id, CancellationToken ct = default);
    Task<MahjongCard?> GetActiveAsync(CancellationToken ct = default);
    /// <summary>Insert (id null) or update (id set). Validates first; throws <see cref="CardValidationException"/>.</summary>
    Task<int> SaveAsync(MahjongCard card, int? id = null, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    /// <summary>Marks one card active (clears the flag on all others).</summary>
    Task SetActiveAsync(int id, CancellationToken ct = default);
    Task<string> ExportJsonAsync(int id, CancellationToken ct = default);
    /// <summary>Validates and stores a card JSON document. Returns the new card id.</summary>
    Task<int> ImportJsonAsync(string json, CancellationToken ct = default);
}

public sealed class CardStore(IDbContextFactory<MahjongTutorDbContext> factory) : ICardStore
{
    public async Task<IReadOnlyList<CardSummary>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Cards.AsNoTracking()
            .OrderBy(c => c.Year).ThenBy(c => c.Name)
            .Select(c => new CardSummary(c.Id, c.Year, c.Name, c.HandCount, c.IsActive))
            .ToListAsync(ct);
    }

    public async Task<MahjongCard?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var doc = await db.Cards.AsNoTracking()
            .Where(c => c.Id == id).Select(c => c.Document).FirstOrDefaultAsync(ct);
        return doc == null ? null : CardJson.Parse(doc);
    }

    public async Task<MahjongCard?> GetActiveAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var doc = await db.Cards.AsNoTracking()
            .Where(c => c.IsActive).Select(c => c.Document).FirstOrDefaultAsync(ct);
        return doc == null ? null : CardJson.Parse(doc);
    }

    public async Task<int> SaveAsync(MahjongCard card, int? id = null, CancellationToken ct = default)
    {
        var errors = CardJson.Validate(card);
        if (errors.Count > 0) throw new CardValidationException(string.Join("; ", errors));
        var document = CardJson.Serialize(card);

        await using var db = await factory.CreateDbContextAsync(ct);
        CardEntity entity;
        if (id is { } existing)
        {
            entity = await db.Cards.FindAsync([existing], ct)
                ?? throw new KeyNotFoundException($"card {existing} not found");
            entity.UpdatedUtc = DateTime.UtcNow;
        }
        else
        {
            entity = new CardEntity { CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
            db.Cards.Add(entity);
        }
        entity.Year = card.Year;
        entity.Name = card.Name;
        entity.HandCount = card.AllHands.Count();
        entity.Document = document;
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Cards.FindAsync([id], ct);
        if (entity == null) return;
        db.Cards.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // Clear-then-set inside one transaction: the filtered unique index on
        // IsActive requires the old flag to be gone before the new one lands.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var target = await db.Cards.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"card {id} not found");
        await foreach (var c in db.Cards.Where(c => c.IsActive).AsAsyncEnumerable().WithCancellation(ct))
            c.IsActive = false;
        await db.SaveChangesAsync(ct);
        target.IsActive = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<string> ExportJsonAsync(int id, CancellationToken ct = default)
    {
        var card = await GetAsync(id, ct) ?? throw new KeyNotFoundException($"card {id} not found");
        return CardJson.Serialize(card);
    }

    public Task<int> ImportJsonAsync(string json, CancellationToken ct = default)
    {
        var card = CardJson.Parse(json);   // validates + throws CardValidationException
        return SaveAsync(card, null, ct);
    }
}
