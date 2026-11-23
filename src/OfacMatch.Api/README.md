# OfacMatch REST API

A self-hosted HTTP service for explainable OFAC SDN name screening. You run it on your own server or laptop, so the names you screen never leave your network. It's free and open source (Apache-2.0).

> Potential matches only. A score measures how alike two names are written, not whether they are the same party. A person must review every potential match. It checks the OFAC SDN list only. Not legal advice.

## Run it

```bash
docker run -p 8080:8080 ghcr.io/krunalsolanki/ofacmatch-api
```

At start-up it downloads the SDN list from OFAC (about 8 MB) and then checks for a newer list every hour, downloading again once the copy is 24 hours old. `GET /health` answers 503 until the list is loaded and 200 after.

Without Docker (needs the .NET 10 SDK):

```bash
dotnet run --project src/OfacMatch.Api -c Release
```

### Settings

| Environment variable | Meaning | Default |
|---|---|---|
| `OFACMATCH_API_KEY` | If set, every `/v1` request needs the header `X-Api-Key: <key>`. `/health` stays open. | not set |
| `OFACMATCH_DATA_DIR` | Folder with your own `SDN.CSV`, `ALT.CSV` and `ADD.CSV` (and `SDN_COMMENTS.CSV`, optional). Nothing is downloaded when set. | not set |
| `OFACMATCH_CACHE_DIR` | Where the downloaded list is kept. Mount a volume here to keep it between restarts. | `/home/app/ofacmatch` in Docker |
| `OFACMATCH_REFRESH_HOURS` | How old the downloaded list may get before it is downloaded again. | `24` |
| `ASPNETCORE_HTTP_PORTS` | Port to listen on. | `8080` in Docker |

Offline, with your own copy of the list:

```bash
docker run -p 8080:8080 -v /path/to/sdn:/data:ro -e OFACMATCH_DATA_DIR=/data ghcr.io/krunalsolanki/ofacmatch-api
```

The service has no TLS of its own. Put it behind your usual reverse proxy or keep it on a private network, and set `OFACMATCH_API_KEY` if anything else can reach it.

## Endpoints

The full OpenAPI description is at `GET /openapi/v1.json`; import it into Postman, Insomnia or a code generator.

| Method and path | What it does |
|---|---|
| `POST /v1/screen` | Screen one name |
| `POST /v1/screen/batch` | Screen up to 1,000 names, each with your own reference |
| `POST /v1/compare` | Explain how two names score against each other |
| `GET /v1/entries/{id}` | The full SDN record behind a match |
| `GET /v1/list` | Which copy of the list is in use |
| `GET /health` | Ready or not |

### Screen one name

```bash
curl -X POST http://localhost:8080/v1/screen -H "Content-Type: application/json" \
     -d '{"name": "Aymen Zawahri", "yearOfBirth": 1951, "country": "Egypt", "type": "individual"}'
```

All fields except `name` are optional: `type` (individual, entity, vessel, aircraft), `yearOfBirth`, `country`, `minScore` (0-100, default 85) and `maxResults` (1-50, default 10).

```json
{
  "name": "Aymen Zawahri",
  "potentialMatches": 1,
  "matches": [
    {
      "score": 95.8,
      "entryId": 2676,
      "listedName": "AL ZAWAHIRI, Dr. Ayman",
      "type": "Individual",
      "programs": ["SDGT"],
      "matchedName": "AL ZAWAHIRI, Dr. Ayman",
      "matchedNameKind": "Primary",
      "technique": "NameParts",
      "dateOfBirthConflict": false,
      "countryConflict": false,
      "parts": [
        { "queryPart": "AYMEN", "matchedPart": "AYMAN", "similarity": 0.907, "soundsAlike": true, "score": 0.953 },
        { "queryPart": "ZAWAHRI", "matchedPart": "ZAWAHIRI", "similarity": 0.946, "soundsAlike": true, "score": 0.973 }
      ],
      "explanation": "Score 95.8 via NameParts against Primary name \"AL ZAWAHIRI, Dr. Ayman\" ..."
    }
  ],
  "minScore": 85,
  "list": { "entries": 19484, "aliases": 20204, "source": "OFAC Sanctions List Service", "downloadedUtc": "2026-11-17 08:00:00Z" },
  "notice": "Potential matches only. ..."
}
```

(Shortened; the real response has the full explanation and notice.)

### Screen a list

```bash
curl -X POST http://localhost:8080/v1/screen/batch -H "Content-Type: application/json" -d '{
  "minScore": 85,
  "items": [
    { "reference": "CUST-1001", "name": "Aymen Zawahri", "yearOfBirth": 1951 },
    { "reference": "CUST-1002", "name": "Emily Carter", "yearOfBirth": 1985, "country": "United States" }
  ]
}'
```

Each result keeps your `reference` and has a `status`:

- `review`: a potential match; a person must review it
- `likely_different_person`: every match has a birth year that differs from the one you gave; still check it, because the list can be wrong or incomplete
- `no_match`: nothing at or above `minScore`
- `error`: that item was invalid (the `error` field says why); the other items are still screened

A `summary` gives the counts. Items are screened in parallel; 1,000 names take about 1.6 seconds on a laptop.

### From Python

```python
import requests

r = requests.post("http://localhost:8080/v1/screen/batch", json={
    "items": [{"reference": c["id"], "name": c["name"], "yearOfBirth": c.get("yob")} for c in customers]
})
for result in r.json()["results"]:
    if result["status"] in ("review", "likely_different_person"):
        print(result["reference"], result["matches"][0]["listedName"], result["matches"][0]["score"])
```

## Errors

| Status | When |
|---|---|
| 400 | Invalid input, with a message for each field (`errors.name`, `errors.minScore`, ...) |
| 401 | `OFACMATCH_API_KEY` is set and the `X-Api-Key` header is missing or wrong |
| 404 | No SDN entry with that number |
| 503 | The SDN list isn't loaded (yet). The API never answers "no match" without a list. |

## Privacy

Request bodies are never logged. The service makes one kind of outgoing call: downloading the public SDN list from OFAC (none at all with `OFACMATCH_DATA_DIR`). There is no analytics or telemetry of its own.
