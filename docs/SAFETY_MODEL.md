# PaperFormat Safety Model

## Invariants

1. Never overwrite the source.
2. Never expose a generic OOXML mutation interface.
3. Never allow unvalidated Agent output to execute.
4. Never let section-change approval waive unrelated integrity failures.
5. Never release a stale or visually unreviewed candidate.
6. Never record full manuscript body text in logs or machine reports.

## Trust boundaries

Manuscripts, templates, rendered content, model output, Agent output, and user
supplied JSON are untrusted. Typed contracts, policy validation, allow-listed
mutators, immutable hashes, and recomputed evidence establish trust.

## Levels

- Safe: deterministic and bounded; still validated.
- Review: deterministic but visually or structurally consequential; exact user
  approval required.
- Experimental: unsupported or high risk; ordinary apply forbidden and any
  selected IDs may only initialize a separate diagnostic attempt. The current
  initializer performs no mutation, sets `readyForUse=false`, and is rejected
  by ordinary export. A future typed executor must still pass every gate.

## Release gates

Require source hash, plan policy, approval, candidate reopen, baseline-aware
Open XML validation, post-check, integrity, deterministic page comparison,
semantic visual review, and final export identity. Any failure prevents ready
status.
