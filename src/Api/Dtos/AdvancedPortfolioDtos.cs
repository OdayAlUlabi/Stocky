namespace Stocky.Api.Dtos;

// =============================================================================
// Advanced Portfolio Management DTOs
// See docs/Advanced_Portfolio_Features.md and GitHub milestone #8.
// Issues: 1.3 Momentum · 1.4 Optimizer · 2.1 VaR · 2.2 Stress · 2.4 Liquidity ·
//         2.5 Concentration · 4.3 Position Sizing
// =============================================================================

// -------- 2.1 Value-at-Risk (3 methods) --------------------------------------
public record VarSuiteDto(
    Guid PortfolioId,
    DateOnly From,
    DateOnly To,
    decimal ConfidenceLevel,           // e.g. 0.95
    int HoldingDays,                   // 1 or 10
    decimal PortfolioValue,
    decimal VarHistoricalDollars,
    decimal VarParametricDollars,
    decimal VarMonteCarloDollars,
    decimal CvarHistoricalDollars,
    decimal VarHistoricalPercent,
    decimal VarParametricPercent,
    decimal VarMonteCarloPercent,
    int MonteCarloSimulations,
    IReadOnlyList<WorstScenarioDto> WorstScenarios);

public record WorstScenarioDto(DateOnly Date, decimal ReturnPercent, decimal LossDollars);

// -------- 2.2 Stress Testing -------------------------------------------------
public record StressShockDto(
    decimal EquityShock,        // e.g. -0.20 = -20% market drop
    decimal RatesShock,         // e.g. +0.02 = +200bp
    decimal UsdShock,           // e.g. +0.10 = +10% USD
    decimal OilShock,           // e.g. -0.40
    decimal VixShock);          // e.g. +2.00

public record StressScenarioDto(string Id, string Name, string Description, StressShockDto Shock);

public record StressTestRequest(
    Guid PortfolioId,
    string? ScenarioId,                  // null when using custom
    StressShockDto? CustomShock,
    string SensitivityMethod = "beta");  // beta | historical | factor

public record StressHoldingImpactDto(
    string Symbol,
    decimal Quantity,
    decimal MarketValue,
    decimal Beta,
    decimal EstimatedPnlDollars,
    decimal EstimatedPnlPercent);

public record StressTestResultDto(
    Guid PortfolioId,
    string ScenarioId,
    string ScenarioName,
    StressShockDto Shock,
    decimal PortfolioValueBefore,
    decimal PortfolioPnlDollars,
    decimal PortfolioPnlPercent,
    IReadOnlyList<StressHoldingImpactDto> HoldingImpacts);

// -------- 2.4 Liquidity Risk -------------------------------------------------
public record LiquidityPositionDto(
    string Symbol,
    decimal Quantity,
    decimal MarketValue,
    decimal AverageDailyVolumeShares,
    decimal AverageDailyVolumeDollars,
    decimal DaysToLiquidate,
    bool Illiquid);

public record LiquidityRiskDto(
    Guid PortfolioId,
    decimal MaxParticipationRate,    // e.g. 0.20
    int LiquidityThresholdDays,
    int AdvLookbackDays,
    decimal PortfolioLiquidityScore, // value-weighted average days-to-liquidate
    IReadOnlyList<LiquidityPositionDto> Positions);

// -------- 2.5 Concentration / HHI -------------------------------------------
public record ConcentrationBucketDto(string Key, decimal Weight);

public record ConcentrationBreachDto(string Limit, string Key, decimal Current, decimal Threshold);

public record ConcentrationRiskDto(
    Guid PortfolioId,
    decimal HhiScore,                              // 0-10000
    string HhiInterpretation,                      // diversified | moderate | concentrated
    decimal DiversificationScore,                  // 0-100 (100 = max diversified)
    IReadOnlyList<ConcentrationBucketDto> Positions,
    IReadOnlyList<ConcentrationBucketDto> Sectors,
    IReadOnlyList<ConcentrationBucketDto> Countries,
    IReadOnlyList<ConcentrationBreachDto> Breaches);

// -------- 1.3 Momentum Scoring ----------------------------------------------
public record MomentumRequest(
    IReadOnlyList<string> Universe,
    IReadOnlyList<int> LookbackDays,    // e.g. [21, 63, 126, 252]
    bool VolatilityScale = true,
    bool SkipLatestMonth = true);

public record MomentumScoreDto(
    string Symbol,
    decimal CompositeScore,             // 0-100 percentile
    IReadOnlyDictionary<int, decimal> WindowReturns,
    IReadOnlyDictionary<int, decimal> WindowVolAdjusted,
    int Rank);

public record MomentumScoreSetDto(
    DateOnly AsOf,
    int UniverseSize,
    IReadOnlyList<int> Windows,
    IReadOnlyList<MomentumScoreDto> Scores);

// -------- 1.4 Mean-Variance Portfolio Optimizer -----------------------------
public record OptimizerRequest(
    IReadOnlyList<string> Symbols,
    int LookbackDays = 252,
    decimal MaxWeight = 0.25m,
    decimal MinWeight = 0.0m,            // 0 = long-only
    decimal RiskFreeRate = 0.0m,
    string CovarianceEstimator = "sample");  // sample | ledoit_wolf

public record FrontierPointDto(decimal ExpectedReturn, decimal Volatility, decimal Sharpe);

public record AssetWeightDto(string Symbol, decimal Weight);

public record OptimizerResultDto(
    DateOnly AsOf,
    IReadOnlyList<string> Symbols,
    int LookbackDays,
    IReadOnlyList<AssetWeightDto> TangencyWeights,
    decimal TangencyReturn,
    decimal TangencyVolatility,
    decimal TangencySharpe,
    IReadOnlyList<AssetWeightDto> MinVarianceWeights,
    decimal MinVarianceReturn,
    decimal MinVarianceVolatility,
    IReadOnlyList<FrontierPointDto> EfficientFrontier);

// -------- 4.3 Position Sizing -----------------------------------------------
public record PositionSizingRequest(
    decimal AccountSize,
    decimal EntryPrice,
    decimal StopLossPrice,                  // for risk-per-trade method
    decimal RiskPerTradePercent = 0.01m,    // e.g. 0.01 = 1% of account
    decimal? WinRate = null,                // Kelly inputs
    decimal? AvgWinDollars = null,
    decimal? AvgLossDollars = null,
    decimal? AssetVolatilityPercent = null, // for vol-targeting
    decimal? TargetVolatilityPercent = null);

public record PositionSizingResultDto(
    decimal AccountSize,
    decimal EntryPrice,
    decimal RiskPerTradeDollars,
    int FixedFractionalShares,        // shares such that loss-at-stop = risk amount
    decimal FixedFractionalNotional,
    int KellyShares,                  // null inputs => 0
    decimal KellyFraction,
    decimal KellyNotional,
    int VolTargetShares,
    decimal VolTargetNotional,
    int RecommendedShares,
    string RecommendedMethod,
    string Rationale);
