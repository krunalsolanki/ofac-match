# OfacMatch MCP server

<!-- mcp-name: io.github.krunalsolanki/ofac-match -->

An [MCP](https://modelcontextprotocol.io/) server that lets AI assistants such as Claude, GitHub Copilot and other MCP clients screen names against the U.S. Treasury OFAC SDN sanctions list, using [OfacMatch](https://github.com/krunalsolanki/ofac-match).

It runs on your machine, needs no API key, and downloads the official list straight from OFAC.

## Tools

| Tool | What it does |
|---|---|
| `screen_name` | Screens one name and returns potential matches with scores (0–100) and an explanation of each. Optional: entry type, year of birth, country, minimum score. |
| `screen_names` | Screens up to 100 names and returns, for each, the number of potential matches and the best one. |
| `get_sanctions_entry` | Returns the full published record of an SDN entry: names, aliases, programs, dates of birth, addresses, remarks. |
| `compare_names` | Shows how two names score against each other, part by part. |
| `get_list_info` | Reports which copy of the list is loaded and when it was downloaded. |

All tools are read-only.

## Install

**Claude Desktop, one click:** download the extension for your computer from the [latest release](https://github.com/krunalsolanki/ofac-match/releases/latest) and double-click it, then choose **Install**.

| Computer | File |
|---|---|
| Windows | `ofac-match-windows.mcpb` |
| Mac with Apple silicon (M1 and later) | `ofac-match-mac-apple-silicon.mcpb` |
| Mac with Intel | `ofac-match-mac-intel.mcpb` |

The extension includes everything it needs; you don't need .NET. It can optionally use an offline copy of the list (a folder with SDN.CSV, ALT.CSV and ADD.CSV), set in the extension's settings.

**Everything else** runs the NuGet package and requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet), which includes the `dnx` command.

**Claude Code**

```bash
claude mcp add ofac-match -- dnx OfacMatch.Mcp --yes
```

**Claude Desktop** (`claude_desktop_config.json`) and other clients that use `mcpServers`:

```json
{
  "mcpServers": {
    "ofac-match": {
      "command": "dnx",
      "args": ["OfacMatch.Mcp", "--yes"]
    }
  }
}
```

**VS Code** (`.vscode/mcp.json`):

```json
{
  "servers": {
    "ofac-match": {
      "type": "stdio",
      "command": "dnx",
      "args": ["OfacMatch.Mcp", "--yes"]
    }
  }
}
```

**Visual Studio** (`.mcp.json` in the solution folder) uses the same `servers` format as VS Code.

Don't add this package to a project with **Manage NuGet Packages** or `dotnet add package`. It is a .NET tool that your MCP client runs, not a library, so NuGet rejects it with "has multiple package types, which is not supported". For the screening library, use the [OfacMatch](https://www.nuget.org/packages/OfacMatch) package.

Pin a version with `OfacMatch.Mcp@0.1.0`.

**Windows:** `dnx` is installed as `dnx.cmd`. If your client reports that it cannot find `dnx`, use `"command": "dnx.cmd"`, or `"command": "cmd"` with `"args": ["/c", "dnx", "OfacMatch.Mcp", "--yes"]`.

The first call downloads the list from OFAC (about 8 MB) and takes a few seconds; later calls use the cached copy.

## Configuration

| Environment variable | Default | Description |
|---|---|---|
| `OFACMATCH_DATA_DIR` | none | Folder with SDN.CSV, ALT.CSV and ADD.CSV to use offline. When set, nothing is downloaded. |
| `OFACMATCH_CACHE_DIR` | `%LOCALAPPDATA%/OfacMatch/sdn` | Where downloaded files are cached. |
| `OFACMATCH_REFRESH_HOURS` | 24 | How old the cached list may get before it is downloaded again. |

## Example prompts

- "Screen *Aymen Zawahri*, born 1951, against the OFAC list and explain any match."
- "Screen these vendor names and tell me which need review: …"
- "Show me SDN entry 306 and its aliases."
- "Why does *John Smith* not match *NUMBI, John*?"

## Important

- **Results are potential matches, not findings.** Every result carries this notice, and a person must review each match against the full record before any decision. This is not legal advice.
- **Data handling.** Screening runs locally, but the names you ask about pass through your AI client and its model provider like any other prompt. Follow your organization's rules before screening real customer data through an AI assistant.

See the main project for [how scoring works](https://github.com/krunalsolanki/ofac-match/blob/main/docs/how-scoring-works.md), the [evaluation results](https://github.com/krunalsolanki/ofac-match/blob/main/docs/evaluation.md) and [responsible use](https://github.com/krunalsolanki/ofac-match/blob/main/docs/compliance-notes.md).

## License

Apache License 2.0.
