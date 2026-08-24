# Venue and format coverage

PaperFormat AI v0.7.1 is venue-neutral by source, not by pretending that one
hard-coded profile represents every publication.

## What coverage means

Every task first binds six identity fields:

1. journal or conference;
2. publisher or society;
3. year, volume, or edition;
4. track, article type, or proceedings series;
5. submission stage;
6. template or author-kit version.

It then uses the exact current official artifact. A remembered margin, a
publisher family name, a third-party template, or a prior-year paper is never
silently promoted to an authoritative rule.

## Supported routes

| Manuscript and target evidence | Workflow | Readiness claim |
|---|---|---|
| DOCX plus exact official DOCX/DOTX template | Extract typed rules, inspect, check, approval-gated deterministic repair, content and page validation | `ready` only after every DOCX gate passes |
| DOCX plus an exact hash-pinned bundled template asset | Verify registry identity and hash, derive rules from the binary at runtime, then use the same deterministic DOCX pipeline | Only for the exact recorded identity or explicit exact asset selection |
| DOCX plus explicit official prose/PDF rules | Codex drafts a source-traceable RulePackage, user reviews it, CLI runs with `--rules` | Limited to the reviewed encoded rules |
| LaTeX project plus exact official author kit | Preserve baseline, use immutable official class/style files, minimal reviewed migration, clean build, inventory and PDF review | `ready` only after build, diff, and visual gates pass |
| PDF without editable source | Visual audit only | Never presented as a formatted editable deliverable |
| Missing or conflicting authoritative target | Check-only or blocked | No guessed repair and no compliance claim |

## Routing breadth

The machine-readable catalog currently contains 40 routing families. They
cover major publisher and society ecosystems such as IEEE, ACM, Springer
Nature/LNCS, Elsevier/Cell Press, Nature Portfolio, Science/AAAS, Wiley,
Taylor & Francis, SAGE, OUP, CUP, APS, AIP, IOP, Optica, ACS, RSC, SIAM/AMS,
ASME/AIAA, PLOS/BMC, and major medical journals.

It also routes prominent conference ecosystems including NeurIPS, ICML, ICLR,
ACL-family venues, CVPR/ICCV/WACV, ECCV, AAAI/IJCAI/ECAI, KDD/SIGIR/The Web
Conference, USENIX, systems and networking, databases, software engineering,
programming languages, security, HCI/graphics, and robotics.

The catalog stores aliases, supported source types, and source-discovery
instructions. It intentionally stores no margins, fonts, page limits, column
counts, or other venue rules. Those values change and must come from the exact
official target artifact for the task.

## Bundled template assets

`skills/paperformat/assets/templates/index.json` defines the governance schema
for any future redistributable template. The public registry is currently
empty, and the repository tracks no DOCX, DOTX, PDF, archive, manuscript, or
template binary. The resolver still rejects partial identity queries and would
verify every declared asset hash.

Official top-venue artifacts are acquired per task. They may be added publicly
only as versioned entries with recorded provenance, explicit redistribution
permission, and real derivation/regression evidence.

## Deterministic DOCX scope

The current Word engine can inspect or compare supported page geometry,
columns, paragraph styles, fonts, sizes, emphasis, alignment, spacing, indents,
captions, table text, and structural risks. Mutations stay within typed
allow-lists. Page geometry, sections, columns, broad style changes, tables,
equations, figures, fields, and complex objects retain Review or Experimental
boundaries as applicable.

The controlled IEEE-like fixture is generated at test time into an ignored
directory and remains the fully rehearsed end-to-end baseline. Other venues
become actionable through their exact template or a reviewed RulePackage;
recognizing a catalog alias is not certification.

Format-only automation does not enforce page budgets or compress research
content to meet them. A changed rendered page count is advisory evidence for
the required visual review, not a failure by itself. Low-confidence element
classifications likewise remain advisory unless a proposed structural or
content-sensitive operation actually depends on them.

## Explicit non-claims

PaperFormat does not claim that:

- every venue edition has a pre-certified static rule pack;
- publisher family defaults override a venue's current instructions;
- a successful build guarantees acceptance by a submission portal;
- PDF-only input can be safely converted into an editable source;
- complex Word or LaTeX layout can always be repaired automatically;
- research content, equations, references, figures, or table data may be
  rewritten as part of formatting.

The truthful completion report always names the exact target, sources, encoded
rules, operations, passed gates, and unresolved limitations.
