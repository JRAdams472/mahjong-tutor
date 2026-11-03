using System.Text.Json.Serialization;

namespace MahjongTutor.Core.Card;

/// <summary>A yearly NMJL-style card, entered by the user. Never shipped with the app.</summary>
public sealed record MahjongCard
{
    public required int Year { get; init; }
    public required string Name { get; init; }
    public string? Notes { get; init; }
    public List<CardCategory> Categories { get; init; } = [];

    /// <summary>Every hand on the card, in card order.</summary>
    [JsonIgnore]
    public IEnumerable<CardHand> AllHands => Categories.SelectMany(c => c.Hands);
}

public sealed record CardCategory
{
    public required string Name { get; init; }
    public List<CardHand> Hands { get; init; } = [];
}

public sealed record CardHand
{
    public required string Id { get; init; }
    public required int Points { get; init; }
    public bool Concealed { get; init; }
    /// <summary>Pattern in the card DSL — see <see cref="CardPatternParser"/>.</summary>
    public required string Pattern { get; init; }
    public string? Notes { get; init; }

    [JsonIgnore]
    public ParsedPattern Parsed => _parsed ??= CardPatternParser.Parse(Pattern);
    private ParsedPattern? _parsed;

    /// <summary>Sum of pattern tiles — legal hands total 14.</summary>
    public int TileCount => Parsed.TileCount;
}
