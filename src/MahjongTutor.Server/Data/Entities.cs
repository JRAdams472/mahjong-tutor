namespace MahjongTutor.Server.Data;

/// <summary>A stored yearly card — the whole document lives in <see cref="Document"/> as JSONB.</summary>
public sealed class CardEntity
{
    public int Id { get; set; }
    public int Year { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Denormalized count of hands (inside <see cref="Document"/>) for list views.</summary>
    public int HandCount { get; set; }
    /// <summary>Exactly one card is active at a time; enforced by a filtered unique index.</summary>
    public bool IsActive { get; set; }
    /// <summary>Serialized <c>MahjongCard</c> (CardJson format) — jsonb column.</summary>
    public string Document { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>A named person at the table (no passwords — LAN app).</summary>
public sealed class Player
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

public enum RoomStatus { Open, InProgress, Closed }

/// <summary>A multiplayer room; seats are humans + bots to four.</summary>
public sealed class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public RoomStatus Status { get; set; }
    /// <summary>Room creator's choice: whether live coach advice is allowed in this game.</summary>
    public bool CoachEnabled { get; set; }
    public int? CardId { get; set; }
    public CardEntity? Card { get; set; }
    public DateTime CreatedUtc { get; set; }
    public List<Game> Games { get; set; } = [];
}

public enum GameStatus { InProgress, Finished, Abandoned }

/// <summary>One game (or in-progress session) within a room or solo.</summary>
public sealed class Game
{
    public long Id { get; set; }
    public int? RoomId { get; set; }
    public Room? Room { get; set; }
    public int CardId { get; set; }
    public CardEntity Card { get; set; } = null!;
    public GameStatus Status { get; set; }
    /// <summary>jsonb: seat → { kind: human|bot, playerId?, botLevel? }.</summary>
    public string Seats { get; set; } = "{}";
    public int? WinnerSeat { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public List<GameMove> Moves { get; set; } = [];
}

/// <summary>Append-only event log — powers post-game review, stats, and save/resume.</summary>
public sealed class GameMove
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public Game Game { get; set; } = null!;
    /// <summary>Monotonic sequence within the game.</summary>
    public int Seq { get; set; }
    /// <summary>Event kind: deal, charleston-pass, draw, discard, call, expose, joker-exchange, mahjong, wall-end…</summary>
    public string Type { get; set; } = "";
    /// <summary>jsonb event payload.</summary>
    public string Payload { get; set; } = "";
    public DateTime Utc { get; set; }
}

/// <summary>Rolling per-player aggregates, updated when a game finishes.</summary>
public sealed class PlayerStat
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public long PointsScored { get; set; }
    /// <summary>jsonb: grade distribution, missed-call counts, favorite hands…</summary>
    public string Extra { get; set; } = "{}";
}

/// <summary>A tile image set on disk (bundled or uploaded); files live outside the DB.</summary>
public sealed class TileSetEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Folder containing the named tile images.</summary>
    public string Path { get; set; } = "";
    public bool IsBundled { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>A saved in-progress game — full GameState snapshot as JSON.</summary>
public sealed class SavedGame
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Snapshot { get; set; } = "";
    public DateTime UpdatedUtc { get; set; }
}
