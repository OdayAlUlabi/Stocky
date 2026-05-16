using Microsoft.EntityFrameworkCore;
using Stocky.Api.Domain;

namespace Stocky.Api.Data;

public class StockyDbContext(DbContextOptions<StockyDbContext> options) : DbContext(options)
{
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<InstrumentMetadata> InstrumentMetadata => Set<InstrumentMetadata>();
    public DbSet<Watchlist> Watchlists => Set<Watchlist>();
    public DbSet<WatchlistItem> WatchlistItems => Set<WatchlistItem>();
    public DbSet<PriceQuote> PriceQuotes => Set<PriceQuote>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<TaxLot> TaxLots => Set<TaxLot>();
    public DbSet<RealizedGain> RealizedGains => Set<RealizedGain>();
    public DbSet<PortfolioSnapshot> PortfolioSnapshots => Set<PortfolioSnapshot>();
    public DbSet<NewsItem> NewsItems => Set<NewsItem>();
    public DbSet<EarningsEvent> EarningsEvents => Set<EarningsEvent>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

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

        modelBuilder.Entity<InstrumentMetadata>(e =>
        {
            e.HasKey(x => x.Symbol);
            e.Property(x => x.Symbol).HasMaxLength(16);
            e.Property(x => x.Sector).HasMaxLength(64);
            e.Property(x => x.Industry).HasMaxLength(80);
            e.Property(x => x.Country).HasMaxLength(48);
            e.Property(x => x.MarketCap).HasPrecision(20, 2);
            e.Property(x => x.Beta).HasPrecision(9, 4);
            e.Property(x => x.DividendYield).HasPrecision(9, 4);
            e.HasOne(x => x.Instrument).WithMany().HasForeignKey(x => x.Symbol).OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<Alert>(e =>
        {
            e.HasIndex(x => new { x.OwnerId, x.Status });
            e.Property(x => x.OwnerId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Symbol).HasMaxLength(16).IsRequired();
            e.Property(x => x.Threshold).HasPrecision(18, 8);
            e.Property(x => x.TriggeredValue).HasPrecision(18, 8);
            e.Property(x => x.Note).HasMaxLength(200);
        });

        modelBuilder.Entity<TaxLot>(e =>
        {
            e.HasIndex(x => new { x.PortfolioId, x.Symbol, x.OpenedAt });
            e.Property(x => x.Symbol).HasMaxLength(16).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 8);
            e.Property(x => x.RemainingQuantity).HasPrecision(18, 8);
            e.Property(x => x.CostPerShare).HasPrecision(18, 8);
        });

        modelBuilder.Entity<RealizedGain>(e =>
        {
            e.HasIndex(x => new { x.PortfolioId, x.SoldAt });
            e.Property(x => x.Symbol).HasMaxLength(16).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 8);
            e.Property(x => x.CostBasis).HasPrecision(18, 8);
            e.Property(x => x.Proceeds).HasPrecision(18, 8);
            e.Property(x => x.Gain).HasPrecision(18, 8);
        });

        modelBuilder.Entity<PortfolioSnapshot>(e =>
        {
            e.HasIndex(x => new { x.PortfolioId, x.Date }).IsUnique();
            e.Property(x => x.MarketValue).HasPrecision(18, 8);
            e.Property(x => x.CostBasis).HasPrecision(18, 8);
            e.Property(x => x.DayPnL).HasPrecision(18, 8);
        });

        modelBuilder.Entity<NewsItem>(e =>
        {
            e.HasIndex(x => x.PublishedAt);
            e.HasIndex(x => x.Symbol);
            e.Property(x => x.Headline).HasMaxLength(300).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(2000);
            e.Property(x => x.Source).HasMaxLength(80).IsRequired();
            e.Property(x => x.Url).HasMaxLength(500);
            e.Property(x => x.Symbol).HasMaxLength(16);
            e.Property(x => x.Category).HasMaxLength(24);
        });

        modelBuilder.Entity<EarningsEvent>(e =>
        {
            e.HasIndex(x => new { x.Date, x.Symbol }).IsUnique();
            e.Property(x => x.Symbol).HasMaxLength(16).IsRequired();
            e.Property(x => x.Time).HasMaxLength(8);
            e.Property(x => x.EpsEstimate).HasPrecision(18, 4);
            e.Property(x => x.EpsActual).HasPrecision(18, 4);
            e.Property(x => x.RevenueEstimate).HasPrecision(20, 2);
            e.Property(x => x.RevenueActual).HasPrecision(20, 2);
        });

        modelBuilder.Entity<UserSettings>(e =>
        {
            e.HasKey(x => x.OwnerId);
            e.Property(x => x.OwnerId).HasMaxLength(64);
            e.Property(x => x.DisplayCurrency).HasMaxLength(8);
            e.Property(x => x.Theme).HasMaxLength(8);
            e.Property(x => x.Locale).HasMaxLength(16);
        });
    }
}
