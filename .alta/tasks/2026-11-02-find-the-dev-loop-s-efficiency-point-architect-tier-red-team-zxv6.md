---
title: "Find the dev-loop's efficiency point: architect tier, red-team value, convergence criteria"
kind: improvement
status: pending
created: 2026-11-02
summary: "The devnull-step loop (derive -> worker -> battery -> architect -> red team -> user) works but its cost curve is unmeasured: the architect runs glm-5.3@xhigh every round, the mixed red team adds two more passes, and P19 needed five review cycles. Re-evaluate the tier ladder (complex for structure, standard for delta rounds, possibly glm-5.3-flash for red-team-heavy flows), measure findings-per-round decay, and consider piloting the loop on a second project to see which costs generalize. Use the compare-models script for the tier questions."
---

## Why

P19 needed five review cycles and each architect pass ran on glm-5.3@xhigh; the findings decayed 3 -> 4 -> 2 -> 1 (wrong) -> 0, which suggests later rounds do not need the most expensive eyes. The loop is now battle-tested but unmeasured: no data on cost per finding, tier value per round, or red-team marginal catch rate.

## What to do

- Run `compare-models` (sample script) on architect-review tasks: glm-5.3@xhigh vs glm-5.3@high vs glm-5.3-flash:high over the same step dossier; compare findings caught per token.
- Instrument the loop: log per-round token totals (architect, red team, worker) and findings count; a few P-steps of data is enough to see the decay curve.
- Decide the defaults: architectTier ladder, redTeamModels mix, maxReviewRounds, trivialFastPath threshold.
- Optional: pilot the loop unchanged on a second project to see which costs are DevNull-specific.

## Where

- `.alta/scripts/devnull-step.js` (tier ladder: archTier(), redTeamModels, budget)
- `.alta/derived/*.verdict.json` (per-step review history already recorded)
- `compare-models` sample script under the CodeAlta install
