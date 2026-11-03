using MahjongTutor.Core.Card;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Tests;

public class CardMatcherTests
{
    private static HandFit Fit(string rack, string handId) =>
        CardMatcher.BestFit(TileBag.Parse(rack), TestCards.Hand(handId))!;

    [Fact]
    public void Exact_match_is_mahjong()
    {
        // RUN1 = 11a 222a 3333a 444a 55a, all one suit
        var fit = Fit("1d 1d 2d 2d 2d 3d 3d 3d 3d 4d 4d 4d 5d 5d", "RUN1");
        Assert.True(fit.IsMahjong);
        Assert.Equal(0, fit.TilesNeeded);
        Assert.Equal(14, fit.MatchedTiles);
    }

    [Fact]
    public void Joker_fills_kong_but_not_pair()
    {
        // same hand but the 5-pair is missing one — a joker can't fill a pair
        var fit = Fit("1d 1d 2d 2d 2d 3d 3d 3d 3d 4d 4d 4d 5d J", "RUN1");
        Assert.Equal(1, fit.TilesNeeded);
        Assert.Equal(0, fit.JokersUsed);

        // but a joker CAN fill the fourth of the 3333a kong
        var fit2 = Fit("1d 1d 2d 2d 2d 3d 3d 3d J 4d 4d 4d 5d 5d", "RUN1");
        Assert.True(fit2.IsMahjong);
        Assert.Equal(1, fit2.JokersUsed);
        Assert.Equal([Tile.D3], fit2.JokerFill);
    }

    [Fact]
    public void Group_cannot_be_all_jokers()
    {
        // the 3333a kong would need all four slots from jokers — must keep a natural
        var fit = Fit("1d 1d 2d 2d 2d J J J J 4d 4d 4d 5d 5d", "RUN1");
        Assert.Equal(1, fit.TilesNeeded);
        Assert.Equal(3, fit.JokersUsed);
    }

    [Fact]
    public void Like_numbers_needs_same_rank_distinct_suits()
    {
        // LN1 = FF XXXX0a XXXX0b DDDDc : pair F, two like-number kongs, dragon kong
        var fit = Fit("F F 5d 5d 5d 5d 5b 5b 5b 5b RD RD RD RD", "LN1");
        Assert.True(fit.IsMahjong);   // X=5, a=dot, b=bam, c=crak→red dragon
    }

    [Fact]
    public void Quint_hand_accepts_joker_for_fifth_tile()
    {
        // QUINT1 = FF XXXXX0a XXX0b XXXX0c — only 4 natural copies exist
        var fit = Fit("F F 7d 7d 7d 7d J 7b 7b 7b 7c 7c 7c 7c", "QUINT1");
        Assert.True(fit.IsMahjong);
        Assert.Equal(1, fit.JokersUsed);
    }

    [Fact]
    public void Alternation_group_picks_matching_option()
    {
        // ALT1 = FF XXXX0a NNNN|EEEE XXXX1a
        var fit = Fit("F F 3d 3d 3d 3d E E E E 4d 4d 4d 4d", "ALT1");
        Assert.True(fit.IsMahjong); // X=3, middle group resolved to EEEE
    }

    [Fact]
    public void Mixed_singles_block_matches()
    {
        // NEWS1 = FFF N E W S 2222a 222a
        var fit = Fit("F F F N E S W 2d 2d 2d 2d 2d 2d 2d", "NEWS1");
        Assert.True(fit.IsMahjong);
    }

    [Fact]
    public void Year_hand_with_soap()
    {
        // YEAR1 = NN EE WW SSSS 2025a
        var fit = Fit("N N E E W W S S S S 2d WD 2d 5d", "YEAR1");
        Assert.True(fit.IsMahjong);
    }

    [Fact]
    public void Missing_tiles_reported()
    {
        // one tile short of RUN1 in bams — needs the other 5b
        var fit = Fit("1b 1b 2b 2b 2b 3b 3b 3b 3b 4b 4b 4b 5b 9d", "RUN1");
        Assert.Equal(1, fit.TilesNeeded);
        Assert.Equal([Tile.B5], fit.Missing);
    }

    [Fact]
    public void Analyze_ranks_best_fit_first()
    {
        var fits = CardMatcher.Analyze(
            TileBag.Parse("1d 1d 2d 2d 2d 3d 3d 3d 3d 4d 4d 4d 5d 5d"), TestCards.Card);
        Assert.Equal("RUN1", fits[0].Hand.Id);
        Assert.Equal(0, fits[0].TilesNeeded);
        Assert.True(fits.All(f => f.TilesNeeded >= fits[0].TilesNeeded || f == fits[0]));
    }

    [Fact]
    public void Pool_viability_flags_dead_hands()
    {
        // rack needs a second 5d for RUN1; if no 5d remains in the pool, not viable
        var pool = TileKinds.FullWall();
        var rack = TileBag.Parse("1d 1d 2d 2d 2d 3d 3d 3d 3d 4d 4d 4d 5d 9d");
        var fit = CardMatcher.BestFit(rack, TestCards.Hand("RUN1"), pool);
        Assert.True(fit!.Viable); // three 5d still available

        pool[Tile.D5] = 0;        // all remaining 5-dots are gone
        fit = CardMatcher.BestFit(rack, TestCards.Hand("RUN1"), pool);
        Assert.False(fit!.Viable);
    }

    [Fact]
    public void Exposed_groups_must_match_pattern()
    {
        // ALT1 = FF XXXX0a NNNN|EEEE XXXX1a; a NNNN exposure is legal, WWWW is not
        var rack = TileBag.Parse("F F 3d 3d 3d 3d 4d 4d 4d 4d");
        var north = new List<IReadOnlyList<Tile>> { new List<Tile> { Tile.WN, Tile.WN, Tile.WN, Tile.WN } };
        var west = new List<IReadOnlyList<Tile>> { new List<Tile> { Tile.WW, Tile.WW, Tile.WW, Tile.WW } };

        var fit = CardMatcher.BestFit(rack, TestCards.Hand("ALT1"), exposedGroups: north);
        Assert.True(fit!.IsMahjong);
        fit = CardMatcher.BestFit(rack, TestCards.Hand("ALT1"), exposedGroups: west);
        Assert.Null(fit);
    }

    [Fact]
    public void Two_suit_hand_matches_two_suits()
    {
        // EVEN1 = 2222a 4444a 66b 8888b — a and b must differ
        var fit = Fit("2d 2d 2d 2d 4d 4d 4d 4d 6b 6b 8b 8b 8b 8b", "EVEN1");
        Assert.True(fit.IsMahjong);
        var sa = fit.SuitAssignment;
        Assert.NotEqual(sa['a'], sa['b']);
    }

    [Fact]
    public void Concealed_mirrored_runs()
    {
        // RUN3 (concealed) = FF X0a XX1a XXX2a X0b XX1b XXX2b — two runs, two suits
        var fit = Fit("F F 3d 4d 4d 5d 5d 5d 3b 4b 4b 5b 5b 5b", "RUN3");
        Assert.True(fit.IsMahjong);
        Assert.True(fit.Hand.Concealed);
    }

    [Fact]
    public void Analyze_with_exposure_skips_concealed_hands()
    {
        // Regression: concealed hands + exposures used to spin the option
        // enumerator forever (continue skipped the index advance).
        var rack = TileBag.Parse("1b 2b 3b 4b 5b 6b 7b 8b 9b N E");
        var exposed = new List<IReadOnlyList<Tile>> { new List<Tile> { Tile.D5, Tile.D5, Tile.D5 } };
        var fits = CardMatcher.Analyze(rack, TestCards.Card, exposedGroups: exposed);
        Assert.DoesNotContain(fits, f => f.Hand.Concealed);
    }
}
