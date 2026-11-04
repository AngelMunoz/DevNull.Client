// The DevNull feature loop: ONE issue per run — scout -> derive -> worker
// -> battery -> review pair (the diff AND the rendered captures, vision,
// both models) -> the deriver's scope ruling -> remediation on the same
// worker seat -> a pull request when it passes. No architect: that seat
// belongs to the refactor loop (.alta/scripts/devnull-step.js). The run
// refuses the peak windows for both providers (weekdays 01:00-04:00 and
// 06:00-10:00 UTC) unless forced; action "window" reports status and
// readiness. Never merges and never pushes main: the branch and its pull
// request are the landing, and the merge stays with the user.
export const meta = {
  name: "devnull-feature",
  title: "DevNull feature loop (one issue, one PR)",
  description: "Works exactly one issue from the feature workplan: a parity census against the server repository, a derivation that sanitizes the contract and writes the behavior and visual specs, one kept worker seat, the battery with snapshot captures, a mixed-model review pair reading the diff and the renders, and the deriver ruling every finding's scope. Lands as a pull request, never a merge.",
  whenToUse: "For the ROADMAP issue cycle on DevNull.Client, with steps as checkboxes in audits/features-workplan.md.",
  params: {
    step: { type: "string", description: "The step id in the plan, e.g. F15." },
    action: { type: "string", description: "run | window | battery | derive | land | rollback.", default: "run" },
    plan: { type: "string", description: "The feature plan file, repo-relative.", default: "audits/features-workplan.md" },
    issue: { type: "string", description: "The tracker issue number the step carries (fetches its title and body); empty = plan text only.", default: "" },
    branch: { type: "string", description: "The work branch; empty = feature/<issue or step id>.", default: "" },
    server: { type: "string", description: "The server repository (the contract source); empty = the sibling 'dev-null' folder.", default: "" },
    workerModel: { type: "string", description: "auto (deepseek-flash:xhigh) or an explicit worker model; glm-5.3@xhigh refused.", default: "auto" },
    scoutModel: { type: "string", description: "The parity scout; auto = quick tier (deepseek-flash:xhigh).", default: "auto" },
    deriveTier: { type: "string", description: "Tier of the derive pass when deriveModel is unset.", default: "standard" },
    deriveModel: { type: "string", description: "Explicit model ref for the derive pass (overrides deriveTier); empty = tier.", default: "" },
    reviewModels: { type: "string", description: "The review pair, one pass per model in parallel; BOTH must run (all-dead is an infrastructure failure, never 'clean'). Non-flash glm refs are refused: no vision.", default: "deepseek-flash:xhigh,glm-5.3-flash:high" },
    autoLand: { type: "boolean", description: "Open the pull request on approval without waiting for the user. Keep off.", default: false },
    maxRemediations: { type: "integer", description: "Worker passes after round 0, all causes (battery failures and scope-reviewed directives).", default: 3 },
    maxReviewRounds: { type: "integer", description: "Hard backstop on total worker passes before stopping for the user; the scope gate usually stops first.", default: 3 },
    maxScopeReviews: { type: "integer", description: "Scope-review cycles allowed per issue. The budget is multiplicity, never wall clock: needing one more cycle than this stops for the user's judgment.", default: 2 },
    rederive: { type: "boolean", description: "Regenerate the derivation.", default: false },
    force: { type: "boolean", description: "Run inside a peak window anyway.", default: false },
    snapshotsDir: { type: "string", description: "Where capture PNGs land (gitignored evidence).", default: "artifacts/snapshots" },
    message: { type: "string", description: "land: the commit/PR title.", default: "" },
  },
  steps: [
    { id: "scout", title: "The parity census against the server" },
    { id: "derive", title: "The sanitized contract and the specs", after: ["scout"] },
    { id: "implement", title: "One kept worker seat", after: ["derive"] },
    { id: "battery", title: "The obligated rules run, captures included", after: ["implement"] },
    { id: "review", title: "The pair reads the diff and the renders (fresh, both models)", after: ["battery"] },
    { id: "scope", title: "The deriver rules every finding's scope", after: ["review"] },
    { id: "remediate", title: "The same worker seat applies the in-scope directives", after: ["scope"] },
    { id: "land", title: "Commit and the pull request", after: ["review"] },
  ],
};

const plan = args.plan ?? meta.params.plan.default;
const today = new Date().toISOString().slice(0, 10);
const snapshotsDir = args.snapshotsDir ?? meta.params.snapshotsDir.default;
const requestedWorker = (args.workerModel ?? meta.params.workerModel.default).trim();
if (/glm-5\.3(?!-flash)/i.test(requestedWorker) && /xhigh/i.test(requestedWorker)) {
  throw new Error("glm-5.3@xhigh is refused for workers; it stays the architect's, single-flight.");
}
const workerOpts = requestedWorker === "auto" ? { tier: "quick", subSessions: false } : { model: requestedWorker, subSessions: false };
const workerName = requestedWorker === "auto" ? "auto (deepseek-flash:xhigh)" : requestedWorker;
const scoutModel = (args.scoutModel ?? meta.params.scoutModel.default).trim();
const scoutOpts = scoutModel === "auto" ? { tier: "quick", subSessions: false } : { model: scoutModel, subSessions: false };
const deriveOpts = () => (args.deriveModel ?? "")
  ? { model: args.deriveModel, subSessions: false }
  : { tier: args.deriveTier ?? meta.params.deriveTier.default, subSessions: false };

const sh = async (command, timeoutMs = 1200000) => {
  const out = await tools.shell_command({ command, timeoutMs, login: false });
  return { code: out.exitCode, text: (out.stdout ?? "") + (out.stderr ? "\n" + out.stderr : "") };
};
const ROOT = (await sh("pwd", 30000)).text.trim().split(/\r?\n/)[0];
// The contract source: the server repository beside the client.
const SERVER = (args.server ?? "").trim() || (() => {
  const parts = ROOT.split(/[\\/]/);
  parts.pop();
  parts.push("dev-null");
  return parts.join("/");
})();
const read = async (path) => (await tools.read_file({ path })).content ?? "";
const derivedPath = (id) => `.alta/derived/${id}.md`;
const verdictPath = (id) => `.alta/derived/${id}.verdict.json`;
const scopePath = (id) => `.alta/derived/${id}.scope.json`;
const censusPath = (id) => `.alta/derived/${id}.census.md`;
const prBodyPath = ".alta/derived/PR-body.md";

// Per-pass cost telemetry. Token totals come from the session record;
// kept seats report session-cumulative numbers, so their lines carry d*
// deltas since the seat's previous line. Best-effort: the loop never
// stops for a metrics failure.
let metricsQueue = Promise.resolve();
const withMetrics = (title, extra, run, sid) => {
  const started = Date.now();
  return Promise.resolve(run()).then(
    (result) => {
      const sidVal = typeof sid === "function" ? sid() : sid;
      metricsQueue = metricsQueue.then(async () => {
        try {
          let hitRef;
          let report;
          if (sidVal) {
            report = await alta.session.report({ sessionId: sidVal, include: "metrics" });
          } else {
            const list = await alta.session.list({ limit: 40 });
            const items = Array.isArray(list) ? list : (list.items ?? []);
            const hit = items.find((s) => s.title === title && Date.parse(s.createdAt ?? "") > started - 60000);
            if (!hit) return;
            hitRef = hit.modelSelection?.modelRef;
            report = await alta.session.report({ sessionId: hit.sessionId, include: "metrics" });
          }
          const it = Array.isArray(report) ? report.find((x) => x.type === "alta.session.report.item") : report;
          const po = it?.metrics?.providerOperations ?? {};
          let prior = "";
          try { prior = await read(".alta/derived/loop-metrics.jsonl"); } catch { /* first line */ }
          const separated = prior === "" || prior.endsWith("\n") ? prior : `${prior}\n`;
          const prev = sidVal ? prior.split(/\r?\n/).filter(Boolean).map((l) => { try { const j = JSON.parse(l); return j.sid === sidVal ? j : null; } catch { return null; } }).filter(Boolean).pop() : null;
          if (sidVal && !prev && !hitRef) {
            try {
              const list = await alta.session.list({ limit: 40 });
              const items = Array.isArray(list) ? list : (list.items ?? []);
              hitRef = items.find((s) => s.title === title && Date.parse(s.createdAt ?? "") > started - 60000)?.modelSelection?.modelRef;
            } catch { /* telemetry is best-effort */ }
          }
          const enrich = typeof extra === "function" ? extra(result) : (extra ?? {});
          const line = { ts: new Date().toISOString(), title, ...(sidVal ? { sid: String(sidVal).slice(0, 8) } : {}), ...enrich, model: hitRef ?? prev?.model ?? "(kept seat)", inTok: po.inputTokens ?? 0, cachedIn: po.cachedInputTokens ?? 0, outTok: po.outputTokens ?? 0, reasonTok: po.reasoningTokens ?? 0, dIn: (po.inputTokens ?? 0) - (prev?.inTok ?? 0), dOut: (po.outputTokens ?? 0) - (prev?.outTok ?? 0), dReason: (po.reasoningTokens ?? 0) - (prev?.reasonTok ?? 0), durMs: it?.metrics?.durationMs ?? 0, durMin: Math.round((it?.metrics?.durationMs ?? 0) / 6000) / 10, dMin: Math.round(((it?.metrics?.durationMs ?? 0) - (prev?.durMs ?? 0)) / 6000) / 10 };
          await tools.write_file({ path: ".alta/derived/loop-metrics.jsonl", content: separated + JSON.stringify(line) + "\n" });
        } catch { /* telemetry is best-effort */ }
      });
      return result;
    },
    (err) => { throw err; }
  );
};

const stepText = (planText, id) => {
  const open = new RegExp(`- \\[ \\] \\*\\*${id} —([^*]*)\\*\\*(.*?)(?=\\n- \\[ |\\n## |$)`, "s").exec(planText);
  return open ? { title: open[1].trim(), body: open[2].trim() } : null;
};
const fetchIssue = async (number) => {
  if (!number) return "";
  try {
    const out = await sh(`gh issue view ${number} --json number,title,body`, 60000);
    const parsed = JSON.parse(out.text.trim().split(/\r?\n/).filter(Boolean).pop() ?? "{}");
    return `#${parsed.number ?? number} — ${parsed.title ?? ""}\n\n${parsed.body ?? ""}`.trim();
  } catch { return `(the tracker issue could not be fetched; the plan text below is the whole contract)`; }
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
    .replace(/^(approved|directives|pending|rework|[fF]\d+[a-z]?)\b[-—:.\s]*/i, "")
    .replace(/\([fF]\d+[a-z]?\)/gi, "")
    .replace(/^[fF]\d+[a-z]?\s*[-—:.]\s*/i, "")
    .trim();
  const firstClause = stripped.split(/[.;—]|\bdone\b/i)[0].trim();
  return (firstClause.length > 12 ? firstClause : stripped.slice(0, 80)).slice(0, 100);
};

// The off-peak contract for BOTH providers: weekdays 01:00-04:00 and
// 06:00-10:00 UTC are peak (the GLM window nests inside deepseek's).
const PEAK_WINDOWS = [[1, 4], [6, 10]];
const windowStatus = () => {
  const now = new Date();
  const utc = new Date(now.getTime() + now.getTimezoneOffset() * 0); // display only
  const d = new Date(now.toISOString());
  const hour = d.getUTCHours();
  const day = d.getUTCDay();
  const weekday = day >= 1 && day <= 5;
  const inPeak = weekday && PEAK_WINDOWS.some(([a, b]) => hour >= a && hour < b);
  let nextChange = "";
  for (let h = 1; h <= 24; h++) {
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

const IDIOM = `Uninterrupted fluent chains (no create-then-mutate-then-return, no |> ignore chain interrupts); implicit comprehension items, yield! only for collection splices; Option combinators over re-wrap matches; no direct option comparisons (compare inner values); at most 3 tupled parameters, a contextual record beyond; no private in files with an .fsi; comments belong in the .fsi; every new F# file pairs with its own .fsi (tests and Program entry points except); structured log fields stay separate and every existing debug/success log stays; no speculative slots. RECURSION: recursive async is async/asyncEx, never a task CE; genuinely tail-recursive functions carry [<TailCall>]. MATCHES ARE HONEST OR THEY ARE WRONG: a match whose arms are '_ when' predicates is an if/elif chain in disguise — replace it with data or a parsed value; a match over a tuple with discard-heavy arms is validation — parse the inputs into one small DU first and match that value discretely. No comment in code may reference the plan, steps, phases, agents, or any conversation that produced the code.`;

const FEATURE_RULES = `The server is called "the server" or "the site" in every line of code, comments, tests, the changelog, and the PR: never a brand, a domain, or a repository name. The wire contract arrives sanitized in the derivation — the server repository is out of bounds, and a missing constant is a stop condition, not a reason to browse. Every new wire input gets a parse leaf into a domain type with a private constructor; new failure leaves compose into the app's edge type. New bindings ride the page's own model or the Sub engine only; no store observable is bound in a view; themed brushes are style classes, never observables. New Api modules follow the request-record and signed-call pattern of the existing ones. The changelog gains one bullet under Unreleased -> Added with a bold area prefix, user-facing wording, one bullet per user-facing concern.`;

const SCOUT_BRIEF = (id, issueText) => `You are the PARITY SCOUT for feature step ${id} of the DevNull.Client refactor (client repo ${ROOT}; server repo ${SERVER} — READ-ONLY, no edits in either).

The feature, from the tracker and the work plan:

${issueText}

Census the SERVER side first: every behavior, limit, constant, and flow this feature involves — window lengths, queue caps, character bounds, throttles, page structure, the wording a user sees. Anchor every line: server file:line. Then census the CLIENT side: what the client already carries for each item (decoder, call, view) — client file:line — and what it lacks.

Rules:
- The server is "the server" or "the site" in every line you write; never a brand, a domain, or a repository name.
- Numbers are copied exactly, never rounded or paraphrased.
- You produce evidence for the deriver, not a design: no recommendations, just the two sides of the parity ledger.

Answer as Markdown with three sections — ## Server facts (each with file:line), ## Client parity (item -> has/lacks, file:line), ## Gaps. Cap: 50 lines.`;

const DERIVE_BRIEF = (id, st, issueText, census) => `You derive the working parameters for feature step ${id} of DevNull.Client (repo root ${ROOT}). READ-ONLY: no edits, no commits. Read AGENTS.md, ${plan} (step ${id}), and the code the feature names — ground everything in file:line evidence you actually read. The server repository sits at ${SERVER}: read the primary sources YOURSELF; the parity census below is evidence to reconcile, not truth — promote each line you confirm, refute each you cannot, and add what it missed.

## The feature

${issueText}

## Step ${id} — ${st.title}

${st.body}

## The parity census (reconcile against the primary sources)

${census}

Produce, as Markdown:
1. **Worker brief additions** — concrete targets (file:line as they sit TODAY, greps that locate drifted numbers), the existing page/module this feature mirrors as the pattern to copy, boundaries it must not cross (which other issues own what), and stop conditions where the worker halts and reports instead of improvising.
2. **Behavior spec** — each user-visible behavior the feature adds, each with the test that proves it through the public surface.
3. **Visual spec** — the states worth capturing (empty, content, loading, error, narrow, as the feature has them) and what each must show; write "no visual states" only if the feature has no visual face at all.
4. **Negative space** — from the gate table: wire inputs get parse leaves; bindings only through the page model or Sub; no store observable in a view; themed brushes as classes; at most 3 tupled parameters; new Api modules follow the request-record pattern; failures compose into the edge type.
5. **Changelog bullet** — the Unreleased -> Added entry this feature ships, bold area prefix, user-facing wording.

Mandatory, every derivation:
- **Caller census.** For every symbol the step touches: grep across src/ AND tests/, NAME every caller. A caller not listed is a missed consumer.
- **Public-surface flag.** Any exported type or .fsi change is named as the structural risk with its consumer ripple.
- **Sanitization.** Every constant, wording, and flow leaves this derivation already free of server names; the worker never sees them.

Cap: 40 lines beyond these. Only what is specific to this feature and this tree.`;

const WORKER_BRIEF = (id, st, derivation, extra) => `You are the implementer for feature step ${id} of the work plan in DevNull.Client (repo root ${ROOT} — stay on the current branch). Read AGENTS.md (binding), ${plan} (step ${id} — its scope below is binding), and the skills under .agents/skills/ named in the derivation; if none, read mvuish-element-shapes, mvuish-controls, devnull-mvuish-audit.

## Step ${id} — ${st.title}

${st.body}
${derivation ? `\n## Derived contract, targets, and specs (binding)\n\n${derivation}\n` : ""}${extra ? `\n## Directives (binding, scope-reviewed)\n\n${extra}\n` : ""}
## Idiom rules

${IDIOM}

## Feature rules

${FEATURE_RULES}

## Rules

No git commits, no pushes, no gh. The server repository is out of bounds: the sanitized contract in the derivation carries every constant, and a missing one is a stop condition. If a seam fights, STOP that item and report the evidence.

## Deliver

Code; the behavior spec's tests (each user-visible behavior proven through the public surface); the visual spec's snapshot tests, each mirroring tests/DevNull.Client.UI.Tests/Snapshots.Tests.fs — Harness.captureAsync saves PNGs under ${snapshotsDir}/${id}/; the changelog bullet under Unreleased -> Added in CHANGELOG.md.

## Done when

dotnet fantomas . leaves no diff (run last); dotnet build DevNull.Client.slnx has 0 errors and no new warnings (a leftover DevNull.Client.exe from the repo bin locks the DLL — MSB3021; the battery stops it); Core (dotnet run --project tests/DevNull.Client.Core.Tests -c Debug --no-build -- --summary) and UI (dotnet run --project tests/DevNull.Client.UI.Tests -c Debug --no-build -- --sequenced --summary) green; every visual state in the spec has a capture PNG on disk. Report: what changed and why, each behavior's shape in one line, the capture list, verification tails with pass counts, deviations.`;

const REMEDIATE_BRIEF = (id, directives, round) => `Round ${round} on feature step ${id}. You implemented this step in this session — apply the directives below to the code you already know. Do not re-read the plan, AGENTS.md, or the skills; everything you already hold still binds.

## Directives (binding — battery failures by definition, review findings by the scope referee's ruling)

${directives.map((d, i) => `${i + 1}. ${d}`).join("\n")}

Same bars as round 0: no commits, no pushes; a fix that restores observable behavior gets its failing-first evidence; a visual directive re-captures the state it touches. If a directive fights the seam, stop that item and report why — do not improvise scope. Finish with dotnet fantomas . (last), build 0 errors, both suites green, fresh captures for what moved. Report only: what changed per directive, new test evidence, verification tails.`;

const REVIEW_SCHEMA = {
  type: "object", required: ["verdict", "findings", "notes"],
  properties: {
    verdict: { type: "string", enum: ["clean", "findings"] },
    findings: { type: "array", items: { type: "string" } },
    notes: { type: "string" },
  },
};

const REVIEW_BRIEF = (ref, id, derivation, changed, captures) => `You are the REVIEW PAIR seat [${ref}] for feature step ${id} of the DevNull.Client work (repo root ${ROOT}). There is no architect above you: this pass and its sibling are the whole review. Assume the diff satisfies checklists while breaking their spirit; be the harshest maintainer.

Read first: .alta/derived/rejections.md (the casebook — find each class's next instance), and the derivation below — the behavior spec, the visual spec, and the negative space are the contract.

${derivation ? `## The derivation (contract)\n\n${String(derivation).slice(0, 5000)}\n` : ""}## Your two passes

1. **The diff.** git diff over the changed files (run it yourself):
${changed.map((f) => `- ${f}`).join("\n")}

2. **The renders.** Call view_image on EACH capture below and judge it against the visual spec — layout, states, truncation, spacing, theme, and any UI string leaking process or server-brand words:
${captures.length ? captures.map((c) => `- ${c}`).join("\n") : "- (NONE PRODUCED — the spec named visual states but no capture exists: that is a finding, not a note)"}

Also weigh the feature rules: wire inputs get parse leaves; bindings only through the page model or Sub; no store observable in a view; at most 3 tupled parameters; .fsi pairing; the changelog bullet is user-facing and name-free.

Do NOT re-run the battery and do not restate known justified survivors. Answer: clean, or findings — each file-anchored or state-anchored (name the capture), concrete, one worker pass. Only what would embarrass us in a human review.`;

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

const SCOPE_BRIEF = (id, st, findings) => `Scope review for feature step ${id} of the DevNull.Client work (repo root ${ROOT}). This session derived the step; that derivation is the scope authority. If this session does not hold it, read .alta/derived/${id}.md first, then rule. READ-ONLY: no edits, no commits.

The review pair returned these findings on the step's diff:

${findings.map((f, i) => `${i + 1}. ${f}`).join("\n")}

Rule EACH finding against the step's own text (below) and your derivation — caller census, boundaries, negative space, stop conditions:
- **in-scope** — the feature's promise or its definition of done requires it. Write the directive the worker gets: concrete, file-anchored, one worker pass.
- **task** — real, but beyond this feature's promise (another issue owns it, or it opens surface no issue named). Give taskTitle/taskKind/taskSummary; it becomes a recorded work item, never a directive.
- **refuted** — wrong on the code or the rules. Give the evidence.

Binding precedents:
- The derivation's negative space wins: a finding that grows surface it excluded is out of scope even when the code it points at is real.
- A fix whose shape dwarfs its bug is itself a finding: rule the shape, not just the symptom.
- A reviewer is not right because it spoke, and the worker is not excused because it tried. Read the code each finding names before ruling.

## Step ${id} — ${st.title} (from ${plan})

${st.body}

Answer with one ruling per finding. Never expand the feature to please a reviewer; never bury a real defect to please the worker.`;

const reviewOpts = (ref) => {
  if (/deepseek/i.test(ref) && !/^deepseek:/.test(ref)) return { tier: "quick", subSessions: false };
  const glm = /^glm-5\.3-flash(?::(high|xhigh))?$/.exec(ref.trim());
  if (glm) return { model: `zai:glm-5.3-flash@${glm[1] ?? "high"}`, subSessions: false };
  return { model: ref, subSessions: false };
};
const runReview = async (id, derivation, changed, captures, label) => {
  const refs = (args.reviewModels ?? meta.params.reviewModels.default).split(",").map((r) => r.trim()).filter(Boolean);
  for (const ref of refs) {
    if (/glm-5\.3(?!-flash)/i.test(ref)) throw new Error(`Review ref '${ref}' refused: glm-5.3 has no vision and is not a review seat; the pair is deepseek + glm-5.3-flash.`);
  }
  const passes = await parallel(refs.map((ref) => () =>
    withMetrics(`${id} review [${ref}] ${label ?? ""}`.trim(), { step: id, round: label, role: "review", ref }, () =>
      agent(REVIEW_BRIEF(ref, id, derivation, changed, captures), { ...reviewOpts(ref), title: `${id} review [${ref}] ${label ?? ""}`.trim(), schema: REVIEW_SCHEMA }))
      .then((r) => ({ ref, ...r }))));
  const live = passes.filter(Boolean);
  // Both seats must run: an all-dead pair is an infrastructure failure and
  // must never read as clean.
  if (!live.length) {
    return { verdict: "findings", findings: [`[review] NO review pass could run (${refs.join(", ")}) — infrastructure, not a code finding. Do not treat this as clean; re-run before landing.`], notes: "all passes failed", passes: [], failed: refs.length };
  }
  const findings = live.flatMap((p) => p.findings.map((f) => `[${p.ref}] ${f}`));
  return { verdict: findings.length ? "findings" : "clean", findings, notes: live.map((p) => `[${p.ref}] ${p.notes}`).join(" | ") + (live.length < refs.length ? ` | WARNING: ${refs.length - live.length} pass(es) failed` : ""), passes: live.map((p) => ({ ref: p.ref, verdict: p.verdict, findings: p.findings.length })), failed: refs.length - live.length };
};

const stripAnsi = (t) => t.replace(/\x1b\[[0-9;]*[A-Za-z]/g, "");
const tailOf = (t) => stripAnsi(t).trim().split(/\r?\n/).filter(Boolean).slice(-6).join(" | ");
const parseCounts = (text) => ({
  passed: Number(/Passed:\s*(\d+)/.exec(text)?.[1] ?? NaN),
  failed: Number(/Failed:\s*(\d+)/.exec(text)?.[1] ?? NaN),
  errored: Number(/Errored:\s*(\d+)/.exec(text)?.[1] ?? 0),
});
const suiteOk = (run, counts) => run.code === 0 && (Number.isNaN(counts.failed) || (counts.failed === 0 && counts.errored === 0));

const battery = async (stepId) => {
  await sh(`Get-Process DevNull* -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '${ROOT}*' } | Stop-Process -Force`);
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
const deriveFor = async (id, st, force, seats, issueText, census) => {
  let existing = null;
  try { existing = await read(derivedPath(id)); } catch { /* none */ }
  if (existing && existing.trim() && !force) return existing;
  if (!seats.deriver) seats.deriver = await session({ ...deriveOpts(), title: `${id} deriver + scope referee` });
  const text = await withMetrics(`${id} derivation`, { step: id, round: 0, role: "derive" }, () => seats.deriver.run(DERIVE_BRIEF(id, st, issueText, census)), () => seats.deriver.id);
  await tools.write_file({ path: derivedPath(id), content: `# ${id} derivation — ${today}\n\n${String(text)}` });
  return String(text);
};
const runScope = async (seats, id, st, findings) => {
  const attempt = async () => {
    if (!seats.deriver) seats.deriver = await session({ ...deriveOpts(), title: `${id} deriver + scope referee` });
    return seats.deriver.run(SCOPE_BRIEF(id, st, findings), { schema: SCOPE_SCHEMA });
  };
  try {
    return await attempt();
  } catch {
    seats.deriver = null; // the seat died: one rebuild; the brief tells a fresh deriver to read the derivation file
    return await attempt();
  }
};

// The landing is a pull request: commit on the issue branch, push the
// branch, open the PR against main. Never a merge, never a push of main.
const landStep = async (id, title, summary) => {
  const planText = await read(plan);
  const finalTitle = title || `feat: ${id.toLowerCase()}`;
  const ticked = planText
    .replace(`- [ ] **${id} `, `- [x] **${id} `)
    .replace(new RegExp(`\\| ${id} \\| pending \\|([^|]*)\\|`), `| ${id} | pr ${today} | ${finalTitle.replace(/\|/g, "/")} |`);
  await tools.write_file({ path: plan, content: ticked });
  await sh("dotnet fantomas .");
  const safeTitle = finalTitle.replace(/"/g, "'");
  const commit = await sh(`git add -A; git commit -m "${safeTitle}"`);
  const push = await sh(`git push -u origin HEAD`);
  const body = `## What\n\n${summary || finalTitle}\n\n## Verification\n\n- fantomas clean, build 0 errors\n- Core and UI suites green (sequenced)\n- review pair clean over the diff and the rendered captures\n\n_This pull request was opened by CodeAlta, an AI coding agent, at the user's direction._\n`;
  await tools.write_file({ path: prBodyPath, content: body });
  const pr = await sh(`gh pr create --base main --title "${safeTitle}" --body-file "${prBodyPath}"`, 180000);
  return { title: finalTitle, commit: commit.text.trim().split("\n").slice(0, 2).join(" "), push: push.code === 0, pr: pr.text.trim() };
};

// ---------------- standalone actions ----------------
const action = args.action ?? meta.params.action.default;
const autoLand = args.autoLand ?? meta.params.autoLand.default;
const id = args.step;
const planText = await read(plan);
const st = stepText(planText, id) ?? { title: "(current tree)", body: "(no unchecked step text; working with the tree as it stands)" };

if (action === "window") {
  const w = windowStatus();
  const issueText = await fetchIssue(args.issue ?? "");
  const checks = [];
  checks.push(`${stepText(await read(plan), id) ? "plan: step found" : "plan: STEP NOT FOUND"}`);
  checks.push((await sh(`Test-Path "${SERVER}"`)).text.includes("True") ? "server repo: present" : `server repo: MISSING (${SERVER})`);
  checks.push((await sh("gh auth status", 60000)).code === 0 ? "gh: authenticated" : "gh: NOT AUTHENTICATED (no issue fetch, no PR)");
  const dirty = (await sh("git status --porcelain=v1")).text.trim();
  checks.push(dirty ? `tree: DIRTY (${dirty.split(/\r?\n/).length} path(s))` : "tree: clean");
  checks.push(`captures: ${snapshotsDir} (gitignored evidence)`);
  present(`## Window\n\n- now: ${w.nowUtc} (${w.weekday ? "weekday" : "weekend"})\n- status: **${w.inPeak ? "PEAK — wait" : "off-peak — clear to run"}**\n- next change: ${w.nextChange}\n\n## Readiness\n\n- ${checks.join("\n- ")}${issueText ? `\n\nIssue fetched (${issueText.split("\n")[0]}).` : ""}`);
  return { window: w, checks, inPeak: w.inPeak };
}

if (action === "battery") {
  const results = await step("battery", () => battery(id ?? null));
  const count = (c) => (Number.isNaN(c.passed) ? `exit ${c.exit} (unparsed)` : `${c.passed}/${c.failed}`);
  present(`## ${id ?? "tree"} battery: ${results.pass ? "ALL GREEN" : results.hard ? `GREEN with ${results.hitsOnAdded.length} flag(s)` : "FAILED"}\n- fantomas ${results.fantomas}; build ${results.buildErrors === 0 ? 0 : results.buildErrors} errors\n- Core ${count(results.core)}; UI ${count(results.ui)}${results.uiRetry ? " (retried)" : ""}\n- gates: ${results.gateHits} pre-existing, ${results.hitsOnAdded.length} on added lines\n- captures: ${results.captures.length}${results.captures.length ? " — " + results.captures.map((c) => c.split(/[\\/]/).pop()).join(", ") : ""}`);
  return { battery: results };
}

if (action === "derive") {
  const seats = { deriver: null, worker: null };
  try {
    const issueText = await fetchIssue(args.issue ?? "");
    let census = "";
    try { census = await read(censusPath(id)); } catch { /* no census yet */ }
    const derivation = await deriveFor(id, st, args.rederive ?? meta.params.rederive.default, seats, issueText, census);
    present(`## ${id} derivation\n\n${derivation}`);
    return { step: id, derivation };
  } finally { await closeSeats(seats); }
}

if (action === "land") {
  const wrap = (msg) => (msg && /^(feat|fix|refactor|docs|test|chore):/i.test(msg) ? msg : msg ? `feat: ${msg}` : `feat: ${id.toLowerCase()}`);
  let note = args.message ?? "";
  if (!note) { try { note = JSON.parse(await read(verdictPath(id))).ledgerNote ?? ""; } catch { /* none */ } }
  const out = await step("land", () => landStep(id, wrap(cleanNote(note)), note));
  present(`## ${id} landed as a pull request\n\n\`\`\`\n${String(out.pr).split("\n").slice(0, 2).join("\n")}\n\`\`\``);
  return out;
}

if (action === "rollback") {
  const head = (await sh("git log --oneline -1")).text.trim();
  const steps = [];
  steps.push((await sh("git reset --soft HEAD~1")).text.trim() || "branch soft-reset: changes staged for review");
  steps.push((await sh(`git push origin --delete HEAD 2>$null`)).code === 0 ? "remote branch deleted" : "remote branch left in place");
  const fresh = await read(plan);
  await tools.write_file({ path: plan, content: fresh.replace(`- [x] **${id} `, `- [ ] **${id} `).replace(new RegExp(`\\| ${id} \\| pr [^|]*\\|[^|]*\\|`), `| ${id} | pending | (rolled back for user review) |`) });
  present(`## ${id} rolled back\n\n- ${steps.join("\n- ")}\n- plan unticked. Changes staged on the current branch for your review.`);
  return { undone: head, steps };
}

// ---------------- the one-issue run ----------------
if (action !== "run") throw new Error(`Unknown action '${action}'.`);
const w = guardWindow();
const maxRemediations = args.maxRemediations ?? meta.params.maxRemediations.default;
const budget = Math.min(maxRemediations, args.maxReviewRounds ?? meta.params.maxReviewRounds.default);
const maxScopeReviews = args.maxScopeReviews ?? meta.params.maxScopeReviews.default;
const isTrivial = (ds) => ds.length > 0 && ds.length <= 3 && ds.every((d) => d.length < 220) && ds.every((d) => /\.(fs|fsi|md)/.test(d));
const issueNumber = (args.issue ?? "").trim();
const branch = (args.branch ?? "").trim() || `feature/${issueNumber || id.toLowerCase()}`;
const branchExists = (await sh(`git rev-parse --verify ${branch}`)).code === 0;
const branchStep = branchExists
  ? `git switch ${branch}`
  : `git switch -c ${branch}`;
await sh(branchStep);
const stRun = stepText(await read(plan), id);
if (!stRun) throw new Error(`Step ${id} not found unchecked in ${plan}.`);
const issueText = await fetchIssue(issueNumber);

const seats = { deriver: null, worker: null };
const stopped = [];
try {
  const census = await step("scout", () => withMetrics(`${id} scout`, { step: id, round: 0, role: "scout" }, () => agent(SCOUT_BRIEF(id, issueText || `${stRun.title}\n\n${stRun.body}`), { ...scoutOpts, title: `${id} parity scout`, subSessions: false })));
  await tools.write_file({ path: censusPath(id), content: `# ${id} parity census — ${today}\n\n${String(census)}` });

  const derivation = await deriveFor(id, stRun, args.rederive ?? false, seats, issueText, String(census));
  present(`## ${id} — ${stRun.title}\n\nBranch ${branch}. Census done, derivation done. One kept worker seat, then the battery, then the review pair (diff + renders), then the deriver rules scope; ${maxScopeReviews} scope cycle(s) allowed — needing one more stops. autoLand ${autoLand() ? "ON" : "OFF"}; the landing is a pull request.`);

  seats.worker = await session({ ...workerOpts, title: `${id} worker` });
  const sendWorker = async (round, directives) => {
    try {
      return await seats.worker.run(directives === null ? WORKER_BRIEF(id, stRun, derivation, null) : REMEDIATE_BRIEF(id, directives, round));
    } catch {
      seats.worker = await session({ ...workerOpts, title: `${id} worker (rebuilt)` });
      return await seats.worker.run(WORKER_BRIEF(id, stRun, derivation, directives === null ? null : directives.map((d, i) => `${i + 1}. ${d}`).join("\n")));
    }
  };

  let workerReport = await step("implement", () => withMetrics(`${id} worker`, (r) => ({ step: id, round: 0, role: "worker", reportChars: String(r).length }), () => sendWorker(0, null), () => seats.worker.id));
  let results = await step("battery", () => battery(id));
  let review = await step("review", () => runReview(id, derivation, results.changed, results.captures));
  await tools.write_file({ path: verdictPath(id), content: JSON.stringify({ verdict: review.verdict, directives: review.findings, ledgerNote: `${stRun.title} (${issueNumber ? "#" + issueNumber : id})` }, null, 2) });

  let rounds = 0;
  let scopeReviews = 0;
  let previousFindings = "";
  let halted = null;
  let prResult = null;

  while ((!results.hard || review?.verdict === "findings") && rounds < budget && !halted) {
    const findings = review?.findings ?? [];
    const findingsKey = findings.join("||");
    if (findingsKey && findingsKey === previousFindings) {
      halted = { why: "no progress: the same findings returned after a scope-reviewed pass", findings };
      break;
    }
    if (!results.hard) {
      rounds++;
      previousFindings = findingsKey;
      present(`### ${id} round ${rounds}: the battery failed — same worker seat, mechanical fix`);
      workerReport = await step("remediate", () => withMetrics(`${id} worker r${rounds}`, (r) => ({ step: id, round: rounds, role: "worker", reportChars: String(r).length }), () => sendWorker(rounds, [`The battery failed: ${JSON.stringify({ build: results.buildErrors, core: results.core, ui: results.ui, hitsOnAdded: results.hitsOnAdded })}. Fix what fails.`]), () => seats.worker.id));
      results = await step("battery", () => battery(id));
      review = await step("review", () => runReview(id, derivation, results.changed, results.captures, `r${rounds}`));
      continue;
    }
    if (!findings.length) break;

    if (scopeReviews + 1 > maxScopeReviews) {
      halted = { why: `scope budget spent: review cycle ${scopeReviews + 1} would be needed, ${maxScopeReviews} allowed — your judgment is next`, findings };
      break;
    }
    scopeReviews++;
    const ruling = await step("scope", () => withMetrics(`${id} scope r${scopeReviews}`, (r) => ({ step: id, round: scopeReviews, role: "scope", findings: findings.length, rulings: r?.rulings?.length ?? 0 }), () => runScope(seats, id, stRun, findings), () => seats.deriver?.id));
    await saveScope(id, scopeReviews, findings, ruling);
    for (const t of (ruling.rulings ?? []).filter((r) => r.ruling === "task" && r.taskTitle)) {
      await alta.task.create({ title: t.taskTitle, kind: t.taskKind ?? "improvement", summary: t.taskSummary ?? t.reason, content: `## Why\n\nFound while reviewing feature step ${id}: ${t.finding}\n\nThe scope referee ruled it real, but beyond this feature's promise.\n\n## What to do\n\n${t.reason}\n\n## Where\n\nA review finding on the ${id} diff.` });
    }
    const directives = (ruling.rulings ?? []).filter((r) => r.ruling === "in-scope" && r.directive).map((r) => r.directive);
    const refuted = (ruling.rulings ?? []).filter((r) => r.ruling === "refuted");
    present(`### ${id} scope review ${scopeReviews} — ${directives.length} in-scope · ${(ruling.rulings ?? []).filter((r) => r.ruling === "task").length} task(s) recorded · ${refuted.length} refuted`);
    previousFindings = findingsKey;

    if (!directives.length) {
      review = { ...review, verdict: "clean", findings: [], notes: `${review.notes} | the scope referee ruled every finding out of scope or refuted (rulings on record); the user's gate reviews them` };
      break;
    }
    rounds++;
    if (isTrivial(directives)) {
      present(`### ${id} round ${rounds}: ${directives.length} trivial directive(s) — fast path, same seat`);
      await step("remediate", () => withMetrics(`${id} worker r${rounds} (fast)`, (r) => ({ step: id, round: rounds, role: "worker", fastPath: true, reportChars: String(r).length }), () => sendWorker(rounds, directives), () => seats.worker.id));
      results = await step("battery", () => battery(id));
      if (!results.hard) { halted = { why: "battery failed after the trivial fast path" }; break; }
      present(`### ${id} ready for review (fast path) — battery green`);
      break;
    }
    present(`### ${id} round ${rounds}: ${directives.length} scope-reviewed directive(s) to the worker seat`);
    workerReport = await step("remediate", () => withMetrics(`${id} worker r${rounds}`, (r) => ({ step: id, round: rounds, role: "worker", reportChars: String(r).length }), () => sendWorker(rounds, directives), () => seats.worker.id));
    results = await step("battery", () => battery(id));
    review = await step("review", () => runReview(id, derivation, results.changed, results.captures, `r${rounds}`));
  }

  if (halted) {
    stopped.push({ step: id, ...halted });
    present(`### ${id} STOPPED\n\n${halted.why}. Nothing landed; the branch keeps the work.`);
  } else if (results.hard && review?.verdict === "clean") {
    present(`### ${id} PASSED — battery green, review pair clean\n\nCaptures: ${results.captures.length ? results.captures.map((c) => c.split(/[\\/]/).pop()).join(", ") : "none"}\n\n${autoLand() ? "Opening the pull request…" : "**Stopped for your review; run action 'land' to open the pull request.**"}${review.notes ? `\n\nReview notes: ${review.notes}` : ""}`);
    if (autoLand()) {
      prResult = await step("land", () => landStep(id, `feat: ${cleanNote(stRun.title)}${issueNumber ? ` (#${issueNumber})` : ""}`, String(workerReport).slice(0, 1500)));
      present(`### ${id} pull request opened\n\n${String(prResult.pr).split("\n")[0]}`);
    }
  } else if (!stopped.length) {
    stopped.push({ step: id, why: !results.hard ? "battery failed after remediation" : `pass budget (${budget} worker passes) spent — your judgment is next`, findings: review?.findings ?? [] });
    present(`### ${id} STOPPED\n\nNothing landed; the branch keeps the work.`);
  }
  return { pr: prResult, stopped, branch, plan, window: w, note: "Nothing was merged; the pull request (if any) waits for review." };
} finally {
  await closeSeats(seats);
}
