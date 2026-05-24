using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stocky.Api.Data;

namespace Stocky.Api;

/// <summary>
/// Shared DI registrations used by both <c>Stocky.Api</c> (the REST + SignalR app)
/// and <c>Stocky.Web.Mvc</c> (the server-rendered HTML app introduced in the
/// non-SPA rewrite — see docs/non-spa-rearchitecture.md). Keeping the
/// registrations in one place prevents drift between the two hosts.
/// </summary>
public static class StockyServicesExtensions
{
    /// <summary>
    /// Registers the application services that both the API and MVC hosts need:
    /// market-data providers, portfolio / analytics / alerts / reports / cash
    /// services, calculators, the audit logger, and SignalR broadcasters.
    /// <para>
    /// Does <b>not</b> register hosted background services (QuoteRefresher,
    /// SnapshotJob, AlertSweepJob, ReportScheduleJob) — those should run in
    /// the API host only.
    /// </para>
    /// <para>
    /// Does <b>not</b> register <see cref="StockyDbContext"/>, caches,
    /// authentication, or OpenTelemetry — those have host-specific shapes.
    /// </para>
    /// </summary>
    public static IServiceCollection AddStockyDomainServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Market data — Alpaca when configured, stub fallback.
        var alpacaKeyId = configuration["MarketData:Alpaca:ApiKeyId"];
        var alpacaSecret = configuration["MarketData:Alpaca:ApiSecret"];
        services.AddSingleton<Services.StubMarketDataProvider>();
        if (!string.IsNullOrWhiteSpace(alpacaKeyId) && !string.IsNullOrWhiteSpace(alpacaSecret))
        {
            var alpacaBase = configuration["MarketData:Alpaca:BaseUrl"] ?? "https://data.alpaca.markets/";
            services.AddHttpClient<Services.AlpacaMarketDataProvider>(client =>
            {
                client.BaseAddress = new Uri(alpacaBase);
                client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", alpacaKeyId);
                client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", alpacaSecret);
                client.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddScoped<Services.IMarketDataProvider>(sp =>
                sp.GetRequiredService<Services.AlpacaMarketDataProvider>());
        }
        else
        {
            services.AddScoped<Services.IMarketDataProvider>(sp =>
                sp.GetRequiredService<Services.StubMarketDataProvider>());
        }

        // Core portfolio services
        services.AddScoped<Services.AlertEvaluator>();
        services.AddScoped<Services.TaxLotService>();
        services.AddScoped<Services.PortfolioLedgerService>();
        services.AddScoped<Services.PortfolioHistoryService>();
        services.AddScoped<Services.PortfolioAnalyticsService>();
        services.AddScoped<Services.WashSaleService>();
        services.AddScoped<Services.RebalanceService>();

        // M8 — Data providers / real-time broadcasters (broadcasters used by API host;
        // MVC host registers them too so HoldingsController etc. can be invoked in-process).
        services.AddScoped<Services.IExtendedMarketDataProvider, Services.StubExtendedMarketDataProvider>();
        services.AddSignalR();
        services.AddSingleton<Services.PriceTickBroadcaster>();
        services.AddSingleton<Services.PortfolioUpdatedBroadcaster>();

        // M9 — Advanced analytics
        services.AddScoped<Services.IAdvancedMarketDataProvider, Services.StubAdvancedMarketDataProvider>();
        services.AddScoped<Services.RiskMetricsService>();
        services.AddScoped<Services.BenchmarkComparisonService>();
        services.AddScoped<Services.BacktestService>();
        services.AddScoped<Services.GoalsService>();
        services.AddSingleton<Services.EarningsSurpriseService>();

        // Advanced portfolio analytics (GitHub milestone #8 — issues 1.3, 1.4,
        // 2.1, 2.2, 2.4, 2.5, 4.3). Pure-math services on top of existing
        // market-data and history infrastructure.
        services.AddScoped<Services.AdvancedRiskService>();
        services.AddScoped<Services.PortfolioOptimizerService>();
        services.AddScoped<Services.MomentumScoringService>();
        services.AddSingleton<Services.PositionSizingService>();

        // Admin force-refresh endpoint (AdminRefreshController).
        services.AddScoped<Services.DataRefreshService>();

        // M10 — Advanced alerts
        services.AddSingleton<Services.TechnicalIndicatorService>();
        services.AddScoped<Services.IInsiderTradeProvider, Services.StubInsiderTradeProvider>();
        services.AddScoped<Services.IAlertChannel, Services.InboxChannel>();
        services.AddScoped<Services.IAlertChannel, Services.EmailChannel>();
        services.AddScoped<Services.IAlertChannel, Services.PushChannel>();
        services.AddScoped<Services.IAlertChannel, Services.WebhookChannel>();
        services.AddHttpClient("stocky-webhook", c => c.Timeout = TimeSpan.FromSeconds(5));
        services.AddScoped<Services.AlertDispatcher>();
        services.AddScoped<Services.TechnicalAlertEvaluator>();
        services.AddScoped<Services.EarningsAlertEvaluator>();
        services.AddScoped<Services.NewsAlertEvaluator>();
        services.AddScoped<Services.DriftAlertEvaluator>();
        services.AddScoped<Services.InsiderAlertEvaluator>();

        // M11 — reporting & sharing
        services.AddScoped<Services.ShareTokenService>();
        services.AddScoped<Services.ReportRenderer>();

        // M14 — platform
        services.AddScoped<Services.AuditLogger>();
        services.AddScoped<Services.CashService>();
        services.AddScoped<Services.ApiKeyService>();

        return services;
    }
}
