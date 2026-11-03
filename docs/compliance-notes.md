# Using OfacMatch responsibly

OfacMatch is a software component. It does not make compliance decisions and is not legal advice. These notes describe how to use it well; your compliance officer and counsel decide your program.

## A match is a lead, not a finding

A score measures how similar two names are written. OFAC itself describes its search results the same way: potential matches to review, with no threshold recommended. Every match at or above your threshold should go to a person, who compares the full record (date of birth, nationality, address, identification numbers) with the listed entry before clearing or escalating it.

## Choosing a threshold

The [evaluation](evaluation.md) on the real SDN list shows the trade-off with the default settings:

| Threshold | Listed parties found (realistic variations) | Ordinary names that alert |
|---|---:|---:|
| 80 | 99.9% | 83.4% |
| 85 | 99.8% | 47.9% |
| 90 | 99.8% | 15.9% |
| 95 | 99.5% | 3.8% |

What this means in practice:

- **Name-only screening alerts often.** At 85, about half of ordinary two-part names resemble some listed name. Expect a review queue and staff it.
- **Collect more than a name.** Supplying `YearOfBirth` and `Country` lets reviewers clear most alerts in seconds using the conflict flags.
- **Between 85 and 90, recall barely changes on these variations, while alerts fall by two thirds.** Spelling drift beyond what the evaluation tests (heavy transliteration, nicknames, partial names) still benefits from lower thresholds. Pick yours from your own risk assessment, record why, and re-test after upgrades.

## Keep the list current

OFAC changes the SDN list often, sometimes several times a week. Reload it at least daily, screen against the current list before each decision, and keep the date of the list you screened against with each result.

## Keep a record

Store, for every screening: the name and data searched, the list date, the settings used, the matches returned, and `match.Explain()` for each, plus the reviewer's decision and reason. That record shows what you checked and why you cleared or escalated it.

## What OfacMatch does not cover

- OFAC's Consolidated (non-SDN) lists, and other countries' lists (UN, EU, UK). These are on the roadmap.
- The 50 Percent Rule: entities owned 50% or more by blocked persons are blocked even if they are not listed by name.
- Identification numbers, vessel IMO numbers and other fields, which need exact matching rather than name similarity.
- Adverse media, politically exposed persons, and other parts of a full KYC/AML program.

## Data and privacy

OfacMatch runs entirely in your process. Names you screen are never sent anywhere; the only network call is the optional download of the public list from OFAC.
