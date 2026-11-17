"""HTTP client for the OfacMatch REST API. Standard library only."""

from __future__ import annotations

import json
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Dict, Iterable, Iterator, List, Mapping, Optional

#: The API accepts at most this many items per batch request; larger lists are split automatically.
MAX_BATCH = 1000

_USER_AGENT = "ofacmatch-python/0.1.0"


class OfacMatchError(Exception):
    """The API refused a request or could not be reached.

    ``status`` is the HTTP status (None if the server could not be reached) and ``errors`` holds
    the per-field messages of a 400 response, for example ``{"name": ["Name is required."]}``.
    """

    def __init__(self, message: str, status: Optional[int] = None, errors: Optional[Dict[str, List[str]]] = None):
        super().__init__(message)
        self.status = status
        self.errors = errors or {}


class Client:
    """Talks to one OfacMatch API server.

    >>> client = Client("http://localhost:8080")
    >>> result = client.screen("Aymen Zawahri", year_of_birth=1951)
    >>> result["potentialMatches"]
    1

    Responses are returned as plain dictionaries, exactly as the API sends them
    (see the API guide for every field).
    """

    def __init__(self, base_url: str = "http://localhost:8080", api_key: Optional[str] = None, timeout: float = 60.0):
        if not base_url.startswith(("http://", "https://")):
            raise ValueError("base_url must start with http:// or https://")
        self.base_url = base_url.rstrip("/")
        self.api_key = api_key
        self.timeout = timeout

    # Screening -----------------------------------------------------------------------------

    def screen(
        self,
        name: str,
        *,
        type: Optional[str] = None,
        year_of_birth: Optional[int] = None,
        country: Optional[str] = None,
        min_score: Optional[float] = None,
        max_results: Optional[int] = None,
    ) -> Dict[str, Any]:
        """Screens one name. ``type`` is individual, entity, vessel or aircraft (default: all)."""
        body = _drop_none(
            name=name,
            type=type,
            yearOfBirth=year_of_birth,
            country=country,
            minScore=min_score,
            maxResults=max_results,
        )
        return self._request("POST", "/v1/screen", body)

    def screen_batch(
        self,
        items: Iterable[Mapping[str, Any]],
        *,
        min_score: Optional[float] = None,
        max_results: Optional[int] = None,
    ) -> Dict[str, Any]:
        """Screens a list of names, sending at most 1,000 per request.

        Each item is a mapping with ``name`` and optionally ``reference`` (your own ID, returned
        unchanged), ``type``, ``year_of_birth`` (or ``yearOfBirth``) and ``country``.

        Returns one combined response: ``results`` in the same order as ``items`` (each with
        ``index``, ``reference``, ``status`` and ``matches``) and a ``summary`` with the counts.
        ``status`` is review, likely_different_person, no_match or error.
        """
        results: List[Dict[str, Any]] = []
        last: Dict[str, Any] = {}
        offset = 0
        for chunk in _chunks((_item(i) for i in items), MAX_BATCH):
            body = _drop_none(items=chunk, minScore=min_score, maxResults=max_results)
            last = self._request("POST", "/v1/screen/batch", body)
            for result in last.get("results", []):
                result["index"] = result.get("index", 0) + offset
                results.append(result)
            offset += len(chunk)

        if offset == 0:
            raise ValueError("items is empty: give at least one name to screen")

        counts = {"review": 0, "likely_different_person": 0, "no_match": 0, "error": 0}
        for result in results:
            counts[result.get("status", "error")] = counts.get(result.get("status", "error"), 0) + 1

        return {
            "summary": {
                "screened": len(results),
                "review": counts["review"],
                "likelyDifferentPerson": counts["likely_different_person"],
                "noMatch": counts["no_match"],
                "errors": counts["error"],
            },
            "results": results,
            "minScore": last.get("minScore"),
            "list": last.get("list"),
            "notice": last.get("notice"),
        }

    def compare(self, name: str, other_name: str, *, companies: bool = False) -> Dict[str, Any]:
        """Explains how two names score against each other."""
        return self._request("POST", "/v1/compare", {"name": name, "otherName": other_name, "companies": companies})

    # Lookups -------------------------------------------------------------------------------

    def entry(self, entry_id: int) -> Dict[str, Any]:
        """The full SDN record behind a match (names, aliases, dates of birth, addresses, remarks)."""
        return self._request("GET", f"/v1/entries/{int(entry_id)}")

    def list_info(self) -> Dict[str, Any]:
        """Which copy of the SDN list the server is using."""
        return self._request("GET", "/v1/list")

    def health(self) -> Dict[str, Any]:
        """``{"status": "ok", ...}`` once the server has loaded the list. Raises OfacMatchError (503) before that."""
        return self._request("GET", "/health")

    # Transport -----------------------------------------------------------------------------

    def _request(self, method: str, path: str, body: Optional[Mapping[str, Any]] = None) -> Dict[str, Any]:
        data = None if body is None else json.dumps(body).encode("utf-8")
        request = urllib.request.Request(self.base_url + path, data=data, method=method)
        request.add_header("Accept", "application/json")
        request.add_header("User-Agent", _USER_AGENT)
        if data is not None:
            request.add_header("Content-Type", "application/json")
        if self.api_key:
            request.add_header("X-Api-Key", self.api_key)

        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                return json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as error:
            raise _api_error(error) from None
        except urllib.error.URLError as error:
            raise OfacMatchError(f"Could not reach the OfacMatch API at {self.base_url}: {error.reason}") from None


def _api_error(error: urllib.error.HTTPError) -> OfacMatchError:
    try:
        problem = json.loads(error.read().decode("utf-8"))
    except (ValueError, UnicodeDecodeError):
        problem = {}

    errors = problem.get("errors") or {}
    details = "; ".join(m for messages in errors.values() for m in messages)
    message = problem.get("detail") or problem.get("title") or error.reason or "Request failed"
    if details:
        message = f"{message}: {details}"
    return OfacMatchError(f"HTTP {error.code}: {message}", status=error.code, errors=errors)


def _item(item: Mapping[str, Any]) -> Dict[str, Any]:
    if not isinstance(item, Mapping):
        raise TypeError("each batch item must be a mapping such as {'name': 'John Doe'}")
    return _drop_none(
        name=item.get("name"),
        reference=None if item.get("reference") is None else str(item.get("reference")),
        type=item.get("type"),
        yearOfBirth=item.get("year_of_birth", item.get("yearOfBirth")),
        country=item.get("country"),
    )


def _chunks(items: Iterator[Dict[str, Any]], size: int) -> Iterator[List[Dict[str, Any]]]:
    chunk: List[Dict[str, Any]] = []
    for item in items:
        chunk.append(item)
        if len(chunk) == size:
            yield chunk
            chunk = []
    if chunk:
        yield chunk


def _drop_none(**values: Any) -> Dict[str, Any]:
    return {key: value for key, value in values.items() if value is not None}
