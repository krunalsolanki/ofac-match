using System.Text;

namespace OfacMatch.Data;

/// <summary>
/// Minimal RFC 4180 reader for OFAC's legacy flat files (SDN.CSV, ALT.CSV, ADD.CSV).
/// Handles quoted fields, escaped quotes, line breaks inside quotes, the trailing
/// end-of-file marker (0x1A) and OFAC's "-0-" null marker.
/// </summary>
internal static class CsvReader
{
    private const char EndOfFile = '\u001a';

    /// <summary>Reads every record. Null markers ("-0-") are returned as <c>null</c>.</summary>
    public static IEnumerable<string?[]> Read(TextReader reader)
    {
        var fields = new List<string?>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldWasQuoted = false;
        int c;

        while ((c = reader.Read()) != -1)
        {
            var ch = (char)c;

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        field.Append('"');
                        reader.Read();
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    fieldWasQuoted = true;
                    break;
                case ',':
                    fields.Add(Finish(field, fieldWasQuoted));
                    fieldWasQuoted = false;
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(Finish(field, fieldWasQuoted));
                    fieldWasQuoted = false;
                    if (!IsBlank(fields))
                    {
                        yield return fields.ToArray();
                    }

                    fields.Clear();
                    break;
                case EndOfFile:
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(Finish(field, fieldWasQuoted));
            if (!IsBlank(fields))
            {
                yield return fields.ToArray();
            }
        }
    }

    private static string? Finish(StringBuilder field, bool quoted)
    {
        var value = quoted ? field.ToString() : field.ToString().Trim();
        field.Clear();
        return value.Trim() == "-0-" || value.Length == 0 ? null : value;
    }

    private static bool IsBlank(List<string?> fields) => fields.All(f => f is null);
}
