using MahjongTutor.Core.Bots;
using MahjongTutor.Core.Coach;
using MahjongTutor.Core.Rules;
using MahjongTutor.Core.Tiles;
using MahjongTutor.Server.Cards;

namespace MahjongTutor.Server.Game;

public sealed record CoachNote(string Severity, string Text);

/// <summary>
/// A single-player local game: human in seat 0, bots in seats 1-3.
/// Scoped per Blazor circuit. Every human action also consults the coach
/// and records notes (missed calls, suboptimal discards).
/// </summary>
public sealed class GameSession(ICardStore cards, StatsStore stats)
{
    public const int HumanSeat = 0;

    public GameEngine? Engine { get; private set; }
    public List<GameEvent> Log { get; } = [];
    public List<CoachNote> Notes { get; } = [];

    public async Task<GameEngine> StartAsync(CancellationToken ct = default)
    {
        var card = await cards.GetActiveAsync(ct)
            ?? throw new InvalidOperationException("No active card — enter one under Cards first.");
        Engine = new GameEngine(card, seed: Random.Shared.Next());
        Log.Clear();
        Notes.Clear();
        Note($"Game started — {card.Year} {card.Name}. Good luck!", "info");
        return Engine;
    }

    private void Note(string? text, string sev = "info")
    {
        if (text == null) return;
        Notes.Insert(0, new(sev, text));
        if (Notes.Count > 30) Notes.RemoveAt(Notes.Count - 1);
    }

    // ---- human actions (each runs bots afterwards) --------------------------

    public async Task HumanPassAsync(List<Tile> tiles) => await Act(e => e.SubmitPass(0, tiles));
    public async Task HumanCourtesyAsync(List<Tile> tiles) => await Act(e => e.SubmitCourtesy(0, tiles));
    public async Task HumanVoteAsync(bool go) => await Act(e => e.Vote(0, go));
    public async Task HumanDrawAsync() => await Act(e => e.Draw(0));

    public async Task HumanDiscardAsync(Tile tile)
    {
        Note(CoachBrain.DiscardNote(Engine!.State, 0, tile), "miss");
        Note(Defense.DangerNote(Engine!.State, 0, tile), "warn");
        await Act(e => e.Discard(0, tile));
    }

    public async Task HumanClaimAsync(ClaimKind? kind)
    {
        if (kind == null)
            Note(CoachBrain.MissedCallNote(Engine!.State, 0, Engine.State.PendingDiscard!.Value), "miss");
        await Act(e => e.Claim(0, kind));
    }

    public async Task HumanMahjongAsync() => await Act(e => e.DeclareMahjong(0));

    public async Task HumanExchangeAsync(int owner, int meld, Tile natural) =>
        await Act(e => e.ExchangeJoker(0, owner, meld, natural));

    private bool _recorded;

    private async Task Act(Func<GameEngine, IReadOnlyList<GameEvent>> action)
    {
        if (Engine == null) throw new InvalidOperationException("no game");
        Log.AddRange(action(Engine));
        await RunBotsAsync();
        await MaybeRecordAsync();
    }

    private async Task MaybeRecordAsync()
    {
        var s = Engine?.State;
        if (_recorded || s is not { Phase: GamePhase.Finished }) return;
        _recorded = true;
        await stats.RecordFinishedAsync(s,
            new Dictionary<int, string> { [0] = "you" }, Log, solo: true);
    }

    // ---- bots ----------------------------------------------------------------

    /// <summary>Drives bot turns while any bot has a pending action.</summary>
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
