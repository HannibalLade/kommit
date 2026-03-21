# kommit

A lightweight CLI tool that analyzes your staged git changes, branch name, and file structure to automatically generate and apply a [Conventional Commits](https://www.conventionalcommits.org/) message — no LLM required.

## How it works

`kommit` reads your staged diff and branch name, applies heuristic rules to infer the commit type, scope, and description, then runs `git commit -m` with the generated message. No internet connection, no API keys, no dependencies beyond .NET.

**Heuristic rules:**
- Branch name prefix (`fix/`, `feat/`, `docs/`, etc.) → commit type
- Diff content analysis — detects new classes, methods, functions, renames, and deletions across C#, JS/TS, Python, Go, and Rust
- Signal detection — error handling, null checks, TODO removals, performance changes, security, logging, tests, and more
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
# Stage your changes and commit
kommit

# Preview the commit message without committing
kommit --dry-run
```

## Commands

### `kommit push`

Push changes using your configured strategy.

```sh
kommit push
```

### `kommit pull`

Pull changes using your configured strategy.

```sh
kommit pull
```

### `kommit tag`

Bump the version tag, update the `.csproj` version, commit, and push.

```sh
kommit tag          # bump minor (v0.2.0 → v0.3.0)
kommit tag -major   # bump major (v0.2.0 → v1.0.0)
kommit tag -patch   # bump patch (v0.2.0 → v0.2.1)
```

### `kommit merge`

Handle merge conflicts quickly.

```sh
kommit merge              # list conflicted files
kommit merge -incoming    # accept all incoming changes, commit, and push
kommit merge -current     # keep all current changes, commit, and push
```

### `kommit config`

Open the interactive config editor. Use arrow keys to navigate, Enter/Space to toggle booleans or edit values, Q to save and quit.

```sh
kommit config
```

### `kommit update`

Check for and install the latest version.

```sh
kommit update
```

## Configuration

Config is stored in `~/.kommitconfig` (JSON). You can edit it directly or use `kommit config`.

```json
{
  "autoAdd": false,
  "autoPush": false,
  "autoPull": false,
  "pullStrategy": "rebase",
  "pushStrategy": "simple",
  "defaultScope": null,
  "maxCommitLength": 72,
  "maxStagedFiles": null,
  "maxStagedLines": null
}
```

| Option | Default | Description |
|---|---|---|
| `autoAdd` | `false` | Prompt to stage all files if none are staged |
| `autoPush` | `false` | Automatically push after committing |
| `autoPull` | `false` | Automatically pull before committing |
| `pullStrategy` | `"rebase"` | Pull strategy: `"rebase"` or `"merge"` |
| `pushStrategy` | `"simple"` | Push strategy: `"simple"`, `"set-upstream"`, or `"force-with-lease"` |
| `defaultScope` | `null` | Default scope for commit messages (e.g. `"api"` → `feat(api): ...`) |
| `maxCommitLength` | `72` | Max commit message length before truncation |
| `maxStagedFiles` | `null` | File count threshold to trigger interactive commit splitting |
| `maxStagedLines` | `null` | Line count threshold to trigger interactive commit splitting |

## License

GNU General Public License v3.0 — see [LICENSE](LICENSE) for details.
