# Privacy Policy

Last updated: 3 November 2026

This policy covers everything published from this repository: the `OfacMatch` .NET library, the `OfacMatch.Mcp` MCP server (from NuGet, the Claude Desktop extension or the Claude plugin), the command-line sample and the try-it web page.

## Data collection

OfacMatch does not collect, store or send any personal data. The names, years of birth and countries you screen are processed on your own computer (or, for the web page, inside your browser) and are never sent to the author or to anyone else. There is no account, no analytics, no telemetry and no tracking.

## Network use

The only network request OfacMatch makes is to download the public Specially Designated Nationals (SDN) list from the U.S. Treasury's Office of Foreign Assets Control:

- `https://sanctionslistservice.ofac.treas.gov` (files SDN.CSV, ALT.CSV and ADD.CSV)

That request contains no screening data; it is a plain file download with the User-Agent `OfacMatch/0.1 (+https://github.com/krunalsolanki/ofac-match)`. OFAC's own [privacy policy](https://home.treasury.gov/footer/privacy-act) applies to it. If you point the MCP server at an offline copy of the list (`OFACMATCH_DATA_DIR`), it makes no network requests at all.

The web page is hosted on GitHub Pages and loads a copy of the list from there, so GitHub's [privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement) applies to visiting it. What you type into the page stays in your browser.

## Storage

The MCP server caches the downloaded SDN list on your computer so it doesn't download it on every call: `%LOCALAPPDATA%\OfacMatch\sdn` on Windows, `~/Library/Application Support/OfacMatch/sdn` on macOS and `~/.local/share/OfacMatch/sdn` on Linux, or the folder set in `OFACMATCH_CACHE_DIR`. The cache holds only OFAC's public list, never your searches. Delete the folder at any time to remove it.

## Sharing with third parties

None. OfacMatch has no servers of its own and shares nothing with anyone.

When you use the MCP server through an AI assistant such as Claude, the assistant sees the names you ask it to screen and the results, under that assistant's own privacy terms. That exchange is between you and the assistant's provider; OfacMatch adds nothing to it.

## Data retention

OfacMatch keeps no data about you. The cached SDN list is replaced when it is older than 24 hours (configurable with `OFACMATCH_REFRESH_HOURS`) and stays on your computer until you delete it.

## Contact

Questions about this policy: open an issue at https://github.com/krunalsolanki/ofac-match/issues or contact [Krunal Solanki](https://github.com/krunalsolanki) on GitHub.
