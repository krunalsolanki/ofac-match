# ofacmatch (Python)

Python client for the self-hosted [OfacMatch REST API](https://github.com/krunalsolanki/ofac-match/blob/main/src/OfacMatch.Api/README.md): explainable OFAC SDN name screening that runs on **your own server**, so the names you screen never leave your network. Free and open source (Apache-2.0), with no dependencies outside the Python standard library.

> Potential matches only. A score measures how alike two names are written, not whether they are the same party. A person must review every potential match. It checks the OFAC SDN list only. Not legal advice.

## 1. Start the API (once)

```bash
docker run -p 8080:8080 ghcr.io/krunalsolanki/ofacmatch-api
```

It downloads the SDN list from OFAC at start-up and refreshes it daily.

## 2. Install the client

```bash
pip install ofacmatch
```

Or straight from GitHub:

```bash
pip install "git+https://github.com/krunalsolanki/ofac-match#subdirectory=clients/python"
```

## Screen from Python

```python
from ofacmatch import Client

client = Client("http://localhost:8080")          # api_key="..." if the server requires one

result = client.screen("Aymen Zawahri", year_of_birth=1951)
for match in result["matches"]:
    print(match["score"], match["listedName"], match["explanation"])
```

### A whole customer list

```python
customers = [
    {"reference": "CUST-1001", "name": "Aymen Zawahri", "year_of_birth": 1951},
    {"reference": "CUST-1002", "name": "Emily Carter", "year_of_birth": 1985, "country": "United States"},
    # ... any number: sent 1,000 at a time
]

result = client.screen_batch(customers, min_score=85)
print(result["summary"])   # {'screened': 2, 'review': 1, 'likelyDifferentPerson': 0, 'noMatch': 1, 'errors': 0}

for row in result["results"]:
    if row["status"] in ("review", "likely_different_person"):
        best = row["matches"][0]
        print(row["reference"], best["score"], best["listedName"])
```

Each result keeps your `reference` and has a `status`:

- `review`: a potential match; a person must review it
- `likely_different_person`: every match has a birth year that differs from the one you gave; still check it
- `no_match`: nothing at or above the minimum score
- `error`: that item was invalid (`row["error"]` says why); the rest are still screened

With pandas: `client.screen_batch(df.rename(columns={"id": "reference"}).to_dict("records"))`.

### Other calls

```python
client.compare("Jon Doe", "DOE, John")   # how two names score, part by part
client.entry(2676)                       # the full SDN record behind a match
client.list_info()                       # which copy of the list the server uses
client.health()                          # ready once the list is loaded
```

Errors raise `ofacmatch.OfacMatchError` with `.status` (HTTP status, or None if the server can't be reached) and `.errors` (per-field messages for invalid input).

## Command line

Screen a CSV (for example an Excel sheet saved as CSV) and get a results CSV with the best match, the explanation, and empty columns for the reviewer's decision:

```bash
ofacmatch csv customers.csv results.csv
ofacmatch screen "Aymen Zawahri" --yob 1951
```

The input needs a header row with a **Name** column (or **First name** and **Last name**). Optional: **ID** or **Reference**, **Year of birth** or **Date of birth**, **Country**, **Type**. Use `--url` and `--api-key` (or `OFACMATCH_URL` and `OFACMATCH_API_KEY`) for a server other than `http://localhost:8080`.

## Links

- [OfacMatch on GitHub](https://github.com/krunalsolanki/ofac-match): the library, evaluation and paper
- [REST API guide](https://github.com/krunalsolanki/ofac-match/blob/main/src/OfacMatch.Api/README.md)
- [Try it in the browser](https://krunalsolanki.github.io/ofac-match/), no install
