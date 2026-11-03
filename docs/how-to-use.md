# How to Use OfacMatch

This guide covers practical usage of the OfacMatch library for screening names against OFAC's Specially Designated Nationals (SDN) list. For a quick overview, see the [README](../README.md).

## Installation

Via NuGet:
```bash
dotnet add package OfacMatch
```

Or edit your .csproj:
```xml
<ItemGroup>
    <PackageReference Include="OfacMatch" Version="0.1.0" />
</ItemGroup>
```

Then restore:
```bash
dotnet restore
```

## Basic Screening

Load the list once (from the embedded CSV or your own source), then screen multiple names:

```csharp
using OfacMatch;

// Load from embedded data (OFAC SDN list)
var list = await SanctionsList.CreateAsync();

// Create a screener
var screener = new SanctionsScreener(list);

// Screen a name
var matches = screener.Screen("John Smith");

foreach (var match in matches)
{
    Console.WriteLine($"Score: {match.Score:0.0}");
    Console.WriteLine($"Matched: {match.MatchedName}");
    Console.WriteLine($"Entry: {match.Entry.Name} ({match.Entry.Type})");
    Console.WriteLine($"Programs: {string.Join(", ", match.Entry.Programs)}");
    Console.WriteLine();
}
```

## Screening with Context

Provide additional context (date of birth, country, entry type) to flag conflicts:

```csharp
var request = new ScreeningRequest("John Smith")
{
    YearOfBirth = 1975,
    Country = "United States",
    Type = EntryType.Individual
};

var matches = screener.Screen(request);

foreach (var match in matches)
{
    if (match.DateOfBirthConflict)
    {
        Console.WriteLine($"⚠ Date of birth conflict: listed dates are {string.Join(", ", match.Entry.DatesOfBirth)}");
    }

    if (match.CountryConflict)
    {
        Console.WriteLine($"⚠ Country conflict: listed locations are {string.Join(", ", match.Entry.Countries)}");
    }
}
```

## Offline Usage

For offline screening, create a screener once and reuse it:

```csharp
// Application startup
public class ScreeningService
{
    private static SanctionsScreener? _screener;

    public static async Task InitializeAsync()
    {
        var list = await SanctionsList.CreateAsync();
        _screener = new SanctionsScreener(list);
    }

    public static IReadOnlyList<ScreeningMatch> Screen(string name)
    {
        if (_screener == null) throw new InvalidOperationException("Screener not initialized");
        return _screener.Screen(name);
    }
}

// Usage
await ScreeningService.InitializeAsync();
var matches = ScreeningService.Screen("Jane Doe");
```

## Batch and Parallel Screening

Screen multiple names efficiently:

```csharp
// Sequential batch
var names = new[] { "Alice", "Bob", "Charlie" };
foreach (var name in names)
{
    var matches = screener.Screen(name);
    Process(matches);
}

// Parallel batch (reuse one screener across threads)
var results = new ConcurrentBag<(string Name, int MatchCount)>();
Parallel.ForEach(names, name =>
{
    var matches = screener.Screen(name);
    results.Add((name, matches.Count));
});
```

## Looking Up Entries Directly

If you have an entry ID from a previous match:

```csharp
// Find an entry by ID
var entry = list.Entries.FirstOrDefault(e => e.Id == 12345);

if (entry != null)
{
    Console.WriteLine($"ID: {entry.Id}");
    Console.WriteLine($"Name: {entry.Name}");
    Console.WriteLine($"Type: {entry.Type}");
    Console.WriteLine($"Programs: {string.Join(", ", entry.Programs)}");
    Console.WriteLine($"Aliases: {string.Join(", ", entry.Aliases.Select(a => a.Name))}");
    Console.WriteLine($"Addresses: {string.Join("; ", entry.Addresses.Select(a => a.Country))}");
}
```

## Understanding and Explaining Matches

Get a detailed breakdown of why a match occurred:

```csharp
var matches = screener.Screen("John Smith");
if (matches.Count > 0)
{
    var match = matches[0];
    Console.WriteLine(match.Explain());
}
```

Example output:
```
Score 92.0 via Jaro-Winkler against Primary name "SMITH, John"
  Entry 12345: SMITH, John (Individual; programs: SDN)
  Full-name score 92.0; name-parts score 92.0
  JOHN -> JOHN: similarity 1.000
  SMITH -> SMITH: similarity 1.000
```

If there are conflicts:
```
⚠ Date of birth conflict: listed 1950-01-15
⚠ Country conflict: listed Iran
```

## Direct Name Comparison

Compare two names without the full screening database:

```csharp
using OfacMatch.Matching;

var scorer = new NameScorer();
var score = scorer.Score("John Smith", "Smith, John");
Console.WriteLine($"Similarity: {score.Score:0.0}");
Console.WriteLine($"Technique: {score.Technique}"); // e.g., "Jaro-Winkler"
```

## List Management and Refresh

### Using Embedded Data (Default)

The library ships with a pre-loaded OFAC SDN list snapshot:

```csharp
var list = await SanctionsList.CreateAsync();
// Uses embedded CSV data
```

### Using Custom Data

Load from your own CSV files:

```csharp
var list = await SanctionsList.CreateAsync(
    sdnCsvPath: "path/to/sdn.csv",
    altCsvPath: "path/to/alt.csv",
    addCsvPath: "path/to/add.csv"
);
```

### Refreshing the List

Periodically reload to stay current with OFAC updates:

```csharp
// Create a time-boxed refresher
async Task RefreshListAsync()
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromDays(7)); // Weekly refresh
        var newList = await SanctionsList.CreateAsync();

        // Optionally validate the new list has expected number of entries
        if (newList.Entries.Count > 1000)
        {
            _screener = new SanctionsScreener(newList);
            Console.WriteLine($"Updated screener with {newList.Entries.Count} entries");
        }
    }
}

// Run in the background
_ = Task.Run(RefreshListAsync);
```

## Threshold Selection

The default minimum score is 85 (on a 0–100 scale). Choose a threshold based on your risk tolerance:

- **90+**: Very high confidence; fewer false positives, may miss weak matches
- **85**: Recommended baseline; balanced for compliance review
- **75–85**: Medium sensitivity; more review required, catches weak matches
- **<75**: Low threshold; extensive manual review needed, higher risk of false positives

```csharp
var options = new ScreeningOptions
{
    MinScore = 90, // Stricter threshold
};
var screener = new SanctionsScreener(list, options);

// Or override per-request
var request = new ScreeningRequest("John Smith")
{
    MinScore = 88
};
var matches = screener.Screen(request);
```

### Threshold Tuning with Real Data

Test against known entries:

```csharp
var testCases = new[]
{
    ("SMITH, John", 1950),
    ("JONES, Mary", 1960),
    ("DOE, Robert", 1940),
};

foreach (var (name, dob) in testCases)
{
    var request = new ScreeningRequest(name) { YearOfBirth = dob };
    var matches = screener.Screen(request);
    Console.WriteLine($"{name}: {matches.Count} matches at 85 threshold");
}
```

## Error Handling

Handle parsing and validation errors gracefully:

```csharp
try
{
    var list = await SanctionsList.CreateAsync("invalid_path/sdn.csv");
}
catch (FileNotFoundException ex)
{
    Console.Error.WriteLine($"List file not found: {ex.Message}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load list: {ex.Message}");
}

// Screening is safe; invalid input returns empty results
var matches = screener.Screen(""); // Empty list, no error
var matches2 = screener.Screen("   "); // Whitespace only, empty results
```

## Command-Line Usage

If using the CLI tool (OfacMatch.Cli):

```bash
# Screen a single name
ofac-screen "John Smith"

# Screen with context
ofac-screen "John Smith" --dob 1975 --country "United States"

# Batch from file (one name per line)
ofac-screen --batch names.txt

# Use custom list
ofac-screen "Jane Doe" --sdn path/to/sdn.csv --alt path/to/alt.csv

# Show full explanation
ofac-screen "John Smith" --explain
```

## Testing and Validation

Use test cases from the embedded list to validate integration:

```csharp
[Fact]
public async Task Screen_MatchesKnownSDNEntry()
{
    var list = await SanctionsList.CreateAsync();
    var screener = new SanctionsScreener(list);

    // Pick a real entry from the list
    var entry = list.Entries.First();
    var matches = screener.Screen(entry.Name);

    Assert.NotEmpty(matches);
    Assert.Contains(matches, m => m.Entry.Id == entry.Id);
}

[Fact]
public async Task Screen_ContextFlagsConflicts()
{
    var list = await SanctionsList.CreateAsync();
    var screener = new SanctionsScreener(list);

    var entry = list.Entries.FirstOrDefault(e => e.DatesOfBirth.Any());
    Assert.NotNull(entry);

    var request = new ScreeningRequest(entry.Name)
    {
        YearOfBirth = 1920 // Unlikely to match
    };

    var matches = screener.Screen(request);
    if (matches.Any())
    {
        Assert.True(matches[0].DateOfBirthConflict);
    }
}
```

## Performance Considerations

### Memory Usage

The embedded OFAC list contains ~4000+ entries. A `SanctionsList` plus its indexed `SanctionsScreener` consumes ~10–20 MB per screener instance. For most applications, maintain a single screener in memory.

### Screening Speed

A single name screen typically completes in < 10 ms on modern hardware. Bulk screening 1000 names sequentially takes ~10 seconds. Use parallel screening for large batches:

```csharp
// ~1 second for 1000 names on 8-core CPU
var names = GetBatchNames(1000);
var results = names.AsParallel().Select(name => screener.Screen(name)).ToList();
```

### Indexing Overhead

The screener builds an in-memory index of all names and aliases during construction. This takes ~100–200 ms for the standard list. Index once at startup, not per-request.

```csharp
// ✓ Good: Index once
var screener = new SanctionsScreener(list);
for (int i = 0; i < 10000; i++)
{
    screener.Screen(GetName(i));
}

// ✗ Bad: Re-index on every request
for (int i = 0; i < 10000; i++)
{
    var screener = new SanctionsScreener(list);
    screener.Screen(GetName(i));
}
```

## Next Steps

- Review the [README](../README.md) for design rationale and compliance notes
- Check the [publishing guide](publishing.md) for deployment instructions
- See the [sample tests](../tests/OfacMatch.Tests/) for more code examples
- Consult the embedded OfacMatch library code for advanced API usage

For questions or issues, refer to the project repository or documentation.
