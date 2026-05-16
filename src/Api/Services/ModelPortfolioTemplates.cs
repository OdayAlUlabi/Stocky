using Stocky.Api.Domain;

namespace Stocky.Api.Services;

/// <summary>
/// M14 #116 — curated model portfolio templates. Static catalog; targets are
/// instrument symbols + asset class + weight (sums to 100%).
/// </summary>
public static class ModelPortfolioTemplates
{
    public static readonly IReadOnlyList<ModelPortfolioTemplate> All = new[]
    {
        new ModelPortfolioTemplate
        {
            Slug = "bogleheads-3-fund",
            Name = "Bogleheads Three-Fund",
            Description = "Classic low-cost index allocation: US total market, international, and US bonds.",
            Risk = "Moderate",
            Allocations = new[]
            {
                new ModelTemplateAllocation { Symbol = "VTI", Name = "Vanguard Total Stock Market ETF", AssetClass = "Equity", WeightPercent = 60m },
                new ModelTemplateAllocation { Symbol = "VXUS", Name = "Vanguard Total International Stock ETF", AssetClass = "Equity", WeightPercent = 20m },
                new ModelTemplateAllocation { Symbol = "BND", Name = "Vanguard Total Bond Market ETF", AssetClass = "Bond", WeightPercent = 20m }
            }
        },
        new ModelPortfolioTemplate
        {
            Slug = "classic-60-40",
            Name = "Classic 60 / 40",
            Description = "Sixty percent broad equities, forty percent investment-grade bonds.",
            Risk = "Moderate",
            Allocations = new[]
            {
                new ModelTemplateAllocation { Symbol = "VOO", Name = "Vanguard S&P 500 ETF", AssetClass = "Equity", WeightPercent = 60m },
                new ModelTemplateAllocation { Symbol = "AGG", Name = "iShares Core US Aggregate Bond ETF", AssetClass = "Bond", WeightPercent = 40m }
            }
        },
        new ModelPortfolioTemplate
        {
            Slug = "aggressive-growth",
            Name = "Aggressive Growth",
            Description = "Heavy equities tilt with a small bond ballast for younger investors.",
            Risk = "Aggressive",
            Allocations = new[]
            {
                new ModelTemplateAllocation { Symbol = "VTI", Name = "Vanguard Total Stock Market ETF", AssetClass = "Equity", WeightPercent = 70m },
                new ModelTemplateAllocation { Symbol = "VXUS", Name = "Vanguard Total International Stock ETF", AssetClass = "Equity", WeightPercent = 25m },
                new ModelTemplateAllocation { Symbol = "BND", Name = "Vanguard Total Bond Market ETF", AssetClass = "Bond", WeightPercent = 5m }
            }
        },
        new ModelPortfolioTemplate
        {
            Slug = "conservative-income",
            Name = "Conservative Income",
            Description = "Bond-heavy, low-volatility allocation focused on capital preservation and income.",
            Risk = "Conservative",
            Allocations = new[]
            {
                new ModelTemplateAllocation { Symbol = "BND", Name = "Vanguard Total Bond Market ETF", AssetClass = "Bond", WeightPercent = 50m },
                new ModelTemplateAllocation { Symbol = "VOO", Name = "Vanguard S&P 500 ETF", AssetClass = "Equity", WeightPercent = 30m },
                new ModelTemplateAllocation { Symbol = "VYM", Name = "Vanguard High Dividend Yield ETF", AssetClass = "Equity", WeightPercent = 20m }
            }
        }
    };

    public static ModelPortfolioTemplate? FindBySlug(string slug) =>
        All.FirstOrDefault(t => string.Equals(t.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
