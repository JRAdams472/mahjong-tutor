using MahjongTutor.Core.Bots;
using MahjongTutor.Core.Rules;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Tests;

public class BotTests
{
    /// <summary>Runs a 4-bot game to completion; returns the engine.</summary>
    private static GameEngine RunAllBotGame(int seed, int dealer = 0)
    {
        var e = new GameEngine(TestCards.Card, seed, dealer);
        var guard = 0;
        while (e.State.Phase != GamePhase.Finished && guard++ < 2000)
        {
            var s = e.State;
            if (s.Phase == GamePhase.Charleston)
            {
                if (s.CharlestonStage == CharlestonStage.Vote)
                {
                    var b = Enumerable.Range(0, 4).First(x => !s.PendingVotes.ContainsKey(x));
                    e.Vote(b, BotBrain.VoteSecondCharleston(s, b));
                }
                else
                {
                    var b = Enumerable.Range(0, 4).First(x => !s.PendingPasses.ContainsKey(x));
                    if (s.CharlestonStage == CharlestonStage.Courtesy)
                        e.SubmitCourtesy(b, BotBrain.Courtesy(s, b));
                    else
                        e.SubmitPass(b, BotBrain.CharlestonPass(s, b));
                }
                continue;
            }
            switch (s.Stage)
            {
                case TurnStage.AwaitDraw:
                    e.Draw(s.CurrentSeat);
                    break;
                case TurnStage.AwaitDiscard:
                {
                    var seat = s.CurrentSeat;
                    if (BotBrain.JokerExchanges(s, seat) is { Count: > 0 } xs)
                    {
                        e.ExchangeJoker(seat, xs[0].owner, xs[0].meld, xs[0].natural);
                        break;
                    }
                    if (BotBrain.OnTurn(s, seat) is BotTurnAct.Mahjong)
                        e.DeclareMahjong(seat);
                    else if (BotBrain.OnTurn(s, seat) is BotTurnAct.Discard d)
                        e.Discard(seat, d.Tile);
                    break;
                }
                case TurnStage.CallWindow:
                {
                    var b = s.PendingOptions.Keys.First(k => !s.PendingResponded.Contains(k));
                    e.Claim(b, BotBrain.DecideClaim(s, b, s.PendingDiscard!.Value));
                    break;
                }
            }
        }
        return e;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(99)]
    public void Bot_game_terminates_without_exceptions(int seed)
    {
        var e = RunAllBotGame(seed);
        Assert.Equal(GamePhase.Finished, e.State.Phase);
        Assert.NotNull(e.State.EndReason);
        if (e.State.WinnerSeat != null)
            Assert.NotNull(e.State.WinningHand);
    }

    [Fact]
    public void Charleston_pass_returns_held_tiles()
    {
        var e = new GameEngine(TestCards.Card, 5, 0);
        var pass = BotBrain.CharlestonPass(e.State, 0);
        Assert.Equal(3, pass.Count);
        foreach (var t in pass) Assert.True(e.State.Seats[0].Concealed.Contains(t));
    }

    [Fact]
    public void DecideClaim_mahjong_when_hand_completes()
    {
        var e = new GameEngine(TestCards.Card, 5, 0);
        var seat = e.State.Seats[0];
        // one 1d short of RUN1
        foreach (var c in "1d 2d 2d 2d 3d 3d 3d 3d 4d 4d 4d 5d 5d".Split(' '))
            seat.Concealed.Add(TileKinds.FromCode(c));
        // fake an open window
        e.State.PendingDiscard = Tile.D1;
        e.State.PendingOptions[0] = [ClaimKind.Pung, ClaimKind.Mahjong];
        Assert.Equal(ClaimKind.Mahjong, BotBrain.DecideClaim(e.State, 0, Tile.D1));
    }

    [Fact]
    public void DecideClaim_passes_when_tile_not_needed()
    {
        var e = new GameEngine(TestCards.Card, 5, 0);
        var seat = e.State.Seats[0];
        foreach (var c in "1b 2b 2b 2b 3b 3b 3b 3b 4b 4b 4b 5b 5b".Split(' '))
            seat.Concealed.Add(TileKinds.FromCode(c));
        e.State.PendingDiscard = Tile.D9;
        e.State.PendingOptions[0] = [ClaimKind.Pung];
        Assert.Null(BotBrain.DecideClaim(e.State, 0, Tile.D9));
    }

    [Fact]
    public void JokerExchanges_finds_swaps()
    {
        var e = new GameEngine(TestCards.Card, 5, 0);
        e.State.Seats[1].Exposures.Add(new Meld([Tile.D5, Tile.D5, Tile.J], true));
        e.State.Seats[0].Concealed.Add(Tile.D5);
        var xs = BotBrain.JokerExchanges(e.State, 0);
        Assert.Single(xs);
        Assert.Equal(Tile.D5, xs[0].natural);
    }

    [Fact]
    public void OnTurn_discards_a_held_tile()
    {
        var e = new GameEngine(TestCards.Card, 5, 0);
        var act = BotBrain.OnTurn(e.State, 0);
        if (act is BotTurnAct.Discard d)
            Assert.True(e.State.Seats[0].Concealed.Contains(d.Tile));
    }
}
