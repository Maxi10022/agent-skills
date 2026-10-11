# agent-skills

My personal library of **Agent Skills**: packaged know-how that teaches an AI coding agent one specific way of
working. Each skill is a folder of plain Markdown instructions, reference notes and code templates. An agent reads a
skill's short description up front and loads the rest only when a task calls for it.

The skills use the open [Agent Skills format](https://agentskills.io), so they are not tied to one tool. They work
with Claude Code, OpenAI Codex, GitHub Copilot, Cursor, Gemini CLI, OpenCode, Goose and the
[other clients](https://agentskills.io/clients) that support the format.

Skills are grouped by topic into **plugins**, so you can install a whole topic with one command in Claude Code or
Codex, or copy single skills into any other agent.

> **AI agents:** read [AGENTS.md](AGENTS.md) for how to use the skills in this repository and how to work on it.

## Plugins and skills

### dotnet-testing

Four skills for testing .NET applications. They come out of reworking a large production test suite; their examples
use a fictional domain (webhooks, customers, share links).

| Skill | What it teaches | Reach for it when |
|---|---|---|
| [csharp-test-formatting](plugins/dotnet-testing/skills/csharp-test-formatting/SKILL.md) | `Given_<state>_<outcome>` names, Arrange/Act/Assert layout, one assertion per statement, `x.Assert.Y()` helpers as C# 14 extension blocks, the comment policy, convention tests that enforce it all | writing, reviewing or reformatting C# tests, or sweeping a suite to one style |
| [event-sourced-dotnet-integration-tests](plugins/dotnet-testing/skills/event-sourced-dotnet-integration-tests/SKILL.md) | one Postgres container with a database per fixture, the collection-versus-class-fixture rule, `Given(events)`, tracked Wolverine sessions, projection waits, fault and notification tests, a catalogue of flake causes | setting up or fixing integration tests for Marten/Wolverine apps hosted with Alba and Testcontainers, or chasing flaky and slow host tests |
| [blazor-bunit-page-tests](plugins/dotnet-testing/skills/blazor-bunit-page-tests/SKILL.md) | the bUnit setup order, rendering pages with their layout pieces, DOM-based waits, held tasks for confirm dialogs, stubbed message buses for failure paths | writing or fixing bUnit tests for Blazor pages and components, alone or against a real host |
| [test-data-catalog](plugins/dotnet-testing/skills/test-data-catalog/SKILL.md) | `TestValue<T>` with Default/Updated/Invalid variants, per-test data from a Bogus faker seeded by the test's identity, one parameterless builder per command or event | adding or cleaning up test data and builders, or chasing values that change between reads or runs |

The skills assume .NET 10 / C# 14, xUnit v3 and FluentAssertions 7. The integration and page skills add Marten 9,
Wolverine 6, Alba 8, Testcontainers 4 and bUnit 2. Each skill says what to change for other versions or test
frameworks.

### git-workflow

Two skills that keep agents' git history readable and their worktrees tidy. They come out of a repository where agents
wrote commit subjects of up to 1,200 characters and left worktrees behind after their branches were merged.

| Skill | What it teaches | Reach for it when |
|---|---|---|
| [git-commit](plugins/git-workflow/skills/git-commit/SKILL.md) | subjects like `Updated WebhookDispatcher.cs, retries back off exponentially` (past tense, the file or feature, what changed, 72 characters), a body only for the reason, one change per commit, committing by pathspec without sweeping up the user's staged work, a lint script for the draft | any commit an agent makes, or rewording and splitting commits |
| [git-worktree](plugins/git-workflow/skills/git-worktree/SKILL.md) | creating a worktree beside the repository from local main, working without touching the main checkout, landing by rebase and fast-forward, and a script that tells landed branches (merged, rebased or squashed) from unused, unfinished and dirty ones and removes only the landed | starting, landing or finishing work in a worktree, or cleaning up old worktrees and branches |

Both scripts need only Python 3.10 and git. The lint passes 168 of 200 hand-written commits of the source repository
(28 of the other 32 have subjects over 100 characters) and rejects 128 of its last 150 agent commits. The
worktree script was checked against nineteen cases in a scratch repository, from squash merges to locked worktrees,
with git 2.53.

Claude Code adds its own commit instructions to every session ("a concise 1-2 sentence message", copy the style of
`git log`), which compete with `git-commit`. Turn them off with `"includeGitInstructions": false` in
`~/.claude/settings.json`; that also drops the git status snapshot at the start of a session.

## Install

Pick one way per agent. Installing the same skill twice, say as a plugin and as a copied folder, gives the agent two
copies to choose from.

### Claude Code

In a Claude Code session:

```
/plugin marketplace add Maxi10022/agent-skills
/plugin install dotnet-testing@maxi10022
/plugin install git-workflow@maxi10022
```

The first line registers this repository as a plugin marketplace named `maxi10022`; the others install plugins
from it. From the shell, `claude plugin install dotnet-testing --marketplace Maxi10022/agent-skills` does both in one
step (Claude Code 2.1.275 or later). Restart the session or run `/reload-plugins` to load the skills.

To get new versions later, run `/plugin marketplace update maxi10022`, then update the plugin in `/plugin`.

### OpenAI Codex

Register the repository as a plugin marketplace:

```bash
codex plugin marketplace add Maxi10022/agent-skills
```

Then, following [OpenAI's plugin guide](https://developers.openai.com/plugins/build/plugins), restart the ChatGPT
desktop app, open the **Plugins Directory**, choose **Maxi10022 Agent Skills** and install the plugins you want.
`codex plugin marketplace upgrade maxi10022` fetches new versions.

The Codex marketplace files follow OpenAI's documentation but have not been tested in Codex yet. If they do not work
for you, copy the skill folders as described next, and please open an issue.

### Any agent: copy the skill folders

Clone the repository once:

```bash
git clone https://github.com/Maxi10022/agent-skills.git ~/agent-skills
```

Then put the skill folders where your agent looks for skills. A **personal** location serves every project; a
**project** location lives in a repository and is shared with everyone working on it.

| Agent | Personal | Project |
|---|---|---|
| Claude Code | `~/.claude/skills/` | `.claude/skills/` |
| OpenAI Codex | `~/.agents/skills/` | `.agents/skills/` |
| GitHub Copilot (VS Code, CLI) | `~/.copilot/skills/` or `~/.agents/skills/` | `.github/skills/`, `.agents/skills/` or `.claude/skills/` |
| Other agents | see the agent's page in the [client list](https://agentskills.io/clients) | |

Copy all skills of a plugin, or only the ones you want:

```bash
mkdir -p ~/.agents/skills
cp -r ~/agent-skills/plugins/dotnet-testing/skills/* ~/.agents/skills/
cp -r ~/agent-skills/plugins/dotnet-testing/skills/test-data-catalog ~/.agents/skills/
```

To pick up updates with a plain `git pull`, link the folders instead of copying them. Most agents follow symbolic
links; check yours if a linked skill does not show up.

```bash
ln -s ~/agent-skills/plugins/dotnet-testing/skills/test-data-catalog ~/.agents/skills/test-data-catalog
```

The [skills.sh](https://skills.sh) installer copies skills into the agents it supports, all of them or the ones you
name:

```bash
npx skills add Maxi10022/agent-skills
npx skills add Maxi10022/agent-skills --skill test-data-catalog
```

## Use

You usually do not invoke a skill yourself. The agent compares your request with each skill's description and loads
the matching one: ask Claude Code to "clean up these tests", and it reads `csharp-test-formatting` before it starts.
To be explicit, name the skill in your request ("use the test-data-catalog skill"). In Claude Code, a skill installed
through a plugin also runs as `/dotnet-testing:test-data-catalog`; Codex lets you mention a skill with
`$test-data-catalog`.

Inside a skill:

- **`SKILL.md`** holds the rules and the reasons behind them.
- **`references/`** holds longer examples and catalogues, which the agent reads when `SKILL.md` points to them.
- **`templates/`** holds C# files to copy into your test project. They use the namespace `YourApp.Tests.Testing.*` and
  assume the shared test namespaces are global usings; rename them to your project's.
- **`scripts/`** holds small programs the agent runs, such as the commit-message lint. They need only Python 3.10 and
  its standard library.

## Without Claude, and with local models

Nothing in these skills depends on Claude: they are Markdown, C# and Python files. Any agent that supports the format
loads them, including agents that run local models, such as [OpenCode](https://opencode.ai) or
[Goose](https://block.github.io/goose/) talking to Ollama or LM Studio.

For an agent without skill support, add a line to its instructions file (`AGENTS.md`, a system prompt or a rules
file) that tells it to read `plugins/<plugin>/skills/<name>/SKILL.md` before tasks of that kind. You can also paste
the `SKILL.md` text into the conversation.

Two caveats:

- The skills were benchmarked with Claude only. Smaller local models tend to follow long instructions less reliably.
- An agent needs file access, because `SKILL.md` sends it to `references/`, `templates/` and `scripts/` for the
  details.

## How the .NET skills were checked

Each .NET skill was benchmarked on two tasks in a fictional library-catalog app (books, loans, license keys), three
runs per task, against runs with no skill. Five of the eight tasks clearly separate the two configurations. On those,
runs with the skills met 86–100% of the expected outcomes and runs without them 25–88%. Examples of expected outcomes:
"the dialog-opening click is held as a task, not awaited", "the seed is drawn per test from a stably hashed test id",
"the root cause of the slow shutdown is Wolverine's drain timeout".

The other three tasks were solved almost as well without a skill, so they say little either way.

## Repository layout

```
plugins/<plugin>/plugin.json                    the plugin's name, version and description (Claude Code and Codex)
plugins/<plugin>/skills/<name>/SKILL.md         instructions, with a name and description at the top
plugins/<plugin>/skills/<name>/references/      detail an agent reads when SKILL.md points to it
plugins/<plugin>/skills/<name>/templates/       files to copy into a project and adapt
plugins/<plugin>/skills/<name>/scripts/         programs the agent runs, Python standard library only
.claude-plugin/marketplace.json                 the plugin list Claude Code reads
.agents/plugins/marketplace.json                the plugin list Codex reads
tools/validate_skills.py                        checks plugins and skills against the format and the rules here
AGENTS.md                                       instructions for AI agents using or editing this repository
```

## Add a skill

1. Pick the plugin the skill belongs to. For a new topic, create `plugins/<plugin>/plugin.json` and add the plugin to
   both marketplace files.
2. Create `plugins/<plugin>/skills/<name>/SKILL.md`. Write the frontmatter (`name` equal to the folder name, and a
   `description` saying what the skill does and when to use it), then the instructions. Keep `SKILL.md` under 300
   lines and move long examples into `references/`.
3. Raise the `version` in the plugin's `plugin.json`. Claude Code keeps everyone on the version they installed until
   this number changes.
4. Add a row to the plugin's table in this README.
5. Run `python3 tools/validate_skills.py`, and `claude plugin validate .` if you have Claude Code.

[AGENTS.md](AGENTS.md) lists the full set of conventions; point your agent at it when it writes a skill for you.

## License

[MIT](LICENSE)
