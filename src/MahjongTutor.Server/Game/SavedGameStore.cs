using System.Text.Json;
using MahjongTutor.Core.Card;
using MahjongTutor.Core.Rules;
using MahjongTutor.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace MahjongTutor.Server.Game;

public sealed class SavedGameStore(IDbContextFactory<MahjongTutorDbContext> dbf)
{
    public async Task<int> SaveAsync(string name, GameState s, int? id = null)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var json = JsonSerializer.Serialize(GameSnapshot.Capture(s));
        var row = id is { } i ? await db.SavedGames.FindAsync(i) : null;
        if (row == null)
        {
            row = new SavedGame();
            db.SavedGames.Add(row);
        }
        row.Name = name;
        row.Snapshot = json;
        row.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return row.Id;
    }

    public async Task<List<SavedGame>> ListAsync() =>
        await (await dbf.CreateDbContextAsync())
            .SavedGames.OrderByDescending(g => g.UpdatedUtc).ToListAsync();

    public async Task<GameEngine?> LoadAsync(int id, MahjongCard card)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var row = await db.SavedGames.FindAsync(id);
        if (row == null) return null;
        var snap = JsonSerializer.Deserialize<GameSnapshot>(row.Snapshot);
        if (snap == null) return null;
        return new GameEngine(GameSnapshot.Restore(snap, card));
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var row = await db.SavedGames.FindAsync(id);
        if (row != null) { db.SavedGames.Remove(row); await db.SaveChangesAsync(); }
    }
}
