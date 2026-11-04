using MahjongTutor.Core.Card;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Rules;

/// <summary>
/// The authoritative NMJL rules engine: deal → Charleston → draw/discard →
/// calls → mahjong/wall, with joker rules, dead hands, and scoring.
/// Every public action returns the events it produced (or throws
/// <see cref="RuleException"/> for illegal moves).
/// </summary>
public sealed class GameEngine
{
    /// <summary>Minimum natural (non-joker) tiles a call must contribute from the hand,
    /// besides the called tile itself — an exposure may never be all jokers.</summary>
    public const int MinNaturalsInCall = 1;

    public GameState State { get; }

    /// <summary>Resume a saved game from a restored snapshot.</summary>
    public GameEngine(GameState restored)
    {
        State = restored;
    }

    public GameEngine(MahjongCard card, int seed = 0, int dealer = 0)
    {
        var wall = TileKinds.FullWall().ToList();
        var rng = new Random(seed);
        for (var i = wall.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (wall[i], wall[j]) = (wall[j], wall[i]);
        }

        State = new GameState { Card = card, Seed = seed, Dealer = dealer, Wall = [.. wall] };
        for (var s = 0; s < 4; s++)
            for (var n = 0; n < (s == dealer ? 14 : 13); n++)
                State.Seats[s].Concealed.Add(State.Wall[State.WallIndex++]);
        State.CurrentSeat = dealer;
    }

    private List<GameEvent> Events() => new();

    // ------------------------------------------------------------------
    // Charleston
    // ------------------------------------------------------------------

    /// <summary>Pass 3 tiles for the current Charleston pass stage.</summary>
    public IReadOnlyList<GameEvent> SubmitPass(int seat, IReadOnlyList<Tile> tiles)
    {
        Require(State.Phase == GamePhase.Charleston, "not in Charleston");
        var stage = State.CharlestonStage;
        Require(stage is CharlestonStage.PassRight or CharlestonStage.PassAcross
            or CharlestonStage.PassLeft or CharlestonStage.SecondLeft
            or CharlestonStage.SecondAcross or CharlestonStage.SecondRight,
            $"not a pass stage ({stage})");
        Require(!State.PendingPasses.ContainsKey(seat), $"seat {seat} already passed");
        Require(tiles.Count == 3, "must pass exactly 3 tiles");
        Require(tiles.All(State.Seats[seat].Concealed.Contains), "passing tiles not held");

        var ev = Events();
        State.PendingPasses[seat] = [.. tiles];
        foreach (var t in tiles) State.Seats[seat].Concealed.Remove(t);

        if (State.PendingPasses.Count == 4)
        {
            var dir = PassDirection(stage);
            foreach (var (s, ts) in State.PendingPasses)
            {
                var to = dir(s);
                foreach (var t in ts) State.Seats[to].Concealed.Add(t);
                ev.Add(new PassCompleted(stage, s, to, ts.Count));
            }
            State.PendingPasses.Clear();
            AdvanceCharleston(ev);
        }
        return ev;
    }

    /// <summary>Vote on running the second Charleston (must be unanimous).</summary>
    public IReadOnlyList<GameEvent> Vote(int seat, bool goAgain)
    {
        Require(State.Phase == GamePhase.Charleston
            && State.CharlestonStage == CharlestonStage.Vote, "not voting");
        Require(!State.PendingVotes.ContainsKey(seat), $"seat {seat} already voted");
        var ev = Events();
        State.PendingVotes[seat] = goAgain;
        if (State.PendingVotes.Count == 4)
        {
            var proceed = State.PendingVotes.Values.All(v => v);
            State.PendingVotes.Clear();
            ev.Add(new CharlestonVote(proceed));
            SetCharlestonStage(ev, proceed ? CharlestonStage.SecondLeft : CharlestonStage.Courtesy);
        }
        return ev;
    }

    /// <summary>Courtesy pass: offer 0-3 tiles; across-pairs exchange the lesser count.</summary>
    public IReadOnlyList<GameEvent> SubmitCourtesy(int seat, IReadOnlyList<Tile> tiles)
    {
        Require(State.Phase == GamePhase.Charleston
            && State.CharlestonStage == CharlestonStage.Courtesy, "not in courtesy pass");
        Require(!State.PendingPasses.ContainsKey(seat), $"seat {seat} already submitted");
        Require(tiles.Count is >= 0 and <= 3, "courtesy pass is 0-3 tiles");
        Require(tiles.All(State.Seats[seat].Concealed.Contains), "passing tiles not held");

        var ev = Events();
        State.PendingPasses[seat] = [.. tiles];
        foreach (var t in tiles) State.Seats[seat].Concealed.Remove(t);

        if (State.PendingPasses.Count == 4)
        {
            foreach (var pair in new[] { (0, 2), (1, 3) })
            {
                var (a, b) = pair;
                var n = Math.Min(State.PendingPasses[a].Count, State.PendingPasses[b].Count);
                var aTiles = State.PendingPasses[a].Take(n).ToList();
                var bTiles = State.PendingPasses[b].Take(n).ToList();
                // unexchanged offered tiles return home
                foreach (var t in State.PendingPasses[a].Skip(n)) State.Seats[a].Concealed.Add(t);
                foreach (var t in State.PendingPasses[b].Skip(n)) State.Seats[b].Concealed.Add(t);
                foreach (var t in aTiles) State.Seats[b].Concealed.Add(t);
                foreach (var t in bTiles) State.Seats[a].Concealed.Add(t);
                ev.Add(new PassCompleted(CharlestonStage.Courtesy, a, b, aTiles.Count));
                ev.Add(new PassCompleted(CharlestonStage.Courtesy, b, a, bTiles.Count));
            }
            State.PendingPasses.Clear();
            SetCharlestonStage(ev, CharlestonStage.Done);
            State.Phase = GamePhase.Play;
            State.Stage = TurnStage.AwaitDiscard;
            ev.Add(new TurnChanged(State.CurrentSeat, TurnStage.AwaitDiscard));
        }
        return ev;
    }

    private static Func<int, int> PassDirection(CharlestonStage stage) => stage switch
    {
        CharlestonStage.PassRight or CharlestonStage.SecondRight => GameState.Right,
        CharlestonStage.PassAcross or CharlestonStage.SecondAcross => GameState.Across,
        _ => GameState.Left,
    };

    private void AdvanceCharleston(List<GameEvent> ev)
    {
        var next = State.CharlestonStage switch
        {
            CharlestonStage.PassRight => CharlestonStage.PassAcross,
            CharlestonStage.PassAcross => CharlestonStage.PassLeft,
            CharlestonStage.PassLeft => CharlestonStage.Vote,
            CharlestonStage.SecondLeft => CharlestonStage.SecondAcross,
            CharlestonStage.SecondAcross => CharlestonStage.SecondRight,
            CharlestonStage.SecondRight => CharlestonStage.Courtesy,
            _ => throw new InvalidOperationException(),
        };
        SetCharlestonStage(ev, next);
    }

    private void SetCharlestonStage(List<GameEvent> ev, CharlestonStage s)
    {
        State.CharlestonStage = s;
        ev.Add(new CharlestonStageChanged(s));
    }

    // ------------------------------------------------------------------
    // Play
    // ------------------------------------------------------------------

    /// <summary>Current seat draws. Returns the draw event (and wall-game end if empty).</summary>
    public IReadOnlyList<GameEvent> Draw(int seat)
    {
        Require(State.Phase == GamePhase.Play && State.Stage == TurnStage.AwaitDraw, "cannot draw now");
        Require(seat == State.CurrentSeat, "not your turn");
        var ev = Events();
        if (State.WallRemaining == 0)
        {
            EndWallGame(ev);
            return ev;
        }
        var tile = State.Wall[State.WallIndex++];
        State.Seats[seat].Concealed.Add(tile);
        State.Stage = TurnStage.AwaitDiscard;
        ev.Add(new TileDrawn(seat, tile));
        ev.Add(new TurnChanged(seat, TurnStage.AwaitDiscard));
        return ev;
    }

    /// <summary>Current seat discards; opens the call window.</summary>
    public IReadOnlyList<GameEvent> Discard(int seat, Tile tile)
    {
        Require(State.Phase == GamePhase.Play && State.Stage == TurnStage.AwaitDiscard, "cannot discard now");
        Require(seat == State.CurrentSeat, "not your turn");
        Require(State.Seats[seat].Concealed.Remove(tile), "tile not held");

        var ev = Events();
        ev.Add(new TileDiscarded(seat, tile));

        var options = CallOptions(seat, tile);
        if (options.Values.All(k => k.Length == 0))
        {
            Seat(seat).Discards.Add(tile);
            NextTurn(ev);
            return ev;
        }

        State.PendingDiscard = tile;
        State.PendingFrom = seat;
        State.PendingOptions.Clear();
        foreach (var (s, kinds) in options) State.PendingOptions[s] = kinds;
        State.PendingResponded.Clear();
        State.PendingClaims.Clear();
        State.Stage = TurnStage.CallWindow;
        ev.Add(new CallWindowOpened(tile, seat, options));
        ev.Add(new TurnChanged(seat, TurnStage.CallWindow));
        return ev;
    }

    /// <summary>Claim the pending discard (kind = null to pass). Only during CallWindow.</summary>
    public IReadOnlyList<GameEvent> Claim(int seat, ClaimKind? kind)
    {
        Require(State.Phase == GamePhase.Play && State.Stage == TurnStage.CallWindow, "no call window");
        Require(State.PendingOptions.ContainsKey(seat), "no legal claim available");
        Require(!State.PendingResponded.Contains(seat), "already responded");
        var ev = Events();

        if (kind != null)
        {
            // Mahjong may always be attempted — validated at resolution (miscall = dead hand).
            Require(kind == ClaimKind.Mahjong || State.PendingOptions[seat].Contains(kind.Value),
                $"{kind} not a legal claim");
            State.PendingClaims[seat] = kind.Value;
        }
        State.PendingResponded.Add(seat);
        ev.Add(new ClaimMade(seat, kind));

        if (State.PendingResponded.Count == State.PendingOptions.Count)
            ResolveClaims(ev);
        return ev;
    }

    private void ResolveClaims(List<GameEvent> ev)
    {
        var from = State.PendingFrom;
        var tile = State.PendingDiscard!.Value;
        var claims = State.PendingClaims.Select(kv => (seat: kv.Key, kind: kv.Value)).ToList();

        if (claims.Count == 0)
        {
            Seat(from).Discards.Add(tile);
            CloseWindow();
            NextTurn(ev);
            return;
        }

        // Precedence: mahjong beats everything; else nearest in turn order.
        var winner = claims
            .OrderByDescending(c => c.kind == ClaimKind.Mahjong)
            .ThenBy(c => (c.seat - from + 4) % 4)
            .First();

        if (winner.kind == ClaimKind.Mahjong)
        {
            var seat = winner.seat;
            if (ValidateMahjong(seat, tile, out var hand))
            {
                CloseWindow();
                ev.Add(new MahjongDeclared(seat, hand!, SelfPicked: false));
                EndGame(ev, seat, hand!, selfPicked: false, discarder: from);
                return;
            }
            // False mahjong: dead hand, tile returns to the pool, play resumes.
            ev.Add(new HandDeclaredDead(seat, "miscalled mahjong"));
            Seat(seat).Dead = true;
            Seat(seat).DeadReason = "miscalled mahjong";
            Seat(from).Discards.Add(tile);
            CloseWindow();
            NextTurn(ev);
            return;
        }

        {
            var seat = winner.seat;
            var meld = BuildMeld(Seat(seat), tile, winner.kind);
            Seat(seat).Exposures.Add(meld);
            ev.Add(new MeldExposed(seat, meld));
            CloseWindow();
            State.CurrentSeat = seat;
            State.Stage = TurnStage.AwaitDiscard;
            ev.Add(new TurnChanged(seat, TurnStage.AwaitDiscard));
        }
    }

    private void CloseWindow()
    {
        State.PendingDiscard = null;
        State.PendingFrom = -1;
        State.PendingOptions.Clear();
        State.PendingResponded.Clear();
        State.PendingClaims.Clear();
    }

    private void NextTurn(List<GameEvent> ev)
    {
        State.CurrentSeat = GameState.Right(State.CurrentSeat);
        State.Stage = TurnStage.AwaitDraw;
        ev.Add(new TurnChanged(State.CurrentSeat, TurnStage.AwaitDraw));
    }

    /// <summary>Call options per living non-discarder seat (empty array = nothing legal;
    /// mahjong may still be attempted — it's validated at resolution).</summary>
    private Dictionary<int, ClaimKind[]> CallOptions(int discarder, Tile tile)
    {
        var opts = new Dictionary<int, ClaimKind[]>();
        var callable = tile is not (Tile.F or Tile.J);   // flowers and jokers are dead
        for (var s = 0; s < 4; s++)
        {
            if (s == discarder || Seat(s).Dead) continue;
            var kinds = new List<ClaimKind>();
            if (callable)
            {
                var concealed = Seat(s).Concealed;
                var naturals = concealed[tile];
                var jokers = concealed[Tile.J];
                // Called tile + hand partners; need ≥1 natural partner.
                if (naturals >= MinNaturalsInCall)
                {
                    if (naturals + jokers >= 2) kinds.Add(ClaimKind.Pung);
                    if (naturals + jokers >= 3) kinds.Add(ClaimKind.Kong);
                    if (naturals + jokers >= 4) kinds.Add(ClaimKind.Quint);
                }
                // Mahjong: would the rack + this tile satisfy a card hand?
                var rack = concealed.Clone();
                rack.Add(tile);
                if (CardMatcher.Analyze(rack, State.Card, exposedGroups: Exposures(s))
                        .Any(f => f.IsMahjong))
                    kinds.Add(ClaimKind.Mahjong);
            }
            opts[s] = [.. kinds];
        }
        return opts;
    }

    /// <summary>Called tile + naturals (preferred) + jokers to reach group size.</summary>
    private static Meld BuildMeld(SeatState seat, Tile called, ClaimKind kind)
    {
        var size = kind switch
        {
            ClaimKind.Pung => 3, ClaimKind.Kong => 4, ClaimKind.Quint => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var tiles = new List<Tile> { called };
        var need = size - 1;
        while (need > 0 && seat.Concealed.Remove(called)) { tiles.Add(called); need--; }
        while (need > 0 && seat.Concealed.Remove(Tile.J)) { tiles.Add(Tile.J); need--; }
        if (need > 0) throw new RuleException("not enough tiles to form meld");
        return new Meld(tiles, FromDiscard: true);
    }

    // ------------------------------------------------------------------
    // Jokers
    // ------------------------------------------------------------------

    /// <summary>Swap a held natural tile for a joker sitting in any exposure (your turn only).</summary>
    public IReadOnlyList<GameEvent> ExchangeJoker(int seat, int meldOwner, int meldIndex, Tile natural)
    {
        Require(State.Phase == GamePhase.Play && State.Stage is TurnStage.AwaitDraw or TurnStage.AwaitDiscard,
            "can only exchange on your turn");
        Require(seat == State.CurrentSeat, "not your turn");
        Require(State.Seats[seat].Concealed.Contains(natural), "natural tile not held");
        var meld = Seat(meldOwner).Exposures.ElementAtOrDefault(meldIndex)
            ?? throw new RuleException("no such meld");
        Require(meld.Tiles.Contains(Tile.J), "meld has no joker");
        // The natural must match the meld's own tile kind.
        var meldTile = meld.Tiles.First(t => t != Tile.J);
        Require(meldTile == natural, $"{natural.DisplayName()} does not fit this exposure");

        var ev = Events();
        var tiles = meld.Tiles.ToList();
        var ji = tiles.IndexOf(Tile.J);
        tiles[ji] = natural;
        Seat(seat).Concealed.Remove(natural);
        Seat(seat).Concealed.Add(Tile.J);
        Seat(meldOwner).Exposures[meldIndex] = meld with { Tiles = tiles };
        ev.Add(new JokerExchanged(seat, meldOwner, meldIndex, natural));
        return ev;
    }

    // ------------------------------------------------------------------
    // Mahjong / death / end
    // ------------------------------------------------------------------

    /// <summary>Declare mahjong on a self-picked draw (your turn, after drawing).</summary>
    public IReadOnlyList<GameEvent> DeclareMahjong(int seat)
    {
        Require(State.Phase == GamePhase.Play && State.Stage == TurnStage.AwaitDiscard, "cannot declare now");
        Require(seat == State.CurrentSeat, "not your turn");
        Require(!Seat(seat).Dead, "dead hands cannot win");
        var ev = Events();
        if (ValidateMahjong(seat, null, out var hand))
        {
            ev.Add(new MahjongDeclared(seat, hand!, SelfPicked: true));
            EndGame(ev, seat, hand!, selfPicked: true, discarder: null);
        }
        else
        {
            ev.Add(new HandDeclaredDead(seat, "miscalled mahjong"));
            Seat(seat).Dead = true;
            Seat(seat).DeadReason = "miscalled mahjong";
        }
        return ev;
    }

    /// <summary>Declare a seat dead (wrong tile count, illegal exposure, …).</summary>
    public IReadOnlyList<GameEvent> DeclareDead(int seat, string reason)
    {
        Require(State.Phase == GamePhase.Play, "game not in play");
        var ev = Events();
        Seat(seat).Dead = true;
        Seat(seat).DeadReason = reason;
        ev.Add(new HandDeclaredDead(seat, reason));
        return ev;
    }

    /// <summary>Validates a 14-tile win: concealed (+ called tile) + exposures vs the card.</summary>
    private bool ValidateMahjong(int seat, Tile? calledTile, out CardHand? hand)
    {
        hand = null;
        var s = Seat(seat);
        var rack = s.Concealed.Clone();
        if (calledTile != null) rack.Add(calledTile.Value);
        if (rack.Total + s.ExposedTileCount != 14) return false;
        var fit = CardMatcher.Analyze(rack, State.Card, exposedGroups: Exposures(seat))
            .FirstOrDefault(f => f.IsMahjong);
        if (fit == null) return false;
        hand = fit.Hand;
        return true;
    }

    private IReadOnlyList<IReadOnlyList<Tile>> Exposures(int seat) =>
        Seat(seat).Exposures.Select(m => (IReadOnlyList<Tile>)m.Tiles).ToList();

    private void EndGame(List<GameEvent> ev, int winner, CardHand hand, bool selfPicked, int? discarder)
    {
        State.Phase = GamePhase.Finished;
        State.Stage = TurnStage.Finished;
        State.WinnerSeat = winner;
        State.WinningHand = hand;
        State.SelfPicked = selfPicked;
        State.EndReason = "mahjong";
        var payouts = Scoring.Payouts(State, winner, hand, selfPicked, discarder);
        ev.Add(new GameEnded(winner, hand, payouts, "mahjong"));
    }

    private void EndWallGame(List<GameEvent> ev)
    {
        State.Phase = GamePhase.Finished;
        State.Stage = TurnStage.Finished;
        State.EndReason = "wall game";
        var payouts = Enumerable.Range(0, 4).ToDictionary(s => s, _ => 0);
        ev.Add(new GameEnded(null, null, payouts, "wall game"));
    }

    private SeatState Seat(int s) => State.Seats[s];

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new RuleException(message);
    }
}
