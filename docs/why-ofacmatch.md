# Why OfacMatch

U.S. businesses can't deal with people and companies on OFAC's sanctions lists, so banks, lenders, title and escrow companies, payment firms and exporters all screen names. Big institutions buy screening platforms. Smaller firms often search OFAC's website by hand, or run matching code that nobody on the team can explain when an examiner asks.

OfacMatch is meant for that second group, and for .NET developers who need screening inside their own application.

## Who it helps, and how

**Compliance teams** get an explanation with every alert: which listed name or alias it matched, and how each part of the name paired up. That makes alerts quicker to clear and easier to document. The [evaluation](evaluation.md) also gives you real numbers to pick a threshold from, instead of a default nobody can justify. One example from those numbers: a small rule that stops a shared first name from carrying the whole score cut alerts on ordinary names from 63.2% to 47.9% at a threshold of 85, while still finding 99.8% of the tested variations.

**Developers** get one package with no dependencies that runs on .NET Framework 4.7.2+ as well as modern .NET, which matters because a lot of lending and title software still runs on .NET Framework. Downloading and refreshing the official list is one call, a search takes roughly 25–35 ms of CPU against the whole list, and the screener is safe to share across threads for batch jobs. The building blocks (Jaro-Winkler, Soundex, the name normalizer) are public if you need to do something different.

**Smaller firms** can run it for free on their own servers. Customer names never leave their systems and there's no per-search fee.

**Everyone else** can read exactly how scores are calculated, re-run the evaluation with the same seed, and suggest improvements. A fix found by one firm helps all of them.

## Compared with other options

| Option | Good at | Trade-offs |
|---|---|---|
| [OFAC Sanctions List Search](https://sanctionssearch.ofac.treas.gov/) | Official and free | One search at a time by hand; OFAC says it isn't meant for automated use; no explanation of scores |
| Commercial screening platforms and APIs | Many lists, case management, support | Paid per search or seat; customer data goes to a third party; scoring is often not disclosed |
| [OpenSanctions](https://www.opensanctions.org/) | Broad data across many countries, plus matching | A separate service to run or call; check its licence for business use |
| [Sanctions.Net](https://github.com/IsraelIyonsi/Sanctions.NET) | .NET library covering OFAC, UN and EU lists | Different focus; I haven't benchmarked it |
| **OfacMatch** | Explained scores, published evaluation, follows OFAC's described method, runs in-process on old and new .NET | OFAC's SDN list only for now; no case management; see [what it doesn't cover](compliance-notes.md#what-ofacmatch-does-not-cover) |

OfacMatch is a component, not a compliance platform. It fits best when you want OFAC name screening you can see into and test, inside your own .NET application.

## What's next

I'm letting the evaluation decide what to work on: each release goes after the weakest measured area. Right now that's typos at the start of company endings like "LTMITED", and the alert rate on common names, which giving rare words more weight should help with. The full list is in the [README](../README.md#whats-next).
