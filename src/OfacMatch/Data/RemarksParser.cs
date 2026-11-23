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

    // One remark part, e.g. "Email Address info@example.test", "alt. Website www.example.test",
    // "Telephone No. (022) 7363030", "Fax: 97082881184" or "Digital Currency Address - XBT 1A2b3C".
    // Anchored to the start of the part so a title such as "Director of the National Telephone Company"
    // is not read as a phone number.
    private static readonly Regex IdentifierPattern = new(
        @"^(?:alt\.\s+)?(?<label>Email\s+Address|Website|Phone\s+Number|Telephone|Fax|Digital\s+Currency\s+Address\s*-\s*(?<currency>[A-Za-z0-9]+))(?:\s+No\.)?\s*:?\s+(?<value>\S.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IReadOnlyList<SanctionsIdentifier> ParseIdentifiers(string? remarks)
    {
        if (string.IsNullOrEmpty(remarks))
        {
            return Array.Empty<SanctionsIdentifier>();
        }

        var identifiers = new List<SanctionsIdentifier>();
        foreach (var part in remarks!.Split(';'))
        {
            var m = IdentifierPattern.Match(part.Trim());
            if (!m.Success)
            {
                continue;
            }

            var label = Regex.Replace(m.Groups["label"].Value, @"\s+", " ");
            var type = TypeOf(label);
            var value = m.Groups["value"].Value.Trim();

            // Phone numbers can contain spaces, so keep the whole value. Other identifiers are one token;
            // anything after it is commentary ("www.example.test. Offices worldwide.").
            if (type != IdentifierType.Phone)
            {
                value = value.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
            }

            value = value.TrimEnd('.');
            if (value.Length == 0)
            {
                continue;
            }

            var currency = m.Groups["currency"].Success ? m.Groups["currency"].Value.ToUpperInvariant() : null;
            identifiers.Add(new SanctionsIdentifier(type, value, CanonicalLabel(label, currency), currency));
        }

        return identifiers;
    }

    private static string CanonicalLabel(string label, string? currency) => label.ToUpperInvariant() switch
    {
        "EMAIL ADDRESS" => "Email Address",
        "WEBSITE" => "Website",
        "PHONE NUMBER" => "Phone Number",
        "TELEPHONE" => "Telephone",
        "FAX" => "Fax",
        _ => "Digital Currency Address - " + currency,
    };

    private static IdentifierType TypeOf(string label) => label.ToUpperInvariant() switch
    {
        "EMAIL ADDRESS" => IdentifierType.Email,
        "WEBSITE" => IdentifierType.Website,
        "PHONE NUMBER" or "TELEPHONE" or "FAX" => IdentifierType.Phone,
        _ => IdentifierType.CryptoAddress,
    };

    private static int? MonthNumber(string abbreviation)
    {
        var months = new[] { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
        var index = Array.IndexOf(months, abbreviation.ToUpperInvariant());
        return index >= 0 ? index + 1 : null;
    }
}
