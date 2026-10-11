# Committing next to someone else's work

These are the cases where `git commit -F "$msg" -- <paths>` is not enough, because the files or the index hold work
that is not yours. Every command was checked with git 2.53.

## Part of a file, nothing else staged

Your change and the user's sit in the same file, and the index holds nothing else.

1. Write a patch of only your hunks: take `git diff -- <file>` and keep only your hunks. Then stage the patch:

   ```bash
   git apply --cached --check mine.patch && git apply --cached mine.patch
   ```

2. Check that only your path is staged:

   ```bash
   git diff --cached --name-only
   ```

3. Commit the index as it is. Leave out the pathspec, because a pathspec would take the whole file from the working
   tree:

   ```bash
   git commit -F "$msg"
   ```

Interactive staging (`git add -p`) needs a terminal that agents usually do not have. The patch route above does the
same without one.

## Part of a file, while the user has other files staged

The real index holds the user's staged work, so you cannot commit "the index". Build the commit on a temporary index
instead. The real index and the working tree stay as they are.

```bash
tmp=$(mktemp -d)
export GIT_INDEX_FILE="$tmp/index"
git read-tree HEAD

# The file as HEAD has it plus only your change: start from `git show HEAD:<path>` and re-apply your edit
blob=$(git hash-object -w "$tmp/WebhookDispatcher.cs")
git update-index --add --cacheinfo "100644,$blob,src/Webhooks/WebhookDispatcher.cs"
# A deleted file: git update-index --force-remove <path>

tree=$(git write-tree)
unset GIT_INDEX_FILE

old=$(git rev-parse HEAD)
new=$(git commit-tree "$tree" -p "$old" -F "$msg")
git update-ref "refs/heads/$(git branch --show-current)" "$new" "$old"
git reset -q -- src/Webhooks/WebhookDispatcher.cs
```

- **The mode** `100644` is for a regular file. Use `100755` for an executable.
- **`update-ref` with the old value** fails if HEAD moved in the meantime, so a concurrent commit cannot be lost.
- **The final `git reset -- <path>`** moves the real index entry of your path to the new commit. Without it,
  `git status` shows your change as staged in reverse. The user's other staged entries stay staged.
- **Check `git status --short` again a few seconds later.** An IDE that refreshes git in the background can write
  an older index back right after the reset. If your path shows up staged in reverse, run the reset again.

To prove the new file version holds only your change, diff it against HEAD's version:

```bash
git diff --no-index <(git show HEAD:src/Webhooks/WebhookDispatcher.cs) "$tmp/WebhookDispatcher.cs"
```

## A commit that swept up someone else's change

Before it is pushed, rebuild the commit without the foreign hunks:

1. Use the temporary index from above, starting from the parent instead of HEAD: `git read-tree <sha>^`.
2. Add your version of each file, then `git commit-tree "$tree" -p <sha>^`.
3. For the newest commit, move the branch with `git update-ref refs/heads/<branch> <new> <sha>`, then run
   `git reset -q -- <paths>`. Without the reset, the user's swept hunks show up as staged. With it, they are back
   in the working tree, uncommitted and unstaged, as they were before.
4. For an older commit, rebase the commits after it onto the rebuilt one. Then tell the user what happened.

When the swept commit still builds and the only problem is its message, amend the message alone, and only while the
commit is unpushed and yours.

## Stash and autostash drop staged state

Avoid stashing while the user has work in the tree. Two things go wrong:

- **Staged modifications come back unstaged.** `git stash pop` without `--index`, and `git rebase --autostash`,
  restore a staged modification as unstaged; only staged new files stay staged. The user's careful staging is gone.
  If you must stash, restore with `git stash pop --index`, and compare `git status --short` before and after.
- **`git add -N` files stop it.** A file added with `git add -N` (intent to add) makes `git stash` fail with
  "Entry '<file>' not uptodate. Cannot merge."

## Check a commit on its own

A commit made from a shared tree can depend on a file that only exists, uncommitted, in that tree. Build it where
nothing else is around:

```bash
git worktree add --detach "$tmp/check" <sha>
# build and test in "$tmp/check"
git worktree remove "$tmp/check"
```
