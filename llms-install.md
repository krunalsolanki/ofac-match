# Installing the OfacMatch MCP server

These steps are for an AI agent (such as Cline) setting up the OfacMatch MCP server for a user.

OfacMatch screens person, company, vessel and aircraft names against the U.S. Treasury OFAC SDN sanctions list and explains each potential match. It runs locally, needs no API key or account, and the only network request it makes is downloading OFAC's public list.

## 1. Check for the .NET 10 SDK

The server is the `OfacMatch.Mcp` NuGet package, run with `dnx`, which comes with the .NET 10 SDK.

Run:

```bash
dotnet --version
```

If the command is missing or the version is below 10, ask the user to install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet, then open a new terminal so `dnx` is on the PATH.

## 2. Add the server to the MCP settings

Add this entry to the client's MCP settings (for Cline, `cline_mcp_settings.json`):

```json
{
  "mcpServers": {
    "ofac-match": {
      "command": "dnx",
      "args": ["OfacMatch.Mcp", "--yes"],
      "disabled": false,
      "autoApprove": []
    }
  }
}
```

**On Windows**, `dnx` is a `.cmd` file, so start it through `cmd`:

```json
{
  "mcpServers": {
    "ofac-match": {
      "command": "cmd",
      "args": ["/c", "dnx", "OfacMatch.Mcp", "--yes"],
      "disabled": false,
      "autoApprove": []
    }
  }
}
```

No API keys or environment variables are needed. Optional settings:

| Variable | Purpose |
|---|---|
| `OFACMATCH_DATA_DIR` | A folder with SDN.CSV, ALT.CSV and ADD.CSV to use offline instead of downloading |
| `OFACMATCH_CACHE_DIR` | Where the downloaded list is cached |
| `OFACMATCH_REFRESH_HOURS` | How old the cached list may get before it is downloaded again (default 24) |

## 3. Verify

Call the `get_list_info` tool. The first call downloads the package and the list (about 8 MB) and can take 10 to 30 seconds. It should report about 19,000 entries.

Then call `screen_name` with `{"name": "Aymen Zawahri", "yearOfBirth": 1990}`. It should return one potential match, `AL ZAWAHIRI, Dr. Ayman`, scoring about 95.8, with a date-of-birth conflict.

## Tools

| Tool | What it does |
|---|---|
| `screen_name` | Screen one name; returns potential matches with scores and explanations |
| `screen_names` | Screen up to 100 names; returns the best match for each |
| `get_sanctions_entry` | Full published record of an SDN entry |
| `compare_names` | How two names score against each other, part by part |
| `get_list_info` | Which copy of the list is loaded and when it was downloaded |

All tools are read-only. Results are potential matches for a person to review, not decisions, and not legal advice.

More: https://github.com/krunalsolanki/ofac-match/blob/main/src/OfacMatch.Mcp/README.md
