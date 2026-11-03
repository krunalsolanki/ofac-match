using OfacMatch.Data;
using OfacMatch.Matching;

namespace OfacMatch.Evaluation;

internal enum Variation
{
    Exact,
    ListOrder,
    LowercaseNoPunctuation,
    Typo,
    SwappedLetters,
    MissingLetter,
    Transliteration,
    JoinedParticle,
    DroppedMiddleName,
    CompanySuffixChanged,
}

internal sealed class TestCase
{
    public TestCase(int entryId, EntryType type, string original, string query, Variation variation)
    {
        EntryId = entryId;
        Type = type;
        Original = original;
        Query = query;
        Variation = variation;
    }

    public int EntryId { get; }

    public EntryType Type { get; }

    public string Original { get; }

    public string Query { get; }

    public Variation Variation { get; }
}

/// <summary>Generates realistic name variations, the way names drift between documents, systems and alphabets.</summary>
internal static class Variations
{
    private static readonly string[] Particles = { "AL", "EL", "BIN", "BEN", "ABU", "ABD", "ABDUL", "VAN", "DE", "DA", "DOS", "DEL" };

    // Common transliteration differences: Mohammed/Muhammad, Yusuf/Yousef, Khalid/Halid, Qasim/Kasim.
    private static readonly (string From, string To)[] Transliterations =
    {
        ("OU", "U"), ("U", "OU"), ("U", "O"), ("O", "U"), ("EE", "I"), ("I", "EE"), ("A", "E"), ("E", "A"),
        ("KH", "H"), ("Q", "K"), ("Y", "I"), ("I", "Y"), ("MM", "M"), ("M", "MM"), ("DH", "D"), ("TH", "T"),
    };

    private static readonly HashSet<string> Suffixes = new(StringComparer.Ordinal)
    {
        "LTD", "LIMITED", "LLC", "INC", "CO", "COMPANY", "CORP", "CORPORATION", "SA", "PLC", "GMBH", "JSC",
    };

    private static readonly string[] FirstNames =
    {
        "James", "Mary", "Robert", "Patricia", "Michael", "Jennifer", "David", "Linda", "William", "Elizabeth",
        "Jose", "Maria", "Luis", "Carmen", "Juan", "Ana", "Carlos", "Sofia", "Wei", "Li", "Hiroshi", "Yuki",
        "Minjun", "Jiwoo", "Raj", "Priya", "Arjun", "Ananya", "Olga", "Ivan", "Dmitri", "Fatima", "Omar", "Aisha",
        "Hassan", "Layla", "Kwame", "Amara", "Chinedu", "Ngozi", "Pierre", "Camille", "Hans", "Greta", "Giulia",
        "Marco", "Lars", "Ingrid", "Thanh", "Mai",
    };

    private static readonly string[] Surnames =
    {
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez",
        "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson", "Thomas", "Taylor", "Moore", "Jackson", "Martin",
        "Lee", "Kim", "Park", "Chen", "Wang", "Zhang", "Liu", "Tanaka", "Sato", "Patel", "Shah", "Singh", "Kumar",
        "Gupta", "Ivanova", "Petrov", "Kowalski", "Nowak", "Mueller", "Schmidt", "Rossi", "Bianchi", "Dubois",
        "Moreau", "Okafor", "Mensah", "Haddad", "Nasser", "Khan", "Ahmed", "Rahman", "Nguyen", "Tran", "Silva",
        "Santos", "O'Brien", "Murphy",
    };

    public static string Describe(Variation variation) => variation switch
    {
        Variation.Exact => "Exact name",
        Variation.ListOrder => "Surname first (\"DOE John\")",
        Variation.LowercaseNoPunctuation => "Lower case, no punctuation",
        Variation.Typo => "One wrong letter",
        Variation.SwappedLetters => "Two letters swapped",
        Variation.MissingLetter => "One letter missing",
        Variation.Transliteration => "Different transliteration (Mohammed/Muhammad)",
        Variation.JoinedParticle => "Particle joined (\"AL ZAWAHIRI\" → \"ALZAWAHIRI\")",
        Variation.DroppedMiddleName => "Middle name left out",
        Variation.CompanySuffixChanged => "Company suffix changed (LTD → LLC)",
        _ => variation.ToString(),
    };

    public static IEnumerable<TestCase> ForIndividuals(IEnumerable<SanctionsEntry> entries, Random random)
    {
        foreach (var entry in entries)
        {
            var natural = NameNormalizer.ToNaturalOrder(entry.Name);
            var tokens = NameNormalizer.Tokenize(natural).ToList();
            var make = Maker(entry, random);

            yield return make(Variation.Exact, natural);
            yield return make(Variation.ListOrder, entry.Name.Replace(",", string.Empty));
            yield return make(Variation.LowercaseNoPunctuation, string.Join(" ", tokens).ToLowerInvariant());

            foreach (var c in Spelling(tokens, random, make))
            {
                yield return c;
            }

            if (Transliterate(tokens, random) is string transliterated)
            {
                yield return make(Variation.Transliteration, transliterated);
            }

            var particle = tokens.FindIndex(t => Particles.Contains(t));
            if (particle >= 0 && particle < tokens.Count - 1)
            {
                var joined = tokens.ToList();
                joined[particle] += joined[particle + 1];
                joined.RemoveAt(particle + 1);
                yield return make(Variation.JoinedParticle, string.Join(" ", joined));
            }

            if (tokens.Count >= 3)
            {
                var dropped = tokens.ToList();
                dropped.RemoveAt(1 + random.Next(tokens.Count - 2));
                yield return make(Variation.DroppedMiddleName, string.Join(" ", dropped));
            }
        }
    }

    public static IEnumerable<TestCase> ForEntities(IEnumerable<SanctionsEntry> entries, Random random)
    {
        foreach (var entry in entries)
        {
            var tokens = NameNormalizer.Tokenize(entry.Name).ToList();
            var make = Maker(entry, random);

            yield return make(Variation.Exact, entry.Name);
            yield return make(Variation.LowercaseNoPunctuation, string.Join(" ", tokens).ToLowerInvariant());

            foreach (var c in Spelling(tokens, random, make))
            {
                yield return c;
            }

            var changed = tokens.ToList();
            if (Suffixes.Contains(changed[changed.Count - 1]))
            {
                changed[changed.Count - 1] = changed[changed.Count - 1] == "LLC" ? "LIMITED" : "LLC";
            }
            else
            {
                changed.Add("LLC");
            }

            yield return make(Variation.CompanySuffixChanged, string.Join(" ", changed));
        }
    }

    public static List<string> CommonNames(int count, Random random)
    {
        var names = new HashSet<string>();
        var limit = Math.Min(count, FirstNames.Length * Surnames.Length);
        while (names.Count < limit)
        {
            names.Add($"{FirstNames[random.Next(FirstNames.Length)]} {Surnames[random.Next(Surnames.Length)]}");
        }

        return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private static Func<Variation, string, TestCase> Maker(SanctionsEntry entry, Random random) =>
        (variation, query) => new TestCase(entry.Id, entry.Type, entry.Name, query, variation);

    private static IEnumerable<TestCase> Spelling(List<string> tokens, Random random, Func<Variation, string, TestCase> make)
    {
        var candidates = Enumerable.Range(0, tokens.Count).Where(i => tokens[i].Length >= 5).ToList();
        if (candidates.Count == 0)
        {
            yield break;
        }

        var index = candidates[random.Next(candidates.Count)];
        var word = tokens[index];

        var position = 1 + random.Next(word.Length - 1);
        var replacement = (char)('A' + random.Next(26));
        if (replacement == word[position])
        {
            replacement = replacement == 'Z' ? 'A' : (char)(replacement + 1);
        }

        yield return make(Variation.Typo, Replace(tokens, index, word.Substring(0, position) + replacement + word.Substring(position + 1)));

        var swap = 1 + random.Next(word.Length - 2);
        var swapped = word.ToCharArray();
        (swapped[swap], swapped[swap + 1]) = (swapped[swap + 1], swapped[swap]);
        if (new string(swapped) != word)
        {
            yield return make(Variation.SwappedLetters, Replace(tokens, index, new string(swapped)));
        }

        var missing = 1 + random.Next(word.Length - 1);
        yield return make(Variation.MissingLetter, Replace(tokens, index, word.Remove(missing, 1)));
    }

    private static string? Transliterate(List<string> tokens, Random random)
    {
        var options = new List<(int Token, int Position, string From, string To)>();
        for (var t = 0; t < tokens.Count; t++)
        {
            if (tokens[t].Length < 4)
            {
                continue;
            }

            foreach (var (from, to) in Transliterations)
            {
                var at = tokens[t].IndexOf(from, 1, StringComparison.Ordinal);
                if (at > 0)
                {
                    options.Add((t, at, from, to));
                }
            }
        }

        if (options.Count == 0)
        {
            return null;
        }

        var pick = options[random.Next(options.Count)];
        var word = tokens[pick.Token];
        return Replace(tokens, pick.Token, word.Substring(0, pick.Position) + pick.To + word.Substring(pick.Position + pick.From.Length));
    }

    private static string Replace(List<string> tokens, int index, string word)
    {
        var copy = tokens.ToList();
        copy[index] = word;
        return string.Join(" ", copy);
    }
}
