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

    private static void SetHand(GameEngine e, int seat, string codes)
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
