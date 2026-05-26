using Microsoft.EntityFrameworkCore;
using PokemonDraft.Models;

namespace PokemonDraft.Data;

public class DraftDbContext : DbContext
{
    public DraftDbContext(DbContextOptions<DraftDbContext> options) : base(options) { }

    public DbSet<AppUser> AppUsers { get; set; }
    public DbSet<League> Leagues { get; set; }
    public DbSet<Player> Players { get; set; }
    public DbSet<DraftPick> Picks { get; set; }
    public DbSet<PokemonPointValue> PointValues { get; set; }
    public DbSet<Trade> Trades { get; set; }
    public DbSet<TradeItem> TradeItems { get; set; }
    public DbSet<Matchup> Matchups { get; set; }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AppUser>().HasKey(u => u.Id);
        model.Entity<AppUser>().HasIndex(u => u.GoogleId).IsUnique();
        model.Entity<AppUser>()
            .HasMany(u => u.Players).WithOne(p => p.User)
            .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.SetNull);

        model.Entity<League>().HasKey(l => l.Code);
        model.Entity<League>()
            .HasMany(l => l.Players).WithOne(p => p.League)
            .HasForeignKey(p => p.LeagueCode).OnDelete(DeleteBehavior.Cascade);
        model.Entity<League>()
            .HasMany(l => l.Picks).WithOne(p => p.League)
            .HasForeignKey(p => p.LeagueCode).OnDelete(DeleteBehavior.Cascade);
        model.Entity<League>()
            .HasMany(l => l.PointValues).WithOne(p => p.League)
            .HasForeignKey(p => p.LeagueCode).OnDelete(DeleteBehavior.Cascade);
        model.Entity<League>()
            .HasMany(l => l.Trades).WithOne(t => t.League)
            .HasForeignKey(t => t.LeagueCode).OnDelete(DeleteBehavior.Cascade);
        model.Entity<League>()
            .HasMany(l => l.Matchups).WithOne(m => m.League)
            .HasForeignKey(m => m.LeagueCode).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Trade>()
            .HasMany(t => t.Items).WithOne(i => i.Trade)
            .HasForeignKey(i => i.TradeId).OnDelete(DeleteBehavior.Cascade);
    }
}
