#!/usr/bin/env python3
"""Tests for the privacy-safe Skill template asset registry."""

from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[1]
PLUGIN_ROOT = ROOT / "plugins" / "paperformat-ai"
ASSET_ROOT = PLUGIN_ROOT / "skills" / "paperformat" / "assets" / "templates"
REGISTRY = ASSET_ROOT / "index.json"
RESOLVER = (
    PLUGIN_ROOT / "skills" / "paperformat" / "scripts" / "resolve_template.py"
)


def load_resolver_module():
    spec = importlib.util.spec_from_file_location(
        "paperformat_template_resolver_test",
        RESOLVER,
    )
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load the template resolver module.")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def write_minimal_docx(path: Path) -> None:
    with zipfile.ZipFile(path, "w") as package:
        package.writestr(
            "[Content_Types].xml",
            """<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types" />
""",
        )
        package.writestr(
            "word/document.xml",
            """<?xml version="1.0" encoding="utf-8"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body><w:p /></w:body>
</w:document>
""",
        )


def temporary_registry(artifact_hash: str) -> dict:
    return {
        "schemaVersion": "1.0",
        "updated": "2026-08-24",
        "policy": "Temporary resolver test data generated at runtime.",
        "templates": [
            {
                "id": "runtime-test-template",
                "displayName": "Runtime test template",
                "identity": {
                    "venue": "Runtime test venue",
                    "publisherOrSociety": "PaperFormat AI",
                    "yearOrVolume": "1",
                    "trackOrArticleType": "regression testing",
                    "submissionStage": "development only",
                    "templateVersion": "runtime-v1",
                },
                "format": "docx",
                "artifact": {
                    "path": "target/template.docx",
                    "sha256": artifact_hash,
                },
                "provenance": {
                    "kind": "project-generated",
                    "originalFileName": "runtime-template.docx",
                    "sourceUrl": None,
                    "receivedOrRetrieved": "2026-08-24",
                },
                "governance": {
                    "status": "validated",
                    "selectionMode": "explicit-only",
                    "distributionScope": "temporary test directory only",
                    "licenseStatus": "generated during the test",
                    "requiresOnlineFreshnessCheck": False,
                },
                "rulePolicy": "derive-at-runtime",
                "notes": "Created and deleted inside the test process.",
            }
        ],
    }


class TemplateAssetTests(unittest.TestCase):
    def test_public_registry_tracks_no_template_binary(self) -> None:
        registry = json.loads(REGISTRY.read_text(encoding="utf-8"))
        self.assertEqual("1.0", registry["schemaVersion"])
        self.assertEqual([], registry["templates"])
        tracked_artifacts = sorted(
            path
            for path in ASSET_ROOT.rglob("*")
            if path.is_file() and path != REGISTRY
        )
        self.assertEqual([], tracked_artifacts)

    def test_resolver_verifies_empty_public_registry(self) -> None:
        verified = subprocess.run(
            [sys.executable, str(RESOLVER), "--verify"],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(0, verified.returncode, verified.stderr)
        payload = json.loads(verified.stdout)
        self.assertEqual("verified", payload["status"])
        self.assertEqual(0, payload["templateCount"])
        self.assertEqual([], payload["templateIds"])

        listed = subprocess.run(
            [sys.executable, str(RESOLVER), "--list"],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(0, listed.returncode, listed.stderr)
        self.assertEqual([], json.loads(listed.stdout)["templates"])

        missing = subprocess.run(
            [
                sys.executable,
                str(RESOLVER),
                "--id",
                "not-bundled",
            ],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(3, missing.returncode)
        self.assertEqual("not_found", json.loads(missing.stdout)["status"])

    def test_partial_or_complete_identity_never_guesses_an_asset(self) -> None:
        partial = subprocess.run(
            [sys.executable, str(RESOLVER), "--venue", "IEEE"],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(2, partial.returncode)
        self.assertEqual("invalid_identity", json.loads(partial.stdout)["status"])

        complete = subprocess.run(
            [
                sys.executable,
                str(RESOLVER),
                "--venue",
                "Exact venue",
                "--publisher",
                "Exact publisher",
                "--year",
                "2026",
                "--track",
                "Main track",
                "--stage",
                "submission",
                "--template-version",
                "2026.1",
                "--format",
                "docx",
            ],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(3, complete.returncode)
        self.assertEqual(
            "needs_exact_template",
            json.loads(complete.stdout)["status"],
        )

    def test_temporary_registry_rejects_hash_drift_escape_and_bad_governance(
        self,
    ) -> None:
        resolver = load_resolver_module()

        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = Path(temporary)
            artifact = temporary_root / "target" / "template.docx"
            artifact.parent.mkdir(parents=True)
            write_minimal_docx(artifact)
            original_bytes = artifact.read_bytes()
            registry_path = temporary_root / "index.json"
            source_registry = temporary_registry(
                hashlib.sha256(original_bytes).hexdigest(),
            )

            def write_registry(value: dict) -> None:
                registry_path.write_text(
                    json.dumps(value),
                    encoding="utf-8",
                )

            resolver.ASSET_ROOT = temporary_root.resolve()
            resolver.REGISTRY = registry_path

            write_registry(source_registry)
            self.assertEqual(1, len(resolver.load_registry()["templates"]))

            artifact.write_bytes(original_bytes + b"hash-drift")
            with self.assertRaisesRegex(ValueError, "hash mismatch"):
                resolver.load_registry()
            artifact.write_bytes(original_bytes)

            escaped = json.loads(json.dumps(source_registry))
            escaped["templates"][0]["artifact"]["path"] = "../escape.docx"
            write_registry(escaped)
            with self.assertRaisesRegex(ValueError, "escapes"):
                resolver.load_registry()

            incomplete = json.loads(json.dumps(source_registry))
            del incomplete["templates"][0]["governance"]["licenseStatus"]
            write_registry(incomplete)
            with self.assertRaisesRegex(ValueError, "licenseStatus"):
                resolver.load_registry()


if __name__ == "__main__":
    unittest.main()
