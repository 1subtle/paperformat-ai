# PaperFormat installation

## Recommended: install the Codex plugin from GitHub

Requirements:

- Codex with plugin commands;
- Git;
- public GitHub access.

Install the repository marketplace and plugin:

```bash
codex plugin marketplace add 1subtle/paperformat-ai --ref main
codex plugin add paperformat-ai@paperformat-ai
```

Start a new Codex task and invoke `$paperformat`.

The plugin is self-contained: the installed cache copy includes only
`plugins/paperformat-ai/`, which contains its Skill, CLI source, schemas, and
runtime helpers. Repository tests, fixture-generator source, examples, and
maintainer documentation are deliberately outside the installed package.

## Runtime requirements

DOCX operation supports macOS and Linux and requires:

- Bash;
- .NET SDK `8.0.420`, or network access for
  `plugins/paperformat-ai/scripts/bootstrap-dotnet.sh` to install it inside the
  plugin copy;
- LibreOffice Writer and Poppler `pdftoppm` for rendered-page and final visual
  gates.

LaTeX operation requires the build tools declared by the manuscript and exact
official venue package. Node.js 22 is needed only for the controlled
release-example script.

Core inspect, rule, classification, check, planning, repair, and integrity
commands do not require a server, model API, or model credential.

## Rendering dependencies

macOS with Homebrew:

```bash
brew install --cask libreoffice
brew install poppler
```

Ubuntu or Debian:

```bash
sudo apt-get update
sudo apt-get install --yes libreoffice-writer poppler-utils
```

Confirm both tools are discoverable before a visual DOCX workflow:

```bash
libreoffice --version || soffice --version
pdftoppm -v
```

## Development checkout

Contributors can clone the repository and run the packaged launcher directly:

```bash
git clone https://github.com/1subtle/paperformat-ai.git
cd paperformat-ai
./plugins/paperformat-ai/scripts/paperformat --help
./scripts/test-all.sh
```

## Upgrade

```bash
codex plugin marketplace upgrade paperformat-ai
codex plugin add paperformat-ai@paperformat-ai
```

Start a new task after upgrading so Codex discovers the new Skill version.

## Remove

```bash
codex plugin remove paperformat-ai
codex plugin marketplace remove paperformat-ai
```
