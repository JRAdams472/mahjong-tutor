namespace MahjongTutor.Core.Tiles;

/// <summary>
/// A tile kind in an American mahjong set (not a physical instance — the set
/// holds 4 copies of suited/wind/dragon tiles, 8 flowers, 8 jokers: 152 total).
/// </summary>
public enum Tile : byte
{
    // Suited tiles: three suits x ranks 1-9.
    D1, D2, D3, D4, D5, D6, D7, D8, D9,
    B1, B2, B3, B4, B5, B6, B7, B8, B9,
    C1, C2, C3, C4, C5, C6, C7, C8, C9,

    // Winds.
    WN, WE, WS, WW,

    // Dragons: red, green, white ("soap").
    DR, DG, DW,

    F,  // flower (all 8 are interchangeable in play)
    J,  // joker
}

public enum Suit : byte { Dot, Bam, Crak }

public static class TileKinds
{
    public const int KindCount = 36;
    public const int WallSize = 152;

    /// <summary>All 36 tile kinds, in enum order.</summary>
    public static readonly Tile[] All =
        Enumerable.Range(0, KindCount).Select(i => (Tile)i).ToArray();

    public static bool IsSuited(this Tile t) => t <= Tile.C9;
    public static bool IsWind(this Tile t) => t is >= Tile.WN and <= Tile.WW;
    public static bool IsDragon(this Tile t) => t is >= Tile.DR and <= Tile.DW;
    public static bool IsFlower(this Tile t) => t == Tile.F;
    public static bool IsJoker(this Tile t) => t == Tile.J;
    public static bool IsHonor(this Tile t) => IsWind(t) || IsDragon(t);

    /// <summary>Suit of a suited tile (undefined for honors).</summary>
    public static Suit SuitOf(this Tile t) => (Suit)((int)t / 9);

    /// <summary>Rank 1-9 of a suited tile (undefined for honors).</summary>
    public static int RankOf(this Tile t) => (int)t % 9 + 1;

    /// <summary>
    /// Dragon conventionally matching a suit, used by card notation where a
    /// colored "D" means "the dragon of this suit": Bam→Green, Crak→Red, Dot→White.
    /// </summary>
    public static Tile DragonFor(this Suit s) => s switch
    {
        Suit.Bam => Tile.DG,
        Suit.Crak => Tile.DR,
        _ => Tile.DW,
    };

    public static Tile Of(Suit suit, int rank)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rank, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rank, 9);
        return (Tile)((int)suit * 9 + rank - 1);
    }

    /// <summary>
    /// Short canonical code used in JSON and the UI:
    /// "1d".."9d", "1b".."9b", "1c".."9c", "N","E","S","W", "RD","GD","WD", "F", "J".
    /// </summary>
    public static string Code(this Tile t) => t switch
    {
        >= Tile.D1 and <= Tile.D9 => $"{t.RankOf()}d",
        >= Tile.B1 and <= Tile.B9 => $"{t.RankOf()}b",
        >= Tile.C1 and <= Tile.C9 => $"{t.RankOf()}c",
        Tile.WN => "N", Tile.WE => "E", Tile.WS => "S", Tile.WW => "W",
        Tile.DR => "RD", Tile.DG => "GD", Tile.DW => "WD",
        Tile.F => "F", Tile.J => "J",
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    /// <summary>Parses a code produced by <see cref="Code"/>.</summary>
    public static Tile FromCode(string code)
    {
        if (TryFromCode(code, out var t)) return t;
        throw new FormatException($"unknown tile code '{code}'");
    }

    public static bool TryFromCode(string? code, out Tile tile)
    {
        tile = default;
        if (string.IsNullOrWhiteSpace(code)) return false;
        code = code.Trim();
        if (code.Length == 1)
        {
            switch (char.ToUpperInvariant(code[0]))
            {
                case 'N': tile = Tile.WN; return true;
                case 'E': tile = Tile.WE; return true;
                case 'S': tile = Tile.WS; return true;
                case 'W': tile = Tile.WW; return true;
                case 'F': tile = Tile.F; return true;
                case 'J': tile = Tile.J; return true;
                case '0': tile = Tile.DW; return true;   // "0" = soap, as on the card
            }
        }
        if (code.Length == 2)
        {
            var a = char.ToUpperInvariant(code[0]);
            var b = char.ToLowerInvariant(code[1]);
            switch (a)
            {
                case 'R' when b == 'd': tile = Tile.DR; return true;
                case 'G' when b == 'd': tile = Tile.DG; return true;
                case 'W' when b == 'd': tile = Tile.DW; return true;
            }
            if (a is >= '1' and <= '9')
            {
                var rank = a - '0';
                switch (b)
                {
                    case 'd': tile = Of(Suit.Dot, rank); return true;
                    case 'b': tile = Of(Suit.Bam, rank); return true;
                    case 'c': tile = Of(Suit.Crak, rank); return true;
                }
            }
        }
        return false;
    }

    /// <summary>A full 152-tile wall as counts per kind.</summary>
    public static TileBag FullWall()
    {
        var bag = new TileBag();
        foreach (var t in All)
            bag.Add(t, t switch { Tile.F or Tile.J => 8, _ => 4 });
        return bag;
    }
}
