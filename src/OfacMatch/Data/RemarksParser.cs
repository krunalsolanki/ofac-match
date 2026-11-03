using System.Globalization;
using System.Text.RegularExpressions;

namespace OfacMatch.Data;

/// <summary>Extracts structured facts from the free-text remarks column of SDN.CSV.</summary>
internal static class RemarksParser
{
    // Matches "DOB 10 Dec 1948", "DOB Dec 1948", "DOB 1948", "alt. DOB circa 1950".
    private static readonly Regex DobPattern = new(
        @"DOB\s+(?:circa\s+)?(?:(?<day>\d{1,2})\s+)?(?:(?<month>[A-Za-z]{3})[a-z]*\s+)?(?<year>\d{4})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<PartialDate> ParseDatesOfBirth(string? remarks)
    {
        if (string.IsNullOrEmpty(remarks))
        {
            return Array.Empty<PartialDate>();
        }

        var dates = new List<PartialDate>();
        foreach (Match m in DobPattern.Matches(remarks))
        {
            var year = int.Parse(m.Groups["year"].Value, CultureInfo.InvariantCulture);
            if (year < 1)
            {
                continue;
            }

            int? month = m.Groups["month"].Success ? MonthNumber(m.Groups["month"].Value) : null;
            int? day = month is not null && m.Groups["day"].Success
                ? int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture)
                : null;

            // Keep the year and month of a mistyped day such as "DOB 31 Feb 1970" rather than failing the whole list.
            if (day is int d && (d < 1 || d > DateTime.DaysInMonth(year, month!.Value)))
            {
                day = null;
            }

            dates.Add(new PartialDate(year, month, day));
        }

        return dates;
    }

    private static int? MonthNumber(string abbreviation)
    {
        var months = new[] { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
        var index = Array.IndexOf(months, abbreviation.ToUpperInvariant());
        return index >= 0 ? index + 1 : null;
    }
}
