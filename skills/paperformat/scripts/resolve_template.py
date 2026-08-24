#!/usr/bin/env python3
"""Verify and resolve immutable template assets without fuzzy fallbacks."""

from __future__ import annotations

import argparse
from datetime import date
import hashlib
import json
from pathlib import Path
import re
import sys
import unicodedata
from urllib.parse import urlparse
import zipfile


ASSET_ROOT = (Path(__file__).resolve().parents[1] / "assets" / "templates").resolve()
REGISTRY = ASSET_ROOT / "index.json"
IDENTITY_ARGUMENTS = {
    "venue": "venue",
    "publisher": "publisherOrSociety",
    "year": "yearOrVolume",
    "track": "trackOrArticleType",
    "stage": "submissionStage",
    "template_version": "templateVersion",
}
ALLOWED_EXTENSIONS = {
    "docx": {".docx"},
    "dotx": {".dotx"},
    "latex-zip": {".zip"},
}
ALLOWED_PROVENANCE_KINDS = {
    "official-download",
    "user-supplied",
    "publisher-package",
    "project-generated",
}


def normalize(value: str) -> str:
    value = unicodedata.normalize("NFKC", value).casefold()
    return " ".join(value.split())


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_string(value: object, field: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{field} must be a non-empty string.")
    return value


def require_date(value: object, field: str) -> str:
    candidate = require_string(value, field)
    try:
        date.fromisoformat(candidate)
    except ValueError as error:
        raise ValueError(f"{field} must be an ISO date.") from error
    return candidate


def require_boolean(value: object, field: str) -> bool:
    if not isinstance(value, bool):
        raise ValueError(f"{field} must be a boolean.")
    return value


def validate_source_url(value: object, field: str) -> None:
    if value is None:
        return
    candidate = require_string(value, field)
    parsed = urlparse(candidate)
    if parsed.scheme not in {"http", "https"} or not parsed.netloc:
        raise ValueError(f"{field} must be an HTTP(S) URL or null.")


def resolve_asset_path(relative: str) -> Path:
    candidate = (ASSET_ROOT / relative).resolve()
    try:
        candidate.relative_to(ASSET_ROOT)
    except ValueError as error:
        raise ValueError(f"Asset path escapes the template root: {relative}") from error
    return candidate


def load_registry() -> dict:
    value = json.loads(REGISTRY.read_text(encoding="utf-8"))
    if value.get("schemaVersion") != "1.0":
        raise ValueError("Unsupported template registry schemaVersion.")
    require_date(value.get("updated"), "updated")
    require_string(value.get("policy"), "policy")
    templates = value.get("templates")
    if not isinstance(templates, list):
        raise ValueError("Template registry requires a templates array.")

    seen: set[str] = set()
    for index, template in enumerate(templates):
        prefix = f"templates[{index}]"
        if not isinstance(template, dict):
            raise ValueError(f"{prefix} must be an object.")
        asset_id = require_string(template.get("id"), f"{prefix}.id")
        if not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", asset_id):
            raise ValueError(f"Invalid template asset id: {asset_id}")
        if asset_id in seen:
            raise ValueError(f"Duplicate template asset id: {asset_id}")
        seen.add(asset_id)
        require_string(template.get("displayName"), f"{prefix}.displayName")
        require_string(template.get("notes"), f"{prefix}.notes")

        identity = template.get("identity")
        if not isinstance(identity, dict):
            raise ValueError(f"{prefix}.identity must be an object.")
        for field in IDENTITY_ARGUMENTS.values():
            require_string(identity.get(field), f"{prefix}.identity.{field}")

        source_format = require_string(template.get("format"), f"{prefix}.format")
        if source_format not in ALLOWED_EXTENSIONS:
            raise ValueError(f"Unsupported template format: {source_format}")
        if template.get("rulePolicy") != "derive-at-runtime":
            raise ValueError(f"{prefix}.rulePolicy must be derive-at-runtime.")

        artifact = template.get("artifact")
        if not isinstance(artifact, dict):
            raise ValueError(f"{prefix}.artifact must be an object.")
        relative = require_string(artifact.get("path"), f"{prefix}.artifact.path")
        expected_hash = require_string(
            artifact.get("sha256"),
            f"{prefix}.artifact.sha256",
        )
        if not re.fullmatch(r"[a-f0-9]{64}", expected_hash):
            raise ValueError(f"Invalid SHA-256 for {asset_id}.")
        asset_path = resolve_asset_path(relative)
        if not asset_path.is_file():
            raise ValueError(f"Missing template asset: {relative}")
        if asset_path.suffix.casefold() not in ALLOWED_EXTENSIONS[source_format]:
            raise ValueError(f"Template extension does not match format for {asset_id}.")
        if not zipfile.is_zipfile(asset_path):
            raise ValueError(f"Template asset is not a valid ZIP package: {relative}")
        if source_format in {"docx", "dotx"}:
            with zipfile.ZipFile(asset_path) as package:
                names = set(package.namelist())
            for required_part in ("[Content_Types].xml", "word/document.xml"):
                if required_part not in names:
                    raise ValueError(
                        f"Template asset is missing {required_part}: {relative}"
                    )
        actual_hash = sha256(asset_path)
        if actual_hash != expected_hash:
            raise ValueError(
                f"Template hash mismatch for {asset_id}: "
                f"expected {expected_hash}, got {actual_hash}."
            )

        provenance = template.get("provenance")
        if not isinstance(provenance, dict):
            raise ValueError(f"{prefix}.provenance must be an object.")
        if provenance.get("kind") not in ALLOWED_PROVENANCE_KINDS:
            raise ValueError(f"Invalid provenance kind for {asset_id}.")
        require_string(
            provenance.get("originalFileName"),
            f"{prefix}.provenance.originalFileName",
        )
        validate_source_url(
            provenance.get("sourceUrl"),
            f"{prefix}.provenance.sourceUrl",
        )
        require_date(
            provenance.get("receivedOrRetrieved"),
            f"{prefix}.provenance.receivedOrRetrieved",
        )

        governance = template.get("governance")
        if not isinstance(governance, dict):
            raise ValueError(f"{prefix}.governance must be an object.")
        if governance.get("status") not in {"validated", "superseded", "disabled"}:
            raise ValueError(f"Invalid governance status for {asset_id}.")
        if governance.get("selectionMode") not in {"exact-identity", "explicit-only"}:
            raise ValueError(f"Invalid selection mode for {asset_id}.")
        require_string(
            governance.get("distributionScope"),
            f"{prefix}.governance.distributionScope",
        )
        require_string(
            governance.get("licenseStatus"),
            f"{prefix}.governance.licenseStatus",
        )
        require_boolean(
            governance.get("requiresOnlineFreshnessCheck"),
            f"{prefix}.governance.requiresOnlineFreshnessCheck",
        )

        template["_artifactPath"] = str(asset_path)

    return value


def public_template(template: dict) -> dict:
    return {
        key: value
        for key, value in template.items()
        if not key.startswith("_")
    } | {"artifactPath": template["_artifactPath"]}


def print_result(payload: dict) -> None:
    print(json.dumps(payload, indent=2, ensure_ascii=False))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Verify or resolve a versioned PaperFormat template asset."
    )
    parser.add_argument("--verify", action="store_true")
    parser.add_argument("--list", action="store_true")
    parser.add_argument("--id", dest="asset_id")
    parser.add_argument("--venue")
    parser.add_argument("--publisher")
    parser.add_argument("--year")
    parser.add_argument("--track")
    parser.add_argument("--stage")
    parser.add_argument("--template-version")
    parser.add_argument("--format", choices=sorted(ALLOWED_EXTENSIONS))
    args = parser.parse_args(argv)

    try:
        registry = load_registry()
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print_result({"status": "error", "message": str(error)})
        return 2

    templates = registry["templates"]
    if args.verify:
        print_result({
            "schemaVersion": registry["schemaVersion"],
            "status": "verified",
            "registry": str(REGISTRY),
            "templateCount": len(templates),
            "templateIds": [template["id"] for template in templates],
        })
        return 0

    if args.list:
        print_result({
            "schemaVersion": registry["schemaVersion"],
            "status": "listed",
            "policy": registry["policy"],
            "templates": [public_template(template) for template in templates],
        })
        return 0

    if args.asset_id:
        matches = [template for template in templates if template["id"] == args.asset_id]
        if not matches:
            print_result({
                "status": "not_found",
                "assetId": args.asset_id,
                "nextAction": "List assets and provide an exact valid asset ID.",
            })
            return 3
        template = matches[0]
        if template["governance"]["status"] != "validated":
            print_result({
                "status": "unavailable",
                "template": public_template(template),
                "nextAction": "Use a current validated target artifact.",
            })
            return 3
        print_result({
            "schemaVersion": registry["schemaVersion"],
            "status": "resolved",
            "selection": "explicit-asset-id",
            "template": public_template(template),
            "warning": "Confirm that this exact asset is intended for the current venue, year, track, stage, and template version before formatting.",
        })
        return 0

    identity_values = {
        argument: getattr(args, argument)
        for argument in IDENTITY_ARGUMENTS
    }
    supplied = [name for name, value in identity_values.items() if value is not None]
    if supplied or args.format is not None:
        missing = [name for name, value in identity_values.items() if value is None]
        if args.format is None:
            missing.append("format")
        if missing:
            print_result({
                "status": "invalid_identity",
                "missing": missing,
                "nextAction": "Provide every target identity field; partial matching is forbidden.",
            })
            return 2

        matches = []
        for template in templates:
            identity = template["identity"]
            if template["format"] != args.format:
                continue
            if all(
                normalize(identity[field]) == normalize(identity_values[argument])
                for argument, field in IDENTITY_ARGUMENTS.items()
            ):
                matches.append(template)

        if not matches:
            print_result({
                "status": "needs_exact_template",
                "nextAction": "Use the user's exact official artifact, obtain the current official author kit, or remain check-only.",
            })
            return 3
        if len(matches) != 1:
            print_result({"status": "error", "message": "Identity matched multiple assets."})
            return 2
        template = matches[0]
        if template["governance"]["status"] != "validated":
            print_result({"status": "unavailable", "template": public_template(template)})
            return 3
        if template["governance"]["selectionMode"] != "exact-identity":
            print_result({
                "status": "needs_explicit_selection",
                "template": public_template(template),
                "nextAction": f"Confirm the target and rerun with --id {template['id']}.",
            })
            return 3
        print_result({
            "schemaVersion": registry["schemaVersion"],
            "status": "resolved",
            "selection": "exact-identity",
            "template": public_template(template),
        })
        return 0

    parser.error("use --verify, --list, --id, or provide the complete target identity")


if __name__ == "__main__":
    sys.exit(main())
