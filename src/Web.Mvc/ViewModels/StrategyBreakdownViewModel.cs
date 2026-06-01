using Stocky.Api.Dtos;

namespace Stocky.Web.Mvc.ViewModels;

public record StrategyBreakdownViewModel(
    IReadOnlyList<IGrouping<string, StrategyHoldingDto>> Groups,
    decimal TotalMarketValue);
