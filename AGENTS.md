# PaperFormat AI Development Instructions

## Agent-Native manuscript entry

Before processing any Word manuscript, read `skills/paperformat/SKILL.md`.
Do not directly modify DOCX files outside PaperFormat tools. Never overwrite a
source manuscript. A formatting task is not complete until every applicable
validation gate passes.

## 1. Project source of truth

Before starting a task, read the relevant parts of:

1. `README.md` for the public product contract;
2. `skills/paperformat/SKILL.md` for the canonical Codex entry point;
3. `skills/paperformat/references/workflow.md` for manuscript state and gates;
4. `docs/ARCHITECTURE.md` and `docs/SAFETY_MODEL.md` for system boundaries;
5. `schemas/` for machine-readable artifact contracts.

If prose and a schema disagree, fail closed and fix the inconsistency before
processing a manuscript.

## 2. Core product principles

PaperFormat AI is a deterministic Word format checking and repair system.

The required processing flow is:

1. Parse formatting requirements.
2. Parse the source DOCX.
3. Identify manuscript elements.
4. Compare actual formatting against target rules.
5. Apply only approved deterministic repairs.
6. Validate document integrity.
7. Generate reports and output files.

AI may:

- interpret natural-language formatting requirements;
- classify manuscript elements;
- propose structured formatting rules.

AI must not:

- directly rewrite DOCX files;
- modify manuscript body text;
- change formulas, references, captions, tables, or figures semantically;
- silently decide ambiguous formatting rules.

All actual DOCX modifications must be executed by deterministic code.

## 3. Content safety rules

Never overwrite an uploaded source document.

Formatting operations must not intentionally change:

- manuscript text;
- equations;
- table cell text;
- image files;
- hyperlinks;
- bookmarks;
- cross-reference fields;
- footnotes or endnotes;
- citation fields.

Every repair operation must record:

- target element;
- property changed;
- original value;
- new value;
- rule source;
- execution result.

If integrity validation fails, the output must not be marked as ready for use.

## 4. Technical defaults

Unless an approved architecture decision says otherwise:

- Document engine: .NET 8
- DOCX processing: Open XML SDK
- Core tests: xUnit
- Skill/package tests: Python `unittest`
- Development PDF rendering: LibreOffice
- Production rendering must remain replaceable through an abstraction

Do not introduce another programming language or major framework without
recording the decision in `docs/DECISIONS.md`.

## 5. Architecture rules

Keep the Word document engine independent from:

- HTTP, databases, and file-storage services;
- frontend code;
- model providers and credentials.

Prefer these logical layers:

- Domain
- Rule Engine
- DOCX Parser
- Document Classifier
- Check Engine
- Repair Engine
- Integrity Validator
- Report Generator
- Provider-neutral CLI
- Canonical Codex Skill

Do not add a Web application, model gateway, API-key flow, database, or server
dependency to the core workflow.

Formatting rules must use a provider-independent structured schema.

## 6. Development workflow

For every implementation task:

1. Restate the requested scope.
2. Inspect the relevant code and documentation.
3. Present a concise implementation plan.
4. Implement only the requested scope.
5. Add or update automated tests.
6. Run formatters, builds, and tests.
7. Report changed files, commands run, test results, and remaining limitations.

Do not claim completion when tests fail.

Do not replace tests with mocks when real DOCX fixture tests are feasible.

## 7. Testing requirements

Every new checker or repair operation must include:

- a valid fixture that should pass;
- an invalid fixture that should fail;
- expected issue output;
- regression tests;
- integrity checks when the document is modified.

Important tests must use real DOCX files, not only mocked document objects.
Those DOCX fixtures must be generated at test time into the ignored
`tests/fixtures/generated/` directory; never commit a fixture binary.

Test results must be deterministic.

For uploaded or generated DOCX files, tests must also prove that the original
file hash is unchanged and that repaired output passes integrity validation.

## 8. Git safety

`PaperFormat/` lives inside a parent worktree that may contain unrelated user
changes. Never stage the parent repository wholesale. Use path-scoped staging
for `PaperFormat/` files only and inspect the staged diff before every commit.

For a public release, do not commit manuscripts, credentials, local task
outputs, private machine paths, or third-party templates without explicit
redistribution permission. Keep bundled assets immutable and hash-pinned.
The default public release tracks no DOCX, DOTX, PDF, archive, generated
fixture, rendered page, or template binary. Adding one requires a separate
privacy and redistribution review plus a deliberate policy change.

## 9. Code quality

- Enable nullable reference types in C#.
- Use explicit domain types instead of unstructured dictionaries.
- Validate external input.
- Avoid controllers containing business logic.
- Use dependency injection.
- Add XML comments for public domain interfaces.
- Do not commit secrets.
- Do not log manuscript body text.
- Keep functions focused and testable.

## 10. Completion report

At the end of every task, report:

- summary;
- changed files;
- implementation decisions;
- tests executed;
- test results;
- acceptance criteria status;
- known limitations;
- recommended next task.
