---
name: paperformat
description: Drive deterministic checking, formatting, and validation of academic manuscripts against an exact journal or conference template. Use for venue-targeted DOCX/DOTX formatting, conservative LaTeX template migration, format-only audits, and proof that content was preserved. Do not use to rewrite research content or infer venue rules from a publisher family alone.
---

# PaperFormat AI

PaperFormat is a Codex-driven deterministic formatting and validation engine.
Codex resolves intent and semantic structure; PaperFormat performs every DOCX
read, mutation, render, integrity check, and export through typed contracts.
The installed Skill and its deterministic CLI are the product; no Web service,
API server, model credential, or direct OOXML editing is part of the workflow.

## Load the workflow contract

Read [references/workflow.md](references/workflow.md) for every task. It defines
the state machine, source precedence, execution levels, validation gates,
failure handling, and completion contract. Do not improvise an alternate
sequence.

## Resolve the exact target

Before checking or editing, read
[references/workflows/resolve-target.md](references/workflows/resolve-target.md).
Freeze the venue, publisher or society, year or volume, track or article type,
submission stage, and template version.

Use [references/template-library.md](references/template-library.md) when a
bundled template might apply. A bundled asset is eligible only through an
exact identity match or explicit user selection; family-level matches never
become formatting rules. Use `scripts/resolve_venue.py` for discovery routing
and `scripts/resolve_template.py` for the versioned local asset registry.

## Route only to the needed task references

- DOCX/DOTX check-only:
  [references/workflows/check-paper.md](references/workflows/check-paper.md).
- DOCX/DOTX deterministic formatting:
  [references/workflows/format-paper.md](references/workflows/format-paper.md).
- LaTeX author-kit migration:
  [references/workflows/format-latex.md](references/workflows/format-latex.md).
- Reviewed single-to-double-column conversion:
  [references/workflows/convert-layout.md](references/workflows/convert-layout.md).
- Table or Algorithm findings:
  [references/workflows/repair-tables.md](references/workflows/repair-tables.md).
- Final rendered review and export:
  [references/workflows/visual-review.md](references/workflows/visual-review.md)
  and
  [references/workflows/validate-output.md](references/workflows/validate-output.md).

Load a topic guide only when that topic is present:

- [document analysis](references/guidance/document-analysis.md);
- [safe editing](references/guidance/safe-editing.md);
- [captions](references/guidance/captions.md);
- [tables](references/guidance/tables.md);
- [equations](references/guidance/equations.md);
- [references](references/guidance/references.md);
- the controlled [IEEE layout baseline](references/guidance/ieee-layout.md).

## Use the Skill-owned command entry

Run the absolute `<plugin-root>/skills/paperformat/scripts/paperformat` path. In
a repository checkout, `<plugin-root>` is `plugins/paperformat-ai`; in an
installed copy it is the versioned plugin cache directory. The wrapper locates
the deterministic engine inside that boundary. Do not substitute ad-hoc
ZIP/XML edits, `python-docx`, LibreOffice automation, or direct file replacement
when the engine is unavailable.

## Preserve the non-negotiable boundary

- Never overwrite the source manuscript or target artifact.
- Never change research prose, equations, figure data, table data, citations,
  references, fields, media, or identifiers as part of format-only work.
- Use only locations and operations emitted by PaperFormat contracts.
- Safe means bounded content-neutral formatting; it still requires all gates.
- Advisory observations neither execute nor block unrelated Safe formatting.
- Review requires approval of exact structural operation IDs.
- Experimental work remains isolated and cannot use ordinary `apply`.
- Do not enforce page budgets or treat Word UnicodeMath `#(n)` as a defect by
  itself; use the detailed task references for the actual blocking evidence.

## Deliver only verified output

A generated DOCX is a candidate, not a deliverable. Report it as ready only
when the hash-bound post-check, reopen/package validation, content integrity,
page comparison, full visual review, final validation, and export all pass,
and `export-manifest.json` has `status: ready` with no remaining gates.
