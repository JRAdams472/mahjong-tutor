using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Card;

public sealed class PatternParseException(string pattern, string message)
    : FormatException($"invalid card pattern \"{pattern}\": {message}");

/// <summary>
/// Parses the card-notation DSL into a <see cref="ParsedPattern"/>.
///
/// Grammar (mirrors how the NMJL card is printed — colors become suit variables):
/// <code>
///   pattern  := group (WS group)*
///   group    := alt ('|' alt)*              // alternatives within one position
///   alt      := atom+
///   atom     := 'F' | 'N'|'E'|'S'|'W'       // flower, winds
///             | 'R' | 'G' | '0'             // red dragon, green dragon, soap
///             | 'D' suit?                   // dragon matching a suit (suit from own
///                                          //   suffix or group trailing letter)
///             | digit suit?                 // rank 1-9 ('0' is soap, never a rank)
///             | var+ digit? suit?           // rank-var run: "XX1a" = two X+1 tiles,
///                                          //   "X2b" = one X+2 tile
///   suit     := [a-z]                       // suit variable: same letter = same suit,
///                                          //   distinct letters = distinct suits
/// </code>
/// A suit letter at the END of an alternative distributes to every suited atom
/// that lacks one — so "1111a" = four 1s in suit a, "DDDDb" = four dragons of
/// suit b, and "2025a" = 2-soap-2-5 with the numbers in suit a.
/// </summary>
public static class CardPatternParser
{
    private static readonly HashSet<char> RankVarLetters = new("XYZ");

    public static ParsedPattern Parse(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new PatternParseException(pattern ?? "", "pattern is empty");

        var groups = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(g => ParseGroup(g, pattern))
            .ToList();
        return new ParsedPattern(groups, pattern);
    }

    private static PatternGroup ParseGroup(string group, string pattern)
    {
        var options = group.Split('|')
            .Select(alt => new GroupOption(ParseAlternative(alt, pattern)))
            .ToList();
        return new PatternGroup(options);
    }

    private static List<SlotSpec> ParseAlternative(string alt, string pattern)
    {
        if (alt.Length == 0)
            throw new PatternParseException(pattern, "empty group alternative");

        var slots = new List<SlotSpec>();
        var i = 0;
        while (i < alt.Length)
        {
            var c = alt[i];
            switch (c)
            {
                case 'F': slots.Add(new(new SlotValue.Fixed(Tile.F), SuitReq.None)); i++; break;
                case 'N': slots.Add(new(new SlotValue.Fixed(Tile.WN), SuitReq.None)); i++; break;
                case 'E': slots.Add(new(new SlotValue.Fixed(Tile.WE), SuitReq.None)); i++; break;
                case 'S': slots.Add(new(new SlotValue.Fixed(Tile.WS), SuitReq.None)); i++; break;
                case 'W': slots.Add(new(new SlotValue.Fixed(Tile.WW), SuitReq.None)); i++; break;
                case 'R': slots.Add(new(new SlotValue.Fixed(Tile.DR), SuitReq.None)); i++; break;
                case 'G': slots.Add(new(new SlotValue.Fixed(Tile.DG), SuitReq.None)); i++; break;
                case '0': slots.Add(new(new SlotValue.Fixed(Tile.DW), SuitReq.None)); i++; break;
                case 'D':
                {
                    i++;
                    var suit = TrySuit(alt, i, out var v) ? SuitReq.Of(v) : SuitReq.None;
                    if (suit.Kind == SuitReqKind.Var) i++;
                    slots.Add(new(new SlotValue.SuitDragon(), suit));
                    break;
                }
                case >= '1' and <= '9':
                {
                    i++;
                    var suit = TrySuit(alt, i, out var v) ? SuitReq.Of(v) : SuitReq.None;
                    if (suit.Kind == SuitReqKind.Var) i++;
                    slots.Add(new(new SlotValue.Rank(c - '0'), suit));
                    break;
                }
                case var _ when RankVarLetters.Contains(c):
                {
                    var run = 0;
                    while (i < alt.Length && alt[i] == c) { i++; run++; }
                    var offset = 0;
                    if (i < alt.Length && alt[i] is >= '0' and <= '9')
                        offset = alt[i++] - '0';
                    var suit = TrySuit(alt, i, out var v) ? SuitReq.Of(v) : SuitReq.None;
                    if (suit.Kind == SuitReqKind.Var) i++;
                    for (var k = 0; k < run; k++)
                        slots.Add(new(new SlotValue.RankVar(c, offset), suit));
                    break;
                }
                case >= 'a' and <= 'z':
                    throw new PatternParseException(pattern,
                        $"suit letter '{c}' has no preceding tile");
                default:
                    throw new PatternParseException(pattern, $"unexpected character '{c}'");
            }
        }
        DistributeTrailingSuit(slots, pattern);
        return slots;
    }

    private static bool TrySuit(string alt, int i, out char var)
    {
        var = '\0';
        if (i < alt.Length && alt[i] is >= 'a' and <= 'z') { var = alt[i]; return true; }
        return false;
    }

    /// <summary>
    /// Gives every suited atom lacking an explicit suit the alternative's last
    /// explicit suit; remaining unsuited ranks become <see cref="SuitReq.Any"/>.
    /// A suit-dragon with no resolvable suit is an error.
    /// </summary>
    private static void DistributeTrailingSuit(List<SlotSpec> slots, string pattern)
    {
        var last = slots.LastOrDefault(s => s.Suit.Kind == SuitReqKind.Var)?.Suit.Var ?? '\0';
        for (var i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (!s.NeedsSuit || s.Suit.Kind == SuitReqKind.Var) continue;
            if (last != '\0')
                slots[i] = s with { Suit = SuitReq.Of(last) };
            else if (s.Value is SlotValue.SuitDragon)
                throw new PatternParseException(pattern, "dragon 'D' needs a suit letter");
            else
                slots[i] = s with { Suit = SuitReq.Any };
        }
    }
}
