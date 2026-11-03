using System.Text.Json;
using MahjongTutor.Core.Rules;
using MahjongTutor.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace MahjongTutor.Server.Game;

/// <summary>Persists finished games + rolling player aggregates.</summary>
public sealed class StatsStore(IDbContextFactory<MahjongTutorDbContext> dbf)
{
    public async Task RecordFinishedAsync(GameState s, IReadOnlyDictionary<int, string> names,
        IReadOnlyList<GameEvent> log, bool solo)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var card = await db.Cards.FirstOrDefaultAsync(c => c.IsActive);
        if (card == null) return;

        var seats = Enumerable.Range(0, 4)
            .ToDictionary(i => i.ToString(), i => names.GetValueOrDefault(i, $"bot {i}"));
        var game = new Data.Game
        {
            CardId = card.Id, Card = card, Status = GameStatus.Finished,
            Seats = JsonSerializer.Serialize(new { seats, solo, reason = s.EndReason }),
            WinnerSeat = s.WinnerSeat,
            StartedUtc = DateTime.UtcNow, EndedUtc = DateTime.UtcNow,
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        var seq = 0;
        foreach (var e in log)
            db.GameMoves.Add(new GameMove
            {
                GameId = game.Id, Game = game, Seq = seq++,
                Type = e.GetType().Name,
                Payload = JsonSerializer.Serialize(e, e.GetType()),
                Utc = DateTime.UtcNow,
            });

        // rolling aggregates per named player
        foreach (var (seat, name) in names)
        {
            var player = await db.Players.FirstOrDefaultAsync(p => p.DisplayName == name)
                ?? db.Players.Add(new Player { DisplayName = name }).Entity;
            var stat = await db.PlayerStats.FirstOrDefaultAsync(x => x.Player == player);
            stat ??= db.PlayerStats.Add(new PlayerStat { Player = player }).Entity;
            stat.GamesPlayed++;
            if (seat == s.WinnerSeat) stat.Wins++;
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<Data.Game>> RecentAsync(int take = 25) =>
        await (await dbf.CreateDbContextAsync())
            .Games.OrderByDescending(g => g.EndedUtc).Take(take).ToListAsync();

    public async Task<List<PlayerStat>> LeadersAsync() =>
        await (await dbf.CreateDbContextAsync())
            .PlayerStats.Include(x => x.Player)
            .OrderByDescending(x => x.Wins).Take(20).ToListAsync();
}
