#!/usr/bin/env python3
"""Checks every plugin and skill against the Agent Skills specification, both marketplace files and this
repository's conventions.

Usage: python3 tools/validate_skills.py
Exits with 1 when a rule is broken. Needs only the Python standard library.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PLUGINS = ROOT / "plugins"
CLAUDE_MARKETPLACE = ROOT / ".claude-plugin" / "marketplace.json"
CODEX_MARKETPLACE = ROOT / ".agents" / "plugins" / "marketplace.json"
NAME = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
LINK = re.compile(r"\]\(([^)#\s]+)(?:#[^)]*)?\)")

# The specification allows 500 lines; this repository keeps SKILL.md shorter and moves detail into references/
MAX_SKILL_LINES = 300

errors: list[str] = []
warnings: list[str] = []


def relative(path: Path) -> str:
    return str(path.relative_to(ROOT))


def load_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text())
    except FileNotFoundError:
        errors.append(f"{relative(path)}: missing")
    except json.JSONDecodeError as error:
        errors.append(f"{relative(path)}: invalid JSON ({error})")
    return {}


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
            errors.append(f"{relative(file)}: broken link {target}")


def check_skill(skill: Path, readme: str) -> None:
    skill_md = skill / "SKILL.md"
    if not skill_md.exists():
        errors.append(f"{relative(skill)}: missing SKILL.md")
        return

    text = skill_md.read_text()
    fields = frontmatter(text)
    if fields is None:
        errors.append(f"{relative(skill_md)}: does not start with a --- frontmatter block")
        return

    name = fields.get("name", "")
    description = fields.get("description", "")
    if not NAME.match(name) or len(name) > 64:
        errors.append(f"{relative(skill_md)}: name '{name}' must be 1-64 lowercase letters, digits and single hyphens")
    if name != skill.name:
        errors.append(f"{relative(skill_md)}: name '{name}' must match the folder name '{skill.name}'")
    if not 1 <= len(description) <= 1024:
        errors.append(f"{relative(skill_md)}: description has {len(description)} characters, allowed 1-1024")

    lines = text.count("\n") + 1
    if lines > 500:
        errors.append(f"{relative(skill_md)}: {lines} lines, the specification allows 500")
    elif lines > MAX_SKILL_LINES:
        warnings.append(f"{relative(skill_md)}: {lines} lines, this repository aims for {MAX_SKILL_LINES}")

    for markdown in skill.rglob("*.md"):
        check_links(markdown, markdown.read_text())

    if readme and relative(skill) not in readme:
        errors.append(f"{relative(skill)}: not listed in README.md")


readme = (ROOT / "README.md").read_text() if (ROOT / "README.md").exists() else ""
claude = load_json(CLAUDE_MARKETPLACE)
codex = load_json(CODEX_MARKETPLACE)

if claude.get("name") != codex.get("name"):
    errors.append("the Claude and Codex marketplaces must share one name")

claude_entries = {entry.get("name"): entry for entry in claude.get("plugins", [])}
codex_entries = {entry.get("name"): entry for entry in codex.get("plugins", [])}

plugin_dirs = sorted(path for path in PLUGINS.iterdir() if path.is_dir()) if PLUGINS.exists() else []
if not plugin_dirs:
    errors.append("plugins/ holds no plugin folders")

skills_checked = 0
skill_owners: dict[str, Path] = {}
for plugin in plugin_dirs:
    expected_source = f"./{relative(plugin)}"

    manifest = load_json(plugin / "plugin.json")
    if manifest:
        if manifest.get("name") != plugin.name:
            errors.append(f"{relative(plugin)}/plugin.json: name must be '{plugin.name}'")
        if not manifest.get("version"):
            errors.append(f"{relative(plugin)}/plugin.json: needs a version, it is how installs pick up changes")
        if not manifest.get("description"):
            errors.append(f"{relative(plugin)}/plugin.json: needs a description")

    claude_entry = claude_entries.get(plugin.name)
    if claude_entry is None:
        errors.append(f"{relative(plugin)}: missing from {relative(CLAUDE_MARKETPLACE)}")
    elif claude_entry.get("source") != expected_source:
        errors.append(f"{relative(CLAUDE_MARKETPLACE)}: '{plugin.name}' source must be '{expected_source}'")

    codex_entry = codex_entries.get(plugin.name)
    if codex_entry is None:
        errors.append(f"{relative(plugin)}: missing from {relative(CODEX_MARKETPLACE)}")
    elif codex_entry.get("source", {}).get("path") != expected_source:
        errors.append(f"{relative(CODEX_MARKETPLACE)}: '{plugin.name}' source.path must be '{expected_source}'")

    if readme and f"{plugin.name}@{claude.get('name')}" not in readme:
        errors.append(f"{relative(plugin)}: README.md has no install line for {plugin.name}@{claude.get('name')}")

    skills_dir = plugin / "skills"
    skills = sorted(path for path in skills_dir.iterdir() if path.is_dir()) if skills_dir.exists() else []
    if not skills:
        errors.append(f"{relative(plugin)}: skills/ holds no skill folders")
    for skill in skills:
        # A copied skill folder loses its plugin, so two plugins may not ship a skill under one name
        if skill.name in skill_owners:
            errors.append(f"{relative(skill)}: name also used by {relative(skill_owners[skill.name])}")
        skill_owners[skill.name] = skill
        check_skill(skill, readme)
        skills_checked += 1

for name in sorted(set(claude_entries) | set(codex_entries)):
    if not (PLUGINS / str(name)).is_dir():
        errors.append(f"marketplace entry '{name}' has no folder under plugins/")

for markdown in (ROOT / "README.md", ROOT / "AGENTS.md"):
    if markdown.exists():
        check_links(markdown, markdown.read_text())

for warning in warnings:
    print(f"warning: {warning}")
for error in errors:
    print(f"error: {error}")
print(f"{len(plugin_dirs)} plugins and {skills_checked} skills checked, {len(errors)} errors, {len(warnings)} warnings")
sys.exit(1 if errors else 0)
