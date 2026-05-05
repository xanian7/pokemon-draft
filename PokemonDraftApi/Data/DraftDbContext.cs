using Microsoft.EntityFrameworkCore;
using PokemonDraftApi.Models;

namespace PokemonDraftApi.Data;

public class DraftDbContext : DbContext
{
    public DraftDbContext(DbContextOptions<DraftDbContext> options) : base(options) { }

    public DbSet<League> Leagues { get; set; }
    public DbSet<Player> Players { get; set; }
    public DbSet<DraftPick> Picks { get; set; }
    public DbSet<PokemonPointValue> PointValues { get; set; }
    public DbSet<Trade> Trades { get; set; }
    public DbSet<TradeItem> TradeItems { get; set; }

    protected override void OnModelCreating(ModelBuilder model)
    {
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
        model.Entity<Trade>()
            .HasMany(t => t.Items).WithOne(i => i.Trade)
            .HasForeignKey(i => i.TradeId).OnDelete(DeleteBehavior.Cascade);
    }
}
