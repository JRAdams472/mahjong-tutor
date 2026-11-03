using MahjongTutor.Core.Card;
using MahjongTutor.Core.Rules;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Tests;

public class GameEngineTests
{
    private static GameEngine NewGame(int seed = 42) =>
        new(TestCards.Card, seed, dealer: 0);

    /// <summary>Drives every Charleston stage: 3-tile passes, vote, courtesy.</summary>
    private static void DriveCharleston(GameEngine e, bool second = false, int courtesy = 0)
    {
        while (e.State.Phase == GamePhase.Charleston)
        {
            switch (e.State.CharlestonStage)
            {
                case CharlestonStage.Vote:
                    for (var s = 0; s < 4; s++) e.Vote(s, second);
                    break;
                case CharlestonStage.Courtesy:
                    for (var s = 0; s < 4; s++)
                        e.SubmitCourtesy(s, e.State.Seats[s].Concealed.ToList().Take(courtesy).ToList());
                    break;
                default:
                    for (var s = 0; s < 4; s++)
                        e.SubmitPass(s, e.State.Seats[s].Concealed.ToList().Take(3).ToList());
                    break;
            }
        }
    }

    /// <summary>Passes every seat through an open call window; returns the events.</summary>
    private static List<GameEvent> DrainCalls(GameEngine e)
    {
        var all = new List<GameEvent>();
        while (e.State.Stage == TurnStage.CallWindow)
            foreach (var s in e.State.PendingOptions.Keys.ToList())
                if (!e.State.PendingResponded.Contains(s))
                    all.AddRange(e.Claim(s, null));
        return all;
    }

    /// <summary>Replaces a seat's concealed tiles (test setup shortcut).</summary>
    private static void SetHand(GameEngine e, int seat, string codes)
    {
        var bag = e.State.Seats[seat].Concealed;
        foreach (var t in TileKinds.All) bag[t] = 0;
        foreach (var c in codes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            bag.Add(TileKinds.FromCode(c));
    }

    /// <summary>Adds an exposure directly (test setup shortcut).</summary>
    private static void AddMeld(GameEngine e, int seat, string codes)
    {
        var tiles = codes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(TileKinds.FromCode).ToList();
        e.State.Seats[seat].Exposures.Add(new Meld(tiles, FromDiscard: true));
    }

    private static T[] Events<T>(IEnumerable<GameEvent> ev) =>
        ev.OfType<T>().ToArray();

    // ---- deal & determinism -------------------------------------------------

    [Fact]
    public void Deal_gives_dealer_14_others_13()
    {
        var e = NewGame();
        Assert.Equal(14, e.State.Seats[0].Concealed.Total);
        for (var s = 1; s < 4; s++) Assert.Equal(13, e.State.Seats[s].Concealed.Total);
        Assert.Equal(152 - 53, e.State.WallRemaining);
        Assert.Equal(GamePhase.Charleston, e.State.Phase);
    }

    [Fact]
    public void Same_seed_same_wall()
    {
        var a = new GameEngine(TestCards.Card, 7, 0);
        var b = new GameEngine(TestCards.Card, 7, 0);
        Assert.Equal(a.State.Wall, b.State.Wall);
    }

    // ---- charleston -----------------------------------------------------------

    [Fact]
    public void Charleston_passes_move_tiles_right()
    {
        var e = NewGame();
        var offered = Enumerable.Range(0, 4)
            .Select(s => e.State.Seats[s].Concealed.ToList().Take(3).ToList()).ToArray();
        for (var s = 0; s < 4; s++) e.SubmitPass(s, offered[s]);
        for (var s = 0; s < 4; s++)
            foreach (var t in offered[s])
                Assert.True(e.State.Seats[GameState.Right(s)].Concealed.Contains(t));
        Assert.Equal(CharlestonStage.PassAcross, e.State.CharlestonStage);
    }

    [Fact]
    public void Full_charleston_reaches_play_with_even_hands()
    {
        var e = NewGame();
        DriveCharleston(e);
        Assert.Equal(GamePhase.Play, e.State.Phase);
        Assert.Equal(TurnStage.AwaitDiscard, e.State.Stage);
        for (var s = 0; s < 4; s++)
            Assert.Equal(s == 0 ? 14 : 13, e.State.Seats[s].Concealed.Total);
    }

    [Fact]
    public void Pass_requires_held_tiles_and_three_count()
    {
        var e = NewGame();
        Assert.Throws<RuleException>(() => e.SubmitPass(0, [Tile.D1, Tile.D2]));
        Assert.Throws<RuleException>(() => e.SubmitPass(0, [Tile.D1, Tile.D2, Tile.D3]));
    }

    [Fact]
    public void Courtesy_exchanges_min_of_offers()
    {
        var e = NewGame();
        while (e.State.CharlestonStage != CharlestonStage.Courtesy)
        {
            if (e.State.CharlestonStage == CharlestonStage.Vote)
                for (var s = 0; s < 4; s++) e.Vote(s, false);
            else
                for (var s = 0; s < 4; s++)
                    e.SubmitPass(s, e.State.Seats[s].Concealed.ToList().Take(3).ToList());
        }
        var a = e.State.Seats[0].Concealed.ToList().Take(2).ToList();
        var c = e.State.Seats[2].Concealed.ToList().Take(3).ToList();
        e.SubmitCourtesy(0, a); e.SubmitCourtesy(2, c);
        e.SubmitCourtesy(1, []); e.SubmitCourtesy(3, []);
        foreach (var t in a) Assert.True(e.State.Seats[2].Concealed.Contains(t));
        foreach (var t in c.Take(2)) Assert.True(e.State.Seats[0].Concealed.Contains(t));
        Assert.True(e.State.Seats[2].Concealed.Contains(c[2]));
        Assert.Equal(GamePhase.Play, e.State.Phase);
    }

    // ---- turns ---------------------------------------------------------------

    [Fact]
    public void Discard_advances_turn_when_nobody_can_call()
    {
        var e = NewGame();
        DriveCharleston(e);
        for (var s = 0; s < 4; s++) SetHand(e, s, "9c 9c 8b 7b 6b 5b 4b 3b 2b N N E F F" + (s == 0 ? " 1d" : ""));
        e.Discard(0, Tile.D1);
        Assert.Equal(TurnStage.AwaitDraw, e.State.Stage);
        Assert.Equal(1, e.State.CurrentSeat);
    }

    // ---- calls ---------------------------------------------------------------

    [Fact]
    public void Pung_call_exposes_meld_and_caller_discards()
    {
        var e = NewGame();
        DriveCharleston(e);
        SetHand(e, 0, "5d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        SetHand(e, 1, "5d 5d 9c 8b 7b 6b 5b 4b 3b N N E F");
        SetHand(e, 2, "1d 2d 3d 4b 5b 6b 7b 8b 9b N E W F");
        SetHand(e, 3, "1d 2d 3d 4b 5b 6b 7b 8b 9b N E W F");
        e.Discard(0, Tile.D5);

        Assert.Equal(TurnStage.CallWindow, e.State.Stage);
        Assert.Contains(ClaimKind.Pung, e.State.PendingOptions[1]);
        var ev = e.Claim(1, ClaimKind.Pung).Concat(DrainCalls(e)).ToList();
        var meld = Assert.Single(Events<MeldExposed>(ev)).Meld;
        Assert.Equal(3, meld.Size);
        Assert.Equal(3, meld.Tiles.Count(t => t == Tile.D5));
        Assert.Equal(1, e.State.CurrentSeat);
        Assert.Equal(TurnStage.AwaitDiscard, e.State.Stage);
    }

    [Fact]
    public void Nearer_seat_wins_competing_calls()
    {
        var e = NewGame();
        DriveCharleston(e);
        SetHand(e, 0, "5d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        SetHand(e, 1, "5d 5d 9c 8b 7b 6b 5b 4b 3b N N E F");
        SetHand(e, 2, "5d 5d 9c 8b 7b 6b 5b 4b 3b N N E F");
        SetHand(e, 3, "1d 2d 3d 4b 5b 6b 7b 8b 9b N E W F");
        e.Discard(0, Tile.D5);
        e.Claim(2, ClaimKind.Pung);
        e.Claim(1, ClaimKind.Pung);
        DrainCalls(e);
        Assert.Equal(1, e.State.CurrentSeat);
        Assert.Single(e.State.Seats[1].Exposures);
        Assert.Empty(e.State.Seats[2].Exposures);
    }

    [Fact]
    public void Mahjong_claim_beats_pung()
    {
        var e = NewGame();
        DriveCharleston(e);
        SetHand(e, 0, "1d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        SetHand(e, 1, "1d 1d 9c 8b 7b 6b 5b 4b 3b N N E F");
        // seat 2: one 1d short of RUN1 mahjong
        SetHand(e, 2, "1d 2d 2d 2d 3d 3d 3d 3d 4d 4d 4d 5d 5d");
        SetHand(e, 3, "1d 2d 3d 4b 5b 6b 7b 8b 9b E W F F");
        e.Discard(0, Tile.D1);
        var ev = e.Claim(1, ClaimKind.Pung).Concat(e.Claim(2, ClaimKind.Mahjong))
            .Concat(DrainCalls(e)).ToList();
        Assert.Single(Events<MahjongDeclared>(ev));
        Assert.Equal(GamePhase.Finished, e.State.Phase);
        Assert.Equal(2, e.State.WinnerSeat);
        Assert.Equal("RUN1", e.State.WinningHand!.Id);
    }

    [Fact]
    public void Cannot_call_flower_or_joker()
    {
        var e = NewGame();
        DriveCharleston(e);
        SetHand(e, 0, "F J 9c 8b 7b 6b 5b 4b 3b N N E F F");
        SetHand(e, 1, "F F F 8b 7b 6b 5b 4b 3b N N E F");
        e.Discard(0, Tile.F);
        Assert.Equal(TurnStage.AwaitDraw, e.State.Stage); // no window — dead tile
        Assert.Equal(1, e.State.CurrentSeat);
    }

    [Fact]
    public void Quint_call_fills_with_jokers()
    {
        var e = NewGame();
        DriveCharleston(e);
        SetHand(e, 0, "5d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        SetHand(e, 1, "5d J J J 8b 7b 6b 5b 4b 3b N N E");
        SetHand(e, 2, "1d 2d 3d 4b 5b 6b 7b 8b 9b N E W F");
        SetHand(e, 3, "1d 2d 3d 4b 5b 6b 7b 8b 9b N E W F");
        e.Discard(0, Tile.D5);
        Assert.Contains(ClaimKind.Quint, e.State.PendingOptions[1]);
        var ev = e.Claim(1, ClaimKind.Quint).Concat(DrainCalls(e)).ToList();
        var meld = Assert.Single(Events<MeldExposed>(ev)).Meld;
        Assert.Equal(5, meld.Size);
        Assert.Equal(2, meld.Tiles.Count(t => t == Tile.D5));
        Assert.Equal(3, meld.Tiles.Count(t => t == Tile.J));
    }

    [Fact]
    public void Cannot_call_without_natural_partner()
    {
        var e = NewGame();
        DriveCharleston(e);
        SetHand(e, 0, "5d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        SetHand(e, 1, "J J J J 8b 7b 6b 5b 4b 3b N N E");   // all jokers, no 5d
        SetHand(e, 2, "5d 5d 5d 4b 5b 6b 7b 8b 9b N E W F");   // gives window a reason to open
        SetHand(e, 3, "1d 2d 3d 4b 5b 6b 7b 8b 9b N E W F");
        e.Discard(0, Tile.D5);
        Assert.Empty(e.State.PendingOptions[1]);   // seated in window but no legal claim
        Assert.Throws<RuleException>(() => e.Claim(1, ClaimKind.Pung));
        Assert.Throws<RuleException>(() => e.Claim(1, ClaimKind.Kong));
    }

    // ---- joker exchange ---------------------------------------------------------

    [Fact]
    public void Joker_exchange_swaps_natural_for_joker()
    {
        var e = NewGame();
        DriveCharleston(e);   // play phase, seat 0 AwaitDiscard
        AddMeld(e, 1, "5d 5d J J");
        SetHand(e, 0, "5d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        var ev = e.ExchangeJoker(0, 1, 0, Tile.D5);
        Assert.Single(Events<JokerExchanged>(ev));
        Assert.Equal(3, e.State.Seats[1].Exposures[0].Tiles.Count(t => t == Tile.D5));
        Assert.Equal(1, e.State.Seats[0].Concealed[Tile.J]);
    }

    [Fact]
    public void Joker_exchange_rejects_wrong_tile_and_wrong_turn()
    {
        var e = NewGame();
        DriveCharleston(e);
        AddMeld(e, 1, "5d 5d J J");
        SetHand(e, 0, "9d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        Assert.Throws<RuleException>(() => e.ExchangeJoker(0, 1, 0, Tile.D9)); // doesn't fit meld
        Assert.Throws<RuleException>(() => e.ExchangeJoker(1, 1, 0, Tile.D5)); // not your turn
    }

    // ---- mahjong & dead hands ---------------------------------------------------

    [Fact]
    public void Self_picked_mahjong_ends_game_and_pays_double()
    {
        var e = NewGame();
        DriveCharleston(e);
        // seat 0: 13 tiles of RUN1 (bams), needs one more 1b
        SetHand(e, 0, "1b 2b 2b 2b 3b 3b 3b 3b 4b 4b 4b 5b 5b 9c");
        SetHand(e, 1, "1d 2d 3d 4d 5d 6d 7d 8d 9d N E W F");
        SetHand(e, 2, "1d 2d 3d 4d 5d 6d 7d 8d 9d N E W F");
        SetHand(e, 3, "1d 2d 3d 4d 5d 6d 7d 8d 9d N E W F");
        e.Discard(0, Tile.C9);
        DrainCalls(e);
        for (var s = 1; s < 4; s++)
        {
            e.Draw(s);
            e.Discard(s, e.State.Seats[s].Concealed.ToList().First());
            DrainCalls(e);
        }
        e.State.Wall[e.State.WallIndex] = Tile.B1;
        e.Draw(0);
        var ev = e.DeclareMahjong(0);
        Assert.Single(Events<MahjongDeclared>(ev));
        var end = Assert.Single(Events<GameEnded>(ev));
        Assert.Equal(0, end.WinnerSeat);
        Assert.True(e.State.SelfPicked);
        // RUN1 = 25, self-picked → each pays 50; no jokers in hand → double → 100 each
        Assert.Equal(-100, end.Payouts[1]);
        Assert.Equal(300, end.Payouts[0]);
    }

    [Fact]
    public void Miscalled_mahjong_on_draw_is_dead_hand()
    {
        var e = NewGame();
        DriveCharleston(e);
        SetHand(e, 0, "5d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        SetHand(e, 1, "1b 2b 3b 4b 5b 6b 7b 8b 9b N E W F");
        e.Discard(0, Tile.D5);
        DrainCalls(e);
        e.Draw(1);
        var ev = e.DeclareMahjong(1);
        Assert.Single(Events<HandDeclaredDead>(ev));
        Assert.True(e.State.Seats[1].Dead);
        Assert.Equal(GamePhase.Play, e.State.Phase);
    }

    [Fact]
    public void Miscalled_mahjong_on_discard_is_dead_hand()
    {
        var e = NewGame();
        DriveCharleston(e);
        SetHand(e, 0, "5d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        // seat 1 can pung the 5d but wrongly declares mahjong instead
        SetHand(e, 1, "5d 5d 1b 2b 3b 4b 6b 7b 8b 9b N E W");
        SetHand(e, 2, "1d 2d 3d 4b 5b 6b 7b 8b 9b N E W F");
        SetHand(e, 3, "1d 2d 3d 4b 5b 6b 7b 8b 9b N E W F");
        e.Discard(0, Tile.D5);
        var ev = e.Claim(1, ClaimKind.Mahjong).Concat(DrainCalls(e)).ToList();
        Assert.Single(Events<HandDeclaredDead>(ev));
        Assert.True(e.State.Seats[1].Dead);
        Assert.Equal(GamePhase.Play, e.State.Phase);
        Assert.Equal(TurnStage.AwaitDraw, e.State.Stage);
        Assert.Equal(1, e.State.CurrentSeat);   // turn moved on
        Assert.Contains(Tile.D5, e.State.Seats[0].Discards);  // tile back in the pool
    }

    [Fact]
    public void Dead_hand_cannot_claim()
    {
        var e = NewGame();
        DriveCharleston(e);
        e.DeclareDead(1, "wrong tile count");
        SetHand(e, 0, "5d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        SetHand(e, 1, "5d 5d 9c 8b 7b 6b 5b 4b 3b N N E F");
        SetHand(e, 2, "5d 5d 5d 4b 5b 6b 7b 8b 9b N E W F");
        SetHand(e, 3, "1d 2d 3d 4b 5b 6b 7b 8b 9b N E W F");
        e.Discard(0, Tile.D5);
        Assert.False(e.State.PendingOptions.ContainsKey(1));
        Assert.Contains(ClaimKind.Pung, e.State.PendingOptions[2]); // window still works for others
    }

    // ---- wall game & scoring -----------------------------------------------------

    [Fact]
    public void Empty_wall_ends_game()
    {
        var e = NewGame();
        DriveCharleston(e);
        SetHand(e, 0, "9d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        e.Discard(0, Tile.D9);
        DrainCalls(e);
        e.State.WallIndex = e.State.Wall.Length;  // exhaust
        var ev = e.Draw(1);
        var end = Assert.Single(Events<GameEnded>(ev));
        Assert.Null(end.WinnerSeat);
        Assert.All(end.Payouts.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Discard_win_scores_discarder_double_jokerless_again()
    {
        var e = NewGame();
        DriveCharleston(e);
        SetHand(e, 0, "1d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        SetHand(e, 1, "9c 9c 8b 7b 6b 5b 4b 3b 2b N N E F");
        // seat 2: one 1d short of RUN1 — all naturals (jokerless win)
        SetHand(e, 2, "1d 2d 2d 2d 3d 3d 3d 3d 4d 4d 4d 5d 5d");
        SetHand(e, 3, "9c 9c 8b 7b 6b 5b 4b 3b 2b N N E F");
        e.Discard(0, Tile.D1);
        var ev = e.Claim(2, ClaimKind.Mahjong).Concat(DrainCalls(e)).ToList();
        var end = Assert.Single(Events<GameEnded>(ev));
        // RUN1 = 25, discard win → discarder pays 50, others 25; jokerless ×2
        Assert.Equal(-100, end.Payouts[0]);
        Assert.Equal(-50, end.Payouts[1]);
        Assert.Equal(-50, end.Payouts[3]);
        Assert.Equal(200, end.Payouts[2]);
    }

    [Fact]
    public void Joker_in_winning_hand_loses_the_bonus()
    {
        var e = NewGame();
        DriveCharleston(e);
        SetHand(e, 0, "1d 9c 8b 7b 6b 5b 4b 3b 2b N N E F F");
        SetHand(e, 1, "9c 9c 8b 7b 6b 5b 4b 3b 2b N N E F");
        // seat 2 completes RUN1 with a joker in the 3333 kong — no jokerless bonus
        SetHand(e, 2, "1d 2d 2d 2d 3d 3d 3d J 4d 4d 4d 5d 5d");
        SetHand(e, 3, "9c 9c 8b 7b 6b 5b 4b 3b 2b N N E F");
        e.Discard(0, Tile.D1);
        var ev = e.Claim(2, ClaimKind.Mahjong).Concat(DrainCalls(e)).ToList();
        var end = Assert.Single(Events<GameEnded>(ev));
        Assert.Equal(-50, end.Payouts[0]);
        Assert.Equal(100, end.Payouts[2]);
    }

    // ---- scripted replay ----------------------------------------------------------

    [Fact]
    public void Scripted_full_game_is_deterministic()
    {
        var traceA = PlayScripted(new GameEngine(TestCards.Card, 123, 0));
        var traceB = PlayScripted(new GameEngine(TestCards.Card, 123, 0));
        Assert.Equal(traceA, traceB);
    }

    /// <summary>Plays a naive deterministic game: pass first tiles, discard last, pass all calls.</summary>
    private static string PlayScripted(GameEngine e)
    {
        var log = new List<string>();
        DriveCharleston(e);
        var guard = 0;
        while (e.State.Phase == GamePhase.Play && guard++ < 1000)
        {
            switch (e.State.Stage)
            {
                case TurnStage.AwaitDraw:
                    log.AddRange(e.Draw(e.State.CurrentSeat).Select(Describe));
                    break;
                case TurnStage.AwaitDiscard:
                {
                    var t = e.State.Seats[e.State.CurrentSeat].Concealed.ToList().Last();
                    log.AddRange(e.Discard(e.State.CurrentSeat, t).Select(Describe));
                    break;
                }
                case TurnStage.CallWindow:
                    log.AddRange(DrainCalls(e).Select(Describe));
                    break;
            }
        }
        return string.Join('\n', log);
    }

    private static string Describe(GameEvent ev) => ev switch
    {
        TileDrawn d => $"draw s{d.Seat} {d.Tile.Code()}",
        TileDiscarded d => $"disc s{d.Seat} {d.Tile.Code()}",
        GameEnded g => $"end {g.Reason} w{g.WinnerSeat}",
        _ => ev.GetType().Name,
    };
}
