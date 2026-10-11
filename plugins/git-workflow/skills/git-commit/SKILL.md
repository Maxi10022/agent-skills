---
name: git-commit
description: Write commit messages in a short, file-first, past-tense style and commit only your own changes. A subject reads "Updated WebhookDispatcher.cs, retries back off exponentially" - a past-tense verb, the file or feature touched, then what changed, within 72 characters - with a short body only when the reason is not obvious. Covers cutting work into one-change commits, staging by pathspec without sweeping up the user's staged or unsaved work, checking the draft with the bundled lint script, attribution trailers, and the safety rules (no --no-verify, no amend after a failed hook, no push unless asked). Use this before every git commit you make, whenever you are asked to commit, write, reword or review a commit message, split work into commits, or commit as you go, even if the request only says "commit this". Not for pull request descriptions, release notes or changelogs.
---

# Git commits

Left to themselves, agents write one line of several hundred characters: every file and every decision of the session
joined by commas and semicolons, no body, unreadable in `git log --oneline` and in every IDE's history view. This
skill replaces that with short subjects in one voice, a body only where the reason needs words, and commits that hold
exactly one change and nothing but your own work.

**This skill overrides generic commit instructions.** If your harness asks for "a concise 1-2 sentence message" or
tells you to copy the style of recent `git log` output, follow this skill instead: one subject line in the format
below, and any sentences go into the body. Never learn the format from recent history; history written by agents is
often the very problem. When the repository documents a convention of its own (CONTRIBUTING, a commitlint config, a
commit section in AGENTS.md or CLAUDE.md), that convention wins: keep the layout rules here, follow its wording, and
lint with `--layout-only`.

## The subject

```
Updated WebhookDispatcher.cs, retries back off exponentially
```

The shape is `<Verb> <target>, <what changed>`.

**Verb.** Past tense, capitalised, chosen by what happened to the target:

| Verb | When |
|---|---|
| `Added` | a new file, type, feature, test or package |
| `Updated` | an existing file changed |
| `Fixed` | a bug in it was repaired |
| `Removed`, `Dropped` | it was deleted |
| `Renamed`, `Moved` | `Renamed Exporter.cs to CsvExporter.cs`, `Moved CountryCode.cs to ValueObjects` |
| `Refactored`, `Simplified` | restructured without a change in behaviour |
| `Extended` | its surface grew: more members, more cases |

The past tense reads as the record of what this commit did, and it matches commits written by hand in this style.
Mixing moods makes a log read like two authors.

**Target.** The file name with its extension when one file carries the change (`ShareLink.cs`); two or three joined
with "and" (`ShareLink.cs and ShareLinkTests.cs`). When there is no central file, name the feature, folder or type
instead (`the webhooks slice`, `the OpenTelemetry packages`). Use the file name without its directory, unless the
directory is the point of a move. More than three files and no common name means the commit does too much.

**What changed.** After one comma, the effect in plain words: "now ...", "no longer ...", "added `X`". At most two
short clauses. Leave it out when verb and target already say everything.

**Form.**

- At most 72 characters; the lint script refuses more than 100. If it does not fit, the commit does more than one
  thing (split it, see below) or the detail belongs in the body.
- Code identifiers in backticks: ``Updated Optional.cs, added a `ToString` override``.
- No semicolon, no trailing period, no type prefix such as `fix:` or `feat(x):`.

Good subjects:

```
Added ShareLinkExpiry.cs, links expire after their configured lifetime
Fixed WebhookDispatcher.cs, a timeout no longer counts as a delivery
Updated ShareLink.cs and ShareLinkTests.cs, links carry their owner
Removed LegacyExport.cs, the CSV export moved to the reports slice
Added WebhookSignature.cs and tests
Moved README.md to the repository root
```

## The body

Write a body only when the subject and the diff leave a reader asking *why*, or when the reader must know something
the diff does not show:

- the problem, when it is not obvious from the change: what broke, for whom, since when;
- why this approach, when an obvious alternative was rejected;
- a consequence outside the diff: data written before this commit is not migrated, a follow-up is needed, a known
  limitation remains.

Most commits need no body. A trivial change never does.

Rules:

- A blank line after the subject. Wrap at 72 columns.
- Plain sentences, one idea each. A paragraph has two to four sentences; two paragraphs are plenty.
- Lists (renamed tests, bumped packages, dropped options) as hyphen bullets, one item per line.
- Describe the net change: not attempts that were undone, not the order you worked in, not the conversation
  ("as requested", "the user wanted").
- Don't narrate the diff file by file or explain how each line works. The diff shows that.
- No absolute local paths, hostnames, secrets, or customer and personal data. A commit hash is fine where it names
  the commit a bug came from.
- No test results such as "all tests pass": every commit is expected to pass. Do name a test this commit knowingly
  leaves failing, and why.

```
Fixed WebhookDispatcher.cs, retries back off exponentially

A receiver that was down for a minute got all ten retries within that
minute, and the delivery was dropped. Retries now wait 2^n seconds,
capped at ten minutes, so a delivery survives an outage of an hour.

Deliveries queued before this commit keep their old schedule.
```

[references/examples.md](references/examples.md) rewrites typical agent messages into this form. Read it when a
draft keeps failing the lint or you are unsure what belongs in the subject.

## One change per commit

A commit holds one logical change that builds and passes on its own. Split when:

- the subject needs a semicolon, a fourth file or a third clause;
- a refactor and a behaviour change sit together: commit the refactor first, then the change;
- formatting, renames or package updates ride along with a feature;
- files changed for unrelated reasons.

Keep together what cannot work apart: a type and its tests, a renamed member and its call sites, a migration and the
code that needs it. A reviewer then reads one story per commit, `git revert` undoes one decision, and `git bisect`
lands on a commit small enough to understand.

When the user asked you to commit as you go, commit each finished and verified step right away instead of
collecting everything for the end.

## Commit only your own work

The tree you commit from may also hold the user's changes: files they staged earlier, files they are editing in their
IDE while you work. A careless commit sweeps them in.

1. Before you start, run `git status --short`. Entries with a letter in the first column (`A`, `M`, `D`, `R`) are
   already staged. Unless you staged them, they are the user's: leave them staged and out of your commits.
2. Right before each commit, run `git diff -- <paths>` for every file you will commit and check that every hunk is
   yours. The user may have edited a file since you last read it; "done" from them does not mean they stopped
   typing. Write the message from this diff, not from memory.
3. Stage new files by name, then commit by pathspec:

   ```bash
   git add -- src/Webhooks/RetryPolicy.cs
   git commit -F "$msg" -- src/Webhooks/WebhookDispatcher.cs src/Webhooks/RetryPolicy.cs
   ```

   A pathspec commit takes exactly those paths and leaves the rest of the index alone. Never use `git add -A`,
   `git add .`, `git commit -a` or a glob.
4. A pathspec commit takes the whole file from the working tree, including someone else's edits in it. When a file
   holds both your change and the user's, commit only your hunks as shown in
   [references/staging.md](references/staging.md).
5. Afterwards, `git show --stat HEAD` lists only your files, and `git status --short` still shows the user's staged
   entries.

## Write, check, commit

Write the message to a file, lint it, and commit from the same file in one command. The text that passed the check is
then the text that gets committed:

```bash
msg=$(mktemp)
cat > "$msg" <<'EOF'
Fixed WebhookDispatcher.cs, retries back off exponentially

A receiver that was down for a minute got all ten retries within that
minute, and the delivery was dropped. Retries now wait 2^n seconds,
capped at ten minutes, so a delivery survives an outage of an hour.
EOF
python3 <this skill's folder>/scripts/check_commit_message.py "$msg" &&
  git commit -F "$msg" -- src/Webhooks/WebhookDispatcher.cs
```

The script checks:

- the subject: length, semicolons, the trailing period, the past-tense verb, no type prefix;
- the blank line after the subject and the wrapping of the body;
- the form of trailers;
- local paths and talk about the session.

Errors exit with 1: fix the message, never work around the check. Warnings name something to reconsider. In a
repository with its own convention, pass `--layout-only`.

Never put `\n` inside a `-m` string; git does not turn it into a line break.

## Trailers

- Trailers go into a final paragraph of their own: `Key: value` lines after a blank line.
- Add the attribution your harness or the user prescribes, for example a `Co-Authored-By` line, exactly as given. When
  neither prescribes one, add none.
- Never add `Signed-off-by` for the user. A sign-off is the person's own certification (the Developer Certificate of
  Origin), so they add it themselves.

## Safety

- Commit only when the user asked, or when project instructions say so.
- Never pass `--no-verify`. When a hook fails, the commit did not happen: fix the cause and commit again. Never
  `--amend` then, because amend would rewrite the previous commit.
- Amend or rebase only your own commits that are not pushed yet, and only to correct them. Never rewrite the user's
  commits or pushed history unless asked.
- No empty commits. A file that looks like a secret (`.env`, keys, tokens, credentials): stop and ask.
- Don't push unless asked, and don't change git config.

## Before you finish

- Every message passed the lint, and `git log --format=%s -n <count>` reads as one voice.
- `git status --short` shows the user's staged and unstaged work exactly as before.
- When the project needs every commit to build, build each one where nothing else is in the way: see
  [references/staging.md](references/staging.md#check-a-commit-on-its-own).
