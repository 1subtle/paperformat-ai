# Convert a paper layout

Use this workflow for single-column to IEEE-like double-column conversion.

## Analyze first

```bash
skills/paperformat/scripts/paperformat layout-analyze \
  --input "/task/original.docx" \
  --rules "/task/format-spec.json" \
  --classifications "/task/classifications.json" \
  --output "/task/layout-analysis.json"
```

Confirm the semantic boundary between front matter and body. Verify that the
title, authors, affiliations, abstract, and keywords remain full width and the
first body heading starts the double-column section.

## Plan reviewed operations

Use `insertContinuousSectionBreak` when body text should continue on the same
page. Use `insertNextPageSectionBreak` only when the target intentionally starts
on a new page. Make `setSectionColumns` depend on the inserted break and use the
target rule's exact count and spacing.

All section and column operations are Review. Include rendered evidence and
obtain explicit approval.

Treat wide/merged tables, floating drawings, equations, fields, and other
cross-column objects as separate risks. Use `preserveFullWidthObject` only as
an Experimental, non-executable strategy unless a supported deterministic
operation exists.

After apply, verify section count, front-matter columns, body columns, body
start position, page count, blank regions, indents, title hierarchy, fields,
formulas, tables, and images.
