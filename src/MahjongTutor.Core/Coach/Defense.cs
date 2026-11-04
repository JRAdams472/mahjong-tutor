using MahjongTutor.Core.Bots;
using MahjongTutor.Core.Card;
using MahjongTutor.Core.Rules;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Coach;

/// <summary>
/// Defensive coaching: infer which card hands each opponent is probably
/// collecting from their public exposures + the discards, and score how
/// dangerous a candidate discard is (who could call it — or win on it).
/// </summary>
public static class Defense
{
    public sealed record OpponentRead(int Seat, IReadOnlyList<CardHand> Likely);

    /// <summary>Per-opponent guesses: which hands their exposures best explain.</summary>
    public static List<OpponentRead> Reads(GameState s, int seat, int take = 2)
    {
        var pool = BotBrain.RemainingPool(s, seat);
        var reads = new List<OpponentRead>();
        for (var o = 0; o < 4; o++)
        {
            if (o == seat) continue;
            var st = s.Seats[o];
            if (st.Exposures.Count == 0) continue;   // nothing public to infer from
            var fits = OpponentFits(s, pool, o, take);
            if (fits.Count > 0)
                reads.Add(new OpponentRead(o, fits.Select(f => f.Hand).ToList()));
        }
        return reads;
    }

    public sealed record Danger(Tile Tile, IReadOnlyList<int> Callers, bool CouldWin);

    /// <summary>How risky is discarding <paramref name="tile"/>?
    /// Who's likely to call it — and could it be someone's winning tile?</summary>
    public static Danger? Evaluate(GameState s, int seat, Tile tile)
    {
        if (tile is Tile.F or Tile.J) return null;   // dead discards — never callable
        var pool = BotBrain.RemainingPool(s, seat);
        var callers = new List<int>();
        var couldWin = false;
        for (var o = 0; o < 4; o++)
        {
            if (o == seat || s.Seats[o].Dead) continue;
            foreach (var f in OpponentFits(s, pool, o, 3))
            {
                if (!f.Missing.Contains(tile)) continue;
                if (!callers.Contains(o)) callers.Add(o);
                // Close to mahjong: the rest could already be concealed.
                if (f.TilesNeeded <= 2) couldWin = true;
            }
        }
        return callers.Count == 0 ? null : new Danger(tile, callers, couldWin);
    }

    /// <summary>Best-fitting hands for an opponent given their public exposures
    /// (concealed hands excluded — exposures can't be part of them).</summary>
    private static List<HandFit> OpponentFits(GameState s, TileBag pool, int opp, int take)
    {
        var st = s.Seats[opp];
        var rack = new TileBag();
        foreach (var m in st.Exposures)
            foreach (var t in m.Tiles) rack.Add(t);
        // Opponent's concealed tiles are unknown — a tiny rack just constrains
        // which hands their exposures could belong to.
        var exposed = st.Exposures.Select(m => (IReadOnlyList<Tile>)m.Tiles).ToList();
        return CardMatcher.Analyze(rack, s.Card, pool, exposed)
            .Where(f => f.Viable && !f.Hand.Concealed)
            .OrderBy(f => f.TilesNeeded).ThenBy(f => f.JokersUsed)
            .Take(take).ToList();
    }

    /// <summary>Note shown when the player's suggested discard is dangerous.</summary>
    public static string? DangerNote(GameState s, int seat, Tile tile)
    {
        var d = Evaluate(s, seat, tile);
        if (d == null) return null;
        var who = string.Join(", ", d.Callers.Select(c => $"seat {c}"));
        return d.CouldWin
            ? $"Dangerous — {who} may be waiting on exactly {tile.DisplayName()} for mahjong."
            : $"{who} could probably use {tile.DisplayName()} — a live tile.";
    }
}
