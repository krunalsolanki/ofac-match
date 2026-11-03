# OfacMatch

OFAC sanctions name screening for .NET that tells you *why* a name matched.

![OfacMatch screening a misspelled name, flagging a birth-year conflict and clearing a common name](https://raw.githubusercontent.com/krunalsolanki/ofac-match/main/docs/images/demo.gif)

**[Try it in your browser](https://krunalsolanki.github.io/ofac-match/)**: no install, and the names you type never leave your browser.

## Why I built this

I spent ten years building title and mortgage systems at First American, including OFAC screening that stops a loan from going to someone on the Treasury's sanctions list. Name matching sounds simple until a compliance analyst asks why a name was flagged, or why a misspelled one wasn't.

Many teams still search OFAC's website by hand or rely on screening logic they can't see inside. I wanted something small and open: load the official list, score names the way OFAC describes in its own [FAQ](https://ofac.treasury.gov/faqs/topic/1636), and show the reasoning behind every score.

## What it does

OfacMatch downloads the Specially Designated Nationals (SDN) list from OFAC, screens names against it, and gives each potential match a score from 0 to 100, the same scale OFAC's [search tool](https://sanctionssearch.ofac.treas.gov/) uses. Every match comes with an explanation: which name or alias it hit, and how each part of the name paired up.

It handles the messy parts of real names: accents, `LAST, First` ordering, apostrophes and hyphens, titles like "Dr.", company endings like LLC or LTD (even misspelled), and aliases. If you pass a year of birth or country that disagrees with the listed record, the match is flagged rather than hidden, because that call belongs to a person.

It has no dependencies, runs inside your own process (names never leave your systems), and works on modern .NET as well as .NET Framework 4.7.2+.

I measured it on the real list rather than guessing: it finds 99.8% of 4,255 realistic misspellings and variations of listed names at a threshold of 85. The [evaluation](https://github.com/krunalsolanki/ofac-match/blob/main/docs/evaluation.md) has the details, including where it still misses.

This is an early release (0.1). Feedback, bug reports and name variations that score badly are very welcome.

> OfacMatch helps you screen; it doesn't make compliance decisions and isn't legal advice. A score says how alike two names are written, not whether two records are the same person. Pick your threshold from your own risk assessment and have someone review every potential match. More in [Using OfacMatch responsibly](https://github.com/krunalsolanki/ofac-match/blob/main/docs/compliance-notes.md).

## Quick start

```bash
dotnet add package OfacMatch
```

```csharp
using OfacMatch.Data;
using OfacMatch.Screening;

using var http = new HttpClient();
var list = await SanctionsList.DownloadAsync(http, saveToDirectory: "ofac-data");

var screener = new SanctionsScreener(list, new ScreeningOptions { MinScore = 85 });

foreach (var match in screener.Screen("Aymen Zawahri"))
{
    Console.WriteLine(match.Explain());
}
```

Output against the SDN list published 27 October 2026:

```text
Score 95.8 via NameParts against Primary name "AL ZAWAHIRI, Dr. Ayman"
  Entry 2676: AL ZAWAHIRI, Dr. Ayman (Individual; programs: SDGT)
  Full-name score 84.5; name-parts score 95.8
  AYMEN -> AYMAN: similarity 0.907, sounds alike
  ZAWAHRI -> ZAWAHIRI: similarity 0.946, sounds alike
```

[How scoring works](https://github.com/krunalsolanki/ofac-match/blob/main/docs/how-scoring-works.md) walks through this example step by step.

### Screening with more context

```csharp
var request = new ScreeningRequest("John Michael Doe")
{
    Type = EntryType.Individual, // a borrower is a person, so skip companies and vessels
    YearOfBirth = 1971,          // flags matches whose listed dates of birth disagree
    Country = "United States",   // flags matches whose listed addresses are all elsewhere
};

var matches = screener.Screen(request);
```

### Offline use

```csharp
var list = SanctionsList.LoadFromDirectory("ofac-data"); // SDN.CSV, ALT.CSV, ADD.CSV
```

OFAC updates the list often. Reload it at least daily, and screen against the current list before every decision.

### Use it from an AI assistant (MCP server)

`OfacMatch.Mcp` lets Claude, GitHub Copilot and other [MCP](https://modelcontextprotocol.io/) clients screen names, look up SDN entries and explain scores. It runs locally and needs no API key.

**Claude Desktop:** download the extension for your computer from the [latest release](https://github.com/krunalsolanki/ofac-match/releases/latest) (`ofac-match-windows.mcpb` or `ofac-match-mac-*.mcpb`) and double-click it. No .NET or config file needed.

**Claude Code and other clients** (needs the .NET 10 SDK):

```bash
claude mcp add ofac-match -- dnx OfacMatch.Mcp --yes
```

Tools: `screen_name`, `screen_names`, `get_sanctions_entry`, `compare_names`, `get_list_info`. See the [MCP server guide](https://github.com/krunalsolanki/ofac-match/blob/main/src/OfacMatch.Mcp/README.md) for other clients and settings.

### Command line

```bash
dotnet run --project samples/OfacMatch.Cli -- "Aymen Zawahri" --min 85 --yob 1951
```

## Results on the real SDN list

From the [evaluation](https://github.com/krunalsolanki/ofac-match/blob/main/docs/evaluation.md): 600 listed parties (400 individuals, 200 companies) and 4,255 realistic variations of their names, plus 2,000 ordinary names, against the SDN list published 27 October 2026.

| Threshold | Listed parties found | Ordinary names that alert |
|---|---:|---:|
| 80 | 99.9% | 83.4% |
| 85 | 99.8% | 47.9% |
| 90 | 99.8% | 15.9% |
| 95 | 99.5% | 3.8% |

Variations tested: exact, surname first, lower case without punctuation, one wrong letter, two letters swapped, one letter missing, different transliteration, joined particles ("ALZAWAHIRI"), middle name left out, and changed company suffix.

Two things surprised me in these numbers. First, screening on a name alone alerts a lot: about half of ordinary two-part names look like *something* on the list, and a few are spelled exactly like a listed person. Date of birth and country are what let a reviewer clear those quickly. Second, moving the threshold from 85 to 90 cut alerts by two thirds without losing any of these variations, so the threshold deserves a deliberate choice rather than a default.

You can re-run it yourself:

```bash
dotnet run -c Release --project eval/OfacMatch.Evaluation -- --data ofac-data --out docs/evaluation.md
```

## Documentation

| Guide | What it covers |
|---|---|
| [Why OfacMatch](https://github.com/krunalsolanki/ofac-match/blob/main/docs/why-ofacmatch.md) | Benefits and comparison with other options |
| [How scoring works](https://github.com/krunalsolanki/ofac-match/blob/main/docs/how-scoring-works.md) | Every step, with a worked example |
| [API and configuration reference](https://github.com/krunalsolanki/ofac-match/blob/main/docs/reference.md) | Types, options and defaults |
| [Using OfacMatch responsibly](https://github.com/krunalsolanki/ofac-match/blob/main/docs/compliance-notes.md) | Thresholds, review, record keeping, limits |
| [Evaluation](https://github.com/krunalsolanki/ofac-match/blob/main/docs/evaluation.md) | Full results by variation and setting |
| [MCP server](https://github.com/krunalsolanki/ofac-match/blob/main/src/OfacMatch.Mcp/README.md) | Using OfacMatch from AI assistants |

## What's next

Things I'd like to work on, roughly in order:

- [x] Evaluation on the real list
- [ ] Give rare words more weight ("CUBA" should count more than "BANK"), to cut alerts on common names
- [ ] Catch typos at the start of company endings ("LTMITED")
- [ ] Support OFAC's Consolidated (non-SDN) list and the Advanced XML format
- [ ] Faster batch screening
- [ ] An ASP.NET Core sample and a `dotnet tool` package

If you'd like to help with any of these, or have a case that scores badly, open an issue. [CONTRIBUTING.md](https://github.com/krunalsolanki/ofac-match/blob/main/CONTRIBUTING.md) has the details.

## Credits

Built by [Krunal Solanki](https://github.com/krunalsolanki). The idea and the compliance background come from my work on OFAC screening in mortgage lending.

## Privacy Policy

OfacMatch collects nothing. Names you screen are processed on your own computer, or in your browser for the web page, and are never sent anywhere. The only network request is the download of the public SDN list from OFAC, which is cached locally. Full details: [PRIVACY.md](https://github.com/krunalsolanki/ofac-match/blob/main/PRIVACY.md).

## Data

The SDN list is published by the U.S. Department of the Treasury's Office of Foreign Assets Control. OfacMatch downloads it from OFAC's [Sanctions List Service](https://ofac.treasury.gov/sanctions-list-service) and does not redistribute it. The unit tests use fictional names only.

## License

[Apache License 2.0](https://github.com/krunalsolanki/ofac-match/blob/main/LICENSE)
