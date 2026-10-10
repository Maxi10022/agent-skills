# agent-skills

My personal library of **Agent Skills**: packaged know-how that teaches an AI coding agent one specific way of
working. Each skill is a folder of plain Markdown instructions, reference notes and code templates. An agent reads a
skill's short description up front and loads the rest only when a task calls for it.

The skills use the open [Agent Skills format](https://agentskills.io), so they are not tied to one tool. They work
with Claude Code, OpenAI Codex, GitHub Copilot, Cursor, Gemini CLI, OpenCode, Goose and the
[other clients](https://agentskills.io/clients) that support the format.

> **AI agents:** read [AGENTS.md](AGENTS.md) for how to use the skills in this repository and how to work on it.

## Skills

The first four skills cover testing .NET applications. They come out of reworking a large production test suite;
their examples use a fictional domain (webhooks, customers, share links).

| Skill | What it teaches | Reach for it when |
|---|---|---|
| [csharp-test-formatting](skills/csharp-test-formatting/SKILL.md) | `Given_<state>_<outcome>` names, Arrange/Act/Assert layout, one assertion per statement, `x.Assert.Y()` helpers as C# 14 extension blocks, the comment policy, convention tests that enforce it all | writing, reviewing or reformatting C# tests, or sweeping a suite to one style |
| [event-sourced-dotnet-integration-tests](skills/event-sourced-dotnet-integration-tests/SKILL.md) | one Postgres container with a database per fixture, the collection-versus-class-fixture rule, `Given(events)`, tracked Wolverine sessions, projection waits, fault and notification tests, a catalogue of flake causes | setting up or fixing integration tests for Marten/Wolverine apps hosted with Alba and Testcontainers, or chasing flaky and slow host tests |
| [blazor-bunit-page-tests](skills/blazor-bunit-page-tests/SKILL.md) | the bUnit setup order, rendering pages with their layout pieces, DOM-based waits, held tasks for confirm dialogs, stubbed message buses for failure paths | writing or fixing bUnit tests for Blazor pages and components, alone or against a real host |
| [test-data-catalog](skills/test-data-catalog/SKILL.md) | `TestValue<T>` with Default/Updated/Invalid variants, per-test data from a Bogus faker seeded by the test's identity, one parameterless builder per command or event | adding or cleaning up test data and builders, or chasing values that change between reads or runs |

The .NET skills assume .NET 10 / C# 14, xUnit v3 and FluentAssertions 7. The integration and page skills add
Marten 9, Wolverine 6, Alba 8, Testcontainers 4 and bUnit 2. Each skill says what to change for other versions or
test frameworks.

## Install

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

Copy all skills, or only the ones you want:

```bash
mkdir -p ~/.claude/skills
cp -r ~/agent-skills/skills/* ~/.claude/skills/
cp -r ~/agent-skills/skills/test-data-catalog ~/.claude/skills/
```

To pick up updates with a plain `git pull`, link the folders instead of copying them. Most agents follow symbolic
links; check yours if a linked skill does not show up.

```bash
ln -s ~/agent-skills/skills/test-data-catalog ~/.claude/skills/test-data-catalog
```

The [skills.sh](https://skills.sh) installer can also copy the collection into the agents it supports:

```bash
npx skills add Maxi10022/agent-skills
```

## Use

You usually do not invoke a skill yourself. The agent compares your request with each skill's description and loads
the matching one: ask Claude Code to "clean up these tests", and it reads `csharp-test-formatting` before it starts.
To be explicit, name the skill in your request ("use the test-data-catalog skill"). Codex also lets you mention a
skill with `$test-data-catalog`.

Inside a skill:

- **`SKILL.md`** holds the rules and the reasons behind them.
- **`references/`** holds longer examples and catalogues, which the agent reads when `SKILL.md` points to them.
- **`templates/`** holds C# files to copy into your test project. They use the namespace `YourApp.Tests.Testing.*` and
  assume the shared test namespaces are global usings; rename them to your project's.

## Without Claude, and with local models

Nothing in these skills depends on Claude: they are Markdown and C# files. Any agent that supports the format loads
them, including agents that run local models, such as [OpenCode](https://opencode.ai) or
[Goose](https://block.github.io/goose/) talking to Ollama or LM Studio.

For an agent without skill support, add a line to its instructions file (`AGENTS.md`, a system prompt or a rules
file) that tells it to read `skills/<name>/SKILL.md` before tasks of that kind. You can also paste the `SKILL.md` text
into the conversation.

Two caveats:

- The skills were benchmarked with Claude only. Smaller local models tend to follow long instructions less reliably.
- An agent needs file access, because `SKILL.md` sends it to `references/` and `templates/` for the details.

## How the .NET skills were checked

Each .NET skill was benchmarked on two tasks in a fictional library-catalog app (books, loans, license keys), three
runs per task, against runs with no skill. Five of the eight tasks clearly separate the two configurations. On those,
runs with the skills met 86–100% of the expected outcomes and runs without them 25–88%. Examples of expected outcomes:
"the dialog-opening click is held as a task, not awaited", "the seed is drawn per test from a stably hashed test id",
"the root cause of the slow shutdown is Wolverine's drain timeout".

The other three tasks were solved almost as well without a skill, so they say little either way.

## Repository layout

```
skills/<name>/SKILL.md        instructions, with a name and description at the top
skills/<name>/references/     detail an agent reads when SKILL.md points to it
skills/<name>/templates/      files to copy into a project and adapt
tools/validate_skills.py      checks every skill against the format and this repository's rules
AGENTS.md                     instructions for AI agents using or editing this repository
```

## Add a skill

1. Create `skills/<name>/SKILL.md`. Write the frontmatter (`name` equal to the folder name, and a `description`
   saying what the skill does and when to use it), then the instructions. Keep `SKILL.md` under 300 lines and move
   long examples into `references/`.
2. Add a row to the table in this README.
3. Run `python3 tools/validate_skills.py`.

[AGENTS.md](AGENTS.md) lists the full set of conventions; point your agent at it when it writes a skill for you.

## License

[MIT](LICENSE)
