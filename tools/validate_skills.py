#!/usr/bin/env python3
"""Checks every skill in skills/ against the Agent Skills specification and this repository's conventions.

Usage: python3 tools/validate_skills.py
Exits with 1 when a rule is broken. Needs only the Python standard library.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SKILLS = ROOT / "skills"
NAME = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
LINK = re.compile(r"\]\(([^)#\s]+)(?:#[^)]*)?\)")

# The specification allows 500 lines; this repository keeps SKILL.md shorter and moves detail into references/
MAX_SKILL_LINES = 300

errors: list[str] = []
warnings: list[str] = []


def frontmatter(text: str) -> dict[str, str] | None:
    match = re.match(r"^---\n(.*?)\n---\n", text, re.S)
    if not match:
        return None
    fields = {}
    for line in match.group(1).splitlines():
        if ":" in line and not line.startswith((" ", "\t")):
            key, value = line.split(":", 1)
            fields[key.strip()] = value.strip()
    return fields


def check_links(file: Path, text: str) -> None:
    for target in LINK.findall(text):
        if target.startswith(("http://", "https://", "mailto:")):
            continue
        if not (file.parent / target).exists():
            errors.append(f"{file.relative_to(ROOT)}: broken link {target}")


skill_dirs = sorted(path for path in SKILLS.iterdir() if path.is_dir())
if not skill_dirs:
    errors.append("skills/ holds no skill folders")

readme = (ROOT / "README.md").read_text() if (ROOT / "README.md").exists() else ""

for skill in skill_dirs:
    skill_md = skill / "SKILL.md"
    if not skill_md.exists():
        errors.append(f"{skill.name}: missing SKILL.md")
        continue

    text = skill_md.read_text()
    fields = frontmatter(text)
    if fields is None:
        errors.append(f"{skill.name}: SKILL.md does not start with a --- frontmatter block")
        continue

    name = fields.get("name", "")
    description = fields.get("description", "")
    if not NAME.match(name) or len(name) > 64:
        errors.append(f"{skill.name}: name '{name}' must be 1-64 lowercase letters, digits and single hyphens")
    if name != skill.name:
        errors.append(f"{skill.name}: name '{name}' must match the folder name")
    if not 1 <= len(description) <= 1024:
        errors.append(f"{skill.name}: description has {len(description)} characters, allowed 1-1024")

    lines = text.count("\n") + 1
    if lines > 500:
        errors.append(f"{skill.name}: SKILL.md has {lines} lines, the specification allows 500")
    elif lines > MAX_SKILL_LINES:
        warnings.append(f"{skill.name}: SKILL.md has {lines} lines, this repository aims for {MAX_SKILL_LINES}")

    for markdown in skill.rglob("*.md"):
        check_links(markdown, markdown.read_text())

    if readme and f"skills/{skill.name}" not in readme:
        errors.append(f"{skill.name}: not listed in README.md")

for markdown in (ROOT / "README.md", ROOT / "AGENTS.md"):
    if markdown.exists():
        check_links(markdown, markdown.read_text())

for warning in warnings:
    print(f"warning: {warning}")
for error in errors:
    print(f"error: {error}")
print(f"{len(skill_dirs)} skills checked, {len(errors)} errors, {len(warnings)} warnings")
sys.exit(1 if errors else 0)
