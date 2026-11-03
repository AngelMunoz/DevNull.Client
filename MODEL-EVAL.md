# Model eval for the dev loop — plain summary

Date: 2026-11-03. We tested every model in each job of the dev loop
(derive, worker, architect, red team). Every candidate got the same task.
A glm-5.3@xhigh judge scored the answers from 0 to 10 on four things:
accuracy, awareness, completeness, efficiency. The judge also checked the
answers against the real code, and re-ran builds and tests where it
mattered. Full data and evidence: `.alta/derived/role-eval-report.md`.

## Best model per job

| Job | Best | Second | One-line reason | Trust |
|---|---|---|---|---|
| Architect | glm-5.3-flash@high | glm-5.3-flash@xhigh / deepseek | All its directives were true. Zero invented findings. | Low — 1 run, test dossier had a leak |
| Red team | deepseek@xhigh | glm-5.3-flash@high | deepseek finds the hidden stuff; flash-high finds rule breaks others miss | Medium — 1 run, catches verified in code |
| Derive | glm-5.3@high | glm-5.3-flash@high | glm-high was the only one with every count exact; flash-high almost as good at half the cost | High — clean test |
| Worker | glm-5.3-flash@high | deepseek@xhigh | flash-high kept every useful comment and said what it skipped; deepseek deleted 1,129 comment lines that existed nowhere else | Medium — 1 run, judge re-ran the suites |

## Full scores

Judged 0-10: accuracy / awareness / completeness. "Wall" is real minutes
the session ran. "FP" = false or invented claims found by the judge.

### Architect (review step P21)

| Model | Scores | Wall | FP | Caught |
|---|---|---|---|---|
| glm-5.3-flash@high | 10 / 9.5 / 8 | 18.3 | 0 | duplicate line, dead open, deleted AGENTS rule |
| glm-5.3-flash@xhigh | 9 / 9 / 8 | 17.5 | 1 | the four 4-parameter functions |
| glm-5.3@xhigh | 9 / 8.5 / 6.5 | 7.7 | 1 | duplicate line (inside its directives), plus 2 unique catches |
| glm-5.3@high | 8 / 9 / 8 | 5.9 | 2 | sharpest single read; 1 wrong line number |
| deepseek@xhigh | 9.5 / 10 / 10 | 9.2 | 1 | most complete, exact lines |

### Red team (step P21, after an approval)

| Model | Scores | Wall | FP | Caught |
|---|---|---|---|---|
| deepseek@xhigh | 8 / 8 / 8 | 8.6 | 0 | known finding, broke the approval's story, 2 unique |
| glm-5.3-flash@high | 9 / 9 / 6 | 17.9 | 0 | the four 4-parameter functions (nobody else found them) |
| glm-5.3-flash@xhigh | 9 / 10 / 7 | 21.5 | 0 | found the known one but hid it in notes; did not report it as a finding |

### Derive (step P22, next step)

| Model | Scores | Wall | FP |
|---|---|---|---|
| glm-5.3@high | 8 / 9 / 9 | 7.1 | 3 small |
| glm-5.3-flash@high | 8 / 9 / 9 | 9.7 | 3 small |
| deepseek@xhigh | 9 / 7 / 8 | 4.6 | 0 |
| glm-5.3-flash@xhigh | 7 / 7 / 6 | 5.8 | 5 |

### Worker (implement step P22, each in its own copy of the repo)

| Model | Scores | Wall | Result |
|---|---|---|---|
| glm-5.3-flash@high | 9 / 9 / 7 | 31.7 | near-merge-ready; kept all useful comments; said what it skipped; tests green (judge re-ran them) |
| deepseek@xhigh | 4 / 4 / 9 | 16.7 | tests really run, but it deleted 1,129 comment lines that existed nowhere else, then reported the opposite |

## Three corrections (do not trust older notes)

1. "Three GLMs ruled without reading" was wrong. A metrics bug recorded
   only their last message. They did read — the sessions ran 8 to 18
   minutes and quote the code.
2. "All five caught the known finding" proves little. The repo contains
   `.alta/derived/P21.verdict.json`, and its text names that finding. Any
   candidate that reads the repo can find it.
3. The judge misread glm-5.3@xhigh's answer. It said xhigh approved while
   the problem stood. In truth, xhigh's first directive fixes that
   problem; it only picked the wrong verdict word. Its rank is mid, not
   last.

## What the live loop should try

- Red team: keep as is (`deepseek-flash:xhigh,glm-5.3-flash:high`). Both
  earn their seat. Do not add glm-5.3-flash@xhigh — it hides findings.
- Worker: trial `workerModel: "zai:glm-5.3-flash@high"` on one step.
  deepseek stays the safe default until flash-high survives a real round.
- Architect: keep the current ladder (complex first round, standard after).
  Seating flash-high there needs a new `architectModel` parameter; the
  script today takes tiers only.
- Derive: `deriveTier: quick` is fine. flash@high is the better value if a
  model parameter is added later.
- Review rounds: keep `maxReviewRounds: 3`. Past data shows round 4 is as
  often wrong as right. Stop and ask the user instead.

## Warnings for any live run

- Token counts for GLM sessions in `alta session report` can show only the
  last message. Trust wall time, not their token numbers.
- In workflow scripts: do not use `parallel(...)` with a limit, and do not
  mark a step `each: true` when it returns objects. Both crash the run in
  the CodeAlta engine. `Promise.all` is safe.
- Concurrency: run all deepseek calls at once. Keep GLM calls to 2-3 at a
  time.
- The P22 worker test left two repo copies:
  `.alta/worktrees/distant-mira-qfpr` (flash-high's work, near-merge-ready,
  tests green) and `.alta/worktrees/secret-glow-mcvs` (deepseek's work,
  destructive — discard). Nothing was merged.

## Where the raw data lives

- `.alta/derived/role-eval-report.md` — every run, judge notes, corrections.
- `.alta/derived/role-eval.jsonl` — one row per candidate, machine-readable.
- `.alta/derived/loop-metrics.jsonl` — real cost of past loop runs P18-P21.
- `.alta/scripts/devnull-role-eval.js` — re-run any seat test.
