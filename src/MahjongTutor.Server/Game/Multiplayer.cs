using System.Collections.Concurrent;
using MahjongTutor.Core.Bots;
using MahjongTutor.Core.Rules;
using MahjongTutor.Core.Tiles;
using MahjongTutor.Server.Cards;

namespace MahjongTutor.Server.Game;

/// <summary>Who a Blazor circuit is: name + room + seat. Scoped per circuit.</summary>
public sealed class PlayerSession
{
    public string? Name { get; set; }
    public string? RoomCode { get; set; }
    public int Seat { get; set; } = -1;
}

public sealed class Room
{
    public required string Code { get; init; }
    /// <summary>seat → player name (humans only; empty seats get bots on start).</summary>
    public Dictionary<int, string> SeatNames { get; } = [];
    public GameEngine? Engine { get; set; }
    public List<GameEvent> Log { get; } = [];
    public bool Started => Engine != null;

    public int Humans => SeatNames.Count;
    public IEnumerable<int> BotSeats => Enumerable.Range(0, 4).Except(SeatNames.Keys);
}

/// <summary>
/// LAN multiplayer: name + room code, up to 4 humans, bots fill the rest.
/// Singleton; each circuit subscribes to <see cref="Changed"/> to re-render.
/// </summary>
public sealed class RoomManager(ICardStore cards)
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    public event Action? Changed;

    public IReadOnlyCollection<Room> Rooms => _rooms.Values.ToList();

    public Room CreateRoom(string playerName)
    {
        var code = NewCode();
        var room = new Room { Code = code };
        room.SeatNames[0] = playerName;
        _rooms[code] = room;
        Fire();
        return room;
    }

    public Room? Join(string code, string playerName)
    {
        if (!_rooms.TryGetValue(code.ToUpperInvariant(), out var room)) return null;
        if (room.Started || room.Humans >= 4) return null;
        var seat = Enumerable.Range(0, 4).First(s => !room.SeatNames.ContainsKey(s));
        room.SeatNames[seat] = playerName;
        Fire();
        return room;
    }

    public async Task<bool> StartAsync(Room room)
    {
        var card = await cards.GetActiveAsync();
        if (card == null) return false;
        room.Engine = new GameEngine(card, seed: Random.Shared.Next());
        Fire();
        await RunBotsAsync(room);
        return true;
    }

    /// <summary>Applies a player action, broadcasts, then drives bots.</summary>
    public async Task<IReadOnlyList<GameEvent>> ActAsync(Room room, int seat,
        Func<GameEngine, IReadOnlyList<GameEvent>> action)
    {
        if (room.Engine == null) throw new InvalidOperationException("not started");
        var ev = action(room.Engine);
        room.Log.AddRange(ev);
        Fire();
        await RunBotsAsync(room);
        return ev;
    }

    /// <summary>Runs bot decisions while any non-human seat has pending work.</summary>
    public async Task RunBotsAsync(Room room)
    {
        var e = room.Engine;
        if (e == null) return;
        var guard = 0;
        while (guard++ < 300 && BotAction(e, room) is { } act)
        {
            await Task.Delay(200);
            room.Log.AddRange(act());
            Fire();
        }
    }

    private void Fire() => Changed?.Invoke();

    private static string NewCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 4)
            .Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }

    /// <summary>Next bot action, or null if waiting on a human or game over.</summary>
    private static Func<IReadOnlyList<GameEvent>>? BotAction(GameEngine e, Room room)
    {
        var s = e.State;
        if (s.Phase == GamePhase.Finished) return null;

        if (s.Phase == GamePhase.Charleston)
        {
            var b = room.BotSeats.FirstOrDefault(b =>
                s.CharlestonStage == CharlestonStage.Vote
                    ? !s.PendingVotes.ContainsKey(b)
                    : !s.PendingPasses.ContainsKey(b));
            if (!room.BotSeats.Contains(b)) return null;
            return () => s.CharlestonStage == CharlestonStage.Vote
                ? e.Vote(b, BotBrain.VoteSecondCharleston(s, b))
                : s.CharlestonStage == CharlestonStage.Courtesy
                    ? e.SubmitCourtesy(b, BotBrain.Courtesy(s, b))
                    : e.SubmitPass(b, BotBrain.CharlestonPass(s, b));
        }

        if (s.Stage == TurnStage.CallWindow)
        {
            var b = s.PendingOptions.Keys
                .FirstOrDefault(k => room.BotSeats.Contains(k) && !s.PendingResponded.Contains(k));
            if (!room.BotSeats.Contains(b)) return null;
            return () => e.Claim(b, BotBrain.DecideClaim(s, b, s.PendingDiscard!.Value));
        }

        if (s.Stage == TurnStage.Finished || !room.BotSeats.Contains(s.CurrentSeat)) return null;
        var seat = s.CurrentSeat;
        return s.Stage switch
        {
            TurnStage.AwaitDraw => () => e.Draw(seat),
            TurnStage.AwaitDiscard => () =>
            {
                if (BotBrain.JokerExchanges(s, seat) is { Count: > 0 } xs)
                    return e.ExchangeJoker(seat, xs[0].owner, xs[0].meld, xs[0].natural);
                return BotBrain.OnTurn(s, seat) switch
                {
                    BotTurnAct.Mahjong => e.DeclareMahjong(seat),
                    BotTurnAct.Discard d => e.Discard(seat, d.Tile),
                    _ => throw new InvalidOperationException(),
                };
            },
            _ => null,
        };
    }
}
