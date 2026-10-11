---
name: git-worktree
description: Do a task in its own git worktree from start to cleanup - create it beside the repository on a new branch from local main, work and commit there without touching the main checkout, land it on main by rebase and fast-forward, then remove the worktree and its branch as soon as the work is on main. A bundled script tells landed branches (merged, fast-forwarded, rebased, cherry-picked or squashed) apart from unused, unfinished and dirty ones, and removes only what is proven landed. Use this whenever you create, enter or finish a worktree, work in parallel with other agents, are told to land, merge or finish a branch, are asked to clean up worktrees or old branches, or find leftover worktrees in a repository. Not for switching branches in a single checkout, and not for writing commit messages (see git-commit).
---

# Git worktrees, from creation to cleanup

A worktree lets a task run on its own branch in its own folder while the main checkout keeps the user's work in
progress. Two things go wrong in practice:

- agents edit or commit in the main checkout anyway;
- worktrees stay behind long after their branch reached main, until nobody knows which ones still matter.

The lifecycle below ends with nothing left over. A worktree exists while its work is unfinished, and goes away when
the work is on main.

## Where you are

```bash
git worktree list                                          # the first entry is the main checkout
python3 <this skill's folder>/scripts/worktree_report.py   # every worktree and branch, and whether it landed
```

You are in a linked worktree when `git rev-parse --git-dir` and `git rev-parse --git-common-dir` differ. Run the
report when you start. Leftovers it marks `landed` can go (see [Clean up](#clean-up)); everything else is someone's
work.

## Create

1. **The harness's own worktrees.** If the harness has a worktree feature and the user asked for a worktree, use it
   instead of building a second mechanism next to it. In Claude Code that is `EnterWorktree`, `claude --worktree` and
   subagents with `isolation: worktree`. Claude Code branches from `origin/<default branch>` by default, which misses
   commits on local main that are not pushed yet, so run `git rebase main` in the new worktree first.
2. **Otherwise use `git worktree add`.** Follow the layout of the repository's existing worktrees. Without one, use a
   sibling folder named after the repository and the topic:

   ```bash
   git worktree add -b feature/share-link-expiry ../shop-share-link-expiry main
   ```

   - **The base is local `main`,** because it can be ahead of `origin/main`.
   - **Name the branch like the repository's other branches,** such as `feature/<topic>` or `fix/<topic>`.
   - **A sibling folder** stays out of the main checkout's file watchers, searches and IDE index. A folder inside
     the repository needs an ignore entry first.
3. **Copy the local configuration the build needs,** such as untracked settings files or `.env`, from the main
   checkout unchanged. The report treats an identical copy as safe to delete later.
4. **Build and run the tests once** before changing anything. What fails now is the baseline, not your bug.
5. **Tell the user the folder and the branch.** In an IDE, open the worktree's own solution or project file, not its
   folder: JetBrains Rider records an opened folder in the tracked `.idea/.../indexLayout.xml`, which then shows up
   as a change.

## Work

- **Stay in the worktree.** Every edit, build, test and commit happens in the worktree folder. Until you land, the
  main checkout belongs to the user: never edit, stage, stash or check out anything there.
- **Commit as you go,** following the git-commit skill. Uncommitted work in a worktree is invisible from everywhere
  else.
- **Rebase onto main at milestones** (`git rebase main`). Landing then stays a fast-forward, and conflicts come in
  small pieces.
- **Don't push the branch unless asked.**

## Land

When the user says to land, merge or finish the work:

1. **In the worktree:**
   - Make sure everything is committed: `git status --short` is empty. Ask about leftovers you did not create.
   - Run `git rebase main`.
   - Build and run the tests, and compare with the baseline. A new failure stops the landing.
2. **In the main checkout:**

   ```bash
   git -C <main checkout> status --short
   git -C <main checkout> merge --ff-only feature/share-link-expiry
   ```

   The first command shows the user's work in progress. `--ff-only` either moves main to your branch or refuses; it
   never creates a merge commit. It keeps the user's uncommitted changes in files your branch did not touch. When
   it refuses:

   - **"Not possible to fast-forward":** main moved. Go back to step 1.
   - **"Your local changes ... would be overwritten":** the user's work in progress touches your files. Stop and
     tell the user which files. Never stash, reset, commit or discard their changes to get through.

   A Claude Code agent isolated in a worktree is not allowed into the main checkout. It leaves with `ExitWorktree`
   (`keep`) first, or hands the landing to the session that started it.
3. **Don't push main** unless the user asked.
4. **Clean up right away** (next section). A worktree left on disk after its work landed is exactly the leftover this
   skill prevents.
5. **Report** the commits that landed (`git log --oneline <old main>..main`), the test result, and what you removed.

## Clean up

Run the report from the main checkout, read it, then let it clean:

```bash
cd <main checkout>
python3 <this skill's folder>/scripts/worktree_report.py            # report only, changes nothing
python3 <this skill's folder>/scripts/worktree_report.py --clean    # remove what is proven landed
```

| State | Meaning | `--clean` |
|---|---|---|
| `landed` | every change of the branch is on main: merged or fast-forwarded, rebased or cherry-picked, or squashed into one commit | removes the worktree and deletes the branch |
| `unused` | never committed on and still behind main: a fresh workspace someone is about to use | keeps it |
| `ahead` | holds changes main lacks, locally or on its remote branch | keeps it |
| `prunable` | the folder was deleted by hand | prunes git's record of it |

A landed worktree is still kept while any of these is true, and the report names the reason:

- it has modified or untracked files;
- it holds an ignored file that nothing rebuilds, such as a `.env` that differs from the main checkout's;
- it is locked (`git worktree lock`; Claude Code holds a lock while an agent runs in it);
- the current directory is inside it.

After the run:

- **Report every kept item to the user** with its reason, in one short list. Never remove what the script kept: the
  user decides about dirty and unfinished work.
- **Generated output the script does not know** (a compiled stylesheet, a downloaded tool) can be the only thing
  keeping a landed worktree. Pass it with `--disposable '<glob>'`, but only after checking that a build recreates
  it.
- **Remote branches are never deleted by the script.** Deleting one is visible to everyone, so suggest
  `git push origin --delete <branch>` and run it only on request.
- **A worktree Claude Code created itself** can be closed with `ExitWorktree` (`remove`). It refuses a squash-landed
  branch unless `discard_changes` is set, and its own rules require the user's confirmation for that.

### Why the script and not `git branch --merged`

Each of these shortcuts is wrong in a common case:

- **`git branch --merged` and `git branch -d`** see only fast-forwards and merge commits. A rebased or squashed
  branch looks unmerged, and the usual way out, `-D` or `--force`, also deletes real unfinished work.
- **A branch created a minute ago** and never committed on counts as merged too. Deleting it pulls the floor from
  under whoever just created it, so the script reads the branch's reflog to tell unused from landed.
- **A `[gone]` upstream** only means the remote branch was deleted. That also happens to closed, unmerged pull
  requests.
- **`git worktree remove`** deletes ignored files without asking.

[references/landed-detection.md](references/landed-detection.md) holds the git commands behind each check and their
limits. Read it when the script is not available or when its verdict surprises you.

## Rationalizations

| Thought | Reality |
|---|---|
| "It shows as merged, so it's done." | A fresh, never-used branch shows as merged too. |
| "`git branch -d` refused, so I'll use `-D`." | `-d` compares with the upstream or HEAD, not main. It refuses squash-landed branches and accepts pushed, unmerged ones. Only the report's proof counts. |
| "`git worktree remove` refused, so I'll add `--force`." | It refused because of uncommitted or untracked work. Report it. |
| "I'll stash the user's changes so main can fast-forward." | Their work in progress is theirs. Stop and ask. |
| "I'll branch from `origin/main`." | Local main can hold commits that are not pushed. Branch from it and rebase onto it. |
| "I'll clean up at the end of the session." | A session can end at any moment. Clean up when the work lands. |
| "That old worktree looks abandoned." | Unused and dirty worktrees are someone's work. List them and let the user decide. |

## Before you finish

- The work is on main, the tests ran after the rebase, and its worktree and branch are gone.
- The report shows nothing of yours left behind.
- The user has the list of everything kept, each with its reason.
