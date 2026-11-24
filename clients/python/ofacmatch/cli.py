"""Command line: screen one name, or a whole CSV file, through an OfacMatch API server.

    ofacmatch screen "Aymen Zawahri" --yob 1951
    ofacmatch screen "Brand New Trading LLC" --email sales@example.com
    ofacmatch csv customers.csv results.csv
    ofacmatch --url http://screening.internal:8080 --api-key KEY csv customers.csv results.csv
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import re
import sys
from typing import Dict, List, Optional, Sequence

from . import __version__
from .client import Client, OfacMatchError

_YEAR = re.compile(r"\b(1[89]\d\d|20\d\d)\b")

# Header names recognised in an input CSV (compared without case, spaces or punctuation).
_COLUMNS = {
    "name": ["name", "fullname", "customername", "customer", "partyname", "legalname"],
    "first": ["firstname", "givenname", "forename"],
    "middle": ["middlename"],
    "last": ["lastname", "surname", "familyname"],
    "year": ["yearofbirth", "yob", "birthyear", "dateofbirth", "dob", "birthdate"],
    "country": ["country", "nationality", "citizenship", "countryofresidence"],
    "type": ["type", "partytype", "entitytype"],
    "reference": ["id", "reference", "ref", "customerid", "customerno", "accountnumber", "account"],
    "email": ["email", "emailaddress", "mail"],
    "phone": ["phone", "phonenumber", "telephone", "tel", "mobile", "fax"],
    "website": ["website", "web", "url", "homepage", "domain"],
    "crypto": ["cryptoaddress", "crypto", "walletaddress", "wallet", "digitalcurrencyaddress"],
}

_OUTPUT = [
    "reference", "input_name", "result", "potential_matches", "best_score", "best_listed_name",
    "best_entry_id", "best_programs", "date_of_birth_conflict", "country_conflict", "matched_by", "identifier_match", "explanation",
    "reviewer_decision", "reviewer_name", "reviewer_notes",
]


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(prog="ofacmatch", description="Screen names against the OFAC SDN list through your own OfacMatch API server.")
    parser.add_argument("--url", default=os.environ.get("OFACMATCH_URL", "http://localhost:8080"), help="API server (default: $OFACMATCH_URL or http://localhost:8080)")
    parser.add_argument("--api-key", default=os.environ.get("OFACMATCH_API_KEY"), help="API key, if the server requires one (default: $OFACMATCH_API_KEY)")
    parser.add_argument("--version", action="version", version=f"ofacmatch {__version__}")
    commands = parser.add_subparsers(dest="command", required=True)

    one = commands.add_parser("screen", help="screen one name and print the result as JSON")
    one.add_argument("name")
    one.add_argument("--yob", type=int, help="year of birth")
    one.add_argument("--country")
    one.add_argument("--type", choices=["individual", "entity", "vessel", "aircraft"])
    one.add_argument("--email")
    one.add_argument("--phone")
    one.add_argument("--website")
    one.add_argument("--crypto", dest="crypto_address", help="crypto address")
    one.add_argument("--min", type=float, dest="min_score", help="minimum score, 0-100 (default 85)")

    many = commands.add_parser("csv", help="screen every row of a CSV file and write a results CSV")
    many.add_argument("input", help="CSV with a Name column (or First and Last name columns)")
    many.add_argument("output", help="results CSV to write (one row per input row)")
    many.add_argument("--min", type=float, dest="min_score", help="minimum score, 0-100 (default 85)")

    args = parser.parse_args(argv)
    client = Client(args.url, api_key=args.api_key)
    try:
        if args.command == "screen":
            result = client.screen(
                args.name, type=args.type, year_of_birth=args.yob, country=args.country, min_score=args.min_score,
                email=args.email, phone=args.phone, website=args.website, crypto_address=args.crypto_address,
            )
            print(json.dumps(result, indent=2, ensure_ascii=False))
            return 0
        return _screen_csv(client, args.input, args.output, args.min_score)
    except OfacMatchError as error:
        print(f"ofacmatch: {error}", file=sys.stderr)
        return 2
    except (OSError, ValueError) as error:
        print(f"ofacmatch: {error}", file=sys.stderr)
        return 2


def _screen_csv(client: Client, input_path: str, output_path: str, min_score: Optional[float]) -> int:
    with open(input_path, newline="", encoding="utf-8-sig") as f:
        sample = f.read(4096)
        f.seek(0)
        try:
            dialect = csv.Sniffer().sniff(sample, delimiters=",;\t")
        except csv.Error:
            dialect = csv.excel
        rows = list(csv.reader(f, dialect))

    if not rows:
        raise ValueError(f"{input_path} is empty")

    columns = _find_columns(rows[0])
    if "name" not in columns and not ("first" in columns or "last" in columns):
        raise ValueError("no Name column found (or First name and Last name columns); add a header row")

    items: List[Dict[str, object]] = []
    for number, row in enumerate(rows[1:], start=2):
        def cell(key: str) -> str:
            index = columns.get(key)
            return row[index].strip() if index is not None and index < len(row) else ""

        name = cell("name") or " ".join(p for p in (cell("first"), cell("middle"), cell("last")) if p)
        year = _YEAR.search(cell("year"))
        items.append({
            "name": name,
            "reference": cell("reference") or f"row {number}",
            "year_of_birth": int(year.group(1)) if year else None,
            "country": cell("country") or None,
            "type": cell("type") or None,
            "email": cell("email") or None,
            "phone": cell("phone") or None,
            "website": cell("website") or None,
            "crypto_address": cell("crypto") or None,
        })

    if not items:
        raise ValueError(f"{input_path} has a header but no rows")

    response = client.screen_batch(items, min_score=min_score)
    with open(output_path, "w", newline="", encoding="utf-8-sig") as f:
        writer = csv.writer(f)
        writer.writerow(_OUTPUT)
        for item, result in zip(items, response["results"]):
            best = (result.get("matches") or [None])[0]
            writer.writerow([_safe(v) for v in [
                item["reference"],
                item["name"],
                result.get("error") and f"error: {result['error']}" or result.get("status"),
                result.get("potentialMatches", 0),
                best and best["score"],
                best and best["listedName"],
                best and best["entryId"],
                best and ", ".join(best["programs"]),
                best and best["dateOfBirthConflict"],
                best and best["countryConflict"],
                best and best.get("matchedBy"),
                best and "; ".join(m["description"] for m in best.get("identifierMatches") or []),
                best and best["explanation"],
                "", "", "",
            ]])

    summary = response["summary"]
    print(
        f"Screened {summary['screened']}: {summary['review']} to review, "
        f"{summary['likelyDifferentPerson']} likely a different person, {summary['noMatch']} no match, "
        f"{summary['errors']} not screened. Results: {output_path}"
    )
    print("Potential matches only: a person must review each one. OFAC SDN list only.")
    return 0


def _find_columns(header: Sequence[str]) -> Dict[str, int]:
    found: Dict[str, int] = {}
    for index, title in enumerate(header):
        key = re.sub(r"[^a-z]", "", title.lower())
        for column, names in _COLUMNS.items():
            if key in names and column not in found:
                found[column] = index
    return found


def _safe(value: object) -> object:
    """Stops a spreadsheet from running a cell as a formula."""
    if value is None:
        return ""
    if isinstance(value, str) and value[:1] in ("=", "+", "-", "@", "\t", "\r"):
        return "'" + value
    return value


if __name__ == "__main__":
    sys.exit(main())
