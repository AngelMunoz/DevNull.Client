// The DevNull bug loop: ONE issue per run — triage -> repro -> derive ->
// fix (its battery is the verification of record) -> collect -> verify ->
// review pair (round 0 full, deltas on kept seats) -> (red team when the
// risk is high, kept seats too) -> the deriver's scope ruling ->
// remediation on the same worker seat -> a fix: pull request when it
// passes. The reproduction is the
// contract: a reproducer seat writes the failing test FIRST, the loop
// commits it, runs it on the broken tree, and freezes the test files'
// hashes; no seat may touch them afterwards, and the same command must
// pass after the fix (AGENTS.md: a regression test fails before the fix
// and passes after it). No architect: the shape guard (casebook R8) lives
// in the derivation and the review pair. The run refuses the peak windows
// for both providers (weekdays 01:00-04:00 and 06:00-10:00 UTC) unless
// forced; action "window" reports status and readiness. Never merges and
// never pushes main: the branch and its pull request are the landing, and
// the merge stays with the user.
export const meta = {
  name: "devnull-bug",
  title: "DevNull bug loop (one issue, one repro, one PR)",
  description: "Works exactly one bug issue: a triage that states expected and actual behavior and classifies the risk, a reproducer seat that writes the failing test before any fix, a derivation that names the root cause and the boundary, one kept worker seat whose own battery is the verification of record, a cheap collect of files, gates, and captures, the fail-before/pass-after gate, a mixed-model review pair on kept seats (full diff and renders once, deltas after; a red team joins when the triage says high risk), and the deriver ruling every finding's scope. Lands as a fix: pull request, never a merge.",
  whenToUse: "For bug issues on DevNull.Client, one issue per run. Works from the tracker issue alone; a step in audits/bugs-workplan.md is optional.",
  params: {
    step: { type: "string", description: "The bug id, e.g. B12 (optional when the issue carries it; default B<issue>).", default: "" },
    action: { type: "string", description: "run | window | triage | repro | derive | battery | land | rollback.", default: "run" },
    plan: { type: "string", description: "The bug plan file, repo-relative; missing file or missing step = work from the issue alone.", default: "audits/bugs-workplan.md" },
    issue: { type: "string", description: "The tracker issue number (fetches title, body, labels, last comments).", default: "" },
    branch: { type: "string", description: "The work branch; empty = bug/<issue or id>.", default: "" },
    workerModel: { type: "string", description: "auto (deepseek-flash:xhigh) or an explicit worker model; glm-5.3@xhigh refused.", default: "auto" },
    triageModel: { type: "string", description: "The triage pass; auto = quick tier (deepseek-flash:xhigh).", default: "auto" },
    reproModel: { type: "string", description: "The reproducer seat; auto = quick tier (deepseek-flash:xhigh).", default: "auto" },
    deriveTier: { type: "string", description: "Tier of the derive pass when deriveModel is unset.", default: "standard" },
    deriveModel: { type: "string", description: "Explicit model ref for the derive pass (overrides deriveTier); empty = tier.", default: "" },
    reviewModels: { type: "string", description: "The review pair, one pass per model in parallel; BOTH must run (all-dead is an infrastructure failure, never 'clean'). Non-flash glm refs are refused: no vision.", default: "deepseek-flash:xhigh,glm-5.3-flash:high" },
    redTeamModels: { type: "string", description: "One red-team pass per model when the triage risk is high; empty disables.", default: "deepseek-flash:xhigh,glm-5.3-flash:high" },
    autoLand: { type: "boolean", description: "Open the pull request on approval without waiting for the user. Keep off.", default: false },
    maxRemediations: { type: "integer", description: "Worker passes after round 0 (gate flags, a still-failing repro, scope-reviewed directives; the worker's own battery is the quality gate).", default: 3 },
    maxReviewRounds: { type: "integer", description: "Hard backstop on total worker passes before stopping for the user; the scope gate usually stops first.", default: 3 },
    maxScopeReviews: { type: "integer", description: "Scope-review cycles allowed per issue. The budget is multiplicity, never wall clock: needing one more cycle than this stops for the user's judgment.", default: 2 },
    maxReproRounds: { type: "integer", description: "Reproducer passes before stopping: non-test edits and tests that do not fail both cost a round.", default: 2 },
    retriage: { type: "boolean", description: "Regenerate the triage.", default: false },
    rerepro: { type: "boolean", description: "Regenerate the reproduction (reproducer seat, new test commit).", default: false },
    rederive: { type: "boolean", description: "Regenerate the derivation.", default: false },
    resume: { type: "boolean", description: "The branch already carries the repro commit and the fix: skip triage, repro, and the worker first pass; start at the collect step (the review is a full pass again; the seats are new).", default: false },
    force: { type: "boolean", description: "Run inside a peak window anyway.", default: false },
    snapshotsDir: { type: "string", description: "Where capture PNGs land (gitignored evidence).", default: "artifacts/snapshots" },
    message: { type: "string", description: "land: the commit/PR title.", default: "" },
  },
  steps: [
    { id: "triage", title: "The triage: expected, actual, risk" },
    { id: "repro", title: "The reproducer writes the failing test, committed", after: ["triage"] },
    { id: "derive", title: "The root cause and the boundary", after: ["repro"] },
    { id: "fix", title: "One kept worker seat", after: ["derive"] },
    { id: "collect", title: "The changed files, the idiom gates, the captures", after: ["fix"] },
    { id: "verify", title: "The repro gate: hashes frozen, pass-after", after: ["collect"] },
    { id: "review", title: "The pair reads the diff and the renders (full pass, then deltas on kept seats)", after: ["verify"] },
    { id: "redteam", title: "The red team hunts (high risk only; full pass, then deltas)", after: ["review"] },
    { id: "scope", title: "The deriver rules every finding's scope", after: ["review"] },
    { id: "remediate", title: "The same worker seat applies the in-scope directives", after: ["scope"] },
    { id: "land", title: "Commit and the fix: pull request", after: ["review"] },
  ],
};

const plan = args.plan ?? meta.params.plan.default;
const today = new Date().toISOString().slice(0, 10);
const snapshotsDir = args.snapshotsDir ?? meta.params.snapshotsDir.default;
const requestedWorker = (args.workerModel ?? meta.params.workerModel.default).trim();
if (/glm-5\.3(?!-flash)/i.test(requestedWorker) && /xhigh/i.test(requestedWorker)) {
  throw new Error("glm-5.3@xhigh is refused for workers; it stays the architect's, single-flight (refactor loop).");
}
const workerOpts = requestedWorker === "auto" ? { tier: "quick", subSessions: false } : { model: requestedWorker, subSessions: false };
const workerName = requestedWorker === "auto" ? "auto (deepseek-flash:xhigh)" : requestedWorker;
const seatOpts = (ref, label) => {
  if (ref === "auto") return { tier: "quick", subSessions: false };
  return { model: ref, subSessions: false };
};
const triageOpts = () => seatOpts((args.triageModel ?? meta.params.triageModel.default).trim());
const reproOpts = () => seatOpts((args.reproModel ?? meta.params.reproModel.default).trim());
const deriveOpts = () => (args.deriveModel ?? "")
  ? { model: args.deriveModel, subSessions: false }
  : { tier: args.deriveTier ?? meta.params.deriveTier.default, subSessions: false };

const sh = async (command, timeoutMs = 1200000) => {
  const out = await tools.shell_command({ command, timeoutMs, login: false });
  return { code: out.exitCode, text: (out.stdout ?? "") + (out.stderr ? "\n" + out.stderr : "") };
};
// Everything is relative to the project root: this script runs from it, and every seat it starts works from it too.
const ROOT = ".";
const read = async (path) => (await tools.read_file({ path })).content ?? "";
const readPlan = async () => { try { return await read(plan); } catch { return ""; } };
const derivedPath = (id) => `.alta/derived/${id}.md`;
const triagePath = (id) => `.alta/derived/${id}.triage.json`;
const reproPath = (id) => `.alta/derived/${id}.repro.json`;
const verdictPath = (id) => `.alta/derived/${id}.verdict.json`;
const scopePath = (id) => `.alta/derived/${id}.scope.json`;
const prBodyPath = ".alta/derived/PR-bug-body.md";

// The off-peak contract for BOTH providers: weekdays 01:00-04:00 and
// 06:00-10:00 UTC are peak (the GLM window nests inside deepseek's).
const PEAK_WINDOWS = [[1, 4], [6, 10]];
const windowStatus = () => {
  const now = new Date();
  const d = new Date(now.toISOString());
  const hour = d.getUTCHours();
  const day = d.getUTCDay();
  const weekday = day >= 1 && day <= 5;
  const inPeak = weekday && PEAK_WINDOWS.some(([a, b]) => hour >= a && hour < b);
  let nextChange = "";
  // 72h: a Friday evening sits more than 24h from the next peak (Monday).
  for (let h = 1; h <= 72; h++) {
    const hh = (hour + h) % 24;
    const dd = Math.floor((hour + h) / 24);
    const wd = ((day + dd) % 7 + 7) % 7;
    const peakAt = wd >= 1 && wd <= 5 && PEAK_WINDOWS.some(([a, b]) => hh >= a && hh < b);
    if (peakAt !== inPeak) {
      nextChange = `${peakAt ? "peak opens" : "off-peak opens"} ${String(hh).padStart(2, "0")}:00 UTC (in ${h}h)`;
      break;
    }
  }
  return { nowUtc: d.toISOString().slice(0, 16).replace("T", " ") + " UTC", weekday, hour, inPeak, nextChange };
};
const guardWindow = () => {
  const w = windowStatus();
  if (w.inPeak && !(args.force ?? meta.params.force.default)) {
    throw new Error(`Peak window for both providers (${w.nowUtc}); ${w.nextChange}. Re-run when it passes, or pass force:true to override.`);
  }
  return w;
};

const stepText = (planText, id) => {
  const open = new RegExp(`- \\[ \\] \\*\\*${id} —([^*]*)\\*\\*(.*?)(?=\\n- \\[ |\\n## |$)`, "s").exec(planText);
  return open ? { title: open[1].trim(), body: open[2].trim() } : null;
};
const fetchIssue = async (number) => {
  if (!number) return "";
  try {
    const out = await sh(`gh issue view ${number} --json number,title,body,labels,comments`, 60000);
    const parsed = JSON.parse(out.text.trim().split(/\r?\n/).filter(Boolean).pop() ?? "{}");
    const labels = (parsed.labels ?? []).map((l) => l.name ?? "").filter(Boolean).join(", ");
    const comments = (parsed.comments ?? []).slice(-5)
      .map((c) => `--- comment ${c.author?.login ?? ""} ---\n${String(c.body ?? "").slice(0, 2500)}`).join("\n");
    return [`#${parsed.number ?? number} — ${parsed.title ?? ""}`, labels ? `Labels: ${labels}` : "", parsed.body ?? "", comments]
      .filter(Boolean).join("\n\n").trim();
  } catch { return "(the tracker issue could not be fetched; work from the plan text and the tree)"; }
};
const issueStep = (issueText, id) => {
  if (!issueText || issueText.startsWith("(")) return null;
  const lines = issueText.split(/\r?\n/);
  const title = (lines[0] ?? id).replace(/^#\d+\s*[—-]\s*/, "").trim() || id;
  return { title, body: lines.slice(1).join("\n").trim() || "(no issue body)" };
};

const priorRounds = async (id) => {
  const parts = [];
  try {
    const v = JSON.parse(await read(verdictPath(id)));
    parts.push(`A prior review round ended with: ${JSON.stringify({ verdict: v.verdict, findings: v.directives })}. A finding already ruled out of scope or refuted must not return without new evidence.`);
  } catch { parts.push("(first review round on this issue)"); }
  try {
    const s = JSON.parse(await read(scopePath(id)));
    const ruled = (s.rounds ?? []).flatMap((r) => (r.rulings ?? []).map((x) => ({ finding: x.finding, ruling: x.ruling })));
    if (ruled.length) parts.push(`The scope referee already ruled: ${JSON.stringify(ruled)}.`);
  } catch { /* no scope rounds yet */ }
  return parts.join("\n");
};

const cleanNote = (note) => {
  const stripped = String(note)
    .replace(/^(approved|directives|pending|rework|[bB]\d+[a-z]?)\b[-—:.\s]*/i, "")
    .replace(/\([bB]\d+[a-z]?\)/gi, "")
    .replace(/^[bB]\d+[a-z]?\s*[-—:.]\s*/i, "")
    .trim();
  const firstClause = stripped.split(/[.;—]|\bdone\b/i)[0].trim();
  return (firstClause.length > 12 ? firstClause : stripped.slice(0, 80)).slice(0, 100);
};

const fileHash = async (file) => {
  const out = await sh(`Get-FileHash -Algorithm SHA256 "${file}" | Select-Object -ExpandProperty Hash`, 60000);
  return out.text.trim().split(/\r?\n/).filter(Boolean).pop() ?? "";
};
const hashesFor = async (files) => {
  const out = {};
  for (const f of files) out[f] = await fileHash(f);
  return out;
};
// What the reproducer may write: tests and fixtures. Anything under the
// product tree, the build files, the repo metadata, or the loop's own
// folder is a violation.
const forbiddenReproPath = (f) =>
  /^(src|publish-aot|audits|\.github|\.agents|\.alta)\//i.test(f)
  || /\.(slnx|fsproj|csproj|props|targets|gitignore|editorconfig)$/i.test(f)
  || /^Directory\./i.test(f)
  || /^dotnet-tools\.json$/i.test(f);

const IDIOM = `Uninterrupted fluent chains (no create-then-mutate-then-return, no |> ignore chain interrupts); implicit comprehension items, yield! only for collection splices; Option combinators over re-wrap matches; no direct option comparisons (compare inner values); at most 3 tupled parameters, a contextual record beyond; no private in files with an .fsi; comments belong in the .fsi; every new F# file pairs with its own .fsi (tests and Program entry points except); structured log fields stay separate and every existing debug/success log stays; no speculative slots. RECURSION: recursive async is async/asyncEx, never a task CE; genuinely tail-recursive functions carry [<TailCall>]. MATCHES ARE HONEST OR THEY ARE WRONG: a match whose arms are '_ when' predicates is an if/elif chain in disguise — replace it with data or a parsed value; a match over a tuple with discard-heavy arms is validation — parse the inputs into one small DU first and match that value discretely. No comment in code may reference the plan, steps, phases, agents, or any conversation that produced the code.`;

const BUG_RULES = `The reproduction is the contract: the failing test states the defect, and a fix that does not make it pass has not happened. The repro test files are frozen after their commit — no seat may create, edit, or delete them except the reproducer, and a finding that blames the test routes as a re-reproduction task, never as a worker directive. The fix addresses the root cause at the layer that owns it, never the symptom where it shows; a fix whose shape dwarfs its bug is itself a finding (casebook R8). No drive-by refactors, no scope growth past the derived boundary. The server repository is read-only evidence for wire or protocol bugs: cite file:line, call it "the server" or "the site" everywhere, never a brand, a domain, or a repository name.`;

const TRIAGE_SCHEMA = {
  type: "object", required: ["classification", "expected", "actual", "reproClass", "canProceed", "proceedWhy", "risk", "riskWhy", "changelogRule", "changelogWhy", "reproPlan"],
  properties: {
    classification: { type: "string", enum: ["bug", "feature", "refactor", "question", "duplicate"] },
    expected: { type: "string", description: "the behavior the user is owed, in one or two lines" },
    actual: { type: "string", description: "the behavior observed, in one or two lines" },
    reproClass: { type: "string", enum: ["deterministic", "flaky", "environment-specific", "none"] },
    duplicateOf: { type: "string", description: "the issue that already owns it, when classification is duplicate" },
    canProceed: { type: "boolean", description: "false when expected or actual cannot be stated, or the class is not a bug" },
    proceedWhy: { type: "string", description: "one or two lines of evidence for canProceed" },
    risk: { type: "string", enum: ["normal", "high"] },
    riskWhy: { type: "string", description: "one line: why normal, or why high" },
    changelogRule: { type: "string", enum: ["fixed", "amend-added", "amend-changed", "none"] },
    changelogWhy: { type: "string", description: "one line: which release carries the buggy behavior" },
    reproPlan: { type: "string", description: "what test the reproducer must write: suite, file, the assertion that encodes the defect, the command that runs it alone" },
    notes: { type: "string" },
  },
};

const TRIAGE_BRIEF = (id, st, issueText) => `You are the TRIAGE for bug ${id} of DevNull.Client (repo root ${ROOT}). READ-ONLY: no edits, no commits. Read AGENTS.md (binding for the changelog rule), the changelog CHANGELOG.md, and the code and history the issue names — ground every claim in file:line evidence you actually read. Read-only commands are allowed: gh issue list/search (duplicates), git log, grep. No writes.

## The bug, from the tracker and the plan

${issueText || `(no issue fetched)`}

${st ? `## Step ${id} — ${st.title}\n\n${st.body}\n` : ""}
Produce, as the JSON schema asks:
- **expected / actual** — the behavior owed and the behavior observed. If the issue cannot support either, canProceed=false and say what is missing; the loop stops.
- **reproClass** — deterministic beats flaky: say honestly which, and what the reproducer must pin down (state, timing, environment).
- **duplicateOf** — search the tracker first; a duplicate stops the loop.
- **risk** — high when it touches security, data loss or corruption, crashes, concurrency or state ownership, the public or wire surface, or shared code with many callers; say the reason in one line.
- **changelogRule** — apply AGENTS.md's rule: "fixed" only when the buggy behavior shipped in a released version (check CHANGELOG.md's released sections against the code's history); "amend-added" or "amend-changed" when the code is unreleased (under [Unreleased] or in a version never shipped); "none" for internal-only defects. State the release evidence in changelogWhy; when the evidence is genuinely unclear, choose "fixed" and say so.
- **reproPlan** — the concrete test to write: which suite (Core parse/composite or UI headless), which file beside which existing test, the assertion that encodes expected-vs-actual, and the ONE command that runs it alone (Expecto --filter; the UI suite takes --sequenced).`;

const REPRO_SCHEMA = {
  type: "object", required: ["files", "command", "failTail", "assertion"],
  properties: {
    files: { type: "array", items: { type: "string" }, description: "every file you created or changed" },
    command: { type: "string", description: "the ONE command that runs only the repro test(s), deterministic, exit code reflects the result" },
    failTail: { type: "string", description: "the last lines of the failing output you observed" },
    assertion: { type: "string", description: "which test and assertion encodes the reported defect" },
    notes: { type: "string" },
  },
};

const REPRO_BRIEF = (id, st, issueText, triage) => `You are the REPRODUCER for bug ${id} of DevNull.Client (repo root ${ROOT}). You write the failing test FIRST — the fix does not exist yet, and proving the defect is your whole job. No git commits, no pushes, no gh; the loop commits for you.

## The bug

${issueText || `(no issue fetched)`}

${st ? `## Step ${id} — ${st.title}\n\n${st.body}\n` : ""}## The triage (binding)

Expected: ${triage?.expected ?? "("} — Actual: ${triage?.actual ?? "("}
Repro class: ${triage?.reproClass ?? "("}. Risk: ${triage?.risk ?? "("}
Plan: ${triage?.reproPlan ?? "("}

## Rules

- Write ONLY test and fixture files: tests/** and nothing else. Touching src/, the project files, or repo metadata is a violation the loop reverts and charges to your budget.
- The test must fail on today's tree for the REPORTED reason — the assertion encodes the expected-vs-actual gap, not a setup error. Run it yourself and iterate until the failure is the defect.
- One deterministic command runs it alone (Expecto --filter; the UI suite takes --sequenced). No sleeps papering over races; a flaky repro is named, not hidden.
- Follow the tests' existing patterns (fixtures, fakes, headless UI through the session thread); new F# files pair with .fsi except tests; dotnet fantomas . must leave no diff.
- Never fix the bug, never touch src/. If the defect cannot be expressed as a failing test, STOP and report why.

Answer with the JSON schema: every file you wrote, the command, the failure tail you saw, and the assertion of record.`;

const REPRO_RETRY_BRIEF = (id, directive, round) => `Round ${round} on bug ${id}. You are the reproducer; your last pass did not satisfy the gate:

${directive}

Same rules as before: only test/fixture files, the failure must be the REPORTED defect, one deterministic command, fantomas clean. Answer with the same JSON schema.`;

const DERIVE_BRIEF = (id, st, issueText, triage, repro) => `You derive the working parameters for bug ${id} of DevNull.Client (repo root ${ROOT}). READ-ONLY: no edits, no commits. Read AGENTS.md, the code the bug names, and the repro test the reproducer wrote — ground everything in file:line evidence you actually read. The repro evidence below is recorded fact: reconcile it with the code, never re-run it.

## The bug

${issueText || `(no issue fetched)`}

${st ? `## Step ${id} — ${st.title}\n\n${st.body}\n` : ""}## The triage (binding)

Expected: ${triage?.expected ?? "("} — Actual: ${triage?.actual ?? "("}
Risk: ${triage?.risk ?? "("} (${triage?.riskWhy ?? "("}). Changelog rule: ${triage?.changelogRule ?? "("} (${triage?.changelogWhy ?? "("}).

## The reproduction (recorded fact)

- command: ${repro?.command ?? "("}
- assertion of record: ${repro?.assertion ?? "("}
- frozen files: ${(repro?.files ?? []).join(", ") || "("}
- failure observed on the broken tree:\n\n\`\`\`\n${String(repro?.failTail ?? "").slice(-1500)}\n\`\`\`

Produce, as Markdown:
1. **Root cause** — where the defect lives, file:line, and WHY that code produces the observed actual; the layer that owns the cause. If the evidence supports two candidate causes, name both and the experiment that separates them; a cause you cannot ground is a stop condition, not a guess.
2. **Boundary** — the files and areas the fix may touch, and the explicit out-of-bounds list (which issue or refactor owns what).
3. **Caller census** — for every symbol the fix will touch: grep across src/ AND tests/, NAME every caller. A caller not listed is a missed consumer.
4. **Regression surface** — what the fix could break beyond the repro: the behaviors, states, and callers the review pair must check.
5. **Evidence spec** — the repro test stands as written (frozen); name any ADDITIONAL assertions the fix must not need, the captures worth taking if the bug has a visual face, and what "the failure is gone" looks like beyond the command's exit.
6. **Fix shape guard (R8)** — the maximal acceptable shape of the fix in one or two lines; a diff that outgrows this is itself a finding, however correct its races.
7. **Changelog** — apply the triage's changelogRule under AGENTS.md's rules: the exact category and bullet text (bold area prefix, user-facing wording, one concern), or "no changelog entry".

Mandatory, every derivation: stop conditions where the worker halts and reports instead of improvising; sanitization for wire bugs (the server repository stays read-only evidence, cited by file:line, name-free).

One line per fact, no prose padding; concise, never at the cost of an item.`;

const WORKER_BRIEF = (id, st, derivation, repro) => `You are the implementer for bug ${id} of DevNull.Client (repo root ${ROOT} — stay on the current branch). Read AGENTS.md (binding) and the skills under .agents/skills/ the derivation names; if none, read devnull-mvuish-audit when the bug touches a View or Component.

${st ? `## Step ${id} — ${st.title}\n\n${st.body}\n` : ""}## Derived contract (binding)\n\n${derivation}\n\n## The repro (frozen — you may NOT touch these files)\n\n${(repro?.files ?? []).map((f) => `- ${f}`).join("\n") || "- (none recorded)"}\n\n## Rules\n\n${BUG_RULES}\n\n## Idiom rules\n\n${IDIOM}\n\n## Rules\n\nNo git commits, no pushes, no gh. The boundary in the derivation is binding: a fix outside it is a stop condition — STOP that item and report the evidence, do not improvise scope. The repro command must pass when you are done; if it cannot pass without touching the frozen files or leaving the boundary, stop and report.

## Done when\n\n${repro?.command ?? "(the repro command)"} exits green; dotnet fantomas . leaves no diff (run last); dotnet build DevNull.Client.slnx has 0 errors and no new warnings (a leftover DevNull.Client.exe from the repo bin locks the DLL — MSB3021; stop repo-owned DevNull processes first); Core (dotnet run --project tests/DevNull.Client.Core.Tests -c Debug --no-build -- --summary) and UI (dotnet run --project tests/DevNull.Client.UI.Tests -c Debug --no-build -- --sequenced --summary) green. The loop does not re-run this battery: your tails are the record of green. Report: what changed and why it addresses the root cause, the boundary files touched, verification tails with pass counts, deviations.`;

const REMEDIATE_BRIEF = (id, directives, round, repro) => `Round ${round} on bug ${id}. You implemented this fix in this session — apply the directives below to the code you already know. Do not re-read the plan, AGENTS.md, or the skills; everything you already hold still binds.

## Directives (binding — battery failures, a still-failing repro, and review findings by the scope referee's ruling)\n\n${directives.map((d, i) => `${i + 1}. ${d}`).join("\n")}\n\nSame bars as round 0: the repro files (${(repro?.files ?? []).join(", ") || "frozen"}) stay untouched; the fix stays inside the derived boundary; no drive-by refactors. Finish with the repro command green, dotnet fantomas . (last), build 0 errors, both suites green. The loop does not re-run the battery: your tails are the record. Report only: what changed per directive, the verification tails.`;

const REVIEW_SCHEMA = {
  type: "object", required: ["verdict", "findings", "notes"],
  properties: {
    verdict: { type: "string", enum: ["clean", "findings"] },
    findings: { type: "array", items: { type: "string" } },
    notes: { type: "string" },
  },
};

const REVIEW_BRIEF = (ref, id, derivation, changed, captures, repro) => `You are the REVIEW PAIR seat [${ref}] for bug ${id} of the DevNull.Client work (repo root ${ROOT}). There is no architect above you: this pass and its sibling are the whole review. Assume the diff satisfies checklists while breaking their spirit; be the harshest maintainer.

Read first: .alta/derived/rejections.md (the casebook — find each class's next instance), and the derivation below — the root cause, the boundary, the caller census, and the fix shape guard are the contract.

${derivation ? `## The derivation (contract)\n\n${derivation}\n` : ""}## The reproduction\n\n- frozen test files: ${(repro?.files ?? []).join(", ") || "(none)"} — a diff hunk in any of them is an automatic finding\n- assertion of record: ${repro?.assertion ?? "(none)"}\n- failed-before evidence: ${String(repro?.failTail ?? "(none)").slice(-600)}\n\n## Your two passes\n\n1. **The diff.** git diff over the changed files (run it yourself):\n${changed.map((f) => `- ${f}`).join("\n")}\n\n2. **The renders.** Call view_image on EACH capture below and judge it against the derivation's evidence spec — layout, states, truncation, spacing, theme, and any UI string leaking process or server-brand words:\n${captures.length ? captures.map((c) => `- ${c}`).join("\n") : "- (NONE PRODUCED — the evidence spec names visual states but no capture exists: that is a finding, not a note)"}\n\nWeigh, in order: does the diff fix the ROOT CAUSE the derivation names, or a symptom where it shows; is the fix's shape inside the guard (a fix that dwarfs its bug is itself a finding, R8); the frozen repro files untouched; the regression surface checked, every caller from the census honored; the changelog entry correct under AGENTS.md's rules (category and wording); no drive-by refactors inside the boundary.\n\nDo NOT re-run the battery or the repro and do not restate known justified survivors. Answer: clean, or findings — each file-anchored or state-anchored (name the capture), concrete, one worker pass. Prefix a finding with [beyond-repro] when it names a real defect the reproduction never promised: the referee routes it, it is not automatically another worker pass. A regression of existing behavior is always a plain finding. Only what would embarrass us in a human review.`;

const REDTEAM_SCHEMA = {
  type: "object", required: ["verdict", "findings", "notes"],
  properties: {
    verdict: { type: "string", enum: ["approve", "findings"] },
    findings: { type: "array", items: { type: "string" } },
    notes: { type: "string" },
  },
};

const REDTEAM_BRIEF = (id, derivation, changed, repro) => `You are the RED TEAM for bug ${id} of DevNull.Client (repo root ${ROOT}). The triage marked this bug HIGH RISK, and a review pair APPROVED the diff. Your only job: find what they rationalized, did not read, or was gamed on. Read the changed code as its harshest maintainer, assuming the diff satisfies checklists while breaking their spirit.

${derivation ? `## The derivation (contract)\n\n${derivation}\n` : ""}## The diff\n\n${changed.map((f) => `- ${f}`).join("\n")}\n\nThe frozen repro files: ${(repro?.files ?? []).join(", ") || "(none)"}. Hunt where high risk lives: the state the fix shares, the callers the census missed, the error paths the happy path skips, the data the fix corrupts when it races, and the test that would catch the NEXT instance of this bug class and does not exist.`;

const SCOPE_SCHEMA = {
  type: "object", required: ["rulings"],
  properties: {
    rulings: {
      type: "array",
      items: {
        type: "object", required: ["finding", "ruling", "reason"],
        properties: {
          finding: { type: "string", description: "short echo identifying which finding this rules" },
          ruling: { type: "string", enum: ["in-scope", "task", "refuted"] },
          reason: { type: "string", description: "one or two lines of evidence" },
          directive: { type: "string", description: "required when in-scope: the binding, file-anchored worker directive" },
          taskTitle: { type: "string", description: "required when task: the action in one line" },
          taskKind: { type: "string", enum: ["gap", "problem", "improvement"] },
          taskSummary: { type: "string", description: "required when task: what and why, one or two sentences" },
        },
      },
    },
    notes: { type: "string" },
  },
};

const SCOPE_BRIEF = (id, st, findings, round, triage, repro) => `Scope review for bug ${id} of the DevNull.Client work (repo root ${ROOT}). This session derived the fix; that derivation and the reproduction are the scope authority. If this session does not hold them, read .alta/derived/${id}.md and .alta/derived/${id}.repro.json first, then rule. READ-ONLY: no edits, no commits.

The review returned these findings on the fix's diff:\n\n${findings.map((f, i) => `${i + 1}. ${f}`).join("\n")}\n\nRule EACH finding against the reproduction (expected: ${triage?.expected ?? "("}), the derivation's boundary and caller census, and the bug's own text${st ? ` (step ${id} — ${st.title})` : ""}:\n- **in-scope** — the root cause, the callers the census names, the regression test, or a regression the fix itself caused. Write the directive the worker gets: concrete, file-anchored, one worker pass.\n- **task** — real, but beyond this bug's promise: an adjacent pre-existing bug, a refactor, a feature, a harness improvement, or a defect the reproduction never promised. Give taskTitle/taskKind/taskSummary; it becomes a recorded work item, never a directive.\n- **refuted** — wrong on the code, the rules, or the repro. Give the evidence.\n\nBinding precedents:\n- The repro test files are frozen: a finding that asks to change them is NEVER a worker directive. If the test itself is wrong, rule "refuted" for the directive and record the task "re-reproduce ${id}" — the loop stops for the user.\n- The derivation's boundary wins: a finding that grows surface it excluded is out of scope even when the code it points at is real.\n${round >= 2 ? `- Scope cycle ${round}: the derivation is FROZEN. "in-scope" cites the line of the frozen derivation (root cause, boundary, caller census, regression surface) or the repro's assertion of record that the finding enforces; a [beyond-repro] finding, or any real defect the reproduction never promised, is "task" — the next issue's work, never another worker pass here.` : ""}\n- A fix whose shape dwarfs its bug is itself a finding: rule the shape, not just the symptom (casebook R8).\n- A reviewer is not right because it spoke, and the worker is not excused because it tried. Read the code each finding names before ruling.\n\nAnswer with one ruling per finding. Never expand the fix to please a reviewer; never bury a real defect to please the worker.`;

const withMetrics = (title, extra, run, sid) => Promise.resolve(run());

const reviewOpts = (ref) => {
  if (/deepseek/i.test(ref) && !/^deepseek:/.test(ref)) return { tier: "quick", subSessions: false };
  const glm = /^glm-5\.3-flash(?::(high|xhigh))?$/.exec(ref.trim());
  if (glm) return { model: `zai:glm-5.3-flash@${glm[1] ?? "high"}`, subSessions: false };
  return { model: ref, subSessions: false };
};
const stripAnsi = (t) => t.replace(/\x1b\[[0-9;]*[A-Za-z]/g, "");
const tailOf = (t) => stripAnsi(t).trim().split(/\r?\n/).filter(Boolean).slice(-6).join(" | ");
const parseCounts = (text) => ({
  passed: Number(/Passed:\s*(\d+)/.exec(text)?.[1] ?? NaN),
  failed: Number(/Failed:\s*(\d+)/.exec(text)?.[1] ?? NaN),
  errored: Number(/Errored:\s*(\d+)/.exec(text)?.[1] ?? 0),
});
const suiteOk = (run, counts) => run.code === 0 && (Number.isNaN(counts.failed) || (counts.failed === 0 && counts.errored === 0));
const runReproOnce = async (command, timeoutMs = 900000) => {
  const run = await sh(command, timeoutMs);
  const counts = parseCounts(stripAnsi(run.text));
  return { code: run.code, counts, tail: tailOf(run.text), passed: suiteOk(run, counts) };
};

// Kept review seats: round 0 is the full pass (the whole diff, every
// capture); every later pass on the same seat reviews the delta since
// its last look — the files and renders whose content hash changed —
// plus the directives the worker applied in between. A seat that dies
// rebuilds and takes the full brief again: it holds no context.
const reviewSeats = {};
const redTeamSeats = {};
let reviewPrints = null;
let redTeamPrints = null;
const normPath = (p) => p.replace(/\\/g, "/").toLowerCase();
const fingerprintAll = async (files) => {
  const map = {};
  if (!files.length) return map;
  const out = await sh(`Get-FileHash ${files.map((f) => `"${f}"`).join(", ")} -Algorithm SHA256 -ErrorAction SilentlyContinue | ForEach-Object { "$($_.Path.ToLower())|$($_.Hash)" }`);
  for (const line of out.text.split(/\r?\n/)) {
    const bar = line.indexOf("|");
    if (bar > 0) map[normPath(line.slice(0, bar))] = line.slice(bar + 1);
  }
  return map;
};
const REVIEW_DELTA_BRIEF = (ref, id, round, directives, files, captures, repro) => `You are the REVIEW PAIR seat [${ref}] for bug ${id}, pass ${round}. You reviewed this fix's diff earlier in this same session: the derivation, the casebook, the reproduction, and every file you read then are still yours — do not re-read them.

The worker then applied these directives:
${directives.map((d, i) => `${i + 1}. ${d}`).join("\n")}

Files changed since your last pass:
${files.length ? files.map((f) => `- ${f}`).join("\n") : "- (none)"}

Renders added or changed since your last pass (view EACH):
${captures.length ? captures.map((c) => `- ${c}`).join("\n") : "- (none)"}

The frozen repro files (${(repro?.files ?? []).join(", ") || "none"}) stay untouchable; a hunk in any of them is still an automatic finding. Judge: (1) each directive landed — the code now does what the directive says; (2) the delta broke nothing you already accepted; (3) the delta introduces nothing new. Do NOT re-run the battery or the repro and do not restate findings the referee refuted or ruled out of scope. Same bar as your first pass: only what would embarrass us in a human review.`;

const runReview = async (id, derivation, changed, captures, repro, label, appliedDirectives) => {
  const refs = (args.reviewModels ?? meta.params.reviewModels.default).split(",").map((r) => r.trim()).filter(Boolean);
  for (const ref of refs) {
    if (/glm-5\.3(?!-flash)/i.test(ref)) throw new Error(`Review ref '${ref}' refused: glm-5.3 has no vision and is not a review seat; the pair is deepseek + glm-5.3-flash.`);
  }
  const freshPrints = await fingerprintAll([...changed, ...captures]);
  const known = reviewPrints ?? {};
  const delta = {
    files: changed.filter((f) => !(normPath(f) in known) || known[normPath(f)] !== freshPrints[normPath(f)]),
    captures: captures.filter((c) => !(normPath(c) in known) || known[normPath(c)] !== freshPrints[normPath(c)]),
  };
  const isDeltaPass = reviewPrints !== null;
  const seatFor = async (ref) => {
    if (!reviewSeats[ref]) reviewSeats[ref] = await session({ ...reviewOpts(ref), title: `${id} review [${ref}]` });
    return reviewSeats[ref];
  };
  const passes = await parallel(refs.map((ref) => () =>
    (async () => {
      const seat = await seatFor(ref);
      const brief = isDeltaPass
        ? REVIEW_DELTA_BRIEF(ref, id, label ?? "next", appliedDirectives ?? [], delta.files, delta.captures, repro)
        : REVIEW_BRIEF(ref, id, derivation, changed, captures, repro);
      try {
        return await seat.run(brief, { schema: REVIEW_SCHEMA });
      } catch {
        reviewSeats[ref] = await session({ ...reviewOpts(ref), title: `${id} review [${ref}] (rebuilt)` });
        return await reviewSeats[ref].run(REVIEW_BRIEF(ref, id, derivation, changed, captures, repro), { schema: REVIEW_SCHEMA });
      }
    })().then((r) => ({ ref, ...r }))));
  reviewPrints = { ...known, ...freshPrints };
  const live = passes.filter(Boolean);
  // Both seats must run: an all-dead pair is an infrastructure failure and
  // must never read as clean.
  if (!live.length) {
    return { verdict: "findings", findings: [`[review] NO review pass could run (${refs.join(", ")}) — infrastructure, not a code finding. Do not treat this as clean; re-run before landing.`], notes: "all passes failed", passes: [], failed: refs.length };
  }
  const findings = live.flatMap((p) => p.findings.map((f) => `[${p.ref}] ${f}`));
  return { verdict: findings.length ? "findings" : "clean", findings, notes: live.map((p) => `[${p.ref}] ${p.notes}`).join(" | ") + (live.length < refs.length ? ` | WARNING: ${refs.length - live.length} pass(es) failed` : ""), passes: live.map((p) => ({ ref: p.ref, verdict: p.verdict, findings: p.findings.length })), failed: refs.length - live.length };
};

const REDTEAM_DELTA = (ref, id, round, directives, files, repro) => `You are the RED TEAM seat [${ref}] for bug ${id}, pass ${round}. You hunted this fix earlier in this same session; the derivation and the diff you read then are still yours — do not re-read them.

The worker then applied these directives:
${directives.map((d, i) => `${i + 1}. ${d}`).join("\n")}

Files changed since your last pass:
${files.length ? files.map((f) => `- ${f}`).join("\n") : "- (none — judge the review pair's approval and the worker's answer)"}

Hunt where the delta opened: what the fix broke, what the directives papered over, the state the fix shares, the callers the census missed. The frozen repro files (${(repro?.files ?? []).join(", ") || "none"}) stay untouchable. Do NOT re-run the battery or the repro and do not restate findings the referee refuted. Same bar as your first pass.`;

const runRedTeam = async (id, derivation, changed, repro, label, appliedDirectives) => {
  const refs = (args.redTeamModels ?? meta.params.redTeamModels.default).split(",").map((r) => r.trim()).filter(Boolean);
  if (!refs.length) return null;
  const freshPrints = await fingerprintAll(changed);
  const known = redTeamPrints ?? {};
  const deltaFiles = redTeamPrints === null ? null : changed.filter((f) => !(normPath(f) in known) || known[normPath(f)] !== freshPrints[normPath(f)]);
  const seatFor = async (ref) => {
    if (!redTeamSeats[ref]) redTeamSeats[ref] = await session({ ...reviewOpts(ref), title: `${id} red team [${ref}]` });
    return redTeamSeats[ref];
  };
  const passes = await parallel(refs.map((ref) => () =>
    (async () => {
      const seat = await seatFor(ref);
      const brief = deltaFiles ? REDTEAM_DELTA(ref, id, label ?? "next", appliedDirectives ?? [], deltaFiles, repro) : REDTEAM_BRIEF(id, derivation, changed, repro);
      try {
        return await seat.run(brief, { schema: REDTEAM_SCHEMA });
      } catch {
        redTeamSeats[ref] = await session({ ...reviewOpts(ref), title: `${id} red team [${ref}] (rebuilt)` });
        return await redTeamSeats[ref].run(REDTEAM_BRIEF(id, derivation, changed, repro), { schema: REDTEAM_SCHEMA });
      }
    })().then((r) => ({ ref, ...r }))));
  redTeamPrints = { ...known, ...freshPrints };
  const live = passes.filter(Boolean);
  if (!live.length) {
    return { verdict: "findings", findings: [`[redteam] NO red-team pass could run (${refs.join(", ")}) — infrastructure, never a clean approval.`], notes: "all passes failed", failed: refs.length };
  }
  const findings = live.flatMap((p) => p.findings.map((f) => `[redteam:${p.ref}] ${f}`));
  return { verdict: findings.length ? "findings" : "approve", findings, notes: live.map((p) => `[${p.ref}] ${p.notes}`).join(" | "), failed: refs.length - live.length };
};

const battery = async (stepId) => {
  await sh(`Get-Process DevNull* -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$((Get-Location).Path)*" } | Stop-Process -Force`);
  const fantomas = await sh("dotnet fantomas .");
  const build = await sh("dotnet build DevNull.Client.slnx -v q");
  const buildErrors = /error/i.test(build.text) && !/0 Errores/.test(build.text);
  const coreRun = await sh("dotnet run --project tests/DevNull.Client.Core.Tests -c Debug --no-build -- --summary");
  const core = { ...parseCounts(stripAnsi(coreRun.text)), exit: coreRun.code, tail: tailOf(coreRun.text) };
  let uiRun = await sh("dotnet run --project tests/DevNull.Client.UI.Tests -c Debug --no-build -- --sequenced --summary");
  let ui = { ...parseCounts(stripAnsi(uiRun.text)), exit: uiRun.code, tail: tailOf(uiRun.text) };
  let uiRetry = false;
  if (!suiteOk(uiRun, ui)) {
    uiRetry = true;
    uiRun = await sh("dotnet run --project tests/DevNull.Client.UI.Tests -c Debug --no-build -- --sequenced --summary");
    ui = { ...parseCounts(stripAnsi(uiRun.text)), exit: uiRun.code, tail: tailOf(uiRun.text) };
  }
  const status = (await sh("git status --porcelain=v1")).text;
  const changed = status.split(/\r?\n/).filter((l) => l.length > 3).map((l) => l.substring(3).trim().replace(/^"|"$/g, ""));
  const fsFiles = changed.filter((f) => /\.fsi?$/i.test(f));
  const gateOut = fsFiles.length ? (await sh(`pwsh -NoProfile audits/gates.ps1 ${fsFiles.map((f) => `"${f}"`).join(" ")}`)).text : "";
  const hitsOnAdded = [];
  for (const f of fsFiles) {
    const hunkText = (await sh(`git diff -U0 -- "${f}"`)).text;
    const added = [];
    for (const line of hunkText.split("\n")) {
      const m = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/.exec(line);
      if (m) { const s = Number(m[1]); const c = m[2] === undefined ? 1 : Number(m[2]); if (c > 0) for (let i = 0; i < c; i++) added.push(s + i); }
    }
    if (!added.length) continue;
    for (const ln of gateOut.split("\n").filter((l) => l.startsWith(f + ":")).map((l) => Number(/:(\d+):/.exec(l)?.[1] ?? 0))) {
      if (added.includes(ln)) hitsOnAdded.push(`${f}:${ln}`);
    }
  }
  const captures = (stepId ? (await sh(`Get-ChildItem "${snapshotsDir}/${stepId}" -Filter *.png -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName`, 120000)) : { text: "" }).text.split(/\r?\n/).filter(Boolean);
  const hard = /files unchanged|No files were changed|0 files needed formatting/i.test(fantomas.text) && !buildErrors && suiteOk(coreRun, core) && suiteOk(uiRun, ui);
  return { pass: hard && hitsOnAdded.length === 0, hard, fantomas: fantomas.text.trim().split("\n").pop(), buildErrors: buildErrors ? build.text.trim().split("\n").filter((l) => /error/i.test(l)).slice(0, 5) : 0, core, ui, uiRetry, changed, fsFiles, gateHits: (gateOut.match(/FINDINGS/g) ?? []).length, hitsOnAdded, captures };
};

// The worker's battery is the verification of record; the loop never
// re-runs it. What the loop still collects each round is what the review
// consumes: the changed files, the fast idiom gates over added lines,
// and the capture list.
const collect = async (stepId) => {
  const status = (await sh("git status --porcelain=v1")).text;
  const changed = status.split(/\r?\n/).filter((l) => l.length > 3).map((l) => l.substring(3).trim().replace(/^\"|\"$/g, ""));
  const fsFiles = changed.filter((f) => /\.fsi?$/i.test(f));
  const gateOut = fsFiles.length ? (await sh(`pwsh -NoProfile audits/gates.ps1 ${fsFiles.map((f) => `"${f}"`).join(" ")}`)).text : "";
  const hitsOnAdded = [];
  for (const f of fsFiles) {
    const hunkText = (await sh(`git diff -U0 -- "${f}"`)).text;
    const added = [];
    for (const line of hunkText.split("\n")) {
      const m = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/.exec(line);
      if (m) { const s = Number(m[1]); const c = m[2] === undefined ? 1 : Number(m[2]); if (c > 0) for (let i = 0; i < c; i++) added.push(s + i); }
    }
    if (!added.length) continue;
    for (const ln of gateOut.split("\n").filter((l) => l.startsWith(f + ":")).map((l) => Number(/:(\d+):/.exec(l)?.[1] ?? 0))) {
      if (added.includes(ln)) hitsOnAdded.push(`${f}:${ln}`);
    }
  }
  const captures = (stepId ? (await sh(`Get-ChildItem "${snapshotsDir}/${stepId}" -Filter *.png -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName`, 120000)) : { text: "" }).text.split(/\r?\n/).filter(Boolean);
  return { changed, gateHits: (gateOut.match(/FINDINGS/g) ?? []).length, hitsOnAdded, captures };
};

const closeSeats = async (seats) => {
  for (const s of Object.values(seats)) {
    try { await s?.close(); } catch { /* already gone */ }
  }
};
const saveScope = async (id, round, findings, ruling) => {
  let doc = { rounds: [] };
  try { doc = JSON.parse(await read(scopePath(id))); } catch { /* first round */ }
  doc.rounds.push({ round, ts: new Date().toISOString(), findings, rulings: ruling.rulings ?? [], notes: ruling.notes ?? "" });
  await tools.write_file({ path: scopePath(id), content: JSON.stringify(doc, null, 2) });
};
const runTriage = async (id, st, issueText, force) => {
  let existing = null;
  try { existing = JSON.parse(await read(triagePath(id))); } catch { /* none */ }
  if (existing && existing.classification && !force) return existing;
  const triage = await step("triage", () => withMetrics(`${id} triage`, { step: id, round: 0, role: "triage" }, () =>
    agent(TRIAGE_BRIEF(id, st, issueText), { ...triageOpts(), title: `${id} triage`, subSessions: false, schema: TRIAGE_SCHEMA })));
  await tools.write_file({ path: triagePath(id), content: JSON.stringify(triage, null, 2) });
  return triage;
};
const deriveFor = async (id, st, force, seats, issueText, triage, repro) => {
  let existing = null;
  try { existing = await read(derivedPath(id)); } catch { /* none */ }
  if (existing && existing.trim() && !force) return existing;
  if (!seats.deriver) seats.deriver = await session({ ...deriveOpts(), title: `${id} deriver + scope referee` });
  const text = await withMetrics(`${id} derivation`, { step: id, round: 0, role: "derive" }, () => seats.deriver.run(DERIVE_BRIEF(id, st, issueText, triage, repro)), () => seats.deriver.id);
  await tools.write_file({ path: derivedPath(id), content: `# ${id} derivation — ${today}\n\n${String(text)}` });
  return String(text);
};
const runScope = async (seats, id, st, findings, round, triage, repro) => {
  const attempt = async () => {
    if (!seats.deriver) seats.deriver = await session({ ...deriveOpts(), title: `${id} deriver + scope referee` });
    return seats.deriver.run(SCOPE_BRIEF(id, st, findings, round, triage, repro), { schema: SCOPE_SCHEMA });
  };
  try {
    return await attempt();
  } catch {
    seats.deriver = null; // the seat died: one rebuild; the brief tells a fresh deriver to read the derivation file
    return await attempt();
  }
};

// The reproduction gate: the frozen files' hashes must not have moved, and
// the recorded command must pass on the fixed tree.
const verifyRepro = async (id) => {
  const doc = JSON.parse(await read(reproPath(id)));
  const hashes = await hashesFor(Object.keys(doc.hashes ?? {}));
  const changedRepro = Object.keys(doc.hashes ?? {}).filter((f) => hashes[f] !== doc.hashes[f]);
  const rerun = await runReproOnce(doc.command);
  return { changedRepro, rerun, passed: rerun.passed };
};

// The reproducer seat: writes the failing test, the loop commits it and
// proves it fails on the broken tree. A pass that touches the product tree
// or produces a test that does not fail costs one round.
const reproFor = async (id, st, issueText, triage, seats) => {
  let existing = null;
  try { existing = JSON.parse(await read(reproPath(id))); } catch { /* none */ }
  if (existing && existing.command && !args.rerepro) return existing;
  if (!seats.reproducer) seats.reproducer = await session({ ...reproOpts(), title: `${id} reproducer` });
  const maxRounds = args.maxReproRounds ?? meta.params.maxReproRounds.default;
  const issueNumber = (args.issue ?? "").trim();
  let directive = null;
  for (let round = 1; round <= maxRounds; round++) {
    const brief = round === 1 ? REPRO_BRIEF(id, st, issueText, triage) : REPRO_RETRY_BRIEF(id, directive, round);
    const repro = await step("repro", () => withMetrics(`${id} repro r${round}`, { step: id, round, role: "repro" }, () =>
      seats.reproducer.run(brief, { schema: REPRO_SCHEMA }), () => seats.reproducer?.id));
    await sh("dotnet fantomas .");
    const status = (await sh("git status --porcelain=v1")).text;
    const changed = status.split(/\r?\n/).filter((l) => l.length > 3).map((l) => l.substring(3).trim().replace(/^"|"$/g, ""));
    const codeFiles = changed.filter(forbiddenReproPath);
    if (codeFiles.length) {
      const paths = codeFiles.map((f) => `"${f}"`).join(" ");
      await sh(`git checkout -- ${paths}`); // tracked reverts
      await sh(`git clean -f -- ${paths}`); // untracked removals
      directive = `You changed non-test files, now reverted: ${codeFiles.join(", ")}. Write ONLY tests and fixtures under tests/.`;
      continue;
    }
    if (!changed.length || !(repro?.command ?? "").trim()) {
      directive = !changed.length ? "No files changed: write the repro test." : "The command was missing or empty: answer with the exact command that runs the repro test alone.";
      continue;
    }
    const commitMsg = `test: reproduce ${issueNumber ? "#" + issueNumber : id.toLowerCase()}`;
    const headMsg = (await sh("git log -1 --pretty=%s")).text.trim();
    const amend = headMsg === commitMsg ? " --amend --no-edit" : "";
    await sh(`git add -A; git commit${amend} -m "${commitMsg}"`);
    const before = await runReproOnce(repro.command);
    if (before.passed) {
      directive = `The command PASSED on the broken tree — it does not reproduce the defect. Make it fail for the reported reason:\n${before.tail}\nThe loop re-commits the corrected test.`;
      continue;
    }
    const hashes = await hashesFor(changed);
    const doc = { id, issue: issueNumber, ts: new Date().toISOString(), round, command: repro.command, files: changed, hashes, failTail: before.tail, exit: before.code, assertion: repro.assertion ?? "", notes: repro.notes ?? "" };
    await tools.write_file({ path: reproPath(id), content: JSON.stringify(doc, null, 2) });
    return doc;
  }
  return null;
};

// The landing is a fix: pull request: commit on the bug branch, push the
// branch, open the PR against main with the before/after evidence. Never a
// merge, never a push of main.
const landStep = async (id, title, summary) => {
  const repro = JSON.parse(await read(reproPath(id)));
  const planText = await readPlan();
  const finalTitle = title || `fix: ${id.toLowerCase()}`;
  if (planText.includes(`- [ ] **${id} `)) {
    const ticked = planText
      .replace(`- [ ] **${id} `, `- [x] **${id} `)
      .replace(new RegExp(`\\| ${id} \\| pending \\|([^|]*)\\|`), `| ${id} | pr ${today} | ${finalTitle.replace(/\|/g, "/")} |`);
    await tools.write_file({ path: plan, content: ticked });
  }
  await sh("dotnet fantomas .");
  const safeTitle = finalTitle.replace(/"/g, "'");
  const commit = await sh(`git add -A; git commit -m "${safeTitle}"`);
  const push = await sh(`git push -u origin HEAD`);
  const after = await runReproOnce(repro.command);
  const issueRef = repro.issue ? `#${repro.issue}` : id;
  const body = `## What\n\n${summary || finalTitle} (${issueRef})\n\n## Evidence\n\n- fails-before: the frozen repro test failed on the broken tree\n- passes-after: the same command exits green on the fix${after.passed ? "" : " (WARNING: the land-time rerun did not pass — read the CI run)"}\n\nFail tail (broken tree):\n\n\`\`\`\n${String(repro.failTail ?? "").slice(-1200)}\n\`\`\`\n\nPass tail (fixed tree):\n\n\`\`\`\n${after.tail}\n\`\`\`\n\n## Verification\n\n- fantomas clean, build 0 errors\n- Core and UI suites green (sequenced)\n- review pair clean over the diff and the rendered captures\n\n_This pull request was opened by CodeAlta, an AI coding agent, at the user's direction._\n`;
  await tools.write_file({ path: prBodyPath, content: body });
  const pr = await sh(`gh pr create --base main --title "${safeTitle}" --body-file "${prBodyPath}"`, 180000);
  const prNumber = /\/pull\/(\d+)/.exec(pr.text.trim())?.[1] ?? "";
  const shots = (await sh(`(Get-ChildItem "${snapshotsDir}/${id}" -Filter *.png -ErrorAction SilentlyContinue | Sort-Object Name).Name`)).text.split(/\r?\n/).map((s) => s.trim()).filter(Boolean);
  if (prNumber && shots.length) {
    const attachFlags = shots.map((f) => `--attach "${snapshotsDir}/${id}/${f}#${f.replace(/^[bB]\d+-/, "").replace(/^_/, "").replace(/\.png$/, "").replace(/-/g, " ")}"`).join(" ");
    await sh(`gh pr edit ${prNumber} ${attachFlags}`, 600000);
  }
  return { title: finalTitle, commit: commit.text.trim().split("\n").slice(0, 2).join(" "), push: push.code === 0, passAfter: after.passed, captures: shots.length, pr: pr.text.trim() };
};

// ---------------- standalone actions ----------------
const action = args.action ?? meta.params.action.default;
let id = (args.step ?? "").trim();
const issueNumber = (args.issue ?? "").trim();
if (!id && issueNumber) id = `B${issueNumber}`;

if (action === "window") {
  const w = windowStatus();
  const issueText = await fetchIssue(issueNumber);
  const planText = await readPlan();
  const checks = [];
  checks.push(!planText ? `plan: MISSING (${plan}) — the loop works from the issue alone` : `${stepText(planText, id) ? "plan: step found" : "plan: step not found — the loop works from the issue alone"}`);
  checks.push(issueText && !issueText.startsWith("(") ? `issue: fetched (${issueText.split("\n")[0]})` : "issue: NOT FETCHED (pass the issue number)" );
  checks.push((await sh("gh auth status", 60000)).code === 0 ? "gh: authenticated" : "gh: NOT AUTHENTICATED (no issue fetch, no PR)");
  const dirty = (await sh("git status --porcelain=v1")).text.trim();
  checks.push(dirty ? `tree: DIRTY (${dirty.split(/\r?\n/).length} path(s)) — run refuses` : "tree: clean");
  checks.push(`captures: ${snapshotsDir} (gitignored evidence)`);
  present(`## Window\n\n- now: ${w.nowUtc} (${w.weekday ? "weekday" : "weekend"})\n- status: **${w.inPeak ? "PEAK — wait" : "off-peak — clear to run"}**\n- next change: ${w.nextChange}\n\n## Readiness\n\n- ${checks.join("\n- ")}`);
  return { window: w, checks, inPeak: w.inPeak };
}

if (action === "triage") {
  const issueText = await fetchIssue(issueNumber);
  const st = stepText(await readPlan(), id) ?? issueStep(issueText, id);
  if (!st) throw new Error(`No issue text and no plan step for ${id} — nothing to triage.`);
  const triage = await runTriage(id, st, issueText, args.retriage ?? meta.params.retriage.default);
  present(`## ${id} triage\n\n- class: **${triage.classification}**${triage.duplicateOf ? ` (duplicate of ${triage.duplicateOf})` : ""}\n- expected: ${triage.expected}\n- actual: ${triage.actual}\n- repro: ${triage.reproClass}\n- risk: **${triage.risk}** (${triage.riskWhy})\n- changelog: ${triage.changelogRule} (${triage.changelogWhy})\n- proceed: **${triage.canProceed ? "yes" : "NO"}**${triage.canProceed ? "" : ` — ${triage.proceedWhy}`}\n\nRepro plan: ${triage.reproPlan}`);
  return { triage };
}

if (action === "repro") {
  if ((await sh("git status --porcelain=v1")).text.trim()) throw new Error("The working tree is dirty — commit or stash first; the loop commits the repro test.");
  const issueText = await fetchIssue(issueNumber);
  const st = stepText(await readPlan(), id) ?? issueStep(issueText, id);
  if (!st) throw new Error(`No issue text and no plan step for ${id}.`);
  const triage = await runTriage(id, st, issueText, args.retriage ?? meta.params.retriage.default);
  if (!triage.canProceed) throw new Error(`Triage says stop: ${triage.proceedWhy}`);
  const seats = { reproducer: null };
  try {
    const repro = await reproFor(id, st, issueText, triage, seats);
    if (!repro) throw new Error(`The reproduction never landed within ${args.maxReproRounds ?? meta.params.maxReproRounds.default} round(s); the dossier says why.`);
    present(`## ${id} reproduction\n\n- command: \`${repro.command}\`\n- assertion of record: ${repro.assertion}\n- files (frozen): ${repro.files.join(", ")}\n- failed on the broken tree (exit ${repro.exit}); evidence in ${reproPath(id)}`);
    return { repro };
  } finally { await closeSeats(seats); }
}

if (action === "derive") {
  const seats = { deriver: null, reproducer: null };
  try {
    const issueText = await fetchIssue(issueNumber);
    const st = stepText(await readPlan(), id) ?? issueStep(issueText, id);
    const triage = JSON.parse(await read(triagePath(id)));
    const repro = JSON.parse(await read(reproPath(id)));
    const derivation = await deriveFor(id, st, args.rederive ?? meta.params.rederive.default, seats, issueText, triage, repro);
    present(`## ${id} derivation\n\n${derivation}`);
    return { step: id, derivation };
  } finally { await closeSeats(seats); }
}

if (action === "battery") {
  const results = await step("battery", () => battery(id ?? null));
  const count = (c) => (Number.isNaN(c.passed) ? `exit ${c.exit} (unparsed)` : `${c.passed}/${c.failed}`);
  present(`## ${id ?? "tree"} battery: ${results.pass ? "ALL GREEN" : results.hard ? `GREEN with ${results.hitsOnAdded.length} flag(s)` : "FAILED"}\n- fantomas ${results.fantomas}; build ${results.buildErrors === 0 ? 0 : results.buildErrors} errors\n- Core ${count(results.core)}; UI ${count(results.ui)}${results.uiRetry ? " (retried)" : ""}\n- gates: ${results.gateHits} pre-existing, ${results.hitsOnAdded.length} on added lines\n- captures: ${results.captures.length}${results.captures.length ? " — " + results.captures.map((c) => c.split(/[\\/]/).pop()).join(", ") : ""}`);
  return { battery: results };
}

if (action === "land") {
  const wrap = (msg) => (msg && /^(fix|feat|refactor|docs|test|chore):/i.test(msg) ? msg : msg ? `fix: ${msg}` : `fix: ${id.toLowerCase()}`);
  let note = args.message ?? "";
  if (!note) { try { note = JSON.parse(await read(verdictPath(id))).ledgerNote ?? ""; } catch { /* none */ } }
  const out = await landStep(id, wrap(cleanNote(note)), String(note));
  present(`## ${id} landed as a fix: pull request\n\n\`\`\`\n${String(out.pr).split("\n").slice(0, 2).join("\n")}\n\`\`\`\n\npass-after at land: ${out.passAfter ? "green" : "WARNING: did not pass"}`);
  return out;
}

if (action === "rollback") {
  const head = (await sh("git log --oneline -1")).text.trim();
  const steps = [];
  steps.push((await sh("git reset --soft HEAD~1")).text.trim() || "branch soft-reset: changes staged for review");
  steps.push((await sh(`git push origin --delete HEAD 2>$null`)).code === 0 ? "remote branch deleted" : "remote branch left in place");
  const fresh = await readPlan();
  if (fresh.includes(`- [x] **${id} `)) {
    await tools.write_file({ path: plan, content: fresh.replace(`- [x] **${id} `, `- [ ] **${id} `).replace(new RegExp(`\\| ${id} \\| pr [^|]*\\|[^|]*\\|`), `| ${id} | pending | (rolled back for user review) |`) });
    steps.push("plan unticked");
  }
  present(`## ${id} rolled back\n\n- ${steps.join("\n- ")}\n- Changes staged on the current branch for your review.`);
  return { undone: head, steps };
}

// ---------------- the one-issue run ----------------
if (action !== "run") throw new Error(`Unknown action '${action}'.`);
const w = guardWindow();
if ((await sh("git status --porcelain=v1")).text.trim()) {
  throw new Error("The working tree is dirty — commit or stash first; the loop commits the repro test and the fix.");
}
const maxRemediations = args.maxRemediations ?? meta.params.maxRemediations.default;
const budget = Math.min(maxRemediations, args.maxReviewRounds ?? meta.params.maxReviewRounds.default);
const maxScopeReviews = args.maxScopeReviews ?? meta.params.maxScopeReviews.default;
const maxReproRounds = args.maxReproRounds ?? meta.params.maxReproRounds.default;
const resume = args.resume ?? meta.params.resume.default;
const branch = (args.branch ?? "").trim() || `bug/${issueNumber || id.toLowerCase()}`;
const branchStep = (await sh(`git rev-parse --verify ${branch}`)).code === 0 ? `git switch ${branch}` : `git switch -c ${branch}`;

const issueText = await fetchIssue(issueNumber);
const stRun = stepText(await readPlan(), id) ?? issueStep(issueText, id);
if (!stRun) throw new Error(`Step ${id} not found in ${plan} and no issue text — nothing to work from.`);

const seats = { deriver: null, worker: null, reproducer: null };
const stopped = [];
let prResult = null;
try {
  // Triage gates the run before any seat or branch exists.
  const triage = await runTriage(id, stRun, issueText, args.retriage ?? meta.params.retriage.default);
  if (!triage.canProceed || (triage.classification ?? "bug") !== "bug") {
    const why = !triage.canProceed ? triage.proceedWhy : `classified ${triage.classification}${triage.duplicateOf ? ` (duplicate of ${triage.duplicateOf})` : ""} — not bug work`;
    stopped.push({ step: id, why });
    present(`### ${id} STOPPED by triage\n\n${why}. Nothing ran, nothing landed.`);
    return { stopped, branch: null, plan, window: w, triage, note: "The tree is untouched." };
  }
  const highRisk = (triage.risk ?? "normal") === "high";
  await sh(branchStep);

  // The reproduction: cached, or produced and committed by the reproducer.
  let repro = null;
  try { repro = JSON.parse(await read(reproPath(id))); } catch { /* none yet */ }
  if (!resume || !repro?.command) {
    repro = await reproFor(id, stRun, issueText, triage, seats);
    if (!repro) {
      stopped.push({ step: id, why: `no reproduction within ${maxReproRounds} reproducer round(s) — the defect never failed the way the issue reports` });
      present(`### ${id} STOPPED\n\nThe reproduction never landed. The test commits (if any) stay on ${branch}; the dossier says what failed. Your judgment is next.`);
      return { stopped, branch, plan, window: w, triage, note: "Nothing landed; the branch keeps the repro attempts." };
    }
  }

  const derivation = await deriveFor(id, stRun, args.rederive ?? meta.params.rederive.default, seats, issueText, triage, repro);
  present(`## ${id} — ${stRun.title}\n\nBranch ${branch}, risk **${highRisk ? "HIGH" : "normal"}${highRisk ? ` (${triage.riskWhy})` : ""}. Repro committed and failing (exit ${repro.exit}); derivation done. One kept worker seat (its battery the record), one cheap collect, then the repro gate, then the kept review pair (deltas)${highRisk ? " plus the kept red team" : ""}, then the deriver rules scope; ${maxScopeReviews} scope cycle(s) allowed — needing one more stops. autoLand ${args.autoLand ? "ON" : "OFF"}; the landing is a fix: pull request.`);

  seats.worker = await session({ ...workerOpts, title: `${id} worker` });
  const sendWorker = async (round, directives) => {
    try {
      return await seats.worker.run(directives === null ? WORKER_BRIEF(id, stRun, derivation, repro) : REMEDIATE_BRIEF(id, directives, round, repro));
    } catch {
      // The seat died: one cold rebuild. A remediation rides the full brief.
      seats.worker = await session({ ...workerOpts, title: `${id} worker (rebuilt)` });
      return await seats.worker.run(directives === null ? WORKER_BRIEF(id, stRun, derivation, repro) : REMEDIATE_BRIEF(id, directives, round, repro));
    }
  };
  let results = null;
  let verify = null;
  let review = null;
  let redTeam = null;
  const reviewPass = async (label, appliedDirectives) => {
    review = await step("review", () => withMetrics(`${id} review ${label}`.trim(), { step: id, round: label, role: "review" }, () => runReview(id, derivation, results.changed, results.captures, repro, label, appliedDirectives)));
    await tools.write_file({ path: verdictPath(id), content: JSON.stringify({ verdict: review.verdict, directives: review.findings, ledgerNote: `${stRun.title} (${issueNumber ? "#" + issueNumber : id})` }, null, 2) });
    redTeam = null;
    if (highRisk && verify.passed && !verify.changedRepro.length && review?.verdict === "clean") {
      redTeam = await step("redteam", () => withMetrics(`${id} red team ${label}`.trim(), { step: id, round: label, role: "redteam" }, () => runRedTeam(id, derivation, results.changed, repro, label, appliedDirectives)));
    }
  };

  let workerReport = resume
    ? "(resumed: the diff on the branch is the interrupted run's worker report; verify against the tree, not this text)"
    : await step("fix", () => withMetrics(`${id} worker`, (r) => ({ step: id, round: 0, role: "worker", reportChars: String(r).length }), () => sendWorker(0, null), () => seats.worker.id));
  results = await step("collect", () => collect(id));
  verify = await step("verify", () => withMetrics(`${id} verify`, { step: id, round: 0, role: "verify" }, () => verifyRepro(id)));
  await reviewPass("");

  let rounds = 0;
  let scopeReviews = 0;
  let previousFindings = "";
  let halted = null;

  while ((results.hitsOnAdded.length > 0 || !verify.passed || verify.changedRepro.length > 0 || review?.verdict === "findings" || redTeam?.verdict === "findings") && rounds < budget && !halted) {
    const findings = [...(review?.findings ?? []), ...(redTeam?.findings ?? [])];
    const findingsKey = findings.join("||");
    if (findingsKey && findingsKey === previousFindings) {
      halted = { why: "no progress: the same findings returned after a scope-reviewed pass", findings };
      break;
    }
    if (verify.changedRepro.length) {
      halted = { why: `the frozen repro files were touched: ${verify.changedRepro.join(", ")} — the reproduction is the contract; nothing lands`, findings };
      break;
    }

    if (results.hitsOnAdded.length || !verify.passed) {
      // A gate flag or a still-failing repro is mechanical — the step's
      // own bar, no referee.
      rounds++;
      previousFindings = findingsKey;
      const why = results.hitsOnAdded.length
        ? `The idiom gates flagged added lines: ${results.hitsOnAdded.join(", ")}. Fix what they name.`
        : `The repro command still fails on the fixed tree:\n${verify.rerun.tail}\nFix the root cause; the frozen test stays untouched.`;
      present(`### ${id} round ${rounds}: ${results.hitsOnAdded.length ? "the idiom gates flagged added lines" : "the repro still fails"} — same worker seat, mechanical fix`);
      workerReport = await step("remediate", () => sendWorker(rounds, [why]));
      results = await step("collect", () => collect(id));
      verify = await step("verify", () => withMetrics(`${id} verify r${rounds}`, { step: id, round: rounds, role: "verify" }, () => verifyRepro(id)));
      await reviewPass(`r${rounds}`, [why]);
      continue;
    }

    if (!findings.length) {
      rounds++; // pathological verdict: consume a pass so this cannot spin
      await reviewPass(`r${rounds}`);
      continue;
    }

    // ---- The principal gate: the deriver rules every review finding ----
    if (scopeReviews + 1 > maxScopeReviews) {
      halted = { why: `scope budget spent: review cycle ${scopeReviews + 1} would be needed, ${maxScopeReviews} allowed — the loop went back more than twice; your judgment is next`, findings };
      break;
    }
    scopeReviews++;
    const ruling = await step("scope", () => withMetrics(`${id} scope r${scopeReviews}`, (r) => ({ step: id, round: scopeReviews, role: "scope", findings: findings.length, rulings: r?.rulings?.length ?? 0 }), () => runScope(seats, id, stRun, findings, scopeReviews, triage, repro), () => seats.deriver?.id));
    await saveScope(id, scopeReviews, findings, ruling);
    for (const t of (ruling.rulings ?? []).filter((r) => r.ruling === "task" && r.taskTitle)) {
      await alta.task.create({ title: t.taskTitle, kind: t.taskKind ?? "problem", summary: t.taskSummary ?? t.reason, content: `## Why\n\nFound while reviewing bug ${id}: ${t.finding}\n\nThe scope referee ruled it real, but beyond the bug's promise.\n\n## What to do\n\n${t.reason}\n\n## Where\n\nA review finding on the ${id} diff.` });
    }
    const directives = (ruling.rulings ?? []).filter((r) => r.ruling === "in-scope" && r.directive).map((r) => r.directive);
    const refuted = (ruling.rulings ?? []).filter((r) => r.ruling === "refuted").length;
    present(`### ${id} scope review ${scopeReviews} — ${directives.length} in-scope · ${(ruling.rulings ?? []).filter((r) => r.ruling === "task").length} task(s) recorded · ${refuted} refuted`);
    previousFindings = findingsKey;

    if (!directives.length) {
      // Nothing for the worker: every finding was beyond scope or refuted.
      review = { ...review, verdict: "clean", findings: [], notes: `${review.notes} | the scope referee ruled every finding out of scope or refuted (rulings on record); the user's gate reviews them` };
      break;
    }

    rounds++;
    present(`### ${id} round ${rounds}: ${directives.length} scope-reviewed directive(s) to the worker seat — delta review after`);
    workerReport = await step("remediate", () => sendWorker(rounds, directives));
    results = await step("collect", () => collect(id));
    verify = await step("verify", () => withMetrics(`${id} verify r${rounds}`, { step: id, round: rounds, role: "verify" }, () => verifyRepro(id)));
    await reviewPass(`r${rounds}`, directives);
  }

  if (halted) {
    stopped.push({ step: id, ...halted });
    present(`### ${id} STOPPED\n\n${halted.why}. Nothing landed; the branch keeps the work.`);
  } else if (verify.passed && !verify.changedRepro.length && review?.verdict === "clean" && redTeam?.verdict !== "findings") {
    present(`### ${id} PASSED — battery green, repro fails-before and passes-after, review pair clean${redTeam ? ", red team clean" : ""}\n\nRepro: \`${repro.command}\`\nCaptures: ${results.captures.length ? results.captures.map((c) => c.split(/[\\/]/).pop()).join(", ") : "none"}\n\n${args.autoLand ? "Opening the fix: pull request…" : "**Stopped for your review; run action 'land' to open the pull request.**"}${review.notes ? `\n\nReview notes: ${review.notes}` : ""}`);
    if (args.autoLand) {
      prResult = await step("land", () => landStep(id, `fix: ${cleanNote(stRun.title)}${issueNumber ? ` (#${issueNumber})` : ""}`, String(workerReport).slice(0, 1500)));
      present(`### ${id} pull request opened\n\n${String(prResult.pr).split("\n")[0]}\n\npass-after at land: ${prResult.passAfter ? "green" : "WARNING: did not pass"}`);
    }
  } else if (!stopped.length) {
    stopped.push({ step: id, why: !verify.passed ? "the repro still fails after remediation" : verify.changedRepro.length ? "the repro files were touched" : `pass budget (${budget} worker passes) spent — your judgment is next`, findings: [...(review?.findings ?? []), ...(redTeam?.findings ?? [])] });
    present(`### ${id} STOPPED\n\n${!verify.passed ? "The repro still fails." : verify.changedRepro.length ? "The repro files were touched." : "The pass budget is spent."} Nothing landed; the branch keeps the work.`);
  }
  return { pr: prResult, stopped, branch, plan, window: w, risk: triage.risk, note: "Nothing was merged; the pull request (if any) waits for review." };
} finally {
  await closeSeats(seats);
  await closeSeats(reviewSeats);
  await closeSeats(redTeamSeats);
}