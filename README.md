# kommit

A git workflow tool that handles commits, merges, pull requests, tagging, and conflict resolution — all from one command. No context switching, no browser tabs, no copy-pasting branch names.

```sh
kommit                    # stage → generate message → edit → commit
kommit mr develop         # push, pick reviewers, create PR/MR
kommit merge main         # merge with interactive conflict resolution
kommit tag                # bump version, tag, push
kommit undo               # undo the last command
```

## Why kommit

Git has great building blocks but a terrible workflow. Creating a PR means pushing, opening a browser, filling out a form, picking reviewers. Merging means fetching, merging, resolving conflicts across multiple files, staging, committing, pushing. Even committing means writing a conventional commit message from scratch every time.

kommit wraps all of that into single commands with sensible defaults. It also generates commit messages from your diff — not with an LLM, just heuristics — but that's a starting point you can edit, not the main feature.

## Installation

**macOS / Linux:**

```sh
curl -fsSL https://raw.githubusercontent.com/HannibalLade/kommit/main/install.sh | bash
```

**Windows (PowerShell):**

```powershell
irm https://raw.githubusercontent.com/HannibalLade/kommit/main/install.ps1 | iex
```

Downloads the latest release binary for your platform. First-time install walks you through configuration.

### Build from source

Requires [.NET 10+](https://dotnet.microsoft.com/download).

```sh
git clone https://github.com/HannibalLade/kommit.git
cd kommit
dotnet publish kommit.csproj -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

Binary lands in `bin/Release/net10.0/osx-arm64/publish/kommit`. Replace `osx-arm64` with `osx-x64`, `linux-x64`, `linux-arm64`, or `win-x64`.

## Commands

### `kommit` — commit with a generated message

Analyzes your staged diff, branch name, and file structure to generate a [Conventional Commits](https://www.conventionalcommits.org/) message. You get an editable prompt to accept or modify it before committing.

```sh
kommit                          # generate message, edit, commit
kommit --preview                # show what would be generated, don't commit
```

You always get an editable prompt before committing — accept the suggestion with Enter, or clear it and type your own. If you'd rather always write your own messages, set `autoGenerate` to `false` in the config.

The message generator uses heuristics — branch prefix, diff content, symbol detection across C#/JS/TS/Python/Go/Rust, file types, line ratios — to infer the type, scope, and description. No LLM, no internet needed.

### `kommit mr` — create a merge/pull request

Push your branch and create an MR (GitLab) or PR (GitHub) in one command.

```sh
kommit mr develop
```

- Detects GitHub vs GitLab from your remote URL (including self-hosted GitLab)
- Pushes your branch
- Checks for conflicts before creating — if found, offers to open VS Code at the exact conflict lines so you can fix them first
- Auto-assigns you
- Interactive reviewer picker from project members
- Walks you through API token setup on first use

### `kommit merge` — merge with conflict resolution

Merge a branch with interactive per-file conflict resolution.

```sh
kommit merge main
```

For each conflicted file, choose:
- **[i]ncoming** — accept the incoming changes
- **[c]urrent** — keep your changes
- **[v]scode** — open the file in VS Code at the conflict line, then run `kommit continue` when done
- **[s]kip** — leave unresolved for now

Bulk resolve everything at once:

```sh
kommit merge main -incoming     # accept all incoming
kommit merge main -current      # keep all current
```

### `kommit continue` — resume after conflict resolution

After fixing conflicts in your editor, continue the merge:

```sh
kommit continue
```

If conflicts remain, it shows them and offers to open VS Code again. Once all conflicts are resolved, it commits and pushes.

### `kommit undo` — undo the last command

Reverses the last kommit command where possible, or tells you exactly how to do it manually.

```sh
kommit undo
```

| Command | What undo does |
|---|---|
| `kommit` (commit) | Soft reset — changes go back to staging |
| `kommit tag` | Deletes local tag, undoes version commit, tells you how to remove the remote tag |
| `kommit push` | Can't auto-undo — shows the exact commands to run |
| `kommit merge` | Can't auto-undo — shows how to reset or revert |
| `kommit mr` | Can't auto-undo — shows the MR/PR link to close manually |

### `kommit tag` — version bump and release

Bump the version, update `.csproj`, commit, tag, and push — with step-by-step output.

```sh
kommit tag              # minor bump (v0.2.0 → v0.3.0)
kommit tag -major       # major bump (v0.2.0 → v1.0.0)
kommit tag -patch       # patch bump (v0.2.0 → v0.2.1)
kommit tag --preview    # show what would happen without doing it
```

### `kommit push` / `kommit pull`

Push or pull using your configured strategy.

```sh
kommit push
kommit push --preview
kommit pull
```

### `kommit status`

Preview the commit message and staged file list without committing.

```sh
kommit status
```

### `kommit config`

Interactive config editor. Arrow keys to navigate, Enter/Space to toggle, Q to save and quit.

```sh
kommit config
```

### `kommit update`

Check for and install the latest version.

```sh
kommit update
```

## Configuration

Config is stored in `~/.kommit/config.json`. Edit it directly or use `kommit config`.

| Option | Default | Description |
|---|---|---|
| `autoGenerate` | `true` | Auto-generate commit messages from your diff. When off, you write the message from scratch. |
| `autoAdd` | `false` | Prompt to stage all files if none are staged |
| `autoPush` | `false` | Auto-push after committing |
| `autoPull` | `false` | Auto-pull before committing |
| `pullStrategy` | `"rebase"` | `"rebase"` or `"merge"` |
| `pushStrategy` | `"simple"` | `"simple"`, `"set-upstream"`, or `"force-with-lease"` |
| `defaultScope` | `null` | Default scope for commit messages (e.g. `"api"`) |
| `maxCommitLength` | `72` | Max commit message length before truncation |
| `maxStagedFiles` | `null` | File count threshold to trigger interactive commit splitting |
| `maxStagedLines` | `null` | Line count threshold to trigger interactive commit splitting |
| `apiToken` | `null` | GitHub/GitLab API token for merge requests |

## License

GNU General Public License v3.0 — see [LICENSE](LICENSE) for details.
