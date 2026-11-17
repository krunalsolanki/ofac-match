"""Python client for the self-hosted OfacMatch REST API.

Run the API on your own server (``docker run -p 8080:8080 ghcr.io/krunalsolanki/ofacmatch-api``),
then screen names from Python. Names go only to that server, never anywhere else.

Potential matches only: a score measures how alike two names are written, not whether they are
the same party. A person must review every potential match. Checks the OFAC SDN list only.
"""

from .client import Client, OfacMatchError

__all__ = ["Client", "OfacMatchError"]
__version__ = "0.1.0"
