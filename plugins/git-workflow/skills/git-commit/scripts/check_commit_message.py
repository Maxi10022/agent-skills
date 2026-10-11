#!/usr/bin/env python3
"""Checks a commit message draft against the git-commit skill before it is committed.

Usage:
  check_commit_message.py MESSAGE_FILE
  check_commit_message.py - < MESSAGE_FILE
  check_commit_message.py --layout-only MESSAGE_FILE   for repositories with a convention of their own

Errors exit with 1 and must be fixed. Warnings exit with 0 but name something worth a second look.
Needs only the Python standard library.
"""
import argparse
import re
import sys

SOFT_SUBJECT = 72
HARD_SUBJECT = 100
WRAP = 72
LONG_BODY = 20

TRAILER = re.compile(r"^[A-Za-z][A-Za-z0-9-]*: \S")
PREFIX = re.compile(r"^[a-z]+(\([^)]*\))?!?: ")
LOCAL_PATH = re.compile(r"(?<![\w.~])(/home/|/Users/|/tmp/|/mnt/|/var/folders/|[A-Za-z]:\\)")
SESSION_TALK = re.compile(
    r"\b(as requested|as asked|as discussed|you asked|per your|this session|this conversation|"
    r"I have|I've|I added|I changed|I fixed|we've)\b",
    re.I,
)

# Present and imperative forms an agent writes instead of the past tense this style uses
PRESENT_FORMS = {
    "add", "adds", "update", "updates", "fix", "fixes", "remove", "removes", "drop", "drops", "refactor",
    "refactors", "move", "moves", "rename", "renames", "implement", "implements", "simplify", "simplifies",
    "extend", "extends", "bump", "bumps", "change", "changes", "create", "creates", "delete", "deletes",
    "introduce", "introduces", "improve", "improves", "use", "uses", "make", "makes", "replace", "replaces",
    "split", "splits", "clean", "cleans", "configure", "configures", "enable", "enables", "disable",
    "disables", "allow", "allows", "prevent", "prevents", "ensure", "ensures", "handle", "handles",
    "support", "supports", "convert", "converts", "document", "documents", "test", "tests", "upgrade",
    "upgrades", "set", "sets", "keep", "keeps", "let", "lets", "rewrite", "rewrites", "restore", "restores",
}

# Past tenses that do not end in -ed
IRREGULAR_PAST = {
    "built", "made", "split", "set", "reset", "kept", "wrote", "rewrote", "ran", "put", "cut", "undid",
    "hid", "shut", "left", "got", "brought", "took", "gave", "found", "bound", "sent", "spent", "read",
    "re-read", "led", "fed", "let", "dealt", "met", "sped", "swept", "taught", "threw", "began", "became",
    "chose", "drew", "grew", "held", "lost", "meant", "paid", "rebuilt", "said", "saw", "shown", "sold",
    "stood", "told", "thought", "understood", "won", "overrode", "broke", "froze", "spun", "stuck", "struck",
}


def lint(text: str, layout_only: bool) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []

    if "\\n" in text:
        errors.append("the message contains a literal \\n; write real line breaks (a heredoc or a file)")

    lines = [line.rstrip() for line in text.strip("\n").splitlines()]
    if not lines or not lines[0].strip():
        errors.append("the message is empty or starts with a blank line")
        return errors, warnings

    subject = lines[0]
    rest = lines[1:]

    # git writes merge and revert subjects itself; their layout is still checked, their wording is not
    generated = subject.startswith(("Merge ", 'Revert "', "fixup! ", "squash! ", "amend! "))

    length = len(subject)
    if length > HARD_SUBJECT:
        errors.append(
            f"subject has {length} characters, the limit is {HARD_SUBJECT}: keep the target and one change, "
            "move the rest into the body or split the commit"
        )
    elif length > SOFT_SUBJECT:
        warnings.append(f"subject has {length} characters; aim for {SOFT_SUBJECT} or fewer")
    if ";" in subject:
        errors.append("subject contains a semicolon, a sign of two changes: split the commit or move one into the body")
    if subject.endswith(".") and not subject.endswith("..."):
        errors.append("subject ends with a period")
    if subject != subject.lstrip():
        errors.append("subject starts with whitespace")

    if not layout_only and not generated:
        if PREFIX.match(subject):
            errors.append("subject starts with a type prefix such as 'fix:' or 'feat(x):'; this style has none")
        first = subject.split()[0].strip("`\"'")
        if first[:1].islower():
            errors.append(f"subject starts with lowercase '{first}'; start with a capitalised past-tense verb")
        elif first.lower() in PRESENT_FORMS and first.lower() not in IRREGULAR_PAST:
            errors.append(f"subject starts with '{first}'; use the past tense ('Added', 'Updated', 'Fixed', ...)")
        elif not first.lower().endswith("ed") and first.lower() not in IRREGULAR_PAST:
            warnings.append(f"subject starts with '{first}', which does not read as a past-tense verb")
        if len(subject.split()) <= 2:
            warnings.append("subject has two words or fewer; name the target and what changed")
        if subject.count(",") >= 3:
            warnings.append(
                "subject has three or more commas: name the target and at most two changes, "
                "put the rest into the body"
            )

    if rest and rest[0].strip():
        errors.append("line 2 must be blank: separate the subject from the body")

    # The trailer block is the last paragraph when every line in it is a 'Key: value' trailer
    paragraphs: list[list[tuple[int, str]]] = []
    current: list[tuple[int, str]] = []
    for number, line in enumerate(rest, start=2):
        if line.strip():
            current.append((number, line))
        elif current:
            paragraphs.append(current)
            current = []
    if current:
        paragraphs.append(current)

    trailers: list[tuple[int, str]] = []
    if paragraphs and all(TRAILER.match(line) for _, line in paragraphs[-1]):
        trailers = paragraphs.pop()
    body = [item for paragraph in paragraphs for item in paragraph]

    for number, line in body:
        if len(line) > WRAP and " " in line.strip() and not line.startswith(("    ", "\t")):
            errors.append(f"line {number} has {len(line)} characters; wrap the body at {WRAP}")
    if len(body) > LONG_BODY:
        warnings.append(
            f"the body has {len(body)} lines; a body this long usually describes several changes, "
            "consider splitting the commit"
        )

    for number, line in [(1, subject)] + body:
        if LOCAL_PATH.search(line):
            errors.append(f"line {number} contains a local absolute path; name the file relative to the repository")
        if SESSION_TALK.search(line):
            warnings.append(
                f"line {number} talks about the session or its people ({SESSION_TALK.search(line).group(0)!r}); "
                "describe the change itself"
            )

    for number, line in trailers:
        key = line.split(":", 1)[0]
        if key.lower() in ("co-authored-by", "signed-off-by") and not re.search(r": .+ <[^<>@\s]+@[^<>\s]+>$", line):
            errors.append(f"line {number}: {key} needs the form 'Name <email>'")
    for number, line in body:
        if re.match(r"^(Co-authored-by|Co-Authored-By|Signed-off-by): ", line):
            errors.append(f"line {number}: trailers belong in a final paragraph of their own, after a blank line")

    return errors, warnings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("file", help="the message file, or - for standard input")
    parser.add_argument(
        "--layout-only",
        action="store_true",
        help="check only the layout (lengths, blank line, wrapping, trailers), not this style's wording",
    )
    args = parser.parse_args()

    text = sys.stdin.read() if args.file == "-" else open(args.file, encoding="utf-8").read()
    errors, warnings = lint(text, args.layout_only)

    for warning in warnings:
        print(f"warning: {warning}")
    for error in errors:
        print(f"error: {error}")
    if errors:
        print(f"{len(errors)} errors: fix the message before committing")
        return 1
    print("ok" if not warnings else f"ok with {len(warnings)} warnings")
    return 0


if __name__ == "__main__":
    sys.exit(main())
