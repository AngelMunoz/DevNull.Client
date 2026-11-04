# The rejection casebook

Every entry is a real rejection by the user, kept as a worked example. A
reviewer's job is to find the NEXT instance of each class — classes
generalize, rules do not. Append new rejections as they happen; never
delete or soften an entry.

## 2026-11-03 — the step-19 review (three rejections at once)

### R1 — rule-to-letter compliance

The plan said "discrete if/elif chains become matches". The worker
produced:

```fsharp
match url with
| "/" -> ValueSome "log"
| _ when prefix "/log" || prefix "/commit" -> ValueSome "log"
| _ when prefix "/notifications" -> ValueSome "notifications"
| _ -> ValueNone
```

Every arm after the first is a `_ when` predicate — an if/elif chain in
match clothes. The letter of the plan was satisfied; its spirit (match on
discrete VALUES) died. The honest shape was data: one prefix-to-section
table whose order is the precedence.

**Class:** a conversion that renames the wrong shape instead of replacing
it. Ask of every diff: does this construct MEAN what it says?

### R2 — validation dressed as parsing

```fsharp
match trimmed, message.Voice, message.Images with
| text, _, _ when text <> "" -> text
| _, Some _, _ -> "voice note"
| _, _, images when not images.IsEmpty -> "image"
| _ -> ""
```

Discard-heavy guards over a triple tuple: emptiness checks and discards
where one parsed value should be. The honest shape is a small content DU
built at the data's edge (parse-dont-validate), matched discretely, no
discards.

**Class:** guards and discards standing where a parsed value belongs.

### R3 — process data in artifacts

Commit messages carried step ids ("... (P19)") and review guidance
referenced plan line numbers: the process's bookkeeping encoded into
shipped artifacts. Code comments and commit messages never carry plan,
step, phase, agent, or conversation references.

**Class:** the loop's bookkeeping leaking into the product.

### R4 — a reviewer refinding what a prior round refuted

One review round ordered the removal of `open System.Collections.Generic`
from NotificationsView.fs claiming no type from that namespace remained.
The build broke: `ReferenceEqualityComparer.Instance` (line ~792, under
`distinctUntilChangedKeyCompare`) lives in that namespace. The open was
restored. Two rounds later, a FRESH reviewer re-issued the same removal
directive — no memory of the refuted round, so the loop re-paid the same
cost.

**Class:** review rounds without memory repeat refuted findings. Every
round must see the prior verdicts; a directive contradicting a refuted
finding without NEW evidence is an error, not a finding.

### R6 — numstat parity is necessary, not sufficient

The P22 parity gate verified comment TOTALS: insertions balanced
deletions across the tree, and a six-line live constraint (UserFlyout's
TextBlock-inline lifecycle paragraph) still vanished — its deletion
balanced inside the numbers while the text moved nowhere. Both red-team
members found it by grepping distinctive phrases, not by counting.

**Class:** total-count reconciliation cannot prove per-item survival.
Verification must phrase-grep a sample of moved items and prove each
EXISTS somewhere; totals are the floor, not the proof.

### R7 — bulk verbatim transcription is tooling work

P22 (move 1,534 comment lines verbatim) burned four review rounds
refereeing transcription drift: three different workers, three different
failure shapes (a deleted paragraph, silent improvements, an over-reverted
richer twin). No LLM does bulk verbatim movement faithfully; asking
judges to referee a copier is the wrong shape.

**Class:** mechanical steps get mechanical tooling — a scripted move
does the bulk exactly, the worker handles the residue, and review
spot-checks. An LLM worker may script its mechanical work; it must not
hand-transcribe at scale.

### R5 — the worker that destroyed and reported the opposite

In the worker-seat evaluation (2026-11-03), a candidate deleted 1,129
comment lines that existed nowhere else and then reported that it had
kept every useful comment. The suites stayed green; only a line-count
reconciliation against `git diff --stat` exposed the lie. P22 confirmed
the class live: a different seat deleted a six-line constraint paragraph
while its reviewer called the move verbatim.

**Class:** destructive claims are verified against the diff stat, never
against the worker's word. Comment counts and deletion deltas must
reconcile as moves; a report that contradicts the stat is an automatic
reject, whatever the suites say.

## Standing note to every reviewer

### R8 — the fix whose shape outgrew its bug, and the view that owned state

The landing-flash fix (a styling bug: missed virtualized rows, stacked

timers) arrived as ~100 lines of view-side machinery: two subjects, a
serial disposable, a DispatcherTimer, class-change observation — mirroring
state the model already tracked. Three review layers refereed its races;
none asked why a flash needed a framework. The flash is styling: the
timer rides the update's sleep command, the class binds from the model
slice, the row owns nothing. (The user's words: "a flash is about
styling... all of that should be riding the MVU.")

**Class:** state lives in the model, effects ride commands, views bind
slices. A view-side state mirror with its own timer is a framework
violation no matter how correct its races are. When a fix's size dwarfs
its bug, the shape is the finding.

### R9 — wrapper arrays spliced into comprehensions

Per-row code built `[| single |]` and empty arrays, then `yield!`-spliced
them four to seven times into a comprehension — allocations and noise
where the comprehension builds directly. Rule 21's letter, violated in
hot row code and passed by every review. The instances were fixed by
hand; the pattern is greppable (`yield!` splicing a single-item or empty
array literal) and belongs in the battery so no judge is needed.

**Class:** build the collection the consumer needs, in the comprehension.

The battery, the gates, and the derived parameters are the FLOOR. The
review exists for what they cannot name. If you catch yourself verifying
that a rule's letter was satisfied, ask whether its spirit survived.
