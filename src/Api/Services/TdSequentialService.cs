using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Computes TD Sequential, TD Combo, TDST levels, and a fixed-range
/// Volume Profile for a single symbol — a C# port of the TradingView
/// Pine Script "TD Sequential + Combo + VP (DeMark) — Tone Vays style".
///
/// All bar-level state follows Pine's per-bar execution model (oldest bar
/// first). Array index 0 = oldest bar; array index N-1 = most recent bar.
/// Lookback notation: close[N] in Pine == bars[i - N].Close in C#.
/// </summary>
public sealed class TdSequentialService(IAdvancedMarketDataProvider provider)
{
    // ------------------------------------------------------------------ //
    //  Public entry-point
    // ------------------------------------------------------------------ //

    public async Task<TdSequentialResultDto> ComputeAsync(
        string symbol,
        string timeframe = "1D",
        int displayBars = 100,
        int vpLookback = 250,
        int vpRows = 24,
        double vpValueAreaPct = 70.0,
        CancellationToken ct = default)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Fetch enough history for both warmup (9+ bars) and VP lookback.
        var fetchFrom = today.AddYears(-3);
        var allBars = await provider.GetOhlcAsync(symbol, fetchFrom, today, ct);

        var warnings = new List<string>();
        if (allBars.Count == 0)
        {
            warnings.Add("No OHLCV bars were returned for this symbol.");
            return new TdSequentialResultDto(symbol, timeframe, today, today,
                0, 0, 0, 0, false, false, null, null,
                Array.Empty<TdBarSignalDto>(), null, warnings);
        }

        // Sort oldest → newest (provider should already do this, but be safe).
        var allSorted = allBars.OrderBy(b => b.Date).ToArray();

        // Compute TD signals across the full history (warmup included).
        var signalsAll = ComputeSignals(allSorted);

        // Volume profile uses the last vpLookback bars of the full history.
        var vpBars = allSorted.TakeLast(vpLookback).ToArray();
        var vp = vpBars.Length >= 5 ? ComputeVolumeProfile(vpBars, vpRows, vpValueAreaPct) : null;

        // Return only the last displayBars for the table view.
        var barSlice = signalsAll.TakeLast(displayBars).ToArray();
        var lastSignal = signalsAll.Length > 0 ? signalsAll[^1] : null;

        return new TdSequentialResultDto(
            symbol, timeframe,
            barSlice.Length > 0 ? barSlice[0].Date : today,
            barSlice.Length > 0 ? barSlice[^1].Date : today,
            lastSignal?.BuySetup ?? 0,
            lastSignal?.SellSetup ?? 0,
            lastSignal?.BuyCountdown ?? 0,
            lastSignal?.SellCountdown ?? 0,
            /* buyCDActive  */ lastSignal?.BuyCountdown > 0,
            /* sellCDActive */ lastSignal?.SellCountdown > 0,
            lastSignal?.TdstResistance,
            lastSignal?.TdstSupport,
            barSlice,
            vp,
            warnings);
    }

    // ------------------------------------------------------------------ //
    //  TD Setup + Countdown + Combo state machine
    // ------------------------------------------------------------------ //

    private static TdBarSignalDto[] ComputeSignals(OhlcBarDto[] bars)
    {
        var result = new TdBarSignalDto[bars.Length];

        // ── Setup state ──────────────────────────────────────────────────
        int buySetup = 0, sellSetup = 0;
        int prevBuySetup = 0, prevSellSetup = 0;

        // ── TDST levels ─────────────────────────────────────────────────
        double? tdstRes = null, tdstSup = null;
        double prevClose = double.NaN;

        // ── Sequential Countdown ─────────────────────────────────────────
        bool buyCDactive = false, sellCDactive = false;
        int buyCD = 0, sellCD = 0;
        double? buyCD8close = null, sellCD8close = null;

        // ── Combo ────────────────────────────────────────────────────────
        bool cBuyAct = false, cSellAct = false;
        int cBuyCD = 0, cSellCD = 0;
        double? cBuyPrev = null, cSellPrev = null;

        for (int i = 0; i < bars.Length; i++)
        {
            var bar = bars[i];

            double close = (double)bar.Close;
            double high  = (double)bar.High;
            double low   = (double)bar.Low;

            // ── Safe lookbacks ───────────────────────────────────────────
            double C(int n) => i >= n ? (double)bars[i - n].Close : close;
            double H(int n) => i >= n ? (double)bars[i - n].High  : high;
            double L(int n) => i >= n ? (double)bars[i - n].Low   : low;

            double close1 = C(1), close4 = C(4), close5 = C(5);
            double high1 = H(1), high2 = H(2), high3 = H(3);
            double low1  = L(1), low2  = L(2), low3  = L(3);

            // ── Flip signals ─────────────────────────────────────────────
            bool bullFlip = close < close4 && close1 > close5;
            bool bearFlip = close > close4 && close1 < close5;

            // ── Buy Setup ────────────────────────────────────────────────
            prevBuySetup = buySetup;
            if (bullFlip)
                buySetup = 1;
            else if (close < close4 && buySetup >= 1 && buySetup < 9)
                buySetup++;
            else if (close < close4 && buySetup == 9)
                { /* stay at 9 */ }
            else
                buySetup = 0;

            // ── Sell Setup ───────────────────────────────────────────────
            prevSellSetup = sellSetup;
            if (bearFlip)
                sellSetup = 1;
            else if (close > close4 && sellSetup >= 1 && sellSetup < 9)
                sellSetup++;
            else if (close > close4 && sellSetup == 9)
                { /* stay at 9 */ }
            else
                sellSetup = 0;

            // ── Perfected setup ──────────────────────────────────────────
            bool buyPerfected  = buySetup  == 9 && Math.Min(low,  low1)  <= Math.Min(low2,  low3);
            bool sellPerfected = sellSetup == 9 && Math.Max(high, high1) >= Math.Max(high2, high3);

            bool buySetupDone  = buySetup  == 9 && prevBuySetup  == 8;
            bool sellSetupDone = sellSetup == 9 && prevSellSetup == 8;

            // ── TDST levels ──────────────────────────────────────────────
            if (buySetupDone)
            {
                double h = high;
                for (int j = 1; j <= 8 && (i - j) >= 0; j++)
                    h = Math.Max(h, (double)bars[i - j].High);
                tdstRes = h;
            }
            if (sellSetupDone)
            {
                double l = low;
                for (int j = 1; j <= 8 && (i - j) >= 0; j++)
                    l = Math.Min(l, (double)bars[i - j].Low);
                tdstSup = l;
            }

            bool tdstResBreak = tdstRes.HasValue && close > tdstRes.Value && !double.IsNaN(prevClose) && prevClose <= tdstRes.Value;
            bool tdstSupBreak = tdstSup.HasValue && close < tdstSup.Value && !double.IsNaN(prevClose) && prevClose >= tdstSup.Value;

            // ── Countdown activation ─────────────────────────────────────
            // A new buy setup cancels sell CD, and vice-versa.
            if (buySetupDone)
            {
                buyCDactive  = true;  buyCD = 0; buyCD8close  = null;
                sellCDactive = false; sellCD = 0; sellCD8close = null;
                cBuyAct  = true;  cBuyCD = 0; cBuyPrev  = null;
                cSellAct = false; cSellCD = 0;
            }
            if (sellSetupDone)
            {
                sellCDactive = true;  sellCD = 0; sellCD8close = null;
                buyCDactive  = false; buyCD  = 0; buyCD8close  = null;
                cSellAct = true;  cSellCD = 0; cSellPrev = null;
                cBuyAct  = false; cBuyCD  = 0;
            }

            // ── Sequential Countdown increment ───────────────────────────
            bool buyCD13  = false;
            bool sellCD13 = false;

            if (buyCDactive && close <= low2)
            {
                buyCD++;
                if (buyCD == 8) buyCD8close = close;
            }
            if (sellCDactive && close >= H(2))
            {
                sellCD++;
                if (sellCD == 8) sellCD8close = close;
            }

            // ── Countdown completion (with bar-8 qualifier) ──────────────
            if (buyCDactive && buyCD >= 13)
            {
                if (buyCD8close == null || low <= buyCD8close.Value)
                {
                    buyCD13     = true;
                    buyCDactive = false;
                    buyCD       = 0;
                    buyCD8close = null;
                }
                else
                    buyCD = 12; // retry next qualifying bar
            }
            if (sellCDactive && sellCD >= 13)
            {
                if (sellCD8close == null || high >= sellCD8close.Value)
                {
                    sellCD13     = true;
                    sellCDactive = false;
                    sellCD       = 0;
                    sellCD8close = null;
                }
                else
                    sellCD = 12;
            }

            // ── Countdown cancellation (recycling) ───────────────────────
            if (buyCDactive  && close > close4 && close1 > close5)
            {
                buyCDactive = false;
                buyCD = 0;
            }
            if (sellCDactive && close < close4 && close1 < close5)
            {
                sellCDactive = false;
                sellCD = 0;
            }

            // ── Combo Countdown ──────────────────────────────────────────
            bool comboBuy13  = false;
            bool comboSell13 = false;

            bool buyComboQual = cBuyAct
                && close  <= low2
                && low    <  low1
                && close  <  close1
                && (cBuyPrev == null || close < cBuyPrev.Value);

            bool sellComboQual = cSellAct
                && close  >= H(2)
                && high   >  high1
                && close  >  close1
                && (cSellPrev == null || close > cSellPrev.Value);

            if (buyComboQual)  { cBuyCD++;  cBuyPrev  = close; }
            if (sellComboQual) { cSellCD++; cSellPrev = close; }

            if (cBuyAct && cBuyCD >= 13)
            {
                comboBuy13 = true;
                cBuyAct    = false;
                cBuyCD     = 0;
            }
            if (cSellAct && cSellCD >= 13)
            {
                comboSell13 = true;
                cSellAct    = false;
                cSellCD     = 0;
            }

            // Combo cancellation
            if (cBuyAct  && close > close4 && close1 > close5) { cBuyAct  = false; cBuyCD  = 0; }
            if (cSellAct && close < close4 && close1 < close5) { cSellAct = false; cSellCD = 0; }

            // ── Emit signal ──────────────────────────────────────────────
            result[i] = new TdBarSignalDto(
                bar.Date, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume,
                buySetup, sellSetup,
                buyPerfected, sellPerfected,
                buySetupDone, sellSetupDone,
                buyCDactive ? buyCD   : 0,
                sellCDactive ? sellCD : 0,
                buyCD13, sellCD13,
                cBuyAct  ? cBuyCD  : 0,
                cSellAct ? cSellCD : 0,
                comboBuy13, comboSell13,
                tdstRes.HasValue ? (decimal)tdstRes.Value  : null,
                tdstSup.HasValue ? (decimal)tdstSup.Value  : null,
                tdstResBreak, tdstSupBreak
            );

            prevClose = close;
        }

        return result;
    }

    // ------------------------------------------------------------------ //
    //  Fixed-range Volume Profile
    // ------------------------------------------------------------------ //

    private static VolumeProfileDto ComputeVolumeProfile(
        OhlcBarDto[] bars,
        int rows,
        double valueAreaPct)
    {
        double hhVP = bars.Max(b => (double)b.High);
        double llVP = bars.Min(b => (double)b.Low);

        if (hhVP <= llVP || rows < 2)
            rows = Math.Max(2, rows);

        double binSz  = (hhVP - llVP) / rows;
        if (binSz <= 0) binSz = 0.01;

        var vols = new double[rows];

        foreach (var b in bars)
        {
            double bHi = (double)b.High;
            double bLo = (double)b.Low;
            double bV  = b.Volume;

            int loB = Math.Max(0,        (int)Math.Floor((bLo - llVP) / binSz));
            int hiB = Math.Min(rows - 1, (int)Math.Floor((bHi - llVP) / binSz));
            int cnt = hiB - loB + 1;
            double per = bV / cnt;
            for (int j = loB; j <= hiB; j++)
                vols[j] += per;
        }

        // POC = bin with maximum volume
        int pocB = 0;
        for (int k = 1; k < rows; k++)
            if (vols[k] > vols[pocB]) pocB = k;

        // Expand value area from POC until vaPct% of total volume is covered
        double totV   = vols.Sum();
        double target = totV * valueAreaPct / 100.0;
        double acc    = vols[pocB];
        int vaUp = pocB, vaDn = pocB;

        while (acc < target && (vaUp < rows - 1 || vaDn > 0))
        {
            double uV = vaUp < rows - 1 ? vols[vaUp + 1] : -1.0;
            double dV = vaDn > 0        ? vols[vaDn - 1] : -1.0;
            if (uV >= dV) { vaUp++; acc += uV; }
            else           { vaDn--; acc += dV; }
        }

        double pocY = llVP + pocB * binSz + binSz / 2.0;
        double vahY = llVP + (vaUp + 1) * binSz;
        double valY = llVP + vaDn * binSz;

        var bins = new VolumeProfileBinDto[rows];
        for (int k = 0; k < rows; k++)
        {
            bins[k] = new VolumeProfileBinDto(
                PriceLow:  (decimal)(llVP + k * binSz),
                PriceHigh: (decimal)(llVP + (k + 1) * binSz),
                Volume:    vols[k],
                IsPoc:     k == pocB,
                IsValueArea: k >= vaDn && k <= vaUp
            );
        }

        return new VolumeProfileDto(
            Poc: (decimal)pocY,
            Vah: (decimal)vahY,
            Val: (decimal)valY,
            Bins: bins
        );
    }
}
