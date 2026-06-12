using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Stocky.Api.Controllers;
using Stocky.Api.Dtos;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

public class SingleStockAnalysisControllerTests
{
    [Fact]
    public async Task Endpoints_return_payloads_for_symbol()
    {
        var controller = new SingleStockAnalysisController(new SingleStockAnalysisService(new StubMarketDataProvider(), new TechnicalIndicatorService()));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.RequestServices = new ServiceCollection()
            .AddSingleton(new SingleStockTradingBotService(new StubMarketDataProvider(), new TechnicalIndicatorService()))
            .BuildServiceProvider();

        var analysis = await controller.Get("AAPL");
        var analysisOk = Assert.IsType<OkObjectResult>(analysis.Result);
        Assert.IsType<SingleStockAnalysisDto>(analysisOk.Value);

        var backtest = await controller.Backtest("AAPL");
        var backtestOk = Assert.IsType<OkObjectResult>(backtest.Result);
        Assert.IsType<SingleStockBacktestDto>(backtestOk.Value);

        var walkForward = await controller.WalkForward("AAPL");
        var walkForwardOk = Assert.IsType<OkObjectResult>(walkForward.Result);
        Assert.IsType<SingleStockWalkForwardDto>(walkForwardOk.Value);
    }
}
