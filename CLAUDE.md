# gstack

This project uses [gstack](https://github.com/garrytan/gstack) — Claude Code skills installed at `~/.claude/skills/gstack`.

Available skills (use the matching slash command for the task at hand):

| Phase | Skill | Purpose |
|---|---|---|
| Think | `/office-hours` | Product interrogation: six forcing questions before coding |
| Plan | `/plan-ceo-review` | Strategic scope challenge |
| Plan | `/plan-eng-review` | Architecture lock with diagrams and test planning |
| Plan | `/plan-design-review` | Design audit with 0-10 ratings per dimension |
| Plan | `/plan-devex-review` | Developer experience review |
| Plan | `/autoplan` | Full review pipeline (CEO -> design -> DX -> eng), auto-run |
| Build | `/spec` | Convert intent into precise, executable specifications |
| Build | `/design-consultation` | Build complete design systems from scratch |
| Build | `/design-shotgun` | Generate 4-6 mockup variants |
| Build | `/design-html` | Convert mockups to production HTML/CSS |
| Review | `/review` | Staff engineer code audit; auto-fixes obvious issues |
| Review | `/design-review` | Designer-driven audit with atomic fix commits |
| Review | `/cso` | OWASP Top 10 + STRIDE security audit |
| Review | `/investigate` | Systematic root-cause debugging |
| Test | `/qa` | Browser-based testing; finds and fixes bugs |
| Test | `/qa-only` | Bug reporting without code changes |
| Test | `/devex-review` | Live DX testing; compares against plan scores |
| Test | `/benchmark` | Baseline and compare page performance metrics |
| Ship | `/ship` | Sync, test, audit coverage, push, open PR |
| Ship | `/land-and-deploy` | Merge PR, verify CI, deploy, confirm health |
| Ship | `/canary` | Post-deploy monitoring for errors and regressions |
| Reflect | `/retro` | Weekly team retrospective with metrics |
| Reflect | `/document-release` | Update docs to match shipped changes |
| Reflect | `/document-generate` | Create missing docs using Diataxis framework |

Other useful skills:
- `/browse`, `/scrape`, `/setup-browser-cookies` — use `/browse` for any web browsing task
- `/careful`, `/freeze`, `/guard`, `/unfreeze` — guard against destructive/unintended edits
- `/pair-agent` — coordinate multiple AI agents in a shared browser
- `/diagram`, `/make-pdf` — diagram and PDF generation
- `/learn`, `/setup-gbrain`, `/sync-gbrain` — persistent agent memory / knowledge base
- `/gstack-upgrade` — update gstack to the latest version
- `/codex` — independent review pass via OpenAI Codex CLI
- `/ios-qa`, `/ios-fix`, `/ios-design-review`, `/ios-clean`, `/ios-sync` — iOS device workflows (if applicable)

Workflow cycle: **Think -> Plan -> Build -> Review -> Test -> Ship -> Reflect**.
