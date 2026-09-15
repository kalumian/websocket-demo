using Microsoft.EntityFrameworkCore;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RoomEntity> Rooms => Set<RoomEntity>();
    public DbSet<ChatMessageEntity> Messages => Set<ChatMessageEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<RoomEntity>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.InviteCode).IsUnique();
        });

        modelBuilder.Entity<ChatMessageEntity>(e =>
        {
            e.HasOne(x => x.Room)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.RoomId);
        });

        modelBuilder.Entity<RefreshTokenEntity>(e =>
        {
            e.HasKey(x => x.Token);
            e.HasIndex(x => x.Username);
        });
    }
}
