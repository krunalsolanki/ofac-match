"""Tests against a small fake API server, so they run without Docker or .NET.

Run from clients/python:  python -m unittest discover -s tests
"""

import csv
import io
import json
import os
import sys
import tempfile
import threading
import unittest
from contextlib import redirect_stdout
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from ofacmatch import Client, OfacMatchError  # noqa: E402
from ofacmatch.cli import main  # noqa: E402

LIST = {"entries": 3, "aliases": 1, "source": "fake", "downloadedUtc": "2026-11-17 00:00:00Z"}
NOTICE = "Potential matches only. Checks the OFAC SDN list only."


def fake_match(score=97.0, dob_conflict=False):
    return {
        "score": score, "entryId": 100, "listedName": "DOE, John", "type": "Individual", "programs": ["SDGT"],
        "matchedName": "DOE, John", "matchedNameKind": "Primary", "technique": "FullName",
        "dateOfBirthConflict": dob_conflict, "countryConflict": False, "parts": [],
        "explanation": "Score 97.0 via FullName against Primary name \"DOE, John\"",
    }


class FakeApi(BaseHTTPRequestHandler):
    batch_sizes = []
    api_key = None

    def log_message(self, *args):
        pass

    def send(self, status, body):
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def authorised(self):
        if self.api_key and self.headers.get("X-Api-Key") != self.api_key:
            self.send(401, {"title": "Missing or wrong X-Api-Key header.", "status": 401})
            return False
        return True

    def do_GET(self):
        if not self.authorised():
            return
        if self.path == "/health":
            self.send(200, {"status": "ok", "list": LIST})
        elif self.path == "/v1/list":
            self.send(200, LIST)
        elif self.path == "/v1/entries/100":
            self.send(200, {"entryId": 100, "name": "DOE, John"})
        else:
            self.send(404, {"title": "Not Found", "detail": "No SDN entry 999 in the list in use.", "status": 404})

    def do_POST(self):
        if not self.authorised():
            return
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        if self.path == "/v1/screen":
            if not body.get("name"):
                self.send(400, {"title": "One or more validation errors occurred.", "status": 400, "errors": {"name": ["Name is required."]}})
                return
            matches = [fake_match()] if "doe" in body["name"].lower() else []
            self.send(200, {"name": body["name"], "potentialMatches": len(matches), "matches": matches, "minScore": body.get("minScore", 85), "list": LIST, "notice": NOTICE, "echo": body})
        elif self.path == "/v1/screen/batch":
            items = body["items"]
            FakeApi.batch_sizes.append(len(items))
            results = []
            for i, item in enumerate(items):
                name = item.get("name") or ""
                if not name:
                    results.append({"index": i, "reference": item.get("reference"), "name": "", "status": "error", "potentialMatches": 0, "matches": [], "error": "Name is required."})
                elif "doe" in name.lower():
                    conflict = item.get("yearOfBirth") == 1990
                    status = "likely_different_person" if conflict else "review"
                    results.append({"index": i, "reference": item.get("reference"), "name": name.upper(), "status": status, "potentialMatches": 1, "matches": [fake_match(dob_conflict=conflict)], "error": None})
                else:
                    results.append({"index": i, "reference": item.get("reference"), "name": name.upper(), "status": "no_match", "potentialMatches": 0, "matches": [], "error": None})
            self.send(200, {"summary": {}, "results": results, "minScore": body.get("minScore", 85), "list": LIST, "notice": NOTICE})
        elif self.path == "/v1/compare":
            self.send(200, {"score": 92.0, "parts": [], "echo": body})
        else:
            self.send(404, {"title": "Not Found", "status": 404})


class ClientTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), FakeApi)
        cls.url = f"http://127.0.0.1:{cls.server.server_address[1]}"
        threading.Thread(target=cls.server.serve_forever, daemon=True).start()

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()

    def setUp(self):
        FakeApi.batch_sizes = []
        FakeApi.api_key = None
        self.client = Client(self.url)

    def test_screen_sends_api_field_names(self):
        result = self.client.screen("John Doe", type="individual", year_of_birth=1970, country="US", min_score=90, max_results=3)
        self.assertEqual(result["potentialMatches"], 1)
        self.assertEqual(result["echo"], {"name": "John Doe", "type": "individual", "yearOfBirth": 1970, "country": "US", "minScore": 90, "maxResults": 3})

    def test_screen_leaves_out_unset_fields(self):
        self.assertEqual(self.client.screen("Emily Carter")["echo"], {"name": "Emily Carter"})

    def test_validation_error_is_raised_with_field_messages(self):
        with self.assertRaises(OfacMatchError) as raised:
            self.client.screen("")
        self.assertEqual(raised.exception.status, 400)
        self.assertEqual(raised.exception.errors, {"name": ["Name is required."]})
        self.assertIn("Name is required.", str(raised.exception))

    def test_batch_splits_large_lists_and_keeps_order(self):
        items = [{"name": "John Doe" if i % 2 else "Emily Carter", "reference": f"C-{i}"} for i in range(2500)]
        result = self.client.screen_batch(items)
        self.assertEqual(FakeApi.batch_sizes, [1000, 1000, 500])
        self.assertEqual([r["index"] for r in result["results"]], list(range(2500)))
        self.assertEqual(result["results"][1999]["reference"], "C-1999")
        self.assertEqual(result["summary"], {"screened": 2500, "review": 1250, "likelyDifferentPerson": 0, "noMatch": 1250, "errors": 0})

    def test_batch_statuses_and_python_style_keys(self):
        result = self.client.screen_batch([
            {"name": "John Doe", "year_of_birth": 1970, "reference": 1},
            {"name": "John Doe", "yearOfBirth": 1990},
            {"name": ""},
            {"name": "Emily Carter"},
        ])
        self.assertEqual([r["status"] for r in result["results"]], ["review", "likely_different_person", "error", "no_match"])
        self.assertEqual(result["results"][0]["reference"], "1")
        self.assertEqual(result["summary"]["errors"], 1)

    def test_batch_rejects_empty_and_non_mappings(self):
        with self.assertRaises(ValueError):
            self.client.screen_batch([])
        with self.assertRaises(TypeError):
            self.client.screen_batch(["John Doe"])

    def test_lookups(self):
        self.assertEqual(self.client.health()["status"], "ok")
        self.assertEqual(self.client.list_info()["entries"], 3)
        self.assertEqual(self.client.entry(100)["name"], "DOE, John")
        self.assertEqual(self.client.compare("Jon Doe", "DOE, John", companies=True)["echo"], {"name": "Jon Doe", "otherName": "DOE, John", "companies": True})
        with self.assertRaises(OfacMatchError) as raised:
            self.client.entry(999)
        self.assertEqual(raised.exception.status, 404)
        self.assertIn("No SDN entry 999", str(raised.exception))

    def test_api_key(self):
        FakeApi.api_key = "secret"
        with self.assertRaises(OfacMatchError) as raised:
            self.client.list_info()
        self.assertEqual(raised.exception.status, 401)
        self.assertEqual(Client(self.url, api_key="secret").list_info()["entries"], 3)

    def test_unreachable_server(self):
        with self.assertRaises(OfacMatchError) as raised:
            Client("http://127.0.0.1:1", timeout=2).health()
        self.assertIsNone(raised.exception.status)

    def test_base_url_must_be_http(self):
        with self.assertRaises(ValueError):
            Client("localhost:8080")

    def test_cli_csv(self):
        with tempfile.TemporaryDirectory() as folder:
            source = os.path.join(folder, "in.csv")
            target = os.path.join(folder, "out.csv")
            with open(source, "w", newline="", encoding="utf-8") as f:
                f.write("Customer ID;First name;Last name;Date of birth;Country\n")
                f.write("1001;John;Doe;15 Mar 1970;Freedonia\n")
                f.write("1002;Emily;Carter;1985;United States\n")
                f.write("1003;;;;\n")
                f.write("=CMD();John;Doe;1990;\n")

            out = io.StringIO()
            with redirect_stdout(out):
                code = main(["--url", self.url, "csv", source, target])
            self.assertEqual(code, 0, out.getvalue())
            self.assertIn("Screened 4", out.getvalue())

            with open(target, newline="", encoding="utf-8-sig") as f:
                rows = list(csv.DictReader(f))
            self.assertEqual([r["result"] for r in rows], ["review", "no_match", "error: Name is required.", "likely_different_person"])
            self.assertEqual(rows[0]["reference"], "1001")
            self.assertEqual(rows[0]["best_listed_name"], "DOE, John")
            self.assertEqual(rows[2]["reference"], "1003")
            self.assertEqual(rows[3]["reference"], "'=CMD()")
            self.assertIn("reviewer_decision", rows[0])

    def test_cli_needs_a_name_column(self):
        with tempfile.TemporaryDirectory() as folder:
            source = os.path.join(folder, "in.csv")
            with open(source, "w", encoding="utf-8") as f:
                f.write("Customer ID,Phone\n1,555\n")
            err = io.StringIO()
            from contextlib import redirect_stderr
            with redirect_stderr(err):
                code = main(["--url", self.url, "csv", source, os.path.join(folder, "out.csv")])
            self.assertEqual(code, 2)
            self.assertIn("no Name column", err.getvalue())


if __name__ == "__main__":
    unittest.main()
