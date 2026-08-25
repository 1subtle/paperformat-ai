#!/usr/bin/env python3
"""Static contract tests for the canonical Agent-Native entry points."""

from __future__ import annotations

import json
from pathlib import Path
import subprocess
import unittest


ROOT = Path(__file__).resolve().parents[1]
PLUGIN_ROOT = ROOT / "plugins" / "paperformat-ai"
SKILL = PLUGIN_ROOT / "skills" / "paperformat" / "SKILL.md"


class AgentNativeStructureTests(unittest.TestCase):
    def test_canonical_skill_contains_every_required_workflow(self) -> None:
        skill = SKILL.read_text(encoding="utf-8")
        self.assertIn("name: paperformat", skill)
        self.assertLess(len(skill.encode("utf-8")), 8_000)
        for relative in (
            "references/workflow.md",
            "references/template-library.md",
            "references/workflows/check-paper.md",
            "references/workflows/resolve-target.md",
            "references/workflows/format-paper.md",
            "references/workflows/format-latex.md",
            "references/workflows/convert-layout.md",
            "references/workflows/repair-tables.md",
            "references/workflows/visual-review.md",
            "references/workflows/validate-output.md",
            "scripts/paperformat",
            "scripts/resolve_venue.py",
            "scripts/resolve_template.py",
        ):
            self.assertIn(relative, skill)
            self.assertTrue((SKILL.parent / relative).is_file(), relative)

        for guide in (
            "document-analysis.md",
            "safe-editing.md",
            "ieee-layout.md",
            "captions.md",
            "tables.md",
            "equations.md",
            "references.md",
        ):
            self.assertTrue(
                (SKILL.parent / "references" / "guidance" / guide).is_file(),
                guide,
            )

        for routed_resource in (
            "references/venue-coverage.md",
            "references/venue-catalog.json",
            "assets/templates/index.json",
        ):
            self.assertTrue(
                (SKILL.parent / routed_resource).is_file(),
                routed_resource,
            )

        self.assertFalse((SKILL.parent / "workflows").exists())
        self.assertFalse((SKILL.parent / "guidance").exists())

    def test_skill_and_repository_launchers_exist(self) -> None:
        self.assertTrue((ROOT / "examples").is_dir())
        launcher = PLUGIN_ROOT / "scripts" / "paperformat"
        self.assertTrue(launcher.is_file())
        self.assertTrue(launcher.stat().st_mode & 0o111)
        skill_launcher = (
            PLUGIN_ROOT
            / "skills"
            / "paperformat"
            / "scripts"
            / "paperformat"
        )
        self.assertTrue(skill_launcher.is_file())
        self.assertTrue(skill_launcher.stat().st_mode & 0o111)

        result = subprocess.run(
            [str(skill_launcher), "--help"],
            cwd=ROOT,
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("PaperFormat Master Agent-native CLI", result.stdout)

    def test_packaged_plugin_and_ieee_example_are_present(self) -> None:
        rehearsal = ROOT / "scripts" / "rehearse-agent-native-example.mjs"
        example = ROOT / "examples" / "ieee-agent-native" / "example.json"
        protocols = ROOT / "docs" / "PROTOCOL_VERSIONS.md"
        for executable in (rehearsal, PLUGIN_ROOT / "scripts" / "paperformat"):
            self.assertTrue(executable.is_file(), executable)
            self.assertTrue(executable.stat().st_mode & 0o111, executable)
        for document in (example, protocols):
            self.assertTrue(document.is_file(), document)

        configuration = json.loads(example.read_text(encoding="utf-8"))
        self.assertEqual("1.0", configuration["schemaVersion"])
        source = (example.parent / configuration["source"]).resolve()
        self.assertEqual(
            (
                ROOT
                / "tests"
                / "fixtures"
                / "generated"
                / "single-column-ieee-like.docx"
            ).resolve(),
            source,
        )
        self.assertEqual(1, configuration["expected"]["frontMatterColumnCount"])
        self.assertEqual(2, configuration["expected"]["bodyColumnCount"])
        manifest = json.loads(
            (PLUGIN_ROOT / ".codex-plugin" / "plugin.json").read_text(
                encoding="utf-8",
            ),
        )
        self.assertEqual("0.8.0", manifest["version"])

    def test_format_only_scope_is_explicit(self) -> None:
        equations = (
            PLUGIN_ROOT
            / "skills"
            / "paperformat"
            / "references"
            / "guidance"
            / "equations.md"
        ).read_text(encoding="utf-8")
        visual = (
            PLUGIN_ROOT
            / "skills"
            / "paperformat"
            / "references"
            / "workflows"
            / "visual-review.md"
        ).read_text(encoding="utf-8")
        target = (
            PLUGIN_ROOT
            / "skills"
            / "paperformat"
            / "references"
            / "workflows"
            / "resolve-target.md"
        ).read_text(encoding="utf-8")

        self.assertIn("Word UnicodeMath", equations)
        self.assertIn("Do not fail an equation solely", visual)
        self.assertIn("Do not fail because the manuscript has", visual)
        self.assertIn("Page limits are outside", target)

    def test_repo_marketplace_installs_only_the_runtime_plugin_subdirectory(
        self,
    ) -> None:
        manifest = json.loads(
            (PLUGIN_ROOT / ".codex-plugin" / "plugin.json").read_text(
                encoding="utf-8",
            ),
        )
        marketplace = json.loads(
            (ROOT / ".agents" / "plugins" / "marketplace.json").read_text(
                encoding="utf-8",
            ),
        )
        self.assertEqual("paperformat-ai", marketplace["name"])
        self.assertEqual(1, len(marketplace["plugins"]))
        entry = marketplace["plugins"][0]
        self.assertEqual(manifest["name"], entry["name"])
        self.assertEqual("local", entry["source"]["source"])
        self.assertEqual(
            "./plugins/paperformat-ai",
            entry["source"]["path"],
        )
        self.assertEqual("AVAILABLE", entry["policy"]["installation"])
        self.assertEqual("ON_INSTALL", entry["policy"]["authentication"])

        for development_only in (
            "tests",
            "tools",
            "docs",
            "examples",
            "PaperFormat.sln",
            "AGENTS.md",
        ):
            self.assertFalse(PLUGIN_ROOT.joinpath(development_only).exists())

    def test_every_repository_schema_is_versioned_and_uniquely_identified(
        self,
    ) -> None:
        identifiers: set[str] = set()
        schemas = sorted((PLUGIN_ROOT / "schemas").glob("*.schema.json"))
        self.assertGreaterEqual(len(schemas), 20)
        for schema in schemas:
            value = json.loads(schema.read_text(encoding="utf-8"))
            self.assertEqual(
                "https://json-schema.org/draft/2020-12/schema",
                value.get("$schema"),
                schema,
            )
            identifier = value.get("$id")
            self.assertIsInstance(identifier, str, schema)
            self.assertNotIn(identifier, identifiers, schema)
            identifiers.add(identifier)

    def test_public_plugin_has_no_legacy_surfaces_or_private_template(self) -> None:
        for relative in (
            "web",
            "plugins/paperformat-ai/src/PaperFormat.Api",
            "plugins/paperformat-ai/src/PaperFormat.Application",
            "plugins/paperformat-ai/skills/paperformat-docx",
            "adapters",
            "IEEE format.docx",
        ):
            self.assertFalse((ROOT / relative).exists(), relative)

        registry_path = (
            PLUGIN_ROOT
            / "skills"
            / "paperformat"
            / "assets"
            / "templates"
            / "index.json"
        )
        registry = json.loads(registry_path.read_text(encoding="utf-8"))
        self.assertEqual([], registry["templates"])
        registry_text = json.dumps(registry)
        self.assertNotIn("CAC 2026", registry_text)
        self.assertNotIn("user-supplied", registry_text)
        self.assertNotIn("private repository", registry_text)

        workflow = (ROOT / ".github" / "workflows" / "ci.yml").read_text(
            encoding="utf-8",
        )
        self.assertNotIn("actions/upload-artifact", workflow)

    def test_public_git_tracks_no_manuscript_or_template_binary(self) -> None:
        result = subprocess.run(
            ["git", "ls-files", "-z"],
            cwd=ROOT,
            check=False,
            capture_output=True,
        )
        self.assertEqual(0, result.returncode, result.stderr.decode())
        tracked = [
            Path(value.decode("utf-8"))
            for value in result.stdout.split(b"\0")
            if value
        ]
        forbidden = {
            ".doc",
            ".docm",
            ".docx",
            ".dot",
            ".dotm",
            ".dotx",
            ".pdf",
            ".rar",
            ".7z",
            ".zip",
        }
        blocked = [path for path in tracked if path.suffix.casefold() in forbidden]
        self.assertEqual([], blocked)

        ignored = subprocess.run(
            ["git", "check-ignore", "-q", "tests/fixtures/generated/example.docx"],
            cwd=ROOT,
            check=False,
        )
        self.assertEqual(0, ignored.returncode)


if __name__ == "__main__":
    unittest.main()
