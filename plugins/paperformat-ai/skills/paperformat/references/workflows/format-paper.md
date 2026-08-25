# Format a paper

Run the check workflow first. Never plan from the user's description alone.

## Build a proposal

Read the confirmed rules, classifications, layout analysis, candidate scopes,
and rendered source/reference pages. Produce an
`agent-plan-proposal.schema.json` document.

For each candidate scope, choose:

- `apply` with low risk for exact, auto-fixable, content-neutral character or
  paragraph formatting such as font, size, alignment, spacing, or indentation;
- `preserve` when current formatting or structure must remain;
- `reportOnly` when intent or support is insufficient.

`preserve` and `reportOnly` are Advisory decisions, not high-risk executable
work. Do not request user approval for them. Do not raise a Safe local format
change to Review merely because other elements remain advisory.

Include dependencies and rollback strategies for ordered work. Include only
typed layout operations defined by the schema.

## Validate and approve

```bash
skills/paperformat/scripts/paperformat plan-validate \
  --source "/task/original.docx" \
  --report "/task/issue-report.json" \
  --rules "/task/format-spec.json" \
  --proposal "/task/agent-proposal.json" \
  --output "/task/repair-plan.json"
```

Explain the resulting Advisory, Safe, Review, and Experimental sets. Apply Safe
directives directly. Ask the user to approve only exact executable Review
directive and operation IDs. Do not treat the original formatting request as
approval for structural changes.

## Apply to a copy

```bash
skills/paperformat/scripts/paperformat apply \
  --input "/task/original.docx" \
  --rules "/task/format-spec.json" \
  --report "/task/issue-report.json" \
  --plan "/task/repair-plan.json" \
  --approve "review-id-1,review-id-2" \
  --output-dir "/task/attempt-01"
```

Add `--confirm-page-changes` only after explicit page-change approval. Never use
`--approve-all-review` unless the user has reviewed the exact current list.

Continue with rendered review and final validation. If a gate fails, create
`attempt-02` from `original.docx`; never mutate `attempt-01` in place.

## Isolate Experimental work

Ordinary `apply` must never execute an Experimental item. When the user wants
to retain a concrete high-risk strategy for investigation, initialize a new
diagnostic attempt using exact IDs from the validated plan:

```bash
skills/paperformat/scripts/paperformat attempt-init \
  --input "/task/original.docx" \
  --plan "/task/repair-plan.json" \
  --attempt-id "experimental-01" \
  --select-experimental "experimental-id-1" \
  --output-dir "/task/experimental-01"
```

The command creates separate `original.docx` and `candidate.docx` copies,
`experimental-attempt.json`, the validated plan, and `FINAL_STATUS.md`. It
does not execute a mutation. `readyForUse` remains false, and ordinary export
rejects the directory. Do not edit `candidate.docx` with an ad-hoc OOXML tool;
a future typed Experimental executor must supply its own operation evidence
before normal validation gates can even be considered.
