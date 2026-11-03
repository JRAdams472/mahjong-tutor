using MahjongTutor.Core.Card;
using MahjongTutor.Core.Tiles;

namespace MahjongTutor.Core.Rules;

/// <summary>Serializable save-game snapshot — GameState is rebuilt from this.</summary>
public sealed class GameSnapshot
{
    public int Seed { get; set; }
    public int Dealer { get; set; }
    public int CurrentSeat { get; set; }
    public string Phase { get; set; } = "";
    public string Stage { get; set; } = "";
    public string CharlestonStage { get; set; } = "";
    public int WallIndex { get; set; }
    public List<Tile> Wall { get; set; } = [];
    public Tile? PendingDiscard { get; set; }
    public int PendingFrom { get; set; } = -1;
    public Dictionary<int, List<ClaimKind>> PendingOptions { get; set; } = [];
    public List<int> PendingResponded { get; set; } = [];
    public Dictionary<int, ClaimKind> PendingClaims { get; set; } = [];
    public Dictionary<int, List<Tile>> PendingPasses { get; set; } = [];
    public Dictionary<int, bool> PendingVotes { get; set; } = [];
    public int? WinnerSeat { get; set; }
    public string? WinningHandId { get; set; }
    public bool SelfPicked { get; set; }
    public string? EndReason { get; set; }
    public List<SeatSnap> Seats { get; set; } = [];

    public sealed class SeatSnap
    {
        public List<Tile> Concealed { get; set; } = [];
        public List<MeldSnap> Exposures { get; set; } = [];
        public List<Tile> Discards { get; set; } = [];
        public bool Dead { get; set; }
        public string? DeadReason { get; set; }
    }

    public sealed class MeldSnap
    {
        public List<Tile> Tiles { get; set; } = [];
        public bool FromDiscard { get; set; }
    }

    public static GameSnapshot Capture(GameState s) => new()
    {
        Seed = s.Seed, Dealer = s.Dealer, CurrentSeat = s.CurrentSeat,
        Phase = s.Phase.ToString(), Stage = s.Stage.ToString(),
        CharlestonStage = s.CharlestonStage.ToString(),
        WallIndex = s.WallIndex, Wall = [.. s.Wall],
        PendingDiscard = s.PendingDiscard, PendingFrom = s.PendingFrom,
        PendingOptions = s.PendingOptions.ToDictionary(k => k.Key, v => v.Value.ToList()),
        PendingResponded = [.. s.PendingResponded],
        PendingClaims = new(s.PendingClaims),
        PendingPasses = s.PendingPasses.ToDictionary(k => k.Key, v => v.Value.ToList()),
        PendingVotes = new(s.PendingVotes),
        WinnerSeat = s.WinnerSeat, WinningHandId = s.WinningHand?.Id,
        SelfPicked = s.SelfPicked, EndReason = s.EndReason,
        Seats = [.. s.Seats.Select(st => new SeatSnap
        {
            Concealed = st.Concealed.ToList(),
            Exposures = [.. st.Exposures.Select(m =>
                new MeldSnap { Tiles = [.. m.Tiles], FromDiscard = m.FromDiscard })],
            Discards = [.. st.Discards], Dead = st.Dead, DeadReason = st.DeadReason,
        })],
    };

    public static GameState Restore(GameSnapshot d, MahjongCard card)
    {
        var s = new GameState { Card = card, Seed = d.Seed, Dealer = d.Dealer, Wall = d.Wall.ToArray() };
        s.CurrentSeat = d.CurrentSeat;
        s.Phase = Enum.Parse<GamePhase>(d.Phase);
        s.Stage = Enum.Parse<TurnStage>(d.Stage);
        s.CharlestonStage = Enum.Parse<CharlestonStage>(d.CharlestonStage);
        s.WallIndex = d.WallIndex;
        s.PendingDiscard = d.PendingDiscard; s.PendingFrom = d.PendingFrom;
        foreach (var (k, v) in d.PendingOptions) s.PendingOptions[k] = v.ToArray();
        foreach (var i in d.PendingResponded) s.PendingResponded.Add(i);
        foreach (var (k, v) in d.PendingClaims) s.PendingClaims[k] = v;
        foreach (var (k, v) in d.PendingPasses) s.PendingPasses[k] = v;
        foreach (var (k, v) in d.PendingVotes) s.PendingVotes[k] = v;
        s.WinnerSeat = d.WinnerSeat; s.SelfPicked = d.SelfPicked; s.EndReason = d.EndReason;
        s.WinningHand = card.AllHands.FirstOrDefault(h => h.Id == d.WinningHandId);
        for (var i = 0; i < 4 && i < d.Seats.Count; i++)
        {
            var st = s.Seats[i]; var ds = d.Seats[i];
            st.Concealed.Clear();
            foreach (var t in ds.Concealed) st.Concealed.Add(t);
            st.Exposures.Clear();
            foreach (var m in ds.Exposures) st.Exposures.Add(new Meld(m.Tiles, m.FromDiscard));
            st.Discards.Clear(); st.Discards.AddRange(ds.Discards);
            st.Dead = ds.Dead; st.DeadReason = ds.DeadReason;
        }
        return s;
    }
}
