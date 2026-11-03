namespace MahjongTutor.Core.Tiles;

/// <summary>
/// A multiset of tiles — counts per <see cref="Tile"/> kind. Backs racks,
/// hands, walls, and "remaining tile pool" accounting.
/// </summary>
public sealed class TileBag
{
    private readonly int[] _counts = new int[TileKinds.KindCount];

    public TileBag() { }

    private TileBag(int[] counts) => _counts = counts;

    public int this[Tile t]
    {
        get => _counts[(int)t];
        set => _counts[(int)t] = value;
    }

    public int Total => _counts.Sum();
    public int Jokers => _counts[(int)Tile.J];

    public void Add(Tile t, int n = 1) => _counts[(int)t] += n;

    /// <summary>Removes n copies; returns false (unchanged) if fewer than n held.</summary>
    public bool Remove(Tile t, int n = 1)
    {
        if (_counts[(int)t] < n) return false;
        _counts[(int)t] -= n;
        return true;
    }

    public bool Contains(Tile t) => _counts[(int)t] > 0;

    /// <summary>Expands to a flat list (repeats each tile by its count).</summary>
    public List<Tile> ToList()
    {
        var list = new List<Tile>(Total);
        foreach (var t in TileKinds.All)
            for (var i = 0; i < this[t]; i++) list.Add(t);
        return list;
    }

    public TileBag Clone() => new((int[])_counts.Clone());

    /// <summary>Parses a space-separated list of tile codes: "5b N N RD F J".</summary>
    public static TileBag Parse(string codes)
    {
        var bag = new TileBag();
        foreach (var c in codes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            bag.Add(TileKinds.FromCode(c));
        return bag;
    }

    /// <summary>Renders as sorted space-separated codes.</summary>
    public override string ToString() =>
        string.Join(' ', TileKinds.All.Where(t => this[t] > 0)
            .SelectMany(t => Enumerable.Repeat(t.Code(), this[t])));
}
