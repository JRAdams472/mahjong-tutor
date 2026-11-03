using MahjongTutor.Core.Coach;
using MahjongTutor.Core.Rules;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Tests;

public class CoachTests
{
    private static GameEngine PlayGame(int seed = 42)
    {
        var e = new GameEngine(TestCards.Card, seed, 0);
        // skip charleston with fixed passes
        while (e.State.Phase == GamePhase.Charleston)
        {
            switch (e.State.CharlestonStage)
            {
                case CharlestonStage.Vote:
                    for (var s = 0; s < 4; s++) e.Vote(s, false); break;
                case CharlestonStage.Courtesy:
                    for (var s = 0; s < 4; s++) e.SubmitCourtesy(s, []); break;
                default:
                    for (var s = 0; s < 4; s++)
                        e.SubmitPass(s, e.State.Seats[s].Concealed.ToList().Take(3).ToList());
                    break;
            }
        }
        return e;
    }

    internal static void SetHand(GameEngine e, int seat, string codes)
    {
        var bag = e.State.Seats[seat].Concealed;
        foreach (var t in TileKinds.All) bag[t] = 0;
        foreach (var c in codes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            bag.Add(TileKinds.FromCode(c));
    }

    [Fact]
    public void TopFits_ranks_closest_viable_hand_first()
    {
        var e = PlayGame();
        // nearly a RUN1 (13 tiles, missing one 1d)
        SetHand(e, 0, "1d 2d 2d 2d 3d 3d 3d 3d 4d 4d 4d 5d 5d");
        var fits = CoachBrain.TopFits(e.State, 0, 3);
        Assert.Equal("RUN1", fits[0].Hand.Id);
        Assert.Equal(1, fits[0].TilesNeeded);
        Assert.True(fits[0].Viable);
    }

    [Fact]
    public void GradeDiscards_marks_optimal_choice_delta_zero()
    {
        var e = PlayGame();
        SetHand(e, 0, "1d 2d 2d 2d 3d 3d 3d 3d 4d 4d 4d 5d 5d 9c");
        var grades = CoachBrain.GradeDiscards(e.State, 0);
        Assert.Equal(Tile.C9, grades[0].Tile);       // the odd tile out is best to drop
        Assert.Equal(0, grades[0].Delta);
        Assert.True(grades.First(g => g.Tile == Tile.D1).Delta > 0);
    }

    [Fact]
    public void SuggestPass_returns_three_held_tiles()
    {
        var e = PlayGame();
        var pass = CoachBrain.SuggestPass(e.State, 0);
        Assert.Equal(3, pass.Count);
        foreach (var t in pass) Assert.True(e.State.Seats[0].Concealed.Contains(t));
    }

    [Fact]
    public void MissedCallNote_flags_needed_tile_for_open_hand()
    {
        var e = PlayGame();
        // open target needs a 1d for the RUN1 pair
        SetHand(e, 0, "2d 2d 2d 3d 3d 3d 3d 4d 4d 4d 5d 5d N");
        e.State.PendingOptions[0] = [ClaimKind.Pung];
        e.State.PendingDiscard = Tile.D1;
        var note = CoachBrain.MissedCallNote(e.State, 0, Tile.D1);
        Assert.NotNull(note);
        Assert.Contains("RUN1", note);
    }

    [Fact]
    public void MissedCallNote_silent_when_tile_not_needed()
    {
        var e = PlayGame();
        SetHand(e, 0, "2d 2d 2d 3d 3d 3d 3d 4d 4d 4d 5d 5d N");
        e.State.PendingOptions[0] = [ClaimKind.Pung];
        e.State.PendingDiscard = Tile.C9;
        Assert.Null(CoachBrain.MissedCallNote(e.State, 0, Tile.C9));
    }

    [Fact]
    public void DiscardNote_silent_for_optimal_and_speaks_for_mistake()
    {
        var e = PlayGame();
        SetHand(e, 0, "1d 2d 2d 2d 3d 3d 3d 3d 4d 4d 4d 5d 5d 9c");
        Assert.Null(CoachBrain.DiscardNote(e.State, 0, Tile.C9));       // best play
        var note = CoachBrain.DiscardNote(e.State, 0, Tile.D1);       // throws away the pair
        Assert.NotNull(note);
        Assert.Contains("9c", note);
    }
}

public class DefenseTests
{
    private static GameEngine PlayGame(int seed = 42)
    {
        var e = new GameEngine(TestCards.Card, seed, 0);
        while (e.State.Phase == GamePhase.Charleston)
        {
            switch (e.State.CharlestonStage)
            {
                case CharlestonStage.Vote:
                    for (var s = 0; s < 4; s++) e.Vote(s, false); break;
                case CharlestonStage.Courtesy:
                    for (var s = 0; s < 4; s++) e.SubmitCourtesy(s, []); break;
                default:
                    for (var s = 0; s < 4; s++)
                        e.SubmitPass(s, e.State.Seats[s].Concealed.ToList().Take(3).ToList());
                    break;
            }
        }
        return e;
    }

    [Fact]
    public void Reads_identifies_opponent_from_exposures()
    {
        var e = PlayGame();
        CoachTests.SetHand(e, 0, "1d 2d 3d 4d 5d 6d 7d 8d 9d N E W F");
        // seat 2 has exposed two kongs of even dots — smells like 2468
        e.State.Seats[2].Exposures.Add(new Meld(
            [Tile.D2, Tile.D2, Tile.D2, Tile.D2], true));
        e.State.Seats[2].Exposures.Add(new Meld(
            [Tile.D4, Tile.D4, Tile.D4, Tile.D4], true));
        var reads = Defense.Reads(e.State, 0);
        var read = Assert.Single(reads, r => r.Seat == 2);
        Assert.Contains(read.Likely, h => h.Id == "EVEN1");
    }

    [Fact]
    public void Reads_empty_before_any_exposures()
    {
        var e = PlayGame();
        Assert.Empty(Defense.Reads(e.State, 0));
    }

    [Fact]
    public void Evaluate_flags_tile_needed_by_opponent()
    {
        var e = PlayGame();
        e.State.Seats[1].Exposures.Add(new Meld(
            [Tile.D2, Tile.D2, Tile.D2, Tile.D2], true));
        e.State.Seats[1].Exposures.Add(new Meld(
            [Tile.D4, Tile.D4, Tile.D4, Tile.D4], true));
        // EVEN1 candidate still wants 6b/8b � an 8-bam discard is live
        var d = Defense.Evaluate(e.State, 0, Tile.B8);
        Assert.NotNull(d);
        Assert.Contains(1, d!.Callers);
    }

    [Fact]
    public void Evaluate_null_for_dead_discards()
    {
        var e = PlayGame();
        Assert.Null(Defense.Evaluate(e.State, 0, Tile.J));
        Assert.Null(Defense.Evaluate(e.State, 0, Tile.F));
    }

    [Fact]
    public void CouldWin_when_one_tile_short()
    {
        var e = PlayGame();
        // seat 3: three kongs covering 12 of EVEN1's 14 slots � one 6b pair away
        e.State.Seats[3].Exposures.Add(new Meld(
            [Tile.D2, Tile.D2, Tile.D2, Tile.D2], true));
        e.State.Seats[3].Exposures.Add(new Meld(
            [Tile.D4, Tile.D4, Tile.D4, Tile.D4], true));
        e.State.Seats[3].Exposures.Add(new Meld(
            [Tile.B8, Tile.B8, Tile.B8, Tile.B8], true));
        var d = Defense.Evaluate(e.State, 0, Tile.B6);
        Assert.NotNull(d);
        Assert.True(d!.CouldWin);
    }
}
