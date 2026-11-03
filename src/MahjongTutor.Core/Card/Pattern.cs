using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Card;

/// <summary>Suit requirement of a pattern slot.</summary>
public enum SuitReqKind
{
    /// <summary>Slot is an honor/flower — no suit applies.</summary>
    None,
    /// <summary>Any suit, resolved independently per slot (bare digits).</summary>
    Any,
    /// <summary>A suit variable: same letter → same suit; distinct letters → distinct suits.</summary>
    Var,
}

public readonly record struct SuitReq(SuitReqKind Kind, char Var = '\0')
{
    public static readonly SuitReq None = new(SuitReqKind.None);
    public static readonly SuitReq Any = new(SuitReqKind.Any);
    public static SuitReq Of(char var) => new(SuitReqKind.Var, char.ToLowerInvariant(var));

    public override string ToString() => Kind switch
    {
        SuitReqKind.Var => Var.ToString(),
        SuitReqKind.Any => "*",
        _ => "",
    };
}

/// <summary>What tile a pattern slot requires (before variables resolve).</summary>
public abstract record SlotValue
{
    /// <summary>A literal tile: flower, wind, specific dragon, soap.</summary>
    public sealed record Fixed(Tile Tile) : SlotValue;

    /// <summary>A literal rank 1-9; the suit comes from the slot's <see cref="SuitReq"/>.</summary>
    public sealed record Rank(int Value) : SlotValue;

    /// <summary>Rank variable + offset: "X" = X+0, "X2" = X+2. All uses of a var share its rank.</summary>
    public sealed record RankVar(char Name, int Offset) : SlotValue;

    /// <summary>The dragon conventionally matching the slot's suit (card "D").</summary>
    public sealed record SuitDragon() : SlotValue;
}

public sealed record SlotSpec(SlotValue Value, SuitReq Suit)
{
    /// <summary>Slot can hold a suited tile (rank or suit-dragon).</summary>
    public bool NeedsSuit => Value is SlotValue.Rank or SlotValue.RankVar or SlotValue.SuitDragon;

    public override string ToString() => Value switch
    {
        SlotValue.Fixed f => f.Tile.Code(),
        SlotValue.Rank r => $"{r.Value}{Suit}",
        SlotValue.RankVar v => $"{v.Name}{v.Offset}{Suit}",
        SlotValue.SuitDragon => $"D{Suit}",
        _ => "?",
    };
}

/// <summary>One alternative for a group position (groups may have several via "|").</summary>
public sealed record GroupOption(IReadOnlyList<SlotSpec> Slots)
{
    public int Size => Slots.Count;
    public override string ToString() => string.Concat(Slots.Select(s => s.ToString()));
}

/// <summary>A group position in a hand — usually one option, several when the card shows "|" alternatives.</summary>
public sealed record PatternGroup(IReadOnlyList<GroupOption> Options)
{
    /// <summary>Max tiles across options (options normally have equal size).</summary>
    public int MaxSize => Options.Max(o => o.Size);

    public override string ToString() => string.Join('|', Options.Select(o => o.ToString()));
}

/// <summary>A parsed card hand: an ordered list of groups.</summary>
public sealed record ParsedPattern(IReadOnlyList<PatternGroup> Groups, string Source)
{
    /// <summary>Total tiles the pattern demands (must be 14 for a legal hand).</summary>
    public int TileCount => Groups.Sum(g => g.MaxSize);

    /// <summary>Distinct suit-variable letters used.</summary>
    public IEnumerable<char> SuitVars() =>
        Groups.SelectMany(g => g.Options).SelectMany(o => o.Slots)
            .Where(s => s.Suit.Kind == SuitReqKind.Var).Select(s => s.Suit.Var).Distinct();

    /// <summary>Distinct rank-variable letters and their max offset.</summary>
    public IEnumerable<(char Name, int MaxOffset)> RankVars() =>
        Groups.SelectMany(g => g.Options).SelectMany(o => o.Slots)
            .Select(s => s.Value).OfType<SlotValue.RankVar>()
            .GroupBy(v => v.Name).Select(g => (g.Key, g.Max(v => v.Offset)));

    public override string ToString() => string.Join(' ', Groups.Select(g => g.ToString()));
}
