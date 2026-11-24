using System.Reflection;

namespace OfacMatch.Web;

/// <summary>
/// The 25-name vendor comparison test (samples/vendor-comparison.csv, docs/vendor-comparison.md):
/// the file itself, its answer key and the score of a screening run.
/// </summary>
public static class ComparisonTest
{
    public const string FileName = "ofacmatch-comparison-test.csv";

    public const string GuideUrl = "https://github.com/krunalsolanki/ofac-match/blob/main/docs/vendor-comparison.md";

    /// <summary>The test file, built into the page from samples/vendor-comparison.csv so the two never differ.</summary>
    public static string Csv { get; } = ReadCsv();

    public enum Group { Name, Identifier, Ordinary }

    /// <summary>Case ID → group and the SDN entry a good tool should alert on (null for ordinary names).</summary>
    public static readonly IReadOnlyDictionary<string, (Group Group, int? EntryId)> Key = new Dictionary<string, (Group, int?)>
    {
        ["T01"] = (Group.Name, 2676), ["T04"] = (Group.Name, 8279), ["T06"] = (Group.Name, 20157),
        ["T09"] = (Group.Name, 23343), ["T11"] = (Group.Name, 59230), ["T14"] = (Group.Name, 6861),
        ["T16"] = (Group.Name, 59112), ["T19"] = (Group.Name, 22790), ["T21"] = (Group.Name, 24283),
        ["T24"] = (Group.Name, 2686),
        ["T03"] = (Group.Identifier, 59184), ["T07"] = (Group.Identifier, 12453), ["T12"] = (Group.Identifier, 26700),
        ["T17"] = (Group.Identifier, 27307), ["T22"] = (Group.Identifier, 32313),
        ["T02"] = (Group.Ordinary, null), ["T05"] = (Group.Ordinary, null), ["T08"] = (Group.Ordinary, null),
        ["T10"] = (Group.Ordinary, null), ["T13"] = (Group.Ordinary, null), ["T15"] = (Group.Ordinary, null),
        ["T18"] = (Group.Ordinary, null), ["T20"] = (Group.Ordinary, null), ["T23"] = (Group.Ordinary, null),
        ["T25"] = (Group.Ordinary, null),
    };

    /// <summary>One scored case: for listed parties, whether the right entry alerted; for ordinary names, whether anything did.</summary>
    public sealed record CaseResult(string CaseId, Group Group, string Name, bool Correct, string Detail);

    public sealed record Score(int NamesFound, int NamesTotal, int IdentifiersFound, int IdentifiersTotal,
        int FalseAlerts, int OrdinaryTotal, IReadOnlyList<CaseResult> Cases);

    /// <summary>Scores a run of the test file. Rows are matched to case IDs by their position in the file.</summary>
    public static Score Grade(IReadOnlyList<BatchResult> results)
    {
        var ids = CaseIds();
        var cases = new List<CaseResult>();
        foreach (var result in results)
        {
            var index = result.Input.RowNumber - 2; // row 1 is the header
            if (index < 0 || index >= ids.Count || !Key.TryGetValue(ids[index], out var key))
            {
                continue;
            }

            if (key.EntryId is int expected)
            {
                var hit = result.Matches.FirstOrDefault(m => m.Entry.Id == expected);
                cases.Add(new CaseResult(ids[index], key.Group, result.Input.Name, hit is not null,
                    hit is null ? $"Missed entry {expected}" : $"{hit.Entry.Name} ({expected}), {BatchFile.MatchedBy(hit).ToLowerInvariant()}"));
            }
            else
            {
                var best = result.Matches.FirstOrDefault();
                cases.Add(new CaseResult(ids[index], key.Group, result.Input.Name, best is null,
                    best is null ? "No alert"
                    : $"False alert: {best.Entry.Name}, score {best.Score:0}" + (result.Status == BatchStatus.MatchWithConflicts ? " (shown as likely a different person)" : "")));
            }
        }

        int Count(Group g, bool correct) => cases.Count(c => c.Group == g && c.Correct == correct);
        int Total(Group g) => Key.Values.Count(k => k.Group == g);
        return new Score(Count(Group.Name, true), Total(Group.Name), Count(Group.Identifier, true), Total(Group.Identifier),
            Count(Group.Ordinary, false), Total(Group.Ordinary), cases.OrderBy(c => c.Group).ThenBy(c => c.CaseId).ToList());
    }

    private static List<string> CaseIds() =>
        Csv.Split('\n').Skip(1).Select(line => line.Split(',')[0].Trim()).Where(id => id.Length > 0).ToList();

    private static string ReadCsv()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("vendor-comparison.csv")
            ?? throw new InvalidOperationException("The comparison test file is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
