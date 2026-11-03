using Microsoft.EntityFrameworkCore;

namespace MahjongTutor.Server.Data;

public sealed class MahjongTutorDbContext(DbContextOptions<MahjongTutorDbContext> options)
    : DbContext(options)
{
    public DbSet<CardEntity> Cards => Set<CardEntity>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameMove> GameMoves => Set<GameMove>();
    public DbSet<PlayerStat> PlayerStats => Set<PlayerStat>();
    public DbSet<SavedGame> SavedGames => Set<SavedGame>();
    public DbSet<TileSetEntity> TileSets => Set<TileSetEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CardEntity>(e =>
        {
            e.Property(c => c.Document).HasColumnType("jsonb");
            // At most one active card.
            e.HasIndex(c => c.IsActive).IsUnique().HasFilter("\"IsActive\"");
        });

        b.Entity<Player>(e =>
            e.HasIndex(p => p.DisplayName).IsUnique());

        b.Entity<Room>(e =>
        {
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            e.HasOne(r => r.Card).WithMany().HasForeignKey(r => r.CardId);
        });

        b.Entity<Game>(e =>
        {
            e.Property(g => g.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(g => g.Seats).HasColumnType("jsonb");
            e.HasOne(g => g.Card).WithMany().HasForeignKey(g => g.CardId);
            e.HasMany(g => g.Moves).WithOne(m => m.Game).HasForeignKey(m => m.GameId);
        });

        b.Entity<GameMove>(e =>
        {
            e.Property(m => m.Payload).HasColumnType("jsonb");
            e.HasIndex(m => new { m.GameId, m.Seq }).IsUnique();
        });

        b.Entity<PlayerStat>(e =>
        {
            e.Property(s => s.Extra).HasColumnType("jsonb");
            e.HasIndex(s => s.PlayerId).IsUnique();
            e.HasOne(s => s.Player).WithMany().HasForeignKey(s => s.PlayerId);
        });
    }
}
