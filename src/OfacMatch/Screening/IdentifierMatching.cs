using System.Text.RegularExpressions;
using OfacMatch.Data;

namespace OfacMatch.Screening;

/// <summary>How a screened identifier matched a listed one.</summary>
public enum IdentifierMatchLevel
{
    /// <summary>The same email address, website page, phone number or crypto address, after normalization.</summary>
    Exact,
    /// <summary>
    /// The email address or website is on the domain of a listed website (or a subdomain of it), for example
    /// sales@example.test against a listed www.example.test.
    /// </summary>
    SameDomain,
    /// <summary>The phone number ends in the same 9 digits as a listed number, for example with and without a country code.</summary>
    SameNumberEnding,
}

/// <summary>Why a <see cref="ScreeningMatch"/> was returned.</summary>
public enum MatchBasis
{
    /// <summary>The name scored at or above the threshold.</summary>
    Name,
    /// <summary>
    /// An email address, website, phone number or crypto address matched one listed for the entry, but the name
    /// scored below the threshold. This is how a party that changed its name is caught.
    /// </summary>
    Identifier,
    /// <summary>Both the name and an identifier matched.</summary>
    NameAndIdentifier,
}

/// <summary>A screened email address, website, phone number or crypto address that matches one on an SDN entry.</summary>
public sealed class IdentifierMatch
{
    internal IdentifierMatch(SanctionsEntry entry, SanctionsIdentifier listed, IdentifierType queryType, string query, IdentifierMatchLevel level)
    {
        Entry = entry;
        Listed = listed;
        QueryType = queryType;
        Query = query;
        Level = level;
    }

    /// <summary>The listed entry.</summary>
    public SanctionsEntry Entry { get; }

    /// <summary>The identifier as OFAC lists it on the entry.</summary>
    public SanctionsIdentifier Listed { get; }

    /// <summary>What was screened: an email can match a listed website's domain, so this can differ from <see cref="Listed"/>'s type.</summary>
    public IdentifierType QueryType { get; }

    /// <summary>The value that was screened, as given.</summary>
    public string Query { get; }

    /// <summary>Exact, same domain or same number ending.</summary>
    public IdentifierMatchLevel Level { get; }

    /// <summary>One line for an analyst, e.g. "Email sales@example.test has the same domain as listed Website www.example.test".</summary>
    public string Describe()
    {
        var what = QueryType switch
        {
            IdentifierType.Email => "Email",
            IdentifierType.Website => "Website",
            IdentifierType.Phone => "Phone",
            _ => "Crypto address",
        };

        var how = Level switch
        {
            IdentifierMatchLevel.Exact => "matches",
            IdentifierMatchLevel.SameDomain => "has the same domain as",
            _ => "has the same last 9 digits as",
        };

        return $"{what} {Query} {how} listed {Listed.Label} {Listed.Value}";
    }

    /// <inheritdoc />
    public override string ToString() => Describe();
}

/// <summary>Turns identifiers into comparable keys. Returns null for a value that isn't a valid identifier of the type.</summary>
internal static class IdentifierNormalizer
{
    /// <summary>Phone numbers are compared on their last digits when they differ only in a country code or trunk prefix.</summary>
    public const int PhoneEndingDigits = 9;

    /// <summary>Shorter numbers are too ambiguous to screen.</summary>
    public const int MinPhoneDigits = 7;

    private static readonly Regex Scheme = new(@"^[a-z][a-z0-9+.\-]*:/+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Sites where the path, not the domain, identifies the owner (facebook.com/name, t.me/name).
    private static readonly string[] PathPlatforms =
    {
        "facebook.com", "fb.com", "instagram.com", "twitter.com", "x.com", "linkedin.com", "youtube.com",
        "t.me", "telegram.me", "wa.me", "vk.com", "ok.ru", "tiktok.com", "github.com", "medium.com",
        "google.com", "linktr.ee",
    };

    // Sites where each customer gets a subdomain (name.wixsite.com): the subdomain is theirs, the parent isn't.
    private static readonly string[] SubdomainPlatforms =
    {
        "wixsite.com", "wix.com", "blogspot.com", "wordpress.com", "weebly.com", "github.io", "tumblr.com",
        "square.site", "business.site",
    };

    public static string? Email(string value)
    {
        var s = value.Trim();
        if (s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring("mailto:".Length);
        }

        s = s.TrimEnd('.').ToLowerInvariant();
        var at = s.IndexOf('@');
        return at > 0
            && at == s.LastIndexOf('@')
            && s.IndexOf('.', at) > at + 1
            && !s.EndsWith(".", StringComparison.Ordinal)
            && !s.Any(char.IsWhiteSpace)
            ? s
            : null;
    }

    public static string EmailDomain(string normalizedEmail) => normalizedEmail.Substring(normalizedEmail.IndexOf('@') + 1);

    public static string? Phone(string value)
    {
        // "(218) (0)214778301": the (0) is a trunk prefix dialed only inside the country.
        var digits = new string(value.Replace("(0)", string.Empty).Where(c => c >= '0' && c <= '9').ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits.Substring(2);
        }

        return digits.Length >= MinPhoneDigits ? digits : null;
    }

    public static string? PhoneEnding(string normalizedPhone) =>
        normalizedPhone.Length >= PhoneEndingDigits ? normalizedPhone.Substring(normalizedPhone.Length - PhoneEndingDigits) : null;

    /// <summary>Returns the host without "www." and the rest of the address (path and query) without a trailing slash.</summary>
    public static (string Host, string Path)? Website(string value)
    {
        var s = Scheme.Replace(value.Trim().ToLowerInvariant(), string.Empty);
        var cut = s.IndexOfAny(new[] { '/', '?', '#' });
        var host = cut >= 0 ? s.Substring(0, cut) : s;
        var path = cut >= 0 ? s.Substring(cut) : string.Empty;

        var port = host.IndexOf(':');
        if (port >= 0)
        {
            host = host.Substring(0, port);
        }

        host = host.Trim('.');
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host.Substring(4);
        }

        var hash = path.IndexOf('#');
        if (hash >= 0)
        {
            path = path.Substring(0, hash);
        }

        path = path.TrimEnd('/', '.');
        return host.Contains('.') && !host.Any(c => char.IsWhiteSpace(c) || c == '@') ? (host, path) : null;
    }

    public static string WebsiteKey((string Host, string Path) site) => site.Host + site.Path;

    /// <summary>
    /// Whether the listed website's domain belongs to the listed party, so that other addresses on it count as a match.
    /// Not for shared platforms, or for a page deep inside a university or government site.
    /// </summary>
    public static bool IsOwnDomain((string Host, string Path) site)
    {
        if (PathPlatforms.Any(p => IsSameOrSubdomain(site.Host, p)) || SubdomainPlatforms.Contains(site.Host))
        {
            return false;
        }

        if (!IsTrivialPath(site.Path))
        {
            var labels = site.Host.Split('.');
            if (labels.Any(l => l is "edu" or "gov" or "mil" or "ac"))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsSameOrSubdomain(string host, string domain) =>
        host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);

    /// <summary>The host and each parent domain with at least two labels: a.b.example.test, b.example.test, example.test.</summary>
    public static IEnumerable<string> HostAndParents(string host)
    {
        var current = host;
        while (true)
        {
            yield return current;
            var dot = current.IndexOf('.');
            if (dot < 0 || current.IndexOf('.', dot + 1) < 0)
            {
                yield break;
            }

            current = current.Substring(dot + 1);
        }
    }

    public static string? Crypto(string value)
    {
        var s = value.Trim();
        if (s.Length < 10 || s.Any(char.IsWhiteSpace))
        {
            return null;
        }

        // Hex (0x...) and bech32 (bc1...) addresses ignore case; others, such as base58 Bitcoin addresses, don't.
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("bc1", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("ltc1", StringComparison.OrdinalIgnoreCase)
            ? s.ToLowerInvariant()
            : s;
    }

    // "", "/en", "/index.html", "/home": the site's front page, not a page about one person.
    private static bool IsTrivialPath(string path)
    {
        var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return true;
        }

        if (segments.Length > 1)
        {
            return false;
        }

        var s = segments[0];
        // Two letters is a language code (/en, /fa); anything longer may name a person or department.
        return (s.Length == 2 && s.All(char.IsLetter)) || s == "home" || s.StartsWith("index.", StringComparison.Ordinal) || s.StartsWith("home.", StringComparison.Ordinal);
    }
}

/// <summary>Lookup tables for every identifier on the list, built once per screener.</summary>
internal sealed class IdentifierIndex
{
    private readonly Dictionary<string, List<(SanctionsEntry, SanctionsIdentifier)>> _emails = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(SanctionsEntry, SanctionsIdentifier)>> _websites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(SanctionsEntry, SanctionsIdentifier)>> _ownDomains = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(SanctionsEntry, SanctionsIdentifier)>> _phones = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(SanctionsEntry, SanctionsIdentifier)>> _phoneEndings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(SanctionsEntry, SanctionsIdentifier)>> _crypto = new(StringComparer.Ordinal);

    public IdentifierIndex(IEnumerable<SanctionsEntry> entries)
    {
        foreach (var entry in entries)
        {
            foreach (var id in entry.Identifiers)
            {
                switch (id.Type)
                {
                    case IdentifierType.Email when IdentifierNormalizer.Email(id.Value) is string email:
                        Add(_emails, email, entry, id);
                        break;
                    case IdentifierType.Website when IdentifierNormalizer.Website(id.Value) is { } site:
                        Add(_websites, IdentifierNormalizer.WebsiteKey(site), entry, id);
                        if (IdentifierNormalizer.IsOwnDomain(site))
                        {
                            Add(_ownDomains, site.Host, entry, id);
                        }

                        break;
                    case IdentifierType.Phone when IdentifierNormalizer.Phone(id.Value) is string phone:
                        Add(_phones, phone, entry, id);
                        if (IdentifierNormalizer.PhoneEnding(phone) is string ending)
                        {
                            Add(_phoneEndings, ending, entry, id);
                        }

                        break;
                    case IdentifierType.CryptoAddress when IdentifierNormalizer.Crypto(id.Value) is string address:
                        Add(_crypto, address, entry, id);
                        break;
                }
            }
        }
    }

    /// <summary>Finds listed identifiers that match a screened value. The value must already be valid for its type.</summary>
    public List<IdentifierMatch> Find(IdentifierType type, string query)
    {
        var found = new List<IdentifierMatch>();
        switch (type)
        {
            case IdentifierType.Email:
                var email = IdentifierNormalizer.Email(query)!;
                AddAll(found, _emails, email, type, query, IdentifierMatchLevel.Exact);
                AddDomain(found, IdentifierNormalizer.EmailDomain(email), type, query);
                break;
            case IdentifierType.Website:
                var site = IdentifierNormalizer.Website(query)!.Value;
                AddAll(found, _websites, IdentifierNormalizer.WebsiteKey(site), type, query, IdentifierMatchLevel.Exact);
                AddDomain(found, site.Host, type, query);
                break;
            case IdentifierType.Phone:
                var phone = IdentifierNormalizer.Phone(query)!;
                AddAll(found, _phones, phone, type, query, IdentifierMatchLevel.Exact);
                if (IdentifierNormalizer.PhoneEnding(phone) is string ending)
                {
                    AddAll(found, _phoneEndings, ending, type, query, IdentifierMatchLevel.SameNumberEnding);
                }

                break;
            default:
                AddAll(found, _crypto, IdentifierNormalizer.Crypto(query)!, type, query, IdentifierMatchLevel.Exact);
                break;
        }

        // One line per listed identifier, keeping the strongest level (Exact is declared first).
        return found
            .GroupBy(m => (m.Entry.Id, m.Listed))
            .Select(g => g.OrderBy(m => m.Level).First())
            .OrderBy(m => m.Level)
            .ThenBy(m => m.Entry.Id)
            .ToList();
    }

    private void AddDomain(List<IdentifierMatch> found, string host, IdentifierType type, string query)
    {
        foreach (var domain in IdentifierNormalizer.HostAndParents(host))
        {
            AddAll(found, _ownDomains, domain, type, query, IdentifierMatchLevel.SameDomain);
        }
    }

    private static void AddAll(
        List<IdentifierMatch> found,
        Dictionary<string, List<(SanctionsEntry, SanctionsIdentifier)>> map,
        string key,
        IdentifierType type,
        string query,
        IdentifierMatchLevel level)
    {
        if (map.TryGetValue(key, out var hits))
        {
            foreach (var (entry, listed) in hits)
            {
                found.Add(new IdentifierMatch(entry, listed, type, query, level));
            }
        }
    }

    private static void Add(
        Dictionary<string, List<(SanctionsEntry, SanctionsIdentifier)>> map,
        string key,
        SanctionsEntry entry,
        SanctionsIdentifier id)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = new List<(SanctionsEntry, SanctionsIdentifier)>();
        }

        list.Add((entry, id));
    }
}
