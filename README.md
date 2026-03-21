# kommit

A lightweight CLI tool that analyzes your staged git changes, branch name, and file structure to automatically generate and apply a [Conventional Commits](https://www.conventionalcommits.org/) message — no LLM required.

## How it works

`kommit` reads your staged diff and branch name, applies heuristic rules to infer the commit type and scope, and runs `git commit -m` with the generated message. No internet connection, no API keys, no dependencies beyond .NET.

**Heuristic rules:**
- Branch name prefix (`fix/`, `feat/`, `docs/`, etc.) → commit type
- File types changed (only `*.md` → `docs:`, only `*.test.*` → `test:`, etc.)
- Added vs. deleted line ratio to distinguish additions from removals
- Changed file paths to infer scope

## Installation

```sh
curl -fsSL https://raw.githubusercontent.com/HannibalLade/kommit/main/install.sh | bash
```

This detects your OS and architecture, downloads the latest release binary, and installs it to `/usr/local/bin` (or `~/.local/bin`).

### Build from source

Requires [.NET 10+](https://dotnet.microsoft.com/download).

```sh
git clone https://github.com/HannibalLade/kommit.git
cd kommit
dotnet publish -c Release -r osx-arm64 --self-contained
```

Then move the binary from `bin/Release/net10.0/osx-arm64/publish/kommit` to somewhere on your `$PATH`.

## Usage

```sh
# Stage your changes as normal
git add .

# Run kommit
kommit

# Preview the message without committing
kommit --dry-run

# Check version
kommit --version

# Update to latest release
kommit update
```

## License

GNU General Public License v3.0 — see [LICENSE](LICENSE) for details.
