# Format a LaTeX manuscript conservatively

Use this workflow for a LaTeX source tree. The deterministic DOCX engine does
not edit LaTeX.

## Preserve a baseline

1. Resolve the exact official venue package using
   [resolve-target.md](resolve-target.md).
2. Identify the real entry point, build command, bibliography tool, class,
   style files, and generated-file policy from the project documentation.
3. Work in a new copy or reviewable version-control branch. Never modify the
   only source tree or overwrite the baseline PDF.
4. Compile the unchanged baseline when the toolchain is available. Save the
   PDF, log, page count, warnings, and inventories of labels, citations,
   figures, tables, equations, bibliography entries, and included files.
5. Treat official `.cls`, `.sty`, `.bst`, and template support files as
   immutable inputs. Do not patch them to make a manuscript appear compliant.

If the baseline does not compile, separate pre-existing failures from changes
and do not claim a verified migration.

## Plan the migration

Prefer using the official template scaffold and mapping the manuscript into
its documented slots. Propose a reviewable plan before editing.

Safe candidates are narrow, source-backed changes such as the required
document class or documented class options when they do not alter research
content.

Require explicit review for:

- moving sections or front matter;
- anonymous-review metadata changes;
- bibliography style or citation-command migration;
- float placement, wide objects, and page-breaking changes;
- line numbering, supplemental-material wiring, or camera-ready metadata;
- package replacement or macro remapping with document-wide effects.

Do not rewrite prose, equations, captions, table data, bibliography entries,
figure files, labels, or citation targets unless the user separately asks for
content editing.

## Apply minimal changes

- Keep official template files unchanged and versioned with their source.
- Change the smallest set of preamble, wrapper, and structural source files.
- Preserve command arguments that contain research content.
- Do not delete apparently unused packages or macros unless the target build
  requires it and the removal is independently verified.
- Do not hide errors with negative spacing, forced page breaks, font scaling,
  compressed figures, or edits to publisher style files.

## Verify

Rebuild from a clean generated-file state using the project's documented
command. Compare against the baseline and require:

- successful compilation with no new errors;
- no new undefined references or citations;
- unchanged labels, citation keys, bibliography entries, figure files, table
  data, equation content, and included research sections unless approved;
- correct anonymity, class options, bibliography style, and
  required front matter for the exact stage;
- inspection of every changed or layout-sensitive PDF page for clipping,
  overlap, missing objects, bad float order, blank pages, and unexplained
  pagination.

Report `ready` only when the exact official target is recorded, the clean build
passes, approved diffs are content-safe, and visual review passes. Otherwise
report `needs-review` or `blocked` and preserve the diagnostic build.

Do not compress, reject, or otherwise alter a LaTeX manuscript to meet a page
limit. Total page count is outside this plugin's format-only compliance scope.
