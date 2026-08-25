# PaperFormat protocol versions

The JSON Schemas under `plugins/paperformat-ai/schemas/` are authoritative. A
consumer must validate untrusted Agent input, reject unknown major versions,
preserve unknown future artifacts rather than mutating from them, and never
infer readiness from a command exit code alone.

## Current release line

| Contract | Schema | Version |
|---|---|---:|
| CLI envelope | `cli-result.schema.json` | 1.0 |
| Document inspection | `document-inspection.schema.json` | 1.0 |
| Rule package | `rule-package.schema.json` | 1.0 |
| Check report | `check-report.schema.json` | 1.0 |
| Agent plan proposal | `agent-plan-proposal.schema.json` | 2.0 |
| Validated RepairPlan | `repair-plan.schema.json` | 2.0 |
| Apply manifest | `apply-manifest.schema.json` | 1.0 |
| Experimental attempt | `experimental-attempt.schema.json` | 1.0 |
| Layout analysis / change log | `layout-analysis.schema.json`, `layout-change-log.schema.json` | 1.0 |
| Render / page comparison | `render-manifest.schema.json`, `page-comparison.schema.json` | 1.0 |
| Agent visual submission | `agent-visual-review-submission.schema.json` | 1.0 |
| Validated visual review | `validated-visual-review.schema.json` | 1.0 |
| Integrity / final validation | `integrity-report.schema.json`, `validation-report.schema.json` | 1.0 |
| Export / workflow manifest | `export-manifest.schema.json`, `workflow-manifest.schema.json` | 1.0 |

`visual-review.schema.json` is the nested VisualReviewReport v1 shape and does
not carry a second `schemaVersion` property when embedded in another versioned
artifact.

## Compatibility policy

- Patch releases may clarify messages or add non-contract documentation but
  must keep existing required fields and enum meanings.
- A new optional field still requires a schema and consumer review because
  current contracts use `additionalProperties: false`.
- Removing or renaming a field, changing an enum meaning, or changing an
  operation's safety level requires a new major contract version.
- Agent proposals, approvals, reviews, and plans are always source/report/hash
  bound; schema validity alone never authorizes mutation.
- Legacy v1 issue-scoped plans are historical artifacts and cannot be executed
  by the Agent-Native v2 `apply` path.
