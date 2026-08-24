# Validate and export an output

Run only after apply, render, compare-pages, and evidence-bound visual review.

```bash
skills/paperformat/scripts/paperformat validate-output \
  --input-dir "/task/attempt-01" \
  --comparison "/task/attempt-01/page-comparison.json" \
  --visual-review "/task/attempt-01/validated-visual-review.json" \
  --output "/task/attempt-01/validation-report.json"
```

Require all of the following: unchanged original hash, reopen success, valid
apply identity, passed post-check, passed content integrity, no blocking page
anomaly, and passed semantic visual review.

Pending or unclassified semantic elements are advisories. They do not block
validation unless they caused a confirmed format error or an approved
structural operation depended on them.

Export only after validation passes:

```bash
skills/paperformat/scripts/paperformat export \
  --input-dir "/task/attempt-01" \
  --output-dir "/absolute/path/exports/task-id"
```

Read `export-manifest.json`. Deliver `formatted.docx` as ready only when status
is `ready` and `remainingGates` is empty. Otherwise deliver diagnostic reports
and state the blocking reason.
