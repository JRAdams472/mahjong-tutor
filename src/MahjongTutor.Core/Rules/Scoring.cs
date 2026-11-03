using MahjongTutor.Core.Card;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Rules;

/// <summary>
/// NMJL payouts. Winner by discard: the discarder pays 2× the hand's card
/// value, the other two players pay 1× each. Self-picked win: everyone pays
/// 2×. A jokerless hand (no jokers in the winning 14, and the pattern has at
/// least one joker-eligible group of ≥3) doubles again.
/// </summary>
public static class Scoring
{
    public static IReadOnlyDictionary<int, int> Payouts(
        GameState state, int winner, CardHand hand, bool selfPicked, int? discarder)
    {
        var value = hand.Points;

        var w = state.Seats[winner];
        var jokers = w.Concealed[Tile.J]
            + w.Exposures.Sum(m => m.Tiles.Count(t => t == Tile.J));
        var hasJokerGroup = hand.Parsed.Groups.Any(g => g.MaxSize >= 3);
        var mult = jokers == 0 && hasJokerGroup ? 2 : 1;

        var payouts = Enumerable.Range(0, 4).ToDictionary(s => s, _ => 0);
        for (var s = 0; s < 4; s++)
        {
            if (s == winner) continue;
            var pay = selfPicked ? 2 * value
                : s == discarder ? 2 * value
                : value;
            payouts[s] = -pay * mult;
        }
        payouts[winner] = -payouts.Values.Sum();
        return payouts;
    }
}
