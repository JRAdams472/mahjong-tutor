using MahjongTutor.Core.Card;
using MahjongTutor.Core.Rules;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Bots;

/// <summary>
/// Card-aware bot decisions. Strategy: anchor on the best-fitting card hands
/// (CardMatcher), keep tiles they need, pass/discard what hurts least,
/// call only when it advances a non-concealed target, and always trade
/// naturals for rack jokers.
/// </summary>
/// <summary>How sharply a bot plays. Beginner mostly shuffles tiles around,
/// Casual follows the card greedily but ignores defense and jokers,
/// Expert uses the full matcher + safety heuristics.</summary>
public enum BotSkill { Beginner, Casual, Expert }

public static class BotBrain
{
    /// <summary>Tiles still gettable: full wall minus discards, exposures, own concealed.</summary>
    public static TileBag RemainingPool(GameState s, int seat)
    {
        var pool = TileKinds.FullWall().Clone();
        foreach (var se in s.Seats)
        {
            foreach (var d in se.Discards) pool.Remove(d);
            foreach (var m in se.Exposures)
                foreach (var t in m.Tiles) pool.Remove(t);
        }
        foreach (var t in s.Seats[seat].Concealed.ToList()) pool.Remove(t);
        return pool;
    }

    private static IReadOnlyList<IReadOnlyList<Tile>> Exposures(GameState s, int seat) =>
        s.Seats[seat].Exposures.Select(m => (IReadOnlyList<Tile>)m.Tiles).ToList();

    private static List<HandFit> Fits(GameState s, int seat, TileBag rack) =>
        CardMatcher.Analyze(rack, s.Card, RemainingPool(s, seat), Exposures(s, seat));

    /// <summary>Best viable fit (dead fits — needed tiles all gone — are skipped).</summary>
    private static HandFit? Best(GameState s, int seat)
    {
        var fits = Fits(s, seat, s.Seats[seat].Concealed);
        return fits.FirstOrDefault(f => f.Viable) ?? fits.FirstOrDefault();
    }

    /// <summary>Honor/flower tiles are safer to pass/discard (fewer card groups use them).</summary>
    private static int SafetyRank(Tile t) => t switch
    {
        Tile.F => 3,
        >= Tile.WN and <= Tile.WW => 2,
        >= Tile.DR and <= Tile.DW => 1,
        _ => 0,
    };

    /// <summary>How many copies of t are already out (discard pools + exposures): high = safe discard.</summary>
    private static int Deadness(GameState s, Tile t) =>
        s.Seats.Sum(se => se.Discards.Count(d => d == t)
            + se.Exposures.Sum(m => m.Tiles.Count(x => x == t)));

    /// <summary>Pick the n tiles whose loss least worsens the best fit (honors first on ties).</summary>
    public static List<Tile> PickDiscards(GameState s, int seat, int n, BotSkill skill = BotSkill.Expert)
    {
        var seat_ = s.Seats[seat];
        if (skill == BotSkill.Beginner)
            return seat_.Concealed.ToList()
                .OrderBy(t => seat_.Concealed[t] > 1 ? 1 : 0)   // cling to pairs
                .ThenBy(_ => Random.Shared.Next())
                .Take(n).ToList();
        var pool = RemainingPool(s, seat);
        var exposures = Exposures(s, seat);
        var fits = Fits(s, seat, seat_.Concealed);
        var baseNeed = fits.FirstOrDefault()?.TilesNeeded ?? 14;
        // Score each kind only against the two leading hands — much cheaper than a
        // full Analyze per kind and still grounded in the real matcher.
        var topHands = fits.Where(f => f.Viable).Take(2).Select(f => f.Hand).ToList();
        if (topHands.Count == 0) topHands = fits.Take(2).Select(f => f.Hand).ToList();

        return seat_.Concealed.ToList()
            .GroupBy(t => t)
            .Select(g =>
            {
                var rack = seat_.Concealed.Clone();
                rack.Remove(g.Key);
                var need = topHands.Count == 0 ? baseNeed
                    : topHands.Select(h =>
                        CardMatcher.BestFit(rack, h, pool, exposures)?.TilesNeeded ?? 14)
                    .Min();
                return (tile: g.Key, hurt: need - baseNeed, safety: SafetyRank(g.Key), dead: Deadness(s, g.Key));
            })
            .OrderBy(x => x.hurt)
            .ThenByDescending(x => skill == BotSkill.Expert ? x.dead : 0)  // defense awareness is expert-only
            .ThenByDescending(x => x.safety)
            .SelectMany(x => Enumerable.Repeat(x.tile, seat_.Concealed[x.tile]))
            .Take(n).ToList();
    }

    /// <summary>Charleston pass: the 3 least-valuable tiles.</summary>
    public static List<Tile> CharlestonPass(GameState s, int seat, BotSkill skill = BotSkill.Expert) =>
        PickDiscards(s, seat, 3, skill);

    /// <summary>Courtesy: pass up to 3 tiles that don't hurt at all.</summary>
    public static List<Tile> Courtesy(GameState s, int seat, BotSkill skill = BotSkill.Expert)
    {
        var seat_ = s.Seats[seat];
        if (skill == BotSkill.Beginner)
            return seat_.Concealed.ToList().OrderBy(_ => Random.Shared.Next())
                .Take(Random.Shared.Next(4)).ToList();
        var pool = RemainingPool(s, seat);
        var exposures = Exposures(s, seat);
        var fits = Fits(s, seat, seat_.Concealed);
        var baseNeed = fits.FirstOrDefault()?.TilesNeeded ?? 14;
        var topHands = fits.Where(f => f.Viable).Take(2).Select(f => f.Hand).ToList();
        if (topHands.Count == 0) topHands = fits.Take(2).Select(f => f.Hand).ToList();
        return seat_.Concealed.ToList()
            .GroupBy(t => t)
            .Where(g =>
            {
                var rack = seat_.Concealed.Clone();
                rack.Remove(g.Key);
                return topHands.Count == 0 || topHands.Select(h =>
                        CardMatcher.BestFit(rack, h, pool, exposures)?.TilesNeeded ?? 14)
                    .Min() <= baseNeed;
            })
            .Select(g => g.Key)
            .OrderByDescending(SafetyRank)
            .Take(3).ToList();
    }

    /// <summary>Vote second Charleston when the hand is still messy (≥4 tiles out).</summary>
    public static bool VoteSecondCharleston(GameState s, int seat, BotSkill skill = BotSkill.Expert) =>
        skill != BotSkill.Beginner && (Best(s, seat)?.TilesNeeded ?? 14) >= 4;

    /// <summary>Decide what to claim for a discard, or null to pass.</summary>
    public static ClaimKind? DecideClaim(GameState s, int seat, Tile tile, BotSkill skill = BotSkill.Expert)
    {
        var seat_ = s.Seats[seat];
        var options = s.PendingOptions.GetValueOrDefault(seat, []);
        var best = Best(s, seat);

        // Mahjong when it fits.
        if (best is { IsMahjong: false })
        {
            var rack = seat_.Concealed.Clone();
            rack.Add(tile);
            var fit = CardMatcher.Analyze(rack, s.Card, null, Exposures(s, seat))
                .FirstOrDefault(f => f.IsMahjong);
            if (fit != null) return ClaimKind.Mahjong;
        }

        if (skill == BotSkill.Beginner) return null;

        if (skill == BotSkill.Casual)
        {
            // Greedy: grabs any discard that fills a gap in a viable open hand.
            var greedy = Fits(s, seat, seat_.Concealed).FirstOrDefault(f =>
                f.Viable && !f.Hand.Concealed && f.Missing.Contains(tile));
            if (greedy == null) return null;
            return options.Where(k => k is ClaimKind.Pung or ClaimKind.Kong or ClaimKind.Quint)
                .OrderByDescending(k => k).Cast<ClaimKind?>().FirstOrDefault();
        }

        // Exposure call: tile must be missing from the best (non-concealed) fit's pattern.
        if (best is null or { IsMahjong: true } or { Hand.Concealed: true }) return null;
        if (!best.Missing.Contains(tile)) return null;
        // Claim the biggest group we legally can (pung always ≥3; kong/quint if offered).
        return options.Where(k => k is ClaimKind.Pung or ClaimKind.Kong or ClaimKind.Quint)
            .OrderByDescending(k => k).Cast<ClaimKind?>().FirstOrDefault();
    }

    /// <summary>Swap any held naturals for jokers sitting in exposures. Returns exchange tuples.</summary>
    public static List<(int owner, int meld, Tile natural)> JokerExchanges(
        GameState s, int seat, BotSkill skill = BotSkill.Expert)
    {
        if (skill != BotSkill.Expert) return [];
        var found = new List<(int owner, int meld, Tile natural)>();
        var concealed = s.Seats[seat].Concealed;
        for (var o = 0; o < 4; o++)
        {
            for (var m = 0; m < s.Seats[o].Exposures.Count; m++)
            {
                var meld = s.Seats[o].Exposures[m];
                if (!meld.Tiles.Contains(Tile.J)) continue;
                var natural = meld.Tiles.First(t => t != Tile.J);
                var spare = meld.Tiles.Count(t => t == Tile.J);
                var held = concealed[natural] - found.Count(f => f.natural == natural);
                while (spare-- > 0 && held-- > 0) found.Add((o, m, natural));
            }
        }
        return found;
    }

    /// <summary>On-turn action after drawing: exchange jokers, declare mahjong, or discard.</summary>
    public static BotTurnAct OnTurn(GameState s, int seat, BotSkill skill = BotSkill.Expert)
    {
        var best = Best(s, seat);
        if (best is { IsMahjong: true }) return new BotTurnAct.Mahjong();
        var t = PickDiscards(s, seat, 1, skill)[0];
        return new BotTurnAct.Discard(t);
    }
}

public abstract record BotTurnAct
{
    public sealed record Discard(Tile Tile) : BotTurnAct;
    public sealed record Mahjong : BotTurnAct;
}
