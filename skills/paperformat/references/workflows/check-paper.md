# Check a paper

## Create the task

Resolve absolute input and output paths. Require a `.docx` manuscript and
the target identity from `resolve-target.md`. Require exactly one format
source: an exact official `.docx` / `.dotx` template, a reviewed RulePackage,
or the built-in IEEE-like profile when that profile is intentionally selected.

```bash
skills/paperformat/scripts/paperformat run-workflow \
  --manuscript "/absolute/path/manuscript.docx" \
  --ieee \
  --workspace "/absolute/path/projects/task-id"
```

Prefer `--template "/absolute/path/official-template.docx"`. Use
`--rules "/absolute/path/reviewed-venue-rules.json"` when the exact official
requirements were encoded and reviewed. Never use `--ieee` as a generic
fallback for another venue.

## Inspect the evidence

Read at minimum:

- `inspection.json`;
- `format-spec.json`;
- `classifications.json`;
- `layout-analysis.json`;
- `issue-report.json` and `issue-report.html`;
- `plan-candidates.json`;
- `workflow.json` and `FINAL_STATUS.md`.

Do not enforce a rule whose status requires confirmation. Explain pending
classifications and skipped rules as advisories, separately from confirmed
failures. Their presence alone does not block ordinary Safe formatting.

For check-only work, stop without creating `formatted.docx`. Report issue
counts by severity and element, the main structural risks, and the task path.
