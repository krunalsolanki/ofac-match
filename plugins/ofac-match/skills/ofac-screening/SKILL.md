---
name: ofac-screening
description: Screen people, companies, vessels or aircraft against the OFAC SDN sanctions list with the ofac-match tools, and report potential matches with their explanations for a person to review. Use when the user asks to check, screen or look up a name against OFAC, the SDN list or U.S. sanctions.
---

# Screening names against the OFAC SDN list

Use the `ofac-match` MCP server's tools for every screening. Don't answer from memory: the SDN list changes often, and the tools use the current published copy.

## Running a screening

1. For one name, call `screen_name`. Pass `yearOfBirth` and `country` whenever the user knows them, so conflicts with the listed record can be flagged. Pass `type` (individual, entity, vessel or aircraft) when the user says what kind of party it is.
2. For several names, call `screen_names`, then call `screen_name` for each name that has potential matches to get the full explanation.
3. To show the complete published record behind a match, call `get_sanctions_entry` with the match's entry ID.
4. To explain why two specific names score the way they do, call `compare_names`.
5. If the user asks how current the list is, call `get_list_info`.

Keep the default minimum score of 85 unless the user asks for a different threshold, and say which threshold you used.

## Reporting results

- Call every result a **potential match**, never a confirmed match, a hit on the person, or a finding that someone is sanctioned.
- For each potential match, give the score, the listed name, the entry ID, the programs, and the explanation from the tool: which name or alias matched and how the name parts paired.
- Point out every date-of-birth or country conflict the tool flags, and explain that a conflict suggests a different person but doesn't clear the match by itself.
- When there are no potential matches, say that no listed name scored at or above the threshold, and give the list date from the result. Don't say the person is "clear" or "not sanctioned".
- End with the reminder that a person must review each potential match against the full record (date of birth, nationality, address, identification numbers) before any decision, and that this is not legal advice.

## Privacy

The tools run on the user's computer, and the names stay there. Don't paste screened names into other tools or web searches unless the user asks you to.
