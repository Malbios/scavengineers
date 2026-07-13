---
name: code-audit
description: Spawns parallel subagents to review code quality/architecture adherence, find missing test coverage, and flag forward-looking risks (TODOs, localization/save/asset gaps) in this repo. Manually triggered only — not something to auto-invoke. Defaults to just what's changed since main; pass "full" to scan the whole repo, or a path to scope to one area.
disable-model-invocation: true
---

Runs a multi-agent quality pass over this repo and reports the results with `ReportFindings`.
This is a local, on-demand check distinct from the hosted `/code-review ultra` — use it for a
quick, free, repo-aware pass; suggest `/code-review ultra` instead if the user wants a deeper
cloud review.

## 1. Determine scope

Read the skill's `args`:

- **No args, or args mention "changed"/"diff"** (default): find what's changed on the current
  branch relative to `main` — `git fetch origin main --quiet` then
  `git diff --name-only origin/main...HEAD` (fall back to local `main` if there's no remote
  tracking branch). This is the file list every subagent scopes to.
  - If the list is empty (already on `main`, nothing to compare), say so and ask whether to run
    a full-repo pass instead rather than silently doing one.
- **Args contain "full" or "repo"**: scope every subagent to the whole repository (skip the git
  diff step entirely).
- **Args name a path** (e.g. `Scripts/Ship`, `Scavengineers.Sim`): scope every subagent to that
  path specifically.

Always read `CLAUDE.md` (both this repo's and, if relevant, referenced `docs/architecture/*.md`
extracts) before dispatching agents — every subagent prompt below needs the actual rule text
quoted in, not just a pointer to "the project rules," since a fresh subagent has no memory of
this conversation.

## 2. Dispatch three subagents in parallel

Single message, three `Agent` tool calls, `subagent_type: "general-purpose"` (not `Explore` —
this is analysis/judgment work, not code location). Each prompt must be self-contained: state
the scope (exact file list or path from step 1), quote the specific CLAUDE.md rules relevant to
that lane, and explicitly instruct **read-only investigation, no edits** and a concise, scannable
report back (file, line if applicable, one-sentence summary, why it matters). Don't let any
agent write or edit files.

**Lane A — Architecture & code quality.** Check the scoped files against this repo's
non-negotiables (quote them from `CLAUDE.md`): sim/presentation split (no Godot node types
leaking into `Scavengineers.Sim`), data-driven design (ship/item/hazard data hardcoded into
scenes or C# instead of JSON/`.tres`), the one-solver rule (a new flood-fill/graph/connectivity
implementation instead of reusing the shared one), ship-model tiering (walls doubled on an edge
instead of shared, tier violations), verb-driven interaction (a hardcoded one-off interaction
path bypassing the verb system), "ship is an instance" (a reach for a global/singleton "the
ship"), time-acceleration rules, and save/serialization rules (unstable IDs, missing schema
version handling, a missing-ID path that would crash instead of falling back to a placeholder +
log). Also flag generic smells: duplicated logic that should be extracted (this repo's own
`godot-csharp-conventions` skill's DRY rule), dead code, magic numbers without the established
`// Placeholder/tunable` comment convention this codebase uses everywhere else.

**Lane B — Test coverage gaps.** Cross-reference the scoped files against the three test
projects (`Scavengineers.Sim.Tests`, `Scavengineers.Scripts.Tests`, `Scavengineers.NodeTests`).
Flag: sim logic (`Scavengineers.Sim/**`) with no corresponding test; `Player`/verb-target scripts
with real conditional/branching logic and no test coverage; places where a past commit message
or code comment explicitly notes "no automated test" or similar (grep for it) — list these as
known, tracked gaps rather than re-flagging as new findings, but don't drop them either; a
previously-fixed bug (search recent "Fix"-titled commits touching the scoped files) that didn't
get a regression test alongside the fix.

**Lane C — Forward-looking risk.** Grep the scoped files for `TODO`/`FIXME`/`HACK`/"for now"/
"later system"/"not yet implemented"-style comments — this exact pattern is how a real gap
(`SuitResources`' un-consequenced O2/power drain) got found and fixed earlier in this project.
Also check: every `Tr("XXX_YYY")` key referenced in scoped C# actually has a row in
`Localization/strings.csv` with both `en`/`de` columns filled in, and flag if `strings.csv` looks
newer (by git history) than the compiled `.translation` files (the exact "forgot to reimport"
bug this project has hit before — see the `feedback-localization-reimport` lesson if readable);
every texture/model/sound/AI-generated asset referenced in a touched `.tscn` has a matching entry
in `docs/asset-provenance.md`; new `PlayerSaveData`/`BuildTargetSaveData` fields have safe
defaults for an old save missing them; any `.tscn` node whose header (`[node ...]`) carries a
property that Godot's parser would silently ignore outside the recognized header keys (the exact
class of bug this project already hit once — see `feedback-tscn-node-header-attributes` if
readable).

## 3. Consolidate and report

Once all three subagents return, merge their findings into one list: de-duplicate anything two
lanes both flagged, drop anything too speculative/low-value to act on, and rank the rest
most-severe-first. Call `ReportFindings` once with the consolidated list — don't also print the
findings as plain text, and don't call it per-lane. Set `level` to roughly match the scope size
(`low`/`medium` for a diff-scoped run, `high` for a full-repo run). If a lane found nothing,
that's fine — an empty findings array is a valid, useful result, not a failure.
