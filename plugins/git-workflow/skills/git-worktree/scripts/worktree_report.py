#!/usr/bin/env python3
"""Reports every linked worktree and local branch of a repository and whether its work has landed on the main
branch. With --clean it removes what is proven landed and safe to delete, and nothing else.

Usage:
  worktree_report.py                    report only, changes nothing
  worktree_report.py --clean            also remove landed worktrees and branches, prune stale entries
  worktree_report.py --main develop     compare against another branch than the detected main branch
  worktree_report.py --disposable 'wwwroot/css/app.css' --disposable 'tools/*'
                                        treat more ignored files as rebuildable output

States:
  landed     every change of the branch is on main (fast-forward or merge, rebase or cherry-pick, or squash)
  unused     never committed on and still behind main: a fresh workspace, kept
  ahead      has commits whose changes main does not have, kept
  prunable   the worktree folder is gone, only git's record of it is left
  main       a linked worktree holding the main branch itself, kept

A worktree is removed only when its branch is landed, it has no modified or untracked files, it holds no ignored
file outside the rebuildable set, it is not locked, and the current directory is not inside it. A branch is
deleted only when it is landed, no worktree uses it, and its remote branch, if any, holds nothing main lacks.
Remote branches are never touched. Needs Python 3.10 and git 2.38 or later.
"""
import argparse
import filecmp
import fnmatch
import os
import subprocess
import sys
from pathlib import Path

# Ignored paths that a build or a package restore recreates; matched against every path segment
REBUILDABLE = {
    "bin", "obj", "node_modules", "dist", "build", "out", "target", ".venv", "venv", "__pycache__",
    ".pytest_cache", ".mypy_cache", ".ruff_cache", ".gradle", ".next", ".nuxt", ".svelte-kit", ".turbo",
    ".parcel-cache", ".angular", "coverage", ".idea", ".vs", ".cache", "TestResults", ".tox", ".dart_tool",
}
REBUILDABLE_SUFFIXES = (".pyc", ".log", ".user", ".suo", ".tsbuildinfo")

# Reflog messages that only create, point or rename a branch, without any work on it
NO_WORK = ("branch: Created from", "branch: Reset to", "branch: renamed")


def git(*args: str, cwd: str, check: bool = True, stdin: str | None = None) -> str:
    result = subprocess.run(
        ["git", *args], cwd=cwd, input=stdin, capture_output=True, text=True, encoding="utf-8", errors="replace"
    )
    if check and result.returncode != 0:
        raise SystemExit(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout


def succeeds(*args: str, cwd: str) -> bool:
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True).returncode == 0


def detect_main(repo: str) -> str:
    head = git("symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD", cwd=repo, check=False).strip()
    candidates = [head.split("/", 1)[1]] if "/" in head else []
    for name in candidates + ["main", "master", "trunk", "develop"]:
        if succeeds("show-ref", "--verify", "--quiet", f"refs/heads/{name}", cwd=repo):
            return name
    raise SystemExit("could not find the main branch; pass --main <branch>")


def worktrees(repo: str) -> list[dict]:
    entries, current = [], {}
    for line in git("worktree", "list", "--porcelain", cwd=repo).splitlines() + [""]:
        if not line:
            if current:
                entries.append(current)
            current = {}
            continue
        key, _, value = line.partition(" ")
        current[key] = value or True
    return entries


def upstreams(repo: str) -> dict[str, str]:
    """Maps each local branch to its upstream, for upstreams that still exist."""
    result = {}
    lines = git("for-each-ref", "--format=%(refname:short)\t%(upstream:short)\t%(upstream:track)", "refs/heads",
                cwd=repo).splitlines()
    for line in lines:
        branch, upstream, track = (line.split("\t") + ["", ""])[:3]
        if upstream and track != "[gone]" and succeeds("rev-parse", "--verify", "--quiet", upstream, cwd=repo):
            result[branch] = upstream
    return result


def patch_ids(repo: str, diff: str) -> set[str]:
    output = git("patch-id", "--stable", cwd=repo, stdin=diff) if diff.strip() else ""
    return {line.split()[0] for line in output.splitlines() if line}


def was_worked_on(repo: str, branch: str) -> bool | None:
    """True when the branch's reflog shows commits, rebases or merges; None when it has no reflog."""
    log = git("reflog", "show", "--format=%gs", f"refs/heads/{branch}", "--", cwd=repo, check=False)
    entries = [line for line in log.splitlines() if line]
    if not entries:
        return None
    return any(not entry.startswith(NO_WORK) for entry in entries)


def classify(repo: str, main: str, ref: str, branch: str | None) -> tuple[str, str]:
    """Returns (state, detail) for a commit or branch compared with main."""
    if succeeds("merge-base", "--is-ancestor", ref, main, cwd=repo):
        worked = was_worked_on(repo, branch) if branch else None
        if worked is False:
            return "unused", "never committed on, behind main"
        return "landed", "merged" if worked else "merged (no reflog to tell it from an unused branch)"

    cherry = git("cherry", main, ref, cwd=repo).splitlines()
    missing = [line for line in cherry if line.startswith("+")]
    if not missing:
        return "landed", f"rebased or cherry-picked ({len(cherry)} commits)"

    base = git("merge-base", main, ref, cwd=repo, check=False).strip()
    if base:
        squashed = patch_ids(repo, git("diff", base, ref, cwd=repo))
        if not squashed:
            return "ahead", f"{len(missing)} commits that add up to no change"
        on_main = patch_ids(repo, git("log", "-p", "--no-merges", "--format=commit %H", f"{base}..{main}", cwd=repo))
        if squashed <= on_main:
            return "landed", "squashed into one commit on main"
    return "ahead", f"{len(missing)} of {len(cherry)} commits not on main"


def remote_check(repo: str, main: str, upstream: str | None) -> tuple[bool, str | None]:
    """Returns (blocks, note): a remote branch with changes main lacks blocks the removal."""
    if not upstream:
        return False, None
    state, _ = classify(repo, main, upstream, None)
    if state != "landed":
        return True, f"{upstream} has changes main lacks"
    return False, f"{upstream} still exists, delete it by hand if wanted"


def leftovers(path: str, main_checkout: str, disposable: list[str]) -> tuple[list[str], list[str]]:
    """Returns (changed, precious): modified or untracked files, and ignored files nothing rebuilds."""
    status = git("status", "--porcelain", "--untracked-files=all", "--ignored=matching", cwd=path)
    changed, precious = [], []
    for line in status.splitlines():
        code, name = line[:2], line[3:].strip('"')
        if code != "!!":
            changed.append(name)
            continue
        if set(Path(name.rstrip("/")).parts) & REBUILDABLE or name.endswith(REBUILDABLE_SUFFIXES):
            continue
        if any(fnmatch.fnmatch(name.rstrip("/"), pattern.rstrip("/")) for pattern in disposable):
            continue
        # A copy of a file the main checkout also holds, such as a local settings file, loses nothing
        source, copy = Path(main_checkout, name), Path(path, name)
        if source.is_file() and copy.is_file() and filecmp.cmp(source, copy, shallow=False):
            continue
        precious.append(name)
    return changed, precious


def inside(directory: str) -> bool:
    here, target = os.path.realpath(os.getcwd()), os.path.realpath(directory)
    return here == target or here.startswith(target + os.sep)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--main", help="the branch work lands on (default: origin/HEAD, then main, master)")
    parser.add_argument("--clean", action="store_true", help="remove landed worktrees and branches")
    parser.add_argument("--disposable", action="append", default=[], help="glob of ignored paths safe to delete")
    parser.add_argument("--repo", default=".", help="any folder inside the repository (default: current)")
    args = parser.parse_args()

    entries = worktrees(os.path.abspath(args.repo))
    main_checkout = entries[0]["worktree"]
    repo = main_checkout
    main_branch = args.main or detect_main(repo)
    remote = upstreams(repo)

    rows: list[tuple[str, str, str, str]] = []
    actions: dict[str, list[str]] = {}
    used_branches = {entries[0].get("branch", "").removeprefix("refs/heads/")}

    for entry in entries[1:]:
        path = entry["worktree"]
        branch = entry.get("branch", "").removeprefix("refs/heads/") or None
        used_branches.add(branch)
        shown = os.path.relpath(path, main_checkout)
        name = f"{shown} [{branch or 'detached at ' + entry.get('HEAD', '')[:9]}]"

        if "prunable" in entry or not os.path.isdir(path):
            rows.append(("worktree", name, "prunable", "the folder is gone; prune"))
            actions["prune stale worktree entries"] = ["worktree", "prune"]
            continue
        if branch == main_branch:
            rows.append(("worktree", name, "main", "the main branch itself; keep"))
            continue

        state, detail = classify(repo, main_branch, entry["HEAD"], branch)
        blocks, note = remote_check(repo, main_branch, remote.get(branch)) if branch else (False, None)
        if blocks:
            state = "ahead"
        reasons = [note] if note else []
        if "locked" in entry:
            reasons.append("locked" + (f" ({entry['locked']})" if isinstance(entry["locked"], str) else ""))
        changed, precious = leftovers(path, main_checkout, args.disposable)
        if changed:
            reasons.append(f"{len(changed)} modified or untracked: {', '.join(changed[:5])}")
        if precious:
            reasons.append(f"ignored files nothing rebuilds: {', '.join(precious[:5])}")
        if inside(path):
            reasons.append("the current directory is inside it")

        removable = state == "landed" and not blocks and not ("locked" in entry or changed or precious or inside(path))
        rows.append(("worktree", name, state, "; ".join([detail] + reasons) + ("; remove" if removable else "; keep")))
        if removable:
            actions[f"remove worktree {shown}"] = ["worktree", "remove", path]
            if branch:
                actions[f"delete branch {branch}"] = ["branch", "-D", branch]

    for branch in git("for-each-ref", "--format=%(refname:short)", "refs/heads", cwd=repo).split():
        if branch == main_branch or branch in used_branches:
            continue
        state, detail = classify(repo, main_branch, branch, branch)
        blocks, note = remote_check(repo, main_branch, remote.get(branch))
        if blocks:
            state = "ahead"
        detail = "; ".join(part for part in (detail, note) if part)
        if state == "landed":
            rows.append(("branch", branch, state, f"{detail}; delete"))
            actions[f"delete branch {branch}"] = ["branch", "-D", branch]
        else:
            rows.append(("branch", branch, state, f"{detail}; keep"))

    print(f"main checkout: {main_checkout}, main branch: {main_branch}")
    if not rows:
        print("no other worktrees or branches")
    width = max((len(row[1]) for row in rows), default=0)
    for kind, name, state, detail in rows:
        print(f"{kind:8}  {name:{width}}  {state:8}  {detail}")

    if not args.clean:
        if actions:
            print("\nrun again with --clean to: " + ", ".join(actions))
        return 0

    failed = False
    for label, command in actions.items():
        result = subprocess.run(["git", *command], cwd=repo, capture_output=True, text=True)
        if result.returncode == 0:
            print(f"done: {label}")
        else:
            failed = True
            print(f"failed: {label}: {result.stderr.strip()}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
