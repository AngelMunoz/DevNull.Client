---
title: "File the Phase 1 audit's §10 findings as GitHub issues"
kind: gap
status: later
created: 2026-11-01
---

## Why
The Phase 1 audit (repo root, `2026-11-phase1-mvuish-audit.md`, §10) ends with 18 ready issue bodies — 2 leaks, 13 bugs, 3 verification suspects — and ROADMAP.md's Phase 1 rule says "what the audit finds that is a gap or a bug becomes an issue, not phase work". None of them is filed yet.

## What to do
Create one GitHub issue per §10 entry (LEAK-1, LEAK-2, BUG-3 … VERIFY-18), each with the section's text as the body, severity-ordered titles, and the audit report as the reference. Keep bodies in markdown files and create issues with `gh` per AGENTS.md rule 6.

## Where
- `2026-11-phase1-mvuish-audit.md` §10
- `.alta/audit/*.md` (line-number evidence)
