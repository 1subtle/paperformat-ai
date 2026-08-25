# PaperFormat AI

[![CI](https://github.com/1subtle/paperformat-ai/actions/workflows/ci.yml/badge.svg)](https://github.com/1subtle/paperformat-ai/actions/workflows/ci.yml)

PaperFormat AI is a public Codex plugin for deterministic academic manuscript
formatting and validation. Codex understands the requested venue and document
structure; PaperFormat performs bounded DOCX mutation, integrity checks,
rendering, comparison, and export through typed code.

> Codex provides judgment. PaperFormat provides deterministic execution and proof.

中文一句话：安装插件后，把论文和目标刊会的准确模板交给 Codex，并让
`$paperformat` 只改排版、不改研究内容；只有完整性与逐页验证全部通过
才交付结果。

## Why this exists

Formatting a paper is not a safe “rewrite this Word file” prompt. DOCX contains
styles, inheritance, sections, fields, equations, images, references, and
package relationships that can be damaged by free-form editing. PaperFormat
turns Codex's decisions into source-bound, schema-valid operations and lets an
allow-listed engine execute only those operations on a copy.

The public repository is intentionally Skill-first:

- no frontend;
- no HTTP API, database, or model gateway;
- no API key;
- no direct model-generated OOXML;
- no copied official venue templates without redistribution permission.

## Install

Add the public repository as a Codex marketplace and install the plugin:

```bash
codex plugin marketplace add 1subtle/paperformat-ai --ref main
codex plugin add paperformat-ai@paperformat-ai
codex plugin list
```

Start a new Codex task after installation so the Skill is discovered. Invoke
`$paperformat` explicitly on the first task.

The marketplace installs only `plugins/paperformat-ai/`. Development tests,
fixture generators, examples, and maintainer documentation remain outside the
plugin package. Neither the repository nor the installed package contains a
manuscript, generated DOCX, official template binary, PDF, or archive.

Give another Codex this instruction:

```text
Install and enable the public GitHub plugin 1subtle/paperformat-ai. Start a new
task and use $paperformat with the exact venue, year, track/article type,
submission stage, and current official template. Change formatting only,
preserve all research content, and deliver only after every integrity and
page-review gate passes.
```

中文提示词：

```text
请安装并启用公开 GitHub 插件 1subtle/paperformat-ai。新建任务后使用
$paperformat，按照我提供的目标刊会、年份、track/文章类型、投稿阶段和
准确官方模板排版。只修改格式，不改正文、公式、图表数据、引用和参考
文献；完整性检查与逐页复核全部通过后再交付。
```

See [installation details](docs/INSTALLATION.md) for local development,
rendering dependencies, upgrades, and removal.

## Use

For a Word manuscript and its exact official template:

```bash
./plugins/paperformat-ai/skills/paperformat/scripts/paperformat run-workflow \
  --manuscript "/absolute/path/paper.docx" \
  --template "/absolute/path/exact-official-template.docx" \
  --workspace "/absolute/path/new-task"
```

For a reviewed RulePackage derived from explicit official requirements:

```bash
./plugins/paperformat-ai/skills/paperformat/scripts/paperformat run-workflow \
  --manuscript "/absolute/path/paper.docx" \
  --rules "/absolute/path/reviewed-rules.json" \
  --workspace "/absolute/path/new-task"
```

The source and target are never overwritten. `run-workflow` initializes the
evidence and may stop for a typed Codex proposal or exact structural approval.
A successful intermediate command is not a ready manuscript.

## Deterministic workflow

```text
resolve exact target identity and authoritative source
  -> copy and hash immutable inputs
  -> preflight, inspect, derive rules, classify, check, render baseline
  -> Codex creates a typed source-bound proposal
  -> policy validates operations and dependencies
  -> apply Safe operations; approve exact Review IDs only
  -> reopen, package-validate, post-check, and compare protected content
  -> render before/after and inspect every relevant page
  -> bind visual review to exact hashes and page evidence
  -> final validation
  -> export only when export-manifest.json says status: ready
```

Execution levels are deliberately narrow:

- **Safe** — deterministic, content-neutral character and paragraph formatting.
- **Advisory** — non-executable evidence; it does not block unrelated Safe work.
- **Review** — supported structural/page changes with visible impact; exact IDs
  require approval.
- **Experimental** — unsupported or ambiguous layout work; isolated diagnostics
  only, never ordinary export.

Page budgets are outside the format-only engine's scope. A changed page count
is evidence to inspect, not an automatic error. Word UnicodeMath `#(n)` is not
a defect by itself; it blocks only when there is evidence of loss, duplication,
bad numbering, clipping, overlap, or incorrect Word rendering.

## Venue coverage

PaperFormat is venue-neutral by authoritative source, not by hard-coded family
defaults. The catalog routes 40 major publisher, society, journal, and
conference families, including IEEE, ACM, Springer Nature/LNCS, Elsevier,
Nature Portfolio, Science/AAAS, Wiley, Taylor & Francis, SAGE, OUP, CUP, APS,
AIP, IOP, Optica, ACS, RSC, SIAM/AMS, ASME/AIAA, PLOS/BMC, NeurIPS, ICML,
ICLR, ACL-family venues, CVPR/ICCV/WACV, ECCV, AAAI/IJCAI/ECAI, KDD/SIGIR,
USENIX, systems, databases, software engineering, security, HCI/graphics, and
robotics.

Recognition is routing metadata, not certification. Every real task must bind:

1. exact venue;
2. publisher or society;
3. year, volume, or edition;
4. track, article type, or proceedings series;
5. submission or camera-ready stage;
6. exact template or author-kit version.

The public `assets/templates/` registry is deliberately empty: the repository
tracks no DOCX, DOTX, PDF, archive, manuscript, or template binary. Synthetic
test documents are generated into a Git-ignored directory at test time.
Official third-party templates are used from the user's attachment or obtained
from the current official source for that task. See the Skill's
[venue coverage policy](plugins/paperformat-ai/skills/paperformat/references/venue-coverage.md)
and [template governance](plugins/paperformat-ai/skills/paperformat/references/template-library.md).

## Supported scope

For DOCX/DOTX, the engine currently supports deterministic inspection and
selected repair of:

- package validity, page geometry, sections, and columns;
- effective styles, fonts, sizes, emphasis, alignment, spacing, and indents;
- title/front matter, headings, body, captions, table text, equations, and
  reference-section classification evidence;
- typed plan validation, exact Review approvals, and isolated Experimental
  attempts;
- change logs, source-hash checks, protected-content integrity, rendering, page
  comparison, evidence-bound visual review, final validation, and export.

For LaTeX-first venues, the Skill defines a conservative workflow around the
exact official author kit: preserve and compile the baseline, inventory
content, apply a minimal reviewed migration, clean-build, compare, and visually
review the PDF. It does not promise arbitrary automatic TeX reconstruction.

Complex floating objects, cross-column reconstruction, macros, embedded Office
objects, and ambiguous topology may remain Review, Experimental, or unsupported.
PaperFormat never claims that a passing format check guarantees portal
acceptance.

## Runtime requirements

- macOS or Linux;
- Bash;
- .NET SDK `8.0.420` (pinned in the plugin's `global.json`);
- LibreOffice Writer and Poppler `pdftoppm` for visual gates;
- the manuscript's declared TeX toolchain for LaTeX tasks.

If the pinned .NET SDK is unavailable, run
`plugins/paperformat-ai/scripts/bootstrap-dotnet.sh` after reviewing the
network/filesystem action. Core operation needs no server, container, model
API, or credential.

## CLI

```text
paperformat inspect
paperformat derive-template
paperformat classify
paperformat layout-analyze
paperformat check
paperformat plan-validate
paperformat apply
paperformat attempt-init
paperformat render
paperformat compare-pages
paperformat visual-review
paperformat validate-integrity
paperformat validate-output
paperformat export
paperformat run-workflow
```

Exit codes: `0` completed, `2` invalid input, `3` confirmation required, `4`
validation failed, `5` required local tool unavailable, and `10` unexpected
failure. Only a ready export manifest authorizes delivery.

## Repository map

```text
.agents/plugins/marketplace.json       GitHub-backed marketplace
plugins/paperformat-ai/                 exact installable runtime boundary
  .codex-plugin/plugin.json            plugin manifest
  skills/paperformat/                   canonical Skill, references, scripts, assets
  scripts/                              deterministic CLI launcher and SDK helpers
  src/                                  provider-neutral DOCX engine source
  schemas/                              versioned machine contracts
tests/                                  safety, contract, and real-DOCX regressions
tools/PaperFormat.Fixtures/             synthetic fixture source; no binary fixtures
```

## Develop and verify

```bash
git clone https://github.com/1subtle/paperformat-ai.git
cd paperformat-ai
./scripts/test-all.sh
```

The complete test command restores the locked .NET dependencies, generates
temporary synthetic DOCX fixtures outside Git, builds, runs the xUnit suite,
verifies formatting, and runs Skill, marketplace, venue, privacy, and
template-governance contract tests. CI discards generated documents and does
not publish them as downloadable artifacts. A controlled end-to-end
render/export rehearsal is available with:

```bash
node scripts/rehearse-agent-native-example.mjs --output /absolute/path/new-output
```

Architecture, safety, protocol versions, and tool contracts are documented in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md),
[docs/SAFETY_MODEL.md](docs/SAFETY_MODEL.md),
[docs/PROTOCOL_VERSIONS.md](docs/PROTOCOL_VERSIONS.md), and
[docs/TOOL_CONTRACTS.md](docs/TOOL_CONTRACTS.md).

## License

PaperFormat AI source code and fixture-generator source are released under the
[MIT License](LICENSE). Generated fixtures are temporary test outputs and are
not committed. Publisher and venue names are used for source routing only;
their official templates and trademarks remain subject to their owners' terms.
