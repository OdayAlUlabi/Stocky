using Microsoft.EntityFrameworkCore;
using Stocky.Api.Domain;

namespace Stocky.Api.Data;

public class StockyDbContext(DbContextOptions<StockyDbContext> options) : DbContext(options)
{
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<Watchlist> Watchlists => Set<Watchlist>();
    public DbSet<WatchlistItem> WatchlistItems => Set<WatchlistItem>();
    public DbSet<PriceQuote> PriceQuotes => Set<PriceQuote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Instrument>(e =>
        {
            e.HasKey(x => x.Symbol);
            e.Property(x => x.Symbol).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Exchange).HasMaxLength(32);
            e.Property(x => x.Currency).HasMaxLength(8);
            e.Property(x => x.AssetClass).HasMaxLength(16);
        });

        modelBuilder.Entity<Portfolio>(e =>
        {
            e.HasIndex(x => x.OwnerId);
            e.Property(x => x.OwnerId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.BaseCurrency).HasMaxLength(8);
        });

        modelBuilder.Entity<Holding>(e =>
        {
            e.HasIndex(x => new { x.PortfolioId, x.Symbol }).IsUnique();
            e.Property(x => x.Symbol).HasMaxLength(16).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 8);
            e.Property(x => x.AverageCost).HasPrecision(18, 8);
            e.HasOne(x => x.Instrument).WithMany().HasForeignKey(x => x.Symbol).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasIndex(x => new { x.PortfolioId, x.ExecutedAt });
            e.Property(x => x.Symbol).HasMaxLength(16);
            e.Property(x => x.Currency).HasMaxLength(8);
            e.Property(x => x.Quantity).HasPrecision(18, 8);
            e.Property(x => x.Price).HasPrecision(18, 8);
            e.Property(x => x.Fee).HasPrecision(18, 8);
            e.Property(x => x.Notes).HasMaxLength(500);
        });

        modelBuilder.Entity<Watchlist>(e =>
        {
            e.HasIndex(x => x.OwnerId);
            e.Property(x => x.OwnerId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
        });

        modelBuilder.Entity<WatchlistItem>(e =>
        {
            e.HasIndex(x => new { x.WatchlistId, x.Symbol }).IsUnique();
            e.Property(x => x.Symbol).HasMaxLength(16).IsRequired();
            e.HasOne(x => x.Instrument).WithMany().HasForeignKey(x => x.Symbol).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PriceQuote>(e =>
        {
            e.HasIndex(x => new { x.Symbol, x.AsOf });
            e.Property(x => x.Symbol).HasMaxLength(16).IsRequired();
            e.Property(x => x.Price).HasPrecision(18, 8);
            e.Property(x => x.Change).HasPrecision(18, 8);
            e.Property(x => x.ChangePercent).HasPrecision(9, 4);
            e.HasOne(x => x.Instrument).WithMany().HasForeignKey(x => x.Symbol).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
