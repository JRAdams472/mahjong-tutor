using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Server.Tiles;

/// <summary>
/// A tile set = a folder of SVGs keyed:
/// d1-d9, b1-b9, c1-c9, wn,we,ws,ww, dr,dg,dw, f1-f8, j, back.
/// Bundled sets under wwwroot/tiles; uploads (Phase 13) under a user dir.
/// </summary>
public sealed class TileSetManager
{
    public static readonly string[] Keys = BuildKeys();

    private static string[] BuildKeys()
    {
        var k = new List<string>();
        foreach (var s in new[] { "d", "b", "c" })
            for (var r = 1; r <= 9; r++) k.Add($"{s}{r}");
        k.AddRange(["wn", "we", "ws", "ww", "dr", "dg", "dw"]);
        for (var f = 1; f <= 8; f++) k.Add($"f{f}");
        k.AddRange(["j", "back"]);
        return [.. k];
    }

    public string RootDir { get; }

    public TileSetManager(IWebHostEnvironment env) =>
        RootDir = Path.Combine(env.WebRootPath, "tiles");

    public List<string> ListSets() =>
        !Directory.Exists(RootDir) ? [] :
        Directory.GetDirectories(RootDir).Where(IsValidSet)
            .Select(d => Path.GetFileName(d)!).ToList();

    public bool IsValidSet(string dir) =>
        Keys.All(k => File.Exists(Path.Combine(dir, k + ".svg"))
                  || File.Exists(Path.Combine(dir, k + ".png")));

    /// <summary>Web path of a tile image in a named set ("tiles/default/d5.svg").</summary>
    public string TilePath(Tile tile, string set = "default", int variant = 0)
    {
        var key = KeyOf(tile, variant);
        var dir = Path.Combine(RootDir, set);
        return File.Exists(Path.Combine(dir, key + ".svg"))
            ? $"tiles/{set}/{key}.svg" : $"tiles/{set}/{key}.png";
    }

    /// <summary>Tile kind → key. Flowers cycle f1-f8 via <paramref name="variant"/>.</summary>
    public static string KeyOf(Tile t, int variant = 0) => t switch
    {
        >= Tile.D1 and <= Tile.D9 => $"d{t.RankOf()}",
        >= Tile.B1 and <= Tile.B9 => $"b{t.RankOf()}",
        >= Tile.C1 and <= Tile.C9 => $"c{t.RankOf()}",
        Tile.WN => "wn", Tile.WE => "we", Tile.WS => "ws", Tile.WW => "ww",
        Tile.DR => "dr", Tile.DG => "dg", Tile.DW => "dw",
        Tile.F => $"f{variant % 8 + 1}",
        Tile.J => "j",
        _ => "back",
    };
}
