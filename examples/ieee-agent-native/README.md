# IEEE Agent-Native release example

This example uses the deterministic generated fixture at
`tests/fixtures/generated/single-column-ieee-like.docx`. It exercises the
complete checked baseline:

```text
run-workflow
  -> source-bound AgentPlanProposal v2
  -> plan-validate
  -> exact Review approval
  -> apply on a copy
  -> real LibreOffice render
  -> page comparison
  -> evidence-bound visual review
  -> validate-output
  -> ready export
```

Run it from the repository root with a new output directory:

```bash
./scripts/dotnet.sh run --project tools/PaperFormat.Fixtures -- \
  --output tests/fixtures/generated
node scripts/rehearse-agent-native-example.mjs \
  --output projects/ieee-release-example
```

The generated DOCX directory is ignored by Git; no fixture or manuscript
binary belongs in the public repository.

LibreOffice Writer and Poppler `pdftoppm` must be installed. The output
contains the complete resumable task, before/after PNG pages, the approved
candidate, validation evidence, a ready export, `rehearsal-summary.json`, and
`command-transcript.jsonl`.

The `passed` visual submission is valid only for this controlled regression
fixture, whose pages were previously inspected and approved. It proves the
protocol and release pipeline; it must never be reused as a substitute for an
Agent reviewing a user's manuscript.
