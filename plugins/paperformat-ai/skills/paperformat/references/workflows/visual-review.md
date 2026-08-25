# Perform visual review

## Render and compare

```bash
skills/paperformat/scripts/paperformat render --input "/task/attempt-01/original.docx" \
  --output-dir "/task/attempt-01/before-pages"
skills/paperformat/scripts/paperformat render --input "/task/attempt-01/formatted.docx" \
  --output-dir "/task/attempt-01/after-pages"
skills/paperformat/scripts/paperformat compare-pages \
  --before "/task/attempt-01/before-pages" \
  --after "/task/attempt-01/after-pages" \
  --output "/task/attempt-01/page-comparison.json"
```

Inspect every numbered page. Check title and author blocks, heading hierarchy,
indents, columns, captions, tables, Algorithm borders, equations, figures,
references, clipping, overlap, and blank regions introduced by the operation.

Judge formatting regressions and explicit target-format rules, not editorial
or submission-budget compliance. Do not fail because the manuscript has a
particular total page count. A before/after page-count change is evidence to
inspect for clipping or an introduced blank page, not a failure by itself.

Do not fail an equation solely because the rendered text contains `#(n)`.
Word UnicodeMath uses `#` as a native equation-number separator in linear
input, and non-Word renderers may expose it literally. Block only when the
equation is clipped, overlaps content, is lost or duplicated, or is confirmed
to render incorrectly in Microsoft Word.

Pre-existing editorial issues that were not introduced by the operation may
be recorded as Advisory findings, but they do not fail a format-only visual
review unless the user explicitly asked the formatter to correct them.

## Submit bound findings

Create a document conforming to
`schemas/agent-visual-review-submission.schema.json`. Copy `planId` and
`operationId` from `apply-manifest.json`, and use actual page counts from the
render manifests. Use `needsReview` only when an actual output-format defect
remains uncertain. Never use `passed` with a high or blocked formatting
finding.

```bash
skills/paperformat/scripts/paperformat visual-review \
  --apply-manifest "/task/attempt-01/apply-manifest.json" \
  --before-render "/task/attempt-01/before-pages" \
  --after-render "/task/attempt-01/after-pages" \
  --comparison "/task/attempt-01/page-comparison.json" \
  --submission "/task/attempt-01/agent-visual-review.json" \
  --output "/task/attempt-01/validated-visual-review.json"
```

A stale plan, operation, file hash, or page count must be rejected.
