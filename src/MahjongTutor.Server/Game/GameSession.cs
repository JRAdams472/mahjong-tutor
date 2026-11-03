using MahjongTutor.Core.Bots;
using MahjongTutor.Core.Rules;
using MahjongTutor.Core.Tiles;
using MahjongTutor.Server.Cards;

namespace MahjongTutor.Server.Game;

/// <summary>
/// A single-player local game: human in seat 0, naive bots in seats 1-3
/// (draw + discard first tile, pass all calls). Scoped per Blazor circuit.
/// </summary>
public sealed class GameSession(ICardStore cards)
{
    public const int HumanSeat = 0;

    public GameEngine? Engine { get; private set; }
    public List<GameEvent> Log { get; } = [];

    public async Task<GameEngine> StartAsync(CancellationToken ct = default)
    {
        var card = await cards.GetActiveAsync(ct)
            ?? throw new InvalidOperationException("No active card — enter one under Cards first.");
        Engine = new GameEngine(card, seed: Random.Shared.Next());
        Log.Clear();
        return Engine;
    }

    /// <summary>Applies a human action, then drives bots until the human must act.</summary>
    public async Task ActAsync(Func<GameEngine, IReadOnlyList<GameEvent>> action)
    {
        if (Engine == null) throw new InvalidOperationException("no game");
        Log.AddRange(action(Engine));
        await RunBotsAsync();
    }

    /// <summary>Drives naive bot turns while any bot has a pending action.</summary>
    public async Task RunBotsAsync()
    {
        var e = Engine!;
        var guard = 0;
        while (guard++ < 300 && BotAction(e) is { } act)
        {
            await Task.Delay(200);
            Log.AddRange(act());
        }
    }

    /// <summary>Returns the next bot action, or null if waiting on the human/game over.</summary>
    private static Func<IReadOnlyList<GameEvent>>? BotAction(GameEngine e)
    {
        var s = e.State;
        if (s.Phase == GamePhase.Finished) return null;

        if (s.Phase == GamePhase.Charleston)
        {
            var b = Enumerable.Range(1, 3).FirstOrDefault(b =>
                s.CharlestonStage == CharlestonStage.Vote
                    ? !s.PendingVotes.ContainsKey(b)
                    : !s.PendingPasses.ContainsKey(b));
            if (b == 0) return null;
            return () => s.CharlestonStage == CharlestonStage.Vote
                ? e.Vote(b, BotBrain.VoteSecondCharleston(s, b))
                : s.CharlestonStage == CharlestonStage.Courtesy
                    ? e.SubmitCourtesy(b, BotBrain.Courtesy(s, b))
                    : e.SubmitPass(b, BotBrain.CharlestonPass(s, b));
        }

        if (s.Stage == TurnStage.CallWindow)
        {
            var b = s.PendingOptions.Keys.FirstOrDefault(k => k != HumanSeat && !s.PendingResponded.Contains(k));
            return b == 0 ? null : () => e.Claim(b, BotBrain.DecideClaim(s, b, s.PendingDiscard!.Value));
        }

        if (s.CurrentSeat == HumanSeat || s.Stage == TurnStage.Finished) return null;
        var seat = s.CurrentSeat;
        return s.Stage switch
        {
            TurnStage.AwaitDraw => () => e.Draw(seat),
            TurnStage.AwaitDiscard => () =>
            {
                // take every available joker exchange first
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
