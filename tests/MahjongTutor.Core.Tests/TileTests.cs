using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Tests;

public class TileTests
{
    [Fact]
    public void All_36_kinds_have_roundtrip_codes()
    {
        Assert.Equal(36, TileKinds.All.Length);
        foreach (var t in TileKinds.All)
            Assert.Equal(t, TileKinds.FromCode(t.Code()));
    }

    [Fact]
    public void FullWall_has_152_tiles()
    {
        var wall = TileKinds.FullWall();
        Assert.Equal(TileKinds.WallSize, wall.Total);
        Assert.Equal(8, wall[Tile.F]);
        Assert.Equal(8, wall[Tile.J]);
        Assert.Equal(4, wall[Tile.D5]);
        Assert.Equal(4, wall[Tile.WN]);
    }

    [Theory]
    [InlineData("0", Tile.DW)]   // soap
    [InlineData("WD", Tile.DW)]
    [InlineData("RD", Tile.DR)]
    [InlineData("GD", Tile.DG)]
    [InlineData("5b", Tile.B5)]
    [InlineData("9c", Tile.C9)]
    [InlineData("N", Tile.WN)]
    public void FromCode_parses(string code, Tile expected) =>
        Assert.Equal(expected, TileKinds.FromCode(code));

    [Fact]
    public void DragonFor_suit_mapping()
    {
        Assert.Equal(Tile.DG, TileKinds.DragonFor(Suit.Bam));
        Assert.Equal(Tile.DR, TileKinds.DragonFor(Suit.Crak));
        Assert.Equal(Tile.DW, TileKinds.DragonFor(Suit.Dot));
    }

    [Fact]
    public void TileBag_parse_and_render_roundtrip()
    {
        var bag = TileBag.Parse("5b 5b N RD F J J J");
        Assert.Equal(8, bag.Total);
        Assert.Equal(3, bag.Jokers);
        Assert.Equal(2, bag[Tile.B5]);
        Assert.Equal("5b 5b N RD F J J J", bag.ToString());
    }
}
