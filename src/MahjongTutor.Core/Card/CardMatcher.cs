using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Card;

/// <summary>How well a rack fits one card hand.</summary>
public sealed record HandFit
{
    public required CardHand Hand { get; init; }
    /// <summary>Slots still unfilled after natural tiles and jokers (0 = mahjong).</summary>
    public required int TilesNeeded { get; init; }
    public required int JokersUsed { get; init; }
    public required int MatchedTiles { get; init; }
    /// <summary>Concrete tiles that would fill gaps (with multiplicity).</summary>
    public required IReadOnlyList<Tile> Missing { get; init; }
    /// <summary>Ranks that would fill gaps in any suit (for bare-digit slots).</summary>
    public required IReadOnlyList<int> MissingAnySuit { get; init; }
    /// <summary>Concrete tiles jokers currently stand in for.</summary>
    public required IReadOnlyList<Tile> JokerFill { get; init; }
    /// <summary>False when the needed tiles are gone from the remaining pool.</summary>
    public required bool Viable { get; init; }
    /// <summary>Suit-variable assignment of the best fit (for explanations).</summary>
    public required IReadOnlyDictionary<char, Suit> SuitAssignment { get; init; }

    public bool IsMahjong => TilesNeeded == 0;
}

/// <summary>
/// Scores a rack against every hand on a card. For each hand it enumerates
/// suit-variable assignments (distinct letters → distinct suits) and rank-var
/// assignments, materializes the concrete pattern, then scores how much of it
/// the rack fills — jokers may stand in for tiles inside same-tile groups of
/// size ≥ 3 (never pairs, singles, or mixed groups like NEWS), and a group may
/// never be all jokers. Results are sorted: fewest tiles needed, then fewest
/// jokers, then highest points.
/// </summary>
public static class CardMatcher
{
    /// <summary>A materialized slot requirement: either a concrete tile or a rank in any suit.</summary>
    private readonly record struct Req(Tile? Tile, int? AnySuitRank);

    public static List<HandFit> Analyze(
        TileBag rack,
        MahjongCard card,
        TileBag? remainingPool = null,
        IReadOnlyList<IReadOnlyList<Tile>>? exposedGroups = null)
    {
        var fits = new List<HandFit>();
        foreach (var hand in card.AllHands)
        {
            var fit = BestFit(rack, hand, remainingPool, exposedGroups);
            if (fit != null) fits.Add(fit);
        }
        return fits
            .OrderBy(f => f.TilesNeeded)
            .ThenBy(f => f.JokersUsed)
            .ThenByDescending(f => f.Hand.Points)
            .ToList();
    }

    /// <summary>Best fit for a single hand, or null if unsatisfiable (e.g. impossible exposed groups).</summary>
    public static HandFit? BestFit(
        TileBag rack,
        CardHand hand,
        TileBag? remainingPool = null,
        IReadOnlyList<IReadOnlyList<Tile>>? exposedGroups = null)
    {
        var suitVars = hand.Parsed.SuitVars().ToList();
        var rankVars = hand.Parsed.RankVars().ToList();
        HandFit? best = null;

        foreach (var suitMap in SuitAssignments(suitVars))
        foreach (var rankMap in RankAssignments(rankVars))
        {
            var options = hand.Parsed.Groups
                .Select(g => g.Options
                    .Select(o => o.Slots.Select(s => Materialize(s, suitMap, rankMap)).ToArray())
                    .ToList())
                .ToList();
            var fit = ScoreOptions(rack, hand, options, remainingPool, exposedGroups, suitMap);
            if (fit != null && (best == null || IsBetter(fit, best))) best = fit;
        }
        return best;
    }

    private static bool IsBetter(HandFit a, HandFit b) =>
        a.TilesNeeded != b.TilesNeeded ? a.TilesNeeded < b.TilesNeeded
        : a.JokersUsed != b.JokersUsed ? a.JokersUsed < b.JokersUsed
        : a.MatchedTiles > b.MatchedTiles;

    // ---- variable enumeration -------------------------------------------------

    /// <summary>Injections of suit-var letters onto the three suits (same letter = same suit).</summary>
    private static IEnumerable<Dictionary<char, Suit>> SuitAssignments(List<char> vars)
    {
        var suits = new[] { Suit.Dot, Suit.Bam, Suit.Crak };
        var used = new bool[3];
        var map = new Dictionary<char, Suit>();
        IEnumerable<Dictionary<char, Suit>> Go(int k)
        {
            if (k == vars.Count) { yield return new(map); yield break; }
            foreach (var s in suits)
            {
                if (used[(int)s]) continue;
                used[(int)s] = true; map[vars[k]] = s;
                foreach (var m in Go(k + 1)) yield return m;
                used[(int)s] = false; map.Remove(vars[k]);
            }
        }
        if (vars.Count > 3) yield break; // more vars than suits — impossible hand
        foreach (var m in Go(0)) yield return m;
    }

    /// <summary>Rank-var assignments keeping every var+offset inside 1-9.</summary>
    private static IEnumerable<Dictionary<char, int>> RankAssignments(List<(char Name, int MaxOffset)> vars)
    {
        var map = new Dictionary<char, int>();
        IEnumerable<Dictionary<char, int>> Go(int k)
        {
            if (k == vars.Count) { yield return new(map); yield break; }
            var (name, maxOff) = vars[k];
            for (var r = 1; r + maxOff <= 9; r++)
            {
                map[name] = r;
                foreach (var m in Go(k + 1)) yield return m;
            }
            map.Remove(name);
        }
        foreach (var m in Go(0)) yield return m;
    }

    private static Req Materialize(SlotSpec slot, Dictionary<char, Suit> suitMap, Dictionary<char, int> rankMap)
    {
        switch (slot.Value)
        {
            case SlotValue.Fixed f: return new(f.Tile, null);
            case SlotValue.SuitDragon:
                return new(slot.Suit.Kind == SuitReqKind.Var ? suitMap[slot.Suit.Var].DragonFor() : Tile.DW, null);
            case SlotValue.Rank r: return RankReq(r.Value, slot.Suit, suitMap);
            case SlotValue.RankVar v: return RankReq(rankMap[v.Name] + v.Offset, slot.Suit, suitMap);
            default: throw new InvalidOperationException();
        }
    }

    private static Req RankReq(int rank, SuitReq suit, Dictionary<char, Suit> suitMap) =>
        suit.Kind switch
        {
            SuitReqKind.Var => new(TileKinds.Of(suitMap[suit.Var], rank), null),
            _ => new(null, rank), // Any suit, resolved greedily during scoring
        };

    // ---- scoring ---------------------------------------------------------------

    private sealed record GroupEval(Req[] Reqs, bool JokerEligible);

    private static HandFit? ScoreOptions(
        TileBag rack, CardHand hand, List<List<Req[]>> groupOptions,
        TileBag? pool, IReadOnlyList<IReadOnlyList<Tile>>? exposed,
        Dictionary<char, Suit> suitMap)
    {
        // Enumerate option combinations (≤ ~4^6, typically ≤16).
        var indices = new int[groupOptions.Count];
        HandFit? best = null;
        while (true)
        {
            var chosen = new GroupEval[groupOptions.Count];
            for (var g = 0; g < groupOptions.Count; g++)
            {
                var reqs = groupOptions[g][indices[g]];
                chosen[g] = new(reqs, IsJokerEligible(reqs));
            }
            // Concealed hands cannot have exposures.
            if (!(hand.Concealed && exposed is { Count: > 0 }))
            {
                var exposureMap = exposed == null ? null : MatchExposed(exposed, chosen);
                if (exposed == null || exposureMap != null)
                {
                    var fit = Score(rack, hand, chosen, pool, suitMap, exposureMap, exposed);
                    if (best == null || IsBetter(fit, best)) best = fit;
                }
            }
            var p = groupOptions.Count - 1;
            while (p >= 0 && ++indices[p] == groupOptions[p].Count) indices[p--] = 0;
            if (p < 0) break;
        }
        return best;
    }

    /// <summary>Jokers allowed only in groups of ≥3 identical concrete tiles.</summary>
    private static bool IsJokerEligible(Req[] reqs)
    {
        if (reqs.Length < 3 || reqs.Any(r => r.Tile == null)) return false;
        var first = reqs[0].Tile;
        return reqs.All(r => r.Tile == first);
    }

    private static HandFit Score(
        TileBag rack, CardHand hand, GroupEval[] groups,
        TileBag? pool, Dictionary<char, Suit> suitMap,
        int[]? exposureMap, IReadOnlyList<IReadOnlyList<Tile>>? exposed)
    {
        var bag = rack.Clone();
        var jokers = bag[Tile.J];
        bag[Tile.J] = 0;

        var matched = new int[groups.Length];
        var missing = new List<Tile>();
        var missingAnySuit = new List<int>();
        var unfilledIdx = new List<int>[groups.Length];
        var jokersUsed = 0;
        var jokerFill = new List<Tile>();

        for (var g = 0; g < groups.Length; g++) unfilledIdx[g] = [];

        // Groups already satisfied by an exposure are filled (jokers inside
        // the exposure count as committed).
        if (exposureMap != null && exposed != null)
            for (var e = 0; e < exposureMap.Length; e++)
            {
                var g = exposureMap[e];
                matched[g] = groups[g].Reqs.Length;
                foreach (var t in exposed[e].Where(t => t == Tile.J))
                {
                    jokersUsed++;
                    var concrete = groups[g].Reqs.FirstOrDefault(r => r.Tile != null).Tile;
                    if (concrete != null) jokerFill.Add(concrete.Value);
                }
            }

        for (var g = 0; g < groups.Length; g++)
        {
            if (exposureMap != null && exposureMap.Contains(g)) continue;
            var reqs = groups[g].Reqs;
            for (var s = 0; s < reqs.Length; s++)
            {
                var r = reqs[s];
                if (r.Tile is { } t)
                {
                    if (bag.Remove(t)) matched[g]++;
                    else { missing.Add(t); unfilledIdx[g].Add(s); }
                }
                else
                {
                    var rank = r.AnySuitRank!.Value;
                    var filled = false;
                    for (var su = 0; su < 3 && !filled; su++)
                        filled = bag.Remove(TileKinds.Of((Suit)su, rank));
                    if (filled) matched[g]++;
                    else { missingAnySuit.Add(rank); unfilledIdx[g].Add(s); }
                }
            }
        }

        // Jokers: fill unfilled slots in eligible groups; a group keeps ≥1 natural.
        var tilesNeeded = missing.Count + missingAnySuit.Count;
        foreach (var g in Enumerable.Range(0, groups.Length)
                     .Where(g => groups[g].JokerEligible)
                     .OrderByDescending(g => unfilledIdx[g].Count))
        {
            var keepNatural = matched[g] == 0 ? 1 : 0;
            var canTake = Math.Max(0, unfilledIdx[g].Count - keepNatural);
            var take = Math.Min(canTake, jokers - jokersUsed);
            foreach (var s in unfilledIdx[g].Take(take))
                jokerFill.Add(groups[g].Reqs[s].Tile!.Value);
            jokersUsed += take;
            tilesNeeded -= take;
        }

        var viable = true;
        if (pool != null)
        {
            foreach (var t in missing.GroupBy(t => t))
                if (t.Count() > pool[t.Key]) { viable = false; break; }
            if (viable) foreach (var r in missingAnySuit)
            {
                var left = Enumerable.Range(0, 3).Sum(su => pool[TileKinds.Of((Suit)su, r)]);
                if (left == 0) { viable = false; break; }
            }
        }

        var slotsTotal = groups.Sum(g => g.Reqs.Length);
        return new HandFit
        {
            Hand = hand,
            TilesNeeded = tilesNeeded,
            JokersUsed = jokersUsed,
            MatchedTiles = slotsTotal - tilesNeeded,
            Missing = missing,
            MissingAnySuit = missingAnySuit,
            JokerFill = jokerFill,
            Viable = viable,
            SuitAssignment = suitMap,
        };
    }

    // ---- exposed groups ----------------------------------------------------------

    /// <summary>
    /// Every exposed group must correspond to a distinct chosen pattern group:
    /// its natural tiles must appear among that option's tiles and its jokers
    /// may only fill a joker-eligible option. Returns exposure→group mapping
    /// or null if no injective assignment exists.
    /// </summary>
    private static int[]? MatchExposed(IReadOnlyList<IReadOnlyList<Tile>> exposed, GroupEval[] groups)
    {
        var used = new bool[groups.Length];
        var map = new int[exposed.Count];
        bool Go(int e)
        {
            if (e == exposed.Count) return true;
            var tiles = exposed[e];
            var j = tiles.Count(t => t == Tile.J);
            for (var gi = 0; gi < groups.Length; gi++)
            {
                if (used[gi] || groups[gi].Reqs.Length != tiles.Count) continue;
                if (j > 0 && !groups[gi].JokerEligible) continue;
                var want = groups[gi].Reqs.Where(r => r.Tile != null).Select(r => r.Tile!.Value)
                    .GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
                var ok = true;
                foreach (var t in tiles.Where(t => t != Tile.J).GroupBy(t => t))
                {
                    if (!want.TryGetValue(t.Key, out var n) || n < t.Count()) { ok = false; break; }
                    want[t.Key] -= t.Count();
                }
                // leftover concrete slots must be absorbable by the exposure's jokers
                if (ok && want.Values.Sum() != j) ok = false;
                if (!ok) continue;
                used[gi] = true;
                map[e] = gi;
                if (Go(e + 1)) return true;
                used[gi] = false;
            }
            return false;
        }
        return Go(0) ? map : null;
    }
}
