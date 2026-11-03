---
title: "Close the rule-11 arity regressions in Remotes.fs: toggleReaction, addOrgMembers, setOrgMemberRole, removeOrgMember"
kind: problem
status: pending
created: 2026-11-03
summary: "P21 landed four 4-parameter module functions (Remotes.fs:435, 1041, 1071, 1102) against AGENTS rule 11; gates.ps1 has no arity gate so nothing flagged them. Found and verified by the role-eval red team (judge-confirmed in .alta/derived/role-eval-report.md)."
---

## Why

The role-eval red-team pass (glm-5.3-flash@high) found four 4-parameter functions introduced by the P21 landing: `toggleReaction` (Remotes.fs:435), `addOrgMembers` (:1041), `setOrgMemberRole` (:1071), `removeOrgMember` (:1102). AGENTS rule 11 caps parameters at 3 and asks for a contextual record. The production architect and red team both missed them; `audits/gates.ps1` has no arity gate.

## What to do

Give each function a `[<Struct>]` contextual record for its short-lived inputs (env stays a separate intersection parameter). Add a gate or battery grep for 4+-arity `let` in UI module files so the class cannot land silently again. Run fantomas, both suites, gates.

## Where

- src/DevNull.Client.UI/Remotes.fs:435, 1041, 1071, 1102
- audits/gates.ps1 (new arity pattern)
- tests as needed (behavior unchanged; assert through the public surface)
