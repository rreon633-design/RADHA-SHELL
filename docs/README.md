# RADHA SHELL — documentation

The complete product/engineering documentation set, matching the PRD table of contents
(sections 1–24) plus the repository policies.

## Product

| # | Document | What it answers |
|---|---|---|
| 1 | [Problem Statement](01-problem-statement.md) | Why Windows needs this shell |
| 2 | [Aim & Vision](02-aim-vision.md) | What RADHA SHELL is trying to be, and not be |
| 3 | [Objectives](03-objectives.md) | Measurable goals and their current status |
| 4 | [Target Audience](04-target-audience.md) | Who it is for, and their workflow |
| 5 | [Tech Stack](05-tech-stack.md) | Exact technologies, why chosen, what was rejected |
| 6 | [Architecture](06-architecture.md) | Layers, windows, threading, module map |
| 7 | [Design Philosophy & UI/UX](07-design-philosophy.md) | Motion, colour, density, interaction rules |

## Features

| # | Document | What it answers |
|---|---|---|
| 8 | [Features List](08-features.md) | Every feature and its real implementation status |
| 9 | [Novel Features](09-novel-features.md) | What is genuinely different here |
| 10 | [Backend Services](10-backend-services.md) | OS integrations, contracts, failure modes |
| 11 | [API & IPC Layer](11-api-ipc.md) | Protocol, command catalog, validation, audit |
| 12 | [Workflow & Working](12-workflow.md) | Startup, interaction flows, shutdown |

## Engineering

| # | Document | What it answers |
|---|---|---|
| 13 | [Setup & Installation](13-setup-installation.md) | Build, run, configure, uninstall |
| 14 | [Development Process](14-development-process.md) | How to change the code safely |
| 15 | [Phasewise Plan](15-phasewise-plan.md) | Phases, deliverables, exit criteria |
| 16 | [Performance](16-performance.md) | Budgets, measured numbers, CI gate |
| 17 | [Security](17-security.md) | Threat model, controls, verification |
| 18 | [Maintenance](18-maintenance.md) | Releases, migrations, observability |

## Analysis

| # | Document | What it answers |
|---|---|---|
| 19 | [Problematic Areas](19-problematic-areas.md) | Known weak points, stated honestly |
| 20 | [Risk Factors](20-risk-factors.md) | Risk register with mitigations |
| 21 | [Advantages](21-advantages.md) | Comparison against existing tools |
| 22 | [Future Scope](22-future-scope.md) | Where this goes after phase 3 |
| 23 | [Success Metrics](23-success-metrics.md) | KPIs, how each is measured |
| 24 | [Appendix](24-appendix.md) | Glossary, command reference, config reference, test inventory |

## Existing engineering docs

- [ARCHITECTURE.md](ARCHITECTURE.md) — deep dive into layers, windows, IPC and theming
- [BUILD.md](BUILD.md) — Windows prerequisites and first-build troubleshooting
- [ROADMAP.md](ROADMAP.md) — phased checklist with live status
- [SECURITY.md](../SECURITY.md) — disclosure policy and security summary
- [CONTRIBUTING.md](../CONTRIBUTING.md) — contribution rules
- [CHANGELOG.md](../CHANGELOG.md) — release history

## Reading order

New to the project: **1 → 2 → 5 → 6 → 8 → 13**.
Reviewing security: **11 → 17 → 19 → 20**.
Deciding whether to adopt it: **21 → 8 → 19 → 23**.

- [`VIRTUAL-DESKTOPS.md`](VIRTUAL-DESKTOPS.md) — per-build COM dispatch for desktop switching
- [`COMPARISON-CAELESTIA.md`](COMPARISON-CAELESTIA.md) — feature-by-feature comparison with caelestia-shell, gap register and improvement plan
