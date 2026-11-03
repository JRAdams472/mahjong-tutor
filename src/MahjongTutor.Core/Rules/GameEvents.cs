using MahjongTutor.Core.Card;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Rules;

/// <summary>Something that happened; feeds the UI, the event log, and bots.</summary>
public abstract record GameEvent;

public sealed record Dealt() : GameEvent;

public sealed record CharlestonStageChanged(CharlestonStage Stage) : GameEvent;

/// <summary>Tiles moved for a pass (tile identities are known only to sender+receiver).</summary>
public sealed record PassCompleted(CharlestonStage Stage, int FromSeat, int ToSeat, int Count) : GameEvent;

/// <summary>Second-Charleston vote result.</summary>
public sealed record CharlestonVote(bool Proceed) : GameEvent;

public sealed record TileDrawn(int Seat, Tile Tile) : GameEvent;

public sealed record TileDiscarded(int Seat, Tile Tile) : GameEvent;

/// <summary>A discard became callable; <see cref="Options"/> = legal claims per seat.</summary>
public sealed record CallWindowOpened(Tile Tile, int FromSeat, IReadOnlyDictionary<int, ClaimKind[]> Options) : GameEvent;

/// <summary>A seat responded to the call window (null kind = passed).</summary>
public sealed record ClaimMade(int Seat, ClaimKind? Kind) : GameEvent;

public sealed record MeldExposed(int Seat, Meld Meld) : GameEvent;

/// <summary>A natural tile swapped for a joker in a rack exposure.</summary>
public sealed record JokerExchanged(int Seat, int MeldOwner, int MeldIndex, Tile Natural) : GameEvent;

public sealed record MahjongDeclared(int Seat, CardHand Hand, bool SelfPicked) : GameEvent;

public sealed record HandDeclaredDead(int Seat, string Reason) : GameEvent;

public sealed record TurnChanged(int Seat, TurnStage Stage) : GameEvent;

/// <summary>Terminal event. WinnerSeat null = wall game.</summary>
public sealed record GameEnded(int? WinnerSeat, CardHand? Hand,
    IReadOnlyDictionary<int, int> Payouts, string Reason) : GameEvent;
