using MahjongTutor.Core.Card;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Rules;

public enum GamePhase { Charleston, Play, Finished }

public enum TurnStage { AwaitDraw, AwaitDiscard, CallWindow, Finished }

/// <summary>The ordered passing phases of the Charleston.</summary>
public enum CharlestonStage
{
    PassRight, PassAcross, PassLeft,          // first Charleston
    Vote,                                     // elect second Charleston (all must agree)
    SecondLeft, SecondAcross, SecondRight,    // second Charleston
    Courtesy,                                 // swap 0-3 tiles with the player across
    Done,
}

/// <summary>What a discard may be called for.</summary>
public enum ClaimKind { Pung = 1, Kong = 2, Quint = 3, Mahjong = 4 }

/// <summary>An exposed group on a player's rack.</summary>
public sealed record Meld(IReadOnlyList<Tile> Tiles, bool FromDiscard)
{
    public int Size => Tiles.Count;
}

public sealed class SeatState
{
    /// <summary>Concealed tiles on the rack (jokers included).</summary>
    public TileBag Concealed { get; } = new();
    /// <summary>Exposed groups, in call order.</summary>
    public List<Meld> Exposures { get; } = [];
    /// <summary>Discards, in order (last is on top of the pool).</summary>
    public List<Tile> Discards { get; } = [];
    /// <summary>Dead hand — may keep drawing/discarding but can never call or win.</summary>
    public bool Dead { get; set; }
    public string? DeadReason { get; set; }

    public int ExposedTileCount => Exposures.Sum(m => m.Size);
    public int TotalTileCount => Concealed.Total + ExposedTileCount;
}

public sealed class GameState
{
    public required MahjongCard Card { get; init; }
    public required int Seed { get; init; }
    public required int Dealer { get; init; }

    public SeatState[] Seats { get; } = [new(), new(), new(), new()];

    /// <summary>Ordered wall (index 0 = front); <see cref="WallIndex"/> tiles consumed so far.</summary>
    public required Tile[] Wall { get; init; }
    public int WallIndex { get; set; }
    public int WallRemaining => Wall.Length - WallIndex;

    public int CurrentSeat { get; set; }
    public GamePhase Phase { get; set; } = GamePhase.Charleston;
    public TurnStage Stage { get; set; } = TurnStage.AwaitDiscard;
    public CharlestonStage CharlestonStage { get; set; } = CharlestonStage.PassRight;

    /// <summary>Passes submitted for the current Charleston stage: seat → tiles offered.</summary>
    public Dictionary<int, List<Tile>> PendingPasses { get; } = [];
    /// <summary>Second-Charleston votes collected: seat → go again?</summary>
    public Dictionary<int, bool> PendingVotes { get; } = [];

    /// <summary>The discard currently callable, and who threw it.</summary>
    public Tile? PendingDiscard { get; set; }
    public int PendingFrom { get; set; } = -1;
    /// <summary>Seats with any legal claim on the pending discard.</summary>
    public Dictionary<int, ClaimKind[]> PendingOptions { get; } = [];
    /// <summary>Seats that have already responded (claim or pass).</summary>
    public HashSet<int> PendingResponded { get; } = [];
    /// <summary>Actual claims made: seat → kind.</summary>
    public Dictionary<int, ClaimKind> PendingClaims { get; } = [];

    public int? WinnerSeat { get; set; }
    public CardHand? WinningHand { get; set; }
    public bool SelfPicked { get; set; }
    public string? EndReason { get; set; }

    /// <summary>Seat to the right = next in turn order (mahjong rotates counterclockwise).</summary>
    public static int Right(int seat) => (seat + 1) % 4;
    public static int Across(int seat) => (seat + 2) % 4;
    public static int Left(int seat) => (seat + 3) % 4;
}

/// <summary>Thrown for any illegal action.</summary>
public sealed class RuleException(string message) : InvalidOperationException(message);
