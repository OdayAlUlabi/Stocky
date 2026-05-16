using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

public class QuoteRefresherMarketHoursTests
{
    // Helper: build a UTC instant equivalent to a given Eastern wall-clock time
    // on a known weekday. 2025-06-04 was a Wednesday; EDT = UTC-4 in June, so
    // 14:30 UTC == 10:30 ET (open) and 21:30 UTC == 17:30 ET (closed).
    [Theory]
    [InlineData("2025-06-04T14:30:00Z", true)]   // Wed 10:30 ET
    [InlineData("2025-06-04T13:29:00Z", false)]  // Wed 09:29 ET (pre-open)
    [InlineData("2025-06-04T13:30:00Z", true)]   // Wed 09:30 ET (open bell)
    [InlineData("2025-06-04T20:00:00Z", false)]  // Wed 16:00 ET (closing bell)
    [InlineData("2025-06-04T19:59:59Z", true)]   // Wed 15:59 ET
    [InlineData("2025-06-07T14:30:00Z", false)]  // Sat
    [InlineData("2025-06-08T14:30:00Z", false)]  // Sun
    public void IsUsEquityMarketOpen(string utcIso, bool expected)
    {
        var now = DateTimeOffset.Parse(utcIso, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, QuoteRefresher.IsUsEquityMarketOpen(now));
    }
}
