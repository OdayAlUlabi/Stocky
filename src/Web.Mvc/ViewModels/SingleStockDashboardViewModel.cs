using Stocky.Api.Dtos;

namespace Stocky.Web.Mvc.ViewModels;

public sealed record SingleStockDashboardViewModel(
    string Symbol,
    string Timeframe,
    SingleStockStrategyConfigDto Strategy,
    SingleStockAnalysisDto Analysis,
    SingleStockBacktestDto Backtest,
    SingleStockWalkForwardDto WalkForward,
    IReadOnlyList<OhlcBarDto> Bars,
    TdSequentialResultDto? TdSequential);