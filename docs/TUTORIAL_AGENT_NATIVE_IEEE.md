# Agent-Native IEEE formatting tutorial

## 1. Ask the Agent

After installation, attach or provide an absolute path to the manuscript and
say, for example:

```text
请使用 $paperformat 将这篇论文调整为 IEEE 格式。
标题、作者、单位、摘要和关键词保持通栏，从第一节正文开始双栏。
保护公式、图片、引用字段和 Algorithm 三线表。
逐项说明 Review 操作，得到我批准后再执行。
修复后检查每一页，只有所有质量门通过才交付最终 DOCX。
```

The Agent must read `skills/paperformat/SKILL.md` and the routed workflow and
guidance files before it modifies anything.

## 2. Initialize an immutable task

```bash
paperformat run-workflow \
  --manuscript /absolute/path/paper.docx \
  --ieee \
  --workspace /absolute/path/projects/paper-ieee
```

Use `--template /absolute/path/template.docx` instead of `--ieee` when a Word
template is authoritative.

The task contains the immutable original, source hash, parsed model, rules,
classifications, layout analysis, check report, plan candidates, optional
before pages, and `FINAL_STATUS.md`.

## 3. Let the Agent plan, not mutate

The Agent reads the exact IDs in the task and submits an
`AgentPlanProposal` v2. It may choose only `apply`, `preserve`, or
`reportOnly` for emitted scopes and typed layout operations. PaperFormat then
normalizes the proposal:

```bash
paperformat plan-validate \
  --source /absolute/path/paper.docx \
  --report /absolute/path/projects/paper-ieee/issue-report.json \
  --rules /absolute/path/projects/paper-ieee/format-spec.json \
  --proposal /absolute/path/projects/paper-ieee/agent-plan-proposal.json \
  --output /absolute/path/projects/paper-ieee/repair-plan.json
```

Unknown, duplicated, stale, low-confidence, non-repairable, or prohibited
operations fail closed.

## 4. Approve Review operations exactly

Safe operations do not need a broad user waiver. Every Review directive or
layout operation needs its exact current ID:

```bash
paperformat apply \
  --input /absolute/path/paper.docx \
  --rules /absolute/path/projects/paper-ieee/format-spec.json \
  --report /absolute/path/projects/paper-ieee/issue-report.json \
  --plan /absolute/path/projects/paper-ieee/repair-plan.json \
  --approve layout-insert-body-section,layout-set-body-columns \
  --confirm-page-changes \
  --output-dir /absolute/path/projects/paper-ieee/attempt-01
```

PaperFormat writes a candidate copy and logs every applied or skipped change.
It never overwrites the source.

## 5. Render and inspect every relevant page

```bash
paperformat render \
  --input /absolute/path/paper.docx \
  --output-dir /absolute/path/projects/paper-ieee/before-pages

paperformat render \
  --input /absolute/path/projects/paper-ieee/attempt-01/formatted.docx \
  --output-dir /absolute/path/projects/paper-ieee/after-pages

paperformat compare-pages \
  --before /absolute/path/projects/paper-ieee/before-pages \
  --after /absolute/path/projects/paper-ieee/after-pages \
  --output /absolute/path/projects/paper-ieee/attempt-01/page-comparison.json
```

The Agent must inspect title hierarchy, body start, indentation, Algorithm and
three-line tables, equations, figures, captions, references, clipping,
overlap, unexpected whitespace, and page-count changes. Its review submission
must bind the exact plan, operation, page counts, and output evidence.

## 6. Validate and export

After `visual-review`, run:

```bash
paperformat validate-output \
  --input-dir /absolute/path/projects/paper-ieee/attempt-01 \
  --comparison /absolute/path/projects/paper-ieee/attempt-01/page-comparison.json \
  --visual-review /absolute/path/projects/paper-ieee/attempt-01/validated-visual-review.json \
  --output /absolute/path/projects/paper-ieee/attempt-01/validation-report.json

paperformat export \
  --input-dir /absolute/path/projects/paper-ieee/attempt-01 \
  --output-dir /absolute/path/exports/paper-ieee
```

Only `export-manifest.json` with `status: ready` and no remaining gates is a
PaperFormat-ready result.

## 7. Run the controlled release example

To rehearse the protocol without using a private manuscript:

```bash
./scripts/dotnet.sh run --project tools/PaperFormat.Fixtures -- \
  --output tests/fixtures/generated
node scripts/rehearse-agent-native-example.mjs \
  --output projects/ieee-release-example
```

This generated fixture has a previously reviewed baseline and is the only
case where the script creates a regression visual pass automatically. A user
manuscript always requires current Agent or human visual judgment. The fixture
directory is ignored by Git and must never be replaced with a user document.
