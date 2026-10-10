# Instructions for AI agents

This repository is a library of Agent Skills (https://agentskills.io/specification), grouped by topic into plugins.
Each folder under `plugins/<plugin>/skills/` is one skill. The first part below is for agents using the skills on a
user's task; the second is for agents adding or changing skills here.

## Using a skill from this repository

1. **Choose by description.** Read only the frontmatter (`name`, `description`) of each
   `plugins/*/skills/*/SKILL.md`. Load a skill's full `SKILL.md` when the user's task matches its description,
   including its "Not for" boundary.
2. **Follow `SKILL.md` and open the rest on demand.** Read a file in `references/` or `templates/` only when
   `SKILL.md` points to it for the step you are on. Each link says when it is worth reading.
3. **Translate the examples.** Snippets use a fictional domain (`Webhook`, `Customer`, `ShareLink`, `Book`) and the
   namespace `YourApp.Tests.*`. Carry over the pattern with the user's own types and names; never copy the
   fictional names into their code.
4. **Adapt templates before use.**
   - Rename the namespace.
   - Check the package versions the skill assumes (each `SKILL.md` names them) against the user's project.
   - Templates assume the shared test namespaces are global usings; add `using` lines if the project has none.
5. **The project's own rules win.** If the user's repository has its own conventions (`AGENTS.md`, `CLAUDE.md`,
   `.editorconfig`, existing test bases) that conflict with a skill, follow the project, and name the conflict in
   your reply.
6. **Report what you verified.** Say whether you built and ran the tests. Do not describe unrun code as working.

## Adding or changing a skill

### Format

- **Folder and name.** `plugins/<plugin>/skills/<name>/SKILL.md`, where `<name>` uses lowercase letters, digits and
  single hyphens, at most 64 characters, and equals the frontmatter `name`. Skill names stay unique across plugins,
  because a copied skill folder loses its plugin.
- **Plugins.** A plugin is one topic. It has a `plugin.json` at its root (`name` equal to the folder name, `version`,
  `description`) and one entry in each marketplace file, `.claude-plugin/marketplace.json` for Claude Code and
  `.agents/plugins/marketplace.json` for Codex, with the source `./plugins/<plugin>`. Both marketplaces are named
  `maxi10022`; `agent-skills` is reserved by Claude Code.
- **Description.** 1-1024 characters, in the frontmatter. It decides when agents load the skill, so it states:
  - what the skill covers;
  - the concrete situations and phrases that should trigger it;
  - one "Not for ..." boundary.
- **SKILL.md length.** At most 300 lines in this repository (the specification allows 500). Put long examples,
  catalogues and before/after pairs in `references/`, linked from `SKILL.md` one level deep, each with a sentence
  saying when to read it.
- **Templates.** Files an agent copies into a project go in `templates/`. They must compile, or be clearly marked as
  excerpts that need the user's app. Use a neutral namespace (`YourApp.*`) and say in one comment line what to
  adapt.

### Content

- **Explain the reason behind each rule.** "Wait on the DOM, because a child's parameters update before its markup"
  carries over to new cases; a bare "MUST" does not.
- **Keep examples anonymous.** No real company, product, customer, person or internal domain names, no local paths,
  no hostnames and no secrets. Use a fictional domain, consistently within a skill.
- **Only claim what was verified.** Library behaviour that a skill relies on (API names, defaults, failure messages)
  must be checked against the package or its documentation. Name the versions it was checked against.
- **Treat descriptions as stable.** Changing a description changes when agents use the skill. Edit it only when
  triggering is the point of the change.

### Before you finish

- Raise the `version` in the plugin's `plugin.json` whenever a skill in it changes. Claude Code keeps installed
  plugins on their version until this number changes, so without it nobody receives the change.
- Add or update the skill's row in the plugin's table in `README.md`.
- Run `python3 tools/validate_skills.py`. It must report 0 errors. With the reference validator installed, also run
  `skills-ref validate plugins/<plugin>/skills/<name>`. With Claude Code installed, also run
  `claude plugin validate .`.
- When templates changed, compile them in a scratch project against the package versions the skill names.
- Commit one skill per commit, with a message that says what changed and why.
