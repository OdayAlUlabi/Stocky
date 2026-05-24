using System.Globalization;

namespace Stocky.Web.Mvc.Internal;

/// <summary>
/// Lightweight formatters used by Razor views. Keeps the syntax in
/// <c>.cshtml</c> files terse: <c>@row.Value.Money("USD")</c>.
/// </summary>
public static class FormatHelpers
{
    public static string Money(this decimal v, string ccy = "USD")
    {
        try
        {
            var nf = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            nf.CurrencySymbol = ccy switch
            {
                "USD" => "$",
                "EUR" => "€",
                "GBP" => "£",
                "JPY" => "¥",
                _ => ccy + " "
            };
            return v.ToString("C", nf);
        }
        catch { return $"{ccy} {v:0.00}"; }
    }

    public static string Money(this decimal? v, string ccy = "USD") =>
        v.HasValue ? v.Value.Money(ccy) : "—";

    public static string Pct(this decimal v) =>
        $"{(v >= 0 ? "+" : "")}{v.ToString("0.00", CultureInfo.InvariantCulture)}%";

    public static string Pct(this decimal? v) =>
        v.HasValue ? v.Value.Pct() : "—";

    public static string Num(this decimal v, int decimals = 4) =>
        v.ToString($"N{decimals}", CultureInfo.InvariantCulture);

    public static string SignClass(this decimal v) =>
        v > 0 ? "text-green" : v < 0 ? "text-red" : "";
}
