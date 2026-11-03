using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Server.Tiles;

/// <summary>
/// A tile set = a folder of SVGs/PNGs keyed:
/// d1-d9, b1-b9, c1-c9, wn,we,ws,ww, dr,dg,dw, f1-f8, j, back.
/// Bundled sets under wwwroot/tiles; uploads under a writable dir
/// (TILESET_DIR, default ./data/tilesets) served via /tilefile/{set}/{key}.
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
    public string UserDir { get; }

    /// <summary>The currently selected set (app-wide for the LAN app).</summary>
    public string CurrentSet { get; set; } = "default";

    public TileSetManager(IWebHostEnvironment env, IConfiguration cfg)
    {
        RootDir = Path.Combine(env.WebRootPath, "tiles");
        UserDir = cfg["TILESET_DIR"]
            ?? Path.Combine(env.ContentRootPath, "data", "tilesets");
        Directory.CreateDirectory(UserDir);
    }

    /// <summary>All valid set names (bundled + user uploads).</summary>
    public List<string> ListSets()
    {
        var names = new HashSet<string>();
        Add(names, RootDir);
        Add(names, UserDir);
        return [.. names.OrderBy(n => n)];

        static void Add(HashSet<string> names, string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var d in Directory.GetDirectories(dir))
                if (IsValidSet(d)) names.Add(Path.GetFileName(d));
        }
    }

    public static bool IsValidSet(string dir) =>
        Keys.All(k => File.Exists(Path.Combine(dir, k + ".svg"))
                  || File.Exists(Path.Combine(dir, k + ".png")));

    /// <summary>Web path for a tile image. User sets route via /tilefile.</summary>
    public string TilePath(Tile tile, string? set = null, int variant = 0)
    {
        set ??= CurrentSet;
        var key = KeyOf(tile, variant);
        var ext = ExtFor(set, key);
        return ext == null
            ? $"tiles/default/{key}.svg"                     // missing key → fall back to bundled art
            : File.Exists(Path.Combine(RootDir, set, key + ext))
                ? $"tiles/{set}/{key}{ext}"
                : $"tilefile/{set}/{key}{ext}";
    }

    /// <summary>Filesystem path for a user-set file (null if bundled/missing).</summary>
    public string? UserFile(string set, string keyExt)
    {
        var p = Path.Combine(UserDir, set, keyExt);
        return File.Exists(p) ? p : null;
    }

    private string? ExtFor(string set, string key)
    {
        foreach (var ext in new[] { ".svg", ".png" })
            if (File.Exists(Path.Combine(RootDir, set, key + ext))
                || File.Exists(Path.Combine(UserDir, set, key + ext)))
                return ext;
        return null;
    }

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
