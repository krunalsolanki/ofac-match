using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using OfacMatch.Data;
using OfacMatch.Screening;

namespace OfacMatch.Web;

/// <summary>One row of an uploaded list, as read from the file.</summary>
public sealed class BatchInputRow
{
    /// <summary>The row number as a spreadsheet shows it (the header, if any, is row 1).</summary>
    public int RowNumber { get; init; }

    public string Name { get; init; } = "";

    public string? YearOfBirthText { get; init; }

    public string? Country { get; init; }

    public string? TypeText { get; init; }
}

/// <summary>What was read from an uploaded file and which columns were used.</summary>
public sealed class BatchInput
{
    public List<BatchInputRow> Rows { get; } = new();

    public bool HasHeader { get; set; }

    /// <summary>Plain-language description of the columns used, e.g. "Name: “Customer name”".</summary>
    public List<string> ColumnsUsed { get; } = new();

    /// <summary>Rows beyond <see cref="BatchFile.MaxRows"/> that were not read.</summary>
    public int RowsSkipped { get; set; }
}

/// <summary>The screening result for one row.</summary>
public sealed class BatchResult
{
    public required BatchInputRow Input { get; init; }

    public int? YearOfBirth { get; init; }

    public EntryType? Type { get; init; }

    public IReadOnlyList<ScreeningMatch> Matches { get; init; } = Array.Empty<ScreeningMatch>();

    /// <summary>Why the row could not be screened, or null.</summary>
    public string? Error { get; init; }

    /// <summary>Anything a reviewer should know, e.g. a year of birth that was ignored.</summary>
    public string? Note { get; init; }

    public BatchStatus Status =>
        Error is not null ? BatchStatus.Error
        : Matches.Count == 0 ? BatchStatus.NoMatch
        : Matches.All(m => m.DateOfBirthConflict || m.CountryConflict) ? BatchStatus.MatchWithConflicts
        : BatchStatus.Match;
}

public enum BatchStatus
{
    Match,
    MatchWithConflicts,
    NoMatch,
    Error,
}

/// <summary>Reads uploaded lists and writes the audit file. Everything runs in the browser.</summary>
public static class BatchFile
{
    public const int MaxRows = 10_000;

    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly Regex Year = new(@"\b(1[89]\d\d|20\d\d)\b", RegexOptions.CultureInvariant);

    public const string Template =
        "Name,Year of birth,Country,Type\r\n" +
        "John Michael Doe,1970,United States,Individual\r\n" +
        "Jane Example,,,Individual\r\n" +
        "Example Trading Co. Ltd,,,Entity\r\n";

    /// <summary>Reads a CSV (comma, semicolon or tab separated) with or without a header row.</summary>
    public static BatchInput Read(string text)
    {
        var input = new BatchInput();
        var rows = ParseCsv(text, DetectDelimiter(text)).Where(r => r.Any(c => c.Trim().Length > 0)).ToList();
        if (rows.Count == 0)
        {
            return input;
        }

        var header = rows[0].Select(c => c.Trim()).ToList();
        int nameCol = -1, firstCol = -1, middleCol = -1, lastCol = -1, yearCol = -1, countryCol = -1, typeCol = -1;
        for (var i = 0; i < header.Count; i++)
        {
            var h = Normalize(header[i]);
            if (h is "firstname" or "givenname" or "forename") { firstCol = First(firstCol, i); continue; }
            if (h is "middlename" or "middlenames") { middleCol = First(middleCol, i); continue; }
            if (h is "lastname" or "surname" or "familyname") { lastCol = First(lastCol, i); continue; }
            if (h is "name" or "fullname" or "customername" or "clientname" or "partyname" or "borrowername" or "companyname" or "entityname")
            {
                nameCol = nameCol < 0 || Normalize(header[nameCol]) != "name" ? i : nameCol;
                continue;
            }

            if (h.Contains("birth") || h is "yob" or "dob" or "year") { yearCol = First(yearCol, i); continue; }
            if (h.Contains("country") || h.Contains("nationality") || h.Contains("citizenship")) { countryCol = First(countryCol, i); continue; }
            if (h is "type" or "partytype" or "entitytype" or "kind" or "customertype") { typeCol = First(typeCol, i); continue; }
            if (nameCol < 0 && h.EndsWith("name", StringComparison.Ordinal)) { nameCol = i; }
        }

        var splitName = nameCol < 0 && firstCol >= 0 && lastCol >= 0;
        input.HasHeader = nameCol >= 0 || splitName;
        if (!input.HasHeader)
        {
            // No recognizable header: every row is data, and the first column is the name.
            nameCol = 0;
            input.ColumnsUsed.Add("Name: first column (no header row found)");
        }
        else
        {
            input.ColumnsUsed.Add(splitName
                ? $"Name: “{header[firstCol]}”{(middleCol >= 0 ? $" + “{header[middleCol]}”" : "")} + “{header[lastCol]}”"
                : $"Name: “{header[nameCol]}”");
            input.ColumnsUsed.Add(yearCol >= 0 ? $"Year of birth: “{header[yearCol]}”" : "Year of birth: not found");
            input.ColumnsUsed.Add(countryCol >= 0 ? $"Country: “{header[countryCol]}”" : "Country: not found");
            input.ColumnsUsed.Add(typeCol >= 0 ? $"Type: “{header[typeCol]}”" : "Type: not found");
        }

        var first = input.HasHeader ? 1 : 0;
        for (var r = first; r < rows.Count; r++)
        {
            if (input.Rows.Count == MaxRows)
            {
                input.RowsSkipped = rows.Count - r;
                break;
            }

            var row = rows[r];
            string name = splitName
                ? string.Join(" ", new[] { Cell(row, firstCol), Cell(row, middleCol), Cell(row, lastCol) }.Where(s => s.Length > 0))
                : Cell(row, nameCol);

            input.Rows.Add(new BatchInputRow
            {
                RowNumber = r + 1,
                Name = name,
                YearOfBirthText = NullIfEmpty(Cell(row, yearCol)),
                Country = NullIfEmpty(Cell(row, countryCol)),
                TypeText = NullIfEmpty(Cell(row, typeCol)),
            });
        }

        return input;
    }

    /// <summary>Screens one row. Invalid rows get an error instead of an exception.</summary>
    public static BatchResult Screen(SanctionsScreener screener, BatchInputRow row, double minScore, EntryType? defaultType)
    {
        var notes = new List<string>();

        int? year = null;
        if (row.YearOfBirthText is { } yearText)
        {
            var m = Year.Match(yearText);
            if (m.Success && int.Parse(m.Value, CultureInfo.InvariantCulture) is var y
                && y >= ScreeningRequest.MinYearOfBirth && y <= DateTime.UtcNow.Year)
            {
                year = y;
            }
            else
            {
                notes.Add($"Year of birth “{yearText}” not understood; screened without it.");
            }
        }

        var type = defaultType;
        if (row.TypeText is { } typeText)
        {
            if (TryParseType(typeText, out var parsed))
            {
                type = parsed;
            }
            else
            {
                notes.Add($"Type “{typeText}” not understood; screened against all types.");
                type = null;
            }
        }

        string? country = null;
        if (row.Country is { } countryText)
        {
            if (countryText.Length <= ScreeningRequest.MaxCountryLength)
            {
                country = countryText;
            }
            else
            {
                notes.Add("Country is too long; screened without it.");
            }
        }

        ScreeningRequest request;
        try
        {
            request = new ScreeningRequest(row.Name.Trim())
            {
                YearOfBirth = year,
                Type = type,
                MinScore = minScore,
                MaxResults = 10,
                Country = country,
            };
        }
        catch (ArgumentException ex)
        {
            return new BatchResult
            {
                Input = row,
                Error = row.Name.Trim().Length == 0 ? "No name in this row." : ex.Message.Split('\n')[0].Replace($" (Parameter '{ex.ParamName}')", ""),
            };
        }

        return new BatchResult
        {
            Input = row,
            YearOfBirth = year,
            Type = type,
            Matches = screener.Screen(request),
            Note = notes.Count == 0 ? null : string.Join(" ", notes),
        };
    }

    /// <summary>
    /// Writes one line per match (and one per row without matches) for review and record keeping.
    /// Reviewer columns are left empty for the analyst to fill in.
    /// </summary>
    public static string WriteAuditFile(IReadOnlyList<BatchResult> results, double minScore, SanctionsData data, DateTimeOffset screenedUtc)
    {
        var version = typeof(SanctionsScreener).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "";
        var list = data.DownloadedUtc is { } at ? at.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : "unknown";

        var sb = new StringBuilder();
        Line(sb,
            "row", "input_name", "input_year_of_birth", "input_country", "input_type", "result",
            "match_number", "score", "listed_name", "matched_name", "matched_name_kind", "entry_id", "entry_type", "programs",
            "date_of_birth_conflict", "country_conflict", "explanation", "note",
            "reviewer_decision", "reviewer_name", "reviewer_notes",
            "minimum_score", "sdn_list_downloaded", "sdn_list_entries", "screened_at", "ofacmatch_version");

        var tail = new[]
        {
            minScore.ToString("0.#", CultureInfo.InvariantCulture),
            list,
            data.Entries.ToString(CultureInfo.InvariantCulture),
            screenedUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
            version,
        };

        foreach (var r in results)
        {
            var head = new[]
            {
                r.Input.RowNumber.ToString(CultureInfo.InvariantCulture),
                r.Input.Name,
                r.Input.YearOfBirthText ?? "",
                r.Input.Country ?? "",
                r.Input.TypeText ?? "",
                Describe(r.Status),
            };

            if (r.Matches.Count == 0)
            {
                Line(sb, head.Concat(new[] { "", "", "", "", "", "", "", "", "", "", "", r.Error ?? r.Note ?? "", "", "", "" }).Concat(tail).ToArray());
                continue;
            }

            for (var i = 0; i < r.Matches.Count; i++)
            {
                var m = r.Matches[i];
                Line(sb, head.Concat(new[]
                {
                    (i + 1).ToString(CultureInfo.InvariantCulture),
                    m.Score.ToString("0.0", CultureInfo.InvariantCulture),
                    m.Entry.Name,
                    m.MatchedName,
                    m.NameKind.ToString(),
                    m.Entry.Id.ToString(CultureInfo.InvariantCulture),
                    m.Entry.Type.ToString(),
                    string.Join("; ", m.Entry.Programs),
                    m.DateOfBirthConflict ? "yes" : "no",
                    m.CountryConflict ? "yes" : "no",
                    m.Explain().TrimEnd(),
                    r.Note ?? "",
                    "", "", "",
                }).Concat(tail).ToArray());
            }
        }

        return sb.ToString();
    }

    public static string Describe(BatchStatus status) => status switch
    {
        BatchStatus.Match => "Potential match - review",
        BatchStatus.MatchWithConflicts => "Potential match with conflicts - review",
        BatchStatus.NoMatch => "No match at or above the minimum score",
        _ => "Not screened",
    };

    /// <summary>Reads a type such as "Individual" or "Company". "All" or "Any" means no filter.</summary>
    private static bool TryParseType(string text, out EntryType? type)
    {
        type = Normalize(text) switch
        {
            "individual" or "person" or "people" or "natural" or "naturalperson" => EntryType.Individual,
            "entity" or "company" or "business" or "organization" or "organisation" or "corporation" => EntryType.Entity,
            "vessel" or "ship" => EntryType.Vessel,
            "aircraft" => EntryType.Aircraft,
            _ => null,
        };

        return type is not null || Normalize(text) is "" or "all" or "any";
    }

    private static void Line(StringBuilder sb, params string[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(Escape(fields[i]));
        }

        sb.Append("\r\n");
    }

    private static string Escape(string value)
    {
        // Spreadsheets run a cell that starts with = + - @ as a formula. Names from an uploaded
        // file are untrusted, so such values are prefixed with an apostrophe to keep them as text.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        var commas = firstLine.Count(c => c == ',');
        var semicolons = firstLine.Count(c => c == ';');
        var tabs = firstLine.Count(c => c == '\t');
        return tabs > commas && tabs >= semicolons ? '\t' : semicolons > commas ? ';' : ',';
    }

    private static List<List<string>> ParseCsv(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else if (c != '﻿')
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static string Normalize(string header) =>
        new string(header.ToLowerInvariant().Where(char.IsLetter).ToArray());

    private static int First(int current, int candidate) => current >= 0 ? current : candidate;

    private static string Cell(List<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index].Trim() : "";

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
