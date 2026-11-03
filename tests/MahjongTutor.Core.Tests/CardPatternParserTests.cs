using MahjongTutor.Core.Card;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Tests;

public class CardPatternParserTests
{
    private static List<SlotSpec> Slots(string pattern) =>
        CardPatternParser.Parse(pattern).Groups[0].Options[0].Slots.ToList();

    [Fact]
    public void Trailing_suit_letter_distributes_to_all_ranks()
    {
        var slots = Slots("1111a");
        Assert.Equal(4, slots.Count);
        Assert.All(slots, s =>
        {
            var r = Assert.IsType<SlotValue.Rank>(s.Value);
            Assert.Equal(1, r.Value);
            Assert.Equal(SuitReqKind.Var, s.Suit.Kind);
            Assert.Equal('a', s.Suit.Var);
        });
    }

    [Fact]
    public void Dragon_suffix_maps_to_suit_dragon()
    {
        var slots = Slots("DDDDb");
        Assert.Equal(4, slots.Count);
        Assert.All(slots, s =>
        {
            Assert.IsType<SlotValue.SuitDragon>(s.Value);
            Assert.Equal('b', s.Suit.Var);
        });
    }

    [Fact]
    public void Year_pattern_soap_and_suited_digits()
    {
        // "2025a" -> 2a, soap, 2a, 5a  (the 'a' skips the soap)
        var slots = Slots("2025a");
        Assert.Equal(4, slots.Count);
        Assert.Equal(new SlotValue.Rank(2), slots[0].Value);
        Assert.Equal(new SlotValue.Fixed(Tile.DW), slots[1].Value);
        Assert.Equal(new SlotValue.Rank(2), slots[2].Value);
        Assert.Equal(new SlotValue.Rank(5), slots[3].Value);
        Assert.Equal(SuitReqKind.None, slots[1].Suit.Kind);
        foreach (var i in new[] { 0, 2, 3 })
            Assert.Equal('a', slots[i].Suit.Var);
    }

    [Fact]
    public void Rankvar_run_takes_shared_offset()
    {
        // "XX1a" = two tiles of rank X+1 in suit a
        var slots = Slots("XX1a");
        Assert.Equal(2, slots.Count);
        Assert.All(slots, s =>
        {
            var v = Assert.IsType<SlotValue.RankVar>(s.Value);
            Assert.Equal('X', v.Name);
            Assert.Equal(1, v.Offset);
            Assert.Equal('a', s.Suit.Var);
        });
    }

    [Fact]
    public void Rankvar_mixed_offsets_in_one_group()
    {
        // "X0aX1aX2a" = r, r+1, r+2 in suit a (a three-tile consecutive block)
        var slots = Slots("X0aX1aX2a");
        Assert.Equal(new[] { 0, 1, 2 },
            slots.Select(s => Assert.IsType<SlotValue.RankVar>(s.Value).Offset).ToArray());
    }

    [Fact]
    public void Alternation_creates_group_options()
    {
        var group = CardPatternParser.Parse("NNNN|EEEE").Groups[0];
        Assert.Equal(2, group.Options.Count);
        Assert.Equal(4, group.Options[0].Size);
        Assert.Equal(Tile.WN, Assert.IsType<SlotValue.Fixed>(group.Options[0].Slots[0].Value).Tile);
        Assert.Equal(Tile.WE, Assert.IsType<SlotValue.Fixed>(group.Options[1].Slots[0].Value).Tile);
    }

    [Fact]
    public void Bare_digits_without_any_suit_become_any()
    {
        var slots = Slots("222");
        Assert.All(slots, s => Assert.Equal(SuitReqKind.Any, s.Suit.Kind));
    }

    [Fact]
    public void Whole_pattern_groups_and_tile_count()
    {
        var p = CardPatternParser.Parse("11a 333a 5555a 777a 99a");
        Assert.Equal(5, p.Groups.Count);
        Assert.Equal(14, p.TileCount);
        Assert.Equal(new[] { 2, 3, 4, 3, 2 }, p.Groups.Select(g => g.MaxSize).ToArray());
    }

    [Fact]
    public void Fixed_honors_and_flowers()
    {
        var slots = Slots("FNESWRG0");
        var tiles = slots.Select(s => Assert.IsType<SlotValue.Fixed>(s.Value).Tile).ToArray();
        Assert.Equal(new[] { Tile.F, Tile.WN, Tile.WE, Tile.WS, Tile.WW, Tile.DR, Tile.DG, Tile.DW }, tiles);
        Assert.All(slots, s => Assert.Equal(SuitReqKind.None, s.Suit.Kind));
    }

    [Theory]
    [InlineData("")]
    [InlineData("D")]          // dragon with no suit anywhere
    [InlineData("z")]          // stray suit letter
    [InlineData("1a |")]       // empty alternative
    [InlineData("1Q")]         // unknown char (Q not a rank var)
    [InlineData("5!a")]
    public void Parse_errors(string pattern)
    {
        Assert.Throws<PatternParseException>(() => CardPatternParser.Parse(pattern));
    }
}
