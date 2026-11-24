# Compare your screening tool in 10 minutes

A 25-row test file you can run through any OFAC screening tool, your current vendor's included, and OfacMatch. It checks three things: whether the tool finds listed parties under ordinary spelling changes, whether it catches a party that changed its name but kept a listed email, phone, website or crypto address, and how many alerts it raises on ordinary names.

Everything in the file is either public SDN data or made up. **Don't add real customers to it**, and don't upload customer data anywhere to run it.

## Run it

1. Download [samples/vendor-comparison.csv](https://github.com/krunalsolanki/ofac-match/blob/main/samples/vendor-comparison.csv). Columns: Case ID, Name, Year of birth, Country, Type, Email, Phone, Website, Crypto address.
2. Screen it in your tool against the OFAC SDN list, at the threshold you normally use. If your tool has no email, phone, website or crypto field, screen the name only and note that.
3. Optional: see OfacMatch's result in one click: **https://krunalsolanki.github.io/ofac-match/?test=compare** screens the 25 names in your browser and scores them against the answer key below. Nothing is uploaded. The page also has a button to download the file.
4. Compare with the answer key below.

## Send back three numbers

- **Found:** how many of the 10 name cases raised an alert on the right entry.
- **Found by identifier:** how many of the 5 identifier cases raised an alert on the right entry.
- **False alerts:** how many of the 10 ordinary names raised any alert.

No vendor name, threshold or customer data is needed. Reply on the [feedback discussion](https://github.com/krunalsolanki/ofac-match/discussions) or by message. Results will only ever be published as anonymous totals, never per tool.

Many vendor contracts limit publishing benchmark results, so please share counts privately if yours does.

## Answer key

Checked against the SDN list published 24 November 2026. The list changes, so an entry may be removed later.

### Listed names with ordinary variations (10)

A good tool alerts on each one.

| Case | Input | Listed as (entry) | Variation |
|---|---|---|---|
| T01 | Aymen Zawahri | AL ZAWAHIRI, Dr. Ayman (2676) | Two misspellings, missing "Al", title |
| T04 | Viktor Bout | BOUT, Viktor Anatolijevitch (8279) | Patronymic left out |
| T06 | Jong Un Kim | KIM, Jong Un (20157) | Name order reversed |
| T09 | Ramzan Kadirov | KADYROV, Ramzan Akhmatovich (23343) | Different transliteration, patronymic left out |
| T11 | Mellat Exchange Co. | MELLAT EXCHANGE COMPANY (59230) | Legal form abbreviated (listed 23 Nov 2026) |
| T14 | Joaquin Guzman | GUZMAN LOERA, Joaquin (6861) | Second surname left out |
| T16 | Recas Star General Trading | RECAZ STAR GENERAL TRADING LLC (59112) | One-letter typo, legal form left out (listed 23 Nov 2026) |
| T19 | Nicolas Maduro | MADURO MOROS, Nicolas (22790) | Second surname left out |
| T21 | Deripaska, Oleg | DERIPASKA, Oleg Vladimirovich (24283) | "Last, First" format, patronymic left out |
| T24 | Hassan Nasrallah | NASRALLAH, Hasan (2686) | Common alternative spelling |

### Renamed parties with a listed identifier (5)

The name is made up and matches nothing. Only the identifier, which OFAC prints in the entry's remarks, links it to the entry. This is the gap in OFAC's 2026 Pegasus Worldwide Logistics settlement, where a renamed supplier was missed although its email and phone were on its SDN entry.

| Case | Input | Identifier | Listed on (entry) | What it tests |
|---|---|---|---|---|
| T03 | Westbridge Commodities FZE | Website `https://www.berkeleytrade.com/contact` | BERKELEY TRADING LLC (59184) | Same domain as a listed website, written as a full URL |
| T07 | Atlantic Coast Supplies Lda | Email `QASSIM@Golfrate.com` | GOLFRATE HOLDINGS (ANGOLA) LDA (12453) | Listed email in different letter case |
| T12 | Nahr Al Khair Transfers | Phone `+964 780 779 8905` | AL-ARD AL-JADIDAH MONEY EXCHANGE COMPANY (26700) | Listed phone with spaces and a plus sign |
| T17 | Pacific Mesh Labs | Ethereum address in lower case | LAZARUS GROUP (27307) | Listed in mixed-case checksum form |
| T22 | Greenfield Chemical Supply Co | Bitcoin address `1JPfVv5…z4th` | HEBEI ATUN TRADING CO., LTD. (32313) | Exact, case-sensitive address |

### Ordinary names (10)

None of these is a listed party. Any alert is a false alert, although a tool may reasonably show some for review.

T02 Emily Carter, T05 Robert Miller, T08 Maria Gonzalez Ruiz, T10 Daniel Lee, T13 Sarah Johnson, T15 Blue River Holdings LLC, T18 Omar Haddad, T20 Rosewood Trading LLC, T23 Kevin Nguyen, T25 Anna Petrova.

## OfacMatch's own result

OfacMatch 2.1.1, default threshold 85, same list:

- **Found:** 10 of 10.
- **Found by identifier:** 5 of 5.
- **False alerts:** 5 of 10.
  - Maria Gonzalez Ruiz, Daniel Lee and Anna Petrova each matched a listed person with a different birth year, so the web page shows them as "likely a different person".
  - Blue River Holdings LLC (nearest: RG HOLDINGS LTD, 89) and Rosewood Trading LLC (nearest: ROCKSIDE TRADING, 91, an alias of entry 52053) need review. Company names that share common words such as "Holdings" or "Trading" are a known weakness.

Your tool may well do better on false alerts. That's exactly what this test is meant to show, so please send the numbers either way.
