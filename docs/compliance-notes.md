# Using OfacMatch responsibly

OfacMatch is a software component. It does not make compliance decisions and is not legal advice. These notes describe how to use it well; your compliance officer and counsel decide your program.

## A match is a lead, not a finding

A score measures how similar two names are written. OFAC itself describes its search results the same way: potential matches to review, with no threshold recommended. Every match at or above your threshold should go to a person, who compares the full record (date of birth, nationality, address, identification numbers) with the listed entry before clearing or escalating it.

## Choosing a threshold

The [held-out evaluation](evaluation-heldout.md) on the real SDN list, on names never used while developing the method, shows the trade-off with the default settings of version 2.0:

| Threshold | Listed parties found (realistic variations) | Harder variations found | Two-part ordinary names that alert | Three-part ordinary names that alert |
|---|---:|---:|---:|---:|
| 80 | 100.0% | 99.9% | 75.7% | 51.0% |
| 85 | 100.0% | 99.6% | 37.5% | 10.3% |
| 90 | 100.0% | 98.4% | 10.2% | 2.2% |
| 95 | 99.6% | 83.4% | 2.1% | 0.0% |

What this means in practice:

- **Name-only screening alerts often.** At 85, more than a third of ordinary two-part names resemble some listed name, and so do 44.6% of names that share a first name with a listed person. Expect a review queue and staff it.
- **Collect more than a name.** Supplying `YearOfBirth` and `Country` lets reviewers clear most alerts in seconds using the conflict flags. Supplying a party's email, phone number, website or crypto address catches listed parties that have changed their name (see below).
- **Between 85 and 90, alerts on ordinary names fall by about three quarters, while recall on these variations stays at 100% and on the harder ones falls from 99.6% to 98.4%.** Nicknames are the most sensitive: 96.5% are found at 85 and 90.0% at 90. Pick your threshold from your own risk assessment, record why, and re-test after upgrades.
- **Version 2.0 changed scoring.** A searched name with more parts than a listed name (an extra middle name) now scores high when the listed parts all match. This finds people who give a fuller name than the list has, and makes three-part ordinary names alert more often at 85 (10.3% against 3.3% in 0.1.2). Re-test your threshold when you upgrade; `ScoringOptions.AllowExtraSearchedParts = false` restores the earlier behaviour.
- **The tests are synthetic.** They cover common kinds of spelling drift, not every real one. Your own anonymized alert history is the best test of a threshold.

## Screen contact details, not only names

A listed party can change its name and keep its email address, phone number and website. In OFAC's 2026 settlement with Pegasus Worldwide Logistics, the supplier had changed its name, but the email address and phone number on its SDN entry stayed the same; screening only the new name missed it. When you have a party's email, phone, website or crypto address, screen them with the name.

An identifier match is strong evidence even when the names look nothing alike, so a different birth year does not mark it as "likely a different person". A **same domain** or **same number ending** match is weaker than an exact one: a large company's domain can be shared by many staff, and a number ending can be a coincidence. Review it against the full record like any other potential match.

## Keep the list current

OFAC changes the SDN list often, sometimes several times a week. Reload it at least daily, screen against the current list before each decision, and keep the date of the list you screened against with each result.

## Keep a record

Store, for every screening: the name and data searched, the list date, the settings used, the matches returned, and `match.Explain()` for each, plus the reviewer's decision and reason. That record shows what you checked and why you cleared or escalated it.

## What OfacMatch does not cover

- OFAC's Consolidated (non-SDN) lists, and other countries' lists (UN, EU, UK). These are on the roadmap.
- The 50 Percent Rule: entities owned 50% or more by blocked persons are blocked even if they are not listed by name.
- Passport, national ID and tax ID numbers, vessel IMO numbers and other fields listed in remarks. Email addresses, phone numbers, websites and crypto addresses are screened (since 2.1).
- Adverse media, politically exposed persons, and other parts of a full KYC/AML program.

## Data and privacy

OfacMatch runs entirely in your process. Names you screen are never sent anywhere; the only network call is the optional download of the public list from OFAC.
