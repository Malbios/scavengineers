# Localization

Full detail: `docs/project-plan.md` §5 (Localization row), §7 Phase 0.

## Settled

- **String tables from commit #1** — no hardcoded display text anywhere in gameplay/UI code, from the very first UI element.
- **German + English** are the likely target languages, but the string-table scaffolding itself should not assume a fixed language list.
- This is explicitly called out as "cheap now, painful to retrofit" — treat any hardcoded user-facing string as a bug, not a shortcut, even in early greybox/prototype code.

## Before editing this subsystem

Any new user-facing text (UI labels, item names, log/notification messages) goes through the string-table system, not a literal in code — including placeholder/greybox text.
