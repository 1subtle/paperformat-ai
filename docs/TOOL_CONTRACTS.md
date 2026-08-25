# PaperFormat Tool Contracts

The authoritative machine contracts are versioned JSON Schemas under
`plugins/paperformat-ai/schemas/`. CLI stdout uses `cli-result.schema.json`;
generated artifacts use their dedicated schemas.

## Commands

| Command | Primary artifact | Success meaning |
|---|---|---|
| `inspect` | `document-inspection.schema.json` | package and structure inspected |
| `derive-template` | `rule-package.schema.json` | rules derived, notices preserved |
| `classify` | classification JSON | elements classified or left pending |
| `layout-analyze` | `layout-analysis.schema.json` | layout risks and boundary analyzed |
| `check` | `check-report.schema.json` | deterministic check completed |
| `plan-validate` | `repair-plan.schema.json` | proposal normalized, bound to the exact source SHA-256, and policy checked |
| `apply` | `apply-manifest.schema.json` | candidate and deterministic gates produced |
| `attempt-init` | `experimental-attempt.schema.json` | exact Experimental IDs isolated in a diagnostic-only attempt; no mutation or ready output |
| `render` | `render-manifest.schema.json` | page evidence generated |
| `compare-pages` | `page-comparison.schema.json` | deterministic page comparison completed |
| `visual-review` | `validated-visual-review.schema.json` | review bound to exact evidence |
| `validate-integrity` | `validation-report.schema.json` | candidate content compared |
| `validate-output` | `validation-report.schema.json` | all final gates evaluated |
| `export` | `export-manifest.schema.json` | ready or remaining gates recorded |
| `run-workflow` | `workflow-manifest.schema.json` | portable check task initialized |

`run-workflow` requires exactly one target source: `--template` for an exact
official DOCX/DOTX template, `--rules` for an already reviewed RulePackage, or
`--ieee` for the controlled built-in IEEE-like profile. The command never
merges those sources implicitly.

Exit codes are stable: `0` completed, `2` invalid input, `3` confirmation
required, `4` validation failed, `5` local tool unavailable, and `10`
unexpected failure.

Every Agent-originated proposal and review is untrusted input. IDs, hashes,
counts, operation shapes, dependencies, approvals, and evidence bindings are
revalidated by PaperFormat.
