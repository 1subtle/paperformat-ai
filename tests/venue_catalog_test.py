#!/usr/bin/env python3
"""Tests for venue routing metadata and its fail-closed resolver."""

from __future__ import annotations

import json
from pathlib import Path
import re
import subprocess
import sys
import unittest
import unicodedata


ROOT = Path(__file__).resolve().parents[1]
CATALOG = (
    ROOT / "skills" / "paperformat" / "references" / "venue-catalog.json"
)
RESOLVER = ROOT / "skills" / "paperformat" / "scripts" / "resolve_venue.py"


def normalize(value: str) -> str:
    value = unicodedata.normalize("NFKD", value).casefold()
    return " ".join(re.findall(r"[a-z0-9]+", value))


class VenueCatalogTests(unittest.TestCase):
    def test_catalog_is_broad_routing_metadata_without_embedded_rules(
        self,
    ) -> None:
        catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
        self.assertEqual("1.0", catalog["schemaVersion"])
        self.assertGreaterEqual(len(catalog["families"]), 35)
        self.assertIn("not a formatting rule pack", catalog["scope"])

        aliases: dict[str, str] = {}
        for family in catalog["families"]:
            self.assertIn(family["sourceRoute"].split()[0], {"Use", "Resolve"})
            self.assertTrue(set(family["formats"]) <= {"docx", "latex"})
            for alias in [
                family["id"],
                family["displayName"],
                *family["aliases"],
            ]:
                token = normalize(alias)
                self.assertTrue(token)
                if token in aliases:
                    self.assertEqual(aliases[token], family["id"], alias)
                aliases[token] = family["id"]

        raw = CATALOG.read_text(encoding="utf-8")
        for forbidden in (
            '"marginTop"',
            '"fontSize"',
            '"columnCount"',
            '"pageLimit"',
        ):
            self.assertNotIn(forbidden, raw)

    def test_resolver_prefers_the_most_specific_route(self) -> None:
        result = subprocess.run(
            [sys.executable, str(RESOLVER), "ACM CCS 2027 main track"],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        payload = json.loads(result.stdout)
        self.assertEqual("resolved", payload["status"])
        self.assertEqual("security", payload["matches"][0]["id"])
        self.assertIn(
            "Verify the exact current official venue",
            payload["warning"],
        )

    def test_unknown_venue_requires_verification(self) -> None:
        result = subprocess.run(
            [sys.executable, str(RESOLVER), "Unlisted Symposium 2031"],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(3, result.returncode)
        payload = json.loads(result.stdout)
        self.assertEqual("needs_verification", payload["status"])


if __name__ == "__main__":
    unittest.main()
