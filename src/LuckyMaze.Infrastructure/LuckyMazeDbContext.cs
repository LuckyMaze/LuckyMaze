using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Toamaisutaa.EntityFrameworkCore;
using LuckyMaze.Domain;

namespace LuckyMaze.Infrastructure
{
    public class LuckyMazeDbContext(DbContextOptions<LuckyMazeDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Maze> Mazes => Set<Maze>();
        public DbSet<Game> Games => Set<Game>();
        public DbSet<Bet> Bets => Set<Bet>();
        public DbSet<GameSettings> Settings => Set<GameSettings>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(LuckyMazeDbContext).Assembly);
            modelBuilder.ApplyToamaisutaaConfiguration();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ApplySaveChangesGuards();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ApplySaveChangesGuards();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ApplySaveChangesGuards()
        {
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
                if (entry.State == EntityState.Modified)
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
    }

    public class LuckyMazeDbContextFactory : IDesignTimeDbContextFactory<LuckyMazeDbContext>
    {
        public LuckyMazeDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<LuckyMazeDbContext>();
            var connectionString = "Host=localhost;Port=3135;Database=luckymaze-dev;Username=postgres;Password=d4vpas8w0rd13!!!";
            optionsBuilder.UseNpgsql(connectionString);
            return new LuckyMazeDbContext(optionsBuilder.Options);
        }
    }
}
