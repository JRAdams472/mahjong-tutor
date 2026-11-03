using MahjongTutor.Core.Bots;
using MahjongTutor.Core.Card;
using MahjongTutor.Core.Rules;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Coach;

/// <summary>
/// The tutor's brain: ranks your best card targets, grades discard choices
/// against the matcher, flags missed calls, and suggests Charleston passes.
/// Pure analysis — the session/UI decides when to surface it.
/// </summary>
public static class CoachBrain
{
    public sealed record DiscardGrade(Tile Tile, int NeedAfter, int Delta);

    /// <summary>Best-fit hands for the seat, viable targets first.</summary>
    public static List<HandFit> TopFits(GameState s, int seat, int take = 5)
    {
        var pool = BotBrain.RemainingPool(s, seat);
        var exposed = Exposures(s, seat);
        return CardMatcher.Analyze(s.Seats[seat].Concealed, s.Card, pool, exposed)
            .OrderBy(f => !f.Viable)
            .ThenBy(f => f.TilesNeeded).ThenBy(f => f.JokersUsed)
            .ThenByDescending(f => f.Hand.Points)
            .Take(take).ToList();
    }

    /// <summary>Every distinct concealed tile graded: NeedAfter = best fit's
    /// TilesNeeded if you drop it; Delta = how far off the best option.</summary>
    public static List<DiscardGrade> GradeDiscards(GameState s, int seat)
    {
        var seat_ = s.Seats[seat];
        var pool = BotBrain.RemainingPool(s, seat);
        var exposed = Exposures(s, seat);
        var fits = CardMatcher.Analyze(seat_.Concealed, s.Card, pool, exposed);
        var topHands = fits.Where(f => f.Viable).Take(2).Select(f => f.Hand).ToList();
        if (topHands.Count == 0) topHands = fits.Take(2).Select(f => f.Hand).ToList();

        var grades = seat_.Concealed.ToList()
            .GroupBy(t => t)
            .Select(g =>
            {
                var rack = seat_.Concealed.Clone();
                rack.Remove(g.Key);
                var need = topHands.Count == 0 ? 0
                    : topHands.Select(h =>
                        CardMatcher.BestFit(rack, h, pool, exposed)?.TilesNeeded ?? 14).Min();
                return new DiscardGrade(g.Key, need, 0);
            })
            .OrderBy(g => g.NeedAfter).ToList();
        var min = grades.FirstOrDefault()?.NeedAfter ?? 0;
        return grades.Select(g => g with { Delta = g.NeedAfter - min }).ToList();
    }

    /// <summary>Rack-level grader for the standalone analyzer: same scoring as
    /// <see cref="GradeDiscards"/> but on a bare rack + card.</summary>
    public static List<DiscardGrade> GradeRack(TileBag rack, MahjongCard card, TileBag? pool = null)
    {
        var fits = CardMatcher.Analyze(rack, card, pool);
        var topHands = fits.Where(f => f.Viable).Take(2).Select(f => f.Hand).ToList();
        if (topHands.Count == 0) topHands = fits.Take(2).Select(f => f.Hand).ToList();
        var grades = rack.ToList()
            .GroupBy(t => t)
            .Select(g =>
            {
                var r = rack.Clone();
                r.Remove(g.Key);
                var need = topHands.Count == 0 ? 0
                    : topHands.Select(h => CardMatcher.BestFit(r, h, pool)?.TilesNeeded ?? 14).Min();
                return new DiscardGrade(g.Key, need, 0);
            })
            .OrderBy(g => g.NeedAfter).ToList();
        var min = grades.FirstOrDefault()?.NeedAfter ?? 0;
        return grades.Select(g => g with { Delta = g.NeedAfter - min }).ToList();
    }

    /// <summary>The n tiles the coach would pass/discard right now.</summary>
    public static List<Tile> SuggestPass(GameState s, int seat, int n = 3)
    {
        var concealed = s.Seats[seat].Concealed;
        return GradeDiscards(s, seat)
            .OrderBy(g => g.Delta)
            .SelectMany(g => Enumerable.Repeat(g.Tile, concealed[g.Tile]))
            .Take(n).ToList();
    }

    /// <summary>Note when the player could have profitably called a discard.</summary>
    public static string? MissedCallNote(GameState s, int seat, Tile tile)
    {
        var options = s.PendingOptions.GetValueOrDefault(seat, []);
        if (options.Contains(ClaimKind.Mahjong))
            return $"You could have won! {tile.Code()} completes a hand on the card.";
        var fit = TopFits(s, seat, 3)
            .FirstOrDefault(f => f.Viable && !f.Hand.Concealed && f.Missing.Contains(tile));
        return fit == null ? null
            : $"{tile.Code()} was callable toward {fit.Hand.Id} ({fit.TilesNeeded} tiles out) — worth a pung.";
    }

    /// <summary>Note when a discard was suboptimal (null = optimal choice).</summary>
    public static string? DiscardNote(GameState s, int seat, Tile chosen)
    {
        var grades = GradeDiscards(s, seat);
        var mine = grades.FirstOrDefault(g => g.Tile == chosen);
        var best = grades.FirstOrDefault();
        if (mine == null || best == null || mine.Delta == 0) return null;
        var fits = TopFits(s, seat, 3);
        var target = fits.FirstOrDefault(f => f.Viable)?.Hand.Id ?? "your best hand";
        var how = mine.Delta >= 2
            ? $"{chosen.Code()} costs you {mine.Delta} tiles of progress"
            : $"{chosen.Code()} is a tile farther out";
        return $"{how} — {best.Tile.Code()} kept you closer to {target}.";
    }

    private static IReadOnlyList<IReadOnlyList<Tile>> Exposures(GameState s, int seat) =>
        s.Seats[seat].Exposures.Select(m => (IReadOnlyList<Tile>)m.Tiles).ToList();
}
