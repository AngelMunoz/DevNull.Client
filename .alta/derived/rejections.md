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

## Standing note to every reviewer

The battery, the gates, and the derived parameters are the FLOOR. The
review exists for what they cannot name. If you catch yourself verifying
that a rule's letter was satisfied, ask whether its spirit survived.
