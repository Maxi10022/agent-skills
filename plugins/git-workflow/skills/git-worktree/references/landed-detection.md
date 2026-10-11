# Has this branch landed?

`scripts/worktree_report.py` runs these checks in order and stops at the first that proves the branch landed. The
commands below let you run them by hand, or understand a verdict. Every command was checked with git 2.53.

## The checks, in order

```bash
b=feature/share-link-expiry
```

**1. Merged or fast-forwarded**

```bash
git merge-base --is-ancestor "$b" main && echo "on main"
```

A branch that was never committed on passes this check too: it still points at the commit it was created from,
which is on main. Its reflog tells the two apart:

```bash
git reflog show --format=%gs "refs/heads/$b"
```

If every entry is `branch: Created from ...` or `branch: Reset to ...`, the branch is unused. Entries such as
`commit: ...`, `rebase (finish): ...` or `merge ...` mean work was done on it, and that work is on main. An old
branch can have no reflog at all, because git expires reflog entries after 90 days by default (`gc.reflogExpire`).
Neither case can be proven then. The script calls such a branch landed, because deleting a branch whose tip is on
main loses no commit.

**2. Rebased or cherry-picked**

```bash
git cherry main "$b"
```

`git cherry` prints one line per commit of the branch. A `-` means a commit with the same patch is on main; a `+`
means none is. If every line starts with `-`, the branch landed with new commit ids.

**3. Squashed**

```bash
base=$(git merge-base main "$b")
git diff "$base" "$b" | git patch-id --stable
git log -p --no-merges --format='commit %H' "$base..main" | git patch-id --stable
```

The first command gives the patch id of the branch's whole change. If it appears in the output of the second, one
commit on main carries exactly that change, which is what a squash merge produces. A later commit on main may edit
the same lines; the check still holds, because it compares with the squash commit itself.

If none of the three checks passes, the branch is `ahead`. The script reports how many of its commits main lacks.

## Where each check fails

| Check | Proves | Misses |
|---|---|---|
| ancestry | fast-forward, merge commit | rebase, cherry-pick, squash |
| `git cherry` | rebase, cherry-pick | squash; a commit changed while it was picked, for example by a conflict resolution |
| squash patch id | squash merge | a squash that was edited or had conflicts resolved; a branch split over several commits on main |

Every miss leaves the branch `ahead`: kept and reported, never deleted. A false `ahead` costs one question to the
user. A false `landed` would delete work, so every check errs toward keeping.

## Remote branches and pull requests

- A local branch whose remote branch holds commits main lacks is `ahead`, whatever the local branch's own state is.
  Someone pushed more work.
- An upstream marked `[gone]` (after `git fetch --prune`) is only a hint. The remote branch was deleted, which also
  happens when a pull request is closed without merging. The checks above still decide.
- For a pull request on GitHub, `gh pr view <branch> --json state,headRefOid` showing `MERGED` with a `headRefOid`
  equal to `git rev-parse <branch>` also proves the landing. The script does not call `gh`, so it works offline and
  without a login.

## Removing safely

- **Order:** remove the worktree first, then the branch. Git refuses to delete a branch that a worktree has checked
  out.
- **`git worktree remove <path>`, never `--force`.** It refuses a worktree with modified or untracked files, but it
  deletes ignored files without a word. That is why the report lists ignored files that nothing rebuilds and keeps
  such a worktree.
- **A locked worktree** refuses removal, even with a single `--force`. The lock usually means an agent is working
  in it. Leave it.
- **`git worktree prune`** removes git's record of worktrees whose folder was deleted by hand. `git worktree list`
  marks them `prunable`.
- **`git branch -D`, but only after a check above passed.** `git branch -d` is no proof either way. It compares with
  the branch's upstream, or with HEAD when there is none, so it refuses a squash-landed branch and accepts a pushed
  but unmerged one.
