#!/usr/bin/env python3
"""Resolve a venue name to a source-discovery route without inventing rules."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys
import unicodedata


CATALOG = Path(__file__).resolve().parents[1] / "references" / "venue-catalog.json"


def normalize(value: str) -> str:
    decomposed = unicodedata.normalize("NFKD", value).casefold()
    return " ".join(re.findall(r"[a-z0-9]+", decomposed))


def load_catalog() -> dict:
    value = json.loads(CATALOG.read_text(encoding="utf-8"))
    if value.get("schemaVersion") != "1.0" or not isinstance(value.get("families"), list):
        raise ValueError("Unsupported or malformed venue catalog.")
    seen: dict[str, str] = {}
    for family in value["families"]:
        if not isinstance(family.get("id"), str) or not family["id"]:
            raise ValueError("Every venue family requires an id.")
        names = [family["id"], family.get("displayName", ""), *family.get("aliases", [])]
        for name in names:
            token = normalize(name)
            if not token:
                raise ValueError(f"Empty venue alias in {family['id']}.")
            owner = seen.get(token)
            if owner is not None and owner != family["id"]:
                raise ValueError(f"Duplicate venue alias {name!r}: {owner} and {family['id']}.")
            seen[token] = family["id"]
    return value


def alias_candidates(family: dict) -> list[str]:
    return [family["id"], family["displayName"], *family["aliases"]]


def contains_phrase(query: str, alias: str) -> bool:
    return f" {alias} " in f" {query} "


def resolve(query: str, catalog: dict) -> tuple[str, list[dict]]:
    normalized_query = normalize(query)
    matches: list[tuple[int, dict, str]] = []
    for family in catalog["families"]:
        for alias in alias_candidates(family):
            normalized_alias = normalize(alias)
            if normalized_alias == normalized_query or contains_phrase(normalized_query, normalized_alias):
                matches.append((len(normalized_alias), family, alias))

    if matches:
        longest = max(length for length, _, _ in matches)
        winners = [item for item in matches if item[0] == longest]
        family_ids = {family["id"] for _, family, _ in winners}
        if len(family_ids) == 1:
            _, family, alias = winners[0]
            return "resolved", [{"matchedAlias": alias, **family}]

    query_tokens = set(normalized_query.split())
    suggestions: list[tuple[int, dict, str]] = []
    for family in catalog["families"]:
        best_score = 0
        best_alias = ""
        for alias in alias_candidates(family):
            alias_tokens = set(normalize(alias).split())
            score = len(query_tokens & alias_tokens)
            if score > best_score:
                best_score = score
                best_alias = alias
        if best_score:
            suggestions.append((best_score, family, best_alias))
    suggestions.sort(key=lambda item: (-item[0], item[1]["displayName"]))
    return "needs_verification", [
        {"matchedAlias": alias, **family}
        for _, family, alias in suggestions[:5]
    ]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Resolve a journal or conference to an official-source search route."
    )
    parser.add_argument("query", nargs="*", help="Venue name, optionally with year and track")
    parser.add_argument("--list", action="store_true", help="List all routing families")
    args = parser.parse_args(argv)

    try:
        catalog = load_catalog()
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(json.dumps({"status": "error", "message": str(error)}))
        return 2

    if args.list:
        print(json.dumps({
            "schemaVersion": catalog["schemaVersion"],
            "scope": catalog["scope"],
            "families": catalog["families"],
        }, indent=2, ensure_ascii=False))
        return 0

    query = " ".join(args.query).strip()
    if not query:
        parser.error("provide a venue query or use --list")

    status, matches = resolve(query, catalog)
    result = {
        "schemaVersion": catalog["schemaVersion"],
        "query": query,
        "status": status,
        "matches": matches,
        "requiredIdentityFields": catalog["requiredIdentityFields"],
        "sourcePrecedence": catalog["sourcePrecedence"],
        "warning": "This resolves only a discovery route. Verify the exact current official venue, year, track, stage, and template before formatting.",
    }
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0 if status == "resolved" else 3


if __name__ == "__main__":
    sys.exit(main())
