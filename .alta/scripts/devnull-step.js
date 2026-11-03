// The DevNull dev loop: derive -> worker -> battery -> architect -> red team
// -> bounded remediation -> (branch|main) landing. See .alta/derived/rejections.md
// for the review criteria's history. No hard paths: everything is repo-relative.
export const meta = {
  name: "devnull-step",
  title: "DevNull dev-loop step",
  description: "Implements and verifies one P-step of a phase work plan: derivation, one deepseek-first worker, the mechanical battery, an architect-evaluator with round memory, and a mixed-model red team. Stops for the user's review unless autoLand.",
  whenToUse: "For the DevNull.Client phased refactor loop, on a work branch, with steps as checkboxes in a plan file under audits/.",
  params: {
    step: { type: "string", description: "The step id in the plan, e.g. P20." },
    action: { type: "string", description: "run | derive | battery | architect | land | rollback.", default: "run" },
    count: { type: "integer", description: "Plan steps to run in sequence (only chains when autoLand).", default: 1 },
    plan: { type: "string", description: "The plan file, repo-relative.", default: "audits/phase1b-workplan.md" },
    branch: { type: "string", description: "The work branch.", default: "phase1b/mvuish" },
    workerModel: { type: "string", description: "auto (deepseek-flash:xhigh) or an explicit worker model; glm-5.3@xhigh refused.", default: "auto" },
    deriveTier: { type: "string", description: "Tier of the derive pass when deriveModel is unset.", default: "standard" },
    deriveModel: { type: "string", description: "Explicit model ref for the derive pass (overrides deriveTier); empty = tier.", default: "" },
    architectTier: { type: "string", description: "Tier of the architect ladder's first round (later rounds de-escalate to standard) when architectModel is unset.", default: "complex" },
    architectModel: { type: "string", description: "Explicit model ref for ALL architect rounds (overrides the ladder); empty = tier ladder.", default: "" },
    autoLand: { type: "boolean", description: "Land approved steps without the user's review. Keep off.", default: false },
    landTarget: { type: "string", description: "main (ff-merge) or branch (commit only).", default: "main" },
    redTeam: { type: "boolean", description: "Adversarial pass over every approval.", default: true },
    redTeamModels: { type: "string", description: "One pass per model, in parallel; findings merge.", default: "deepseek-flash:xhigh,glm-5.3-flash:high" },
    maxRemediations: { type: "integer", description: "Worker-remediation rounds.", default: 2 },
    maxReviewRounds: { type: "integer", description: "Total review cycles before stopping for the user.", default: 3 },
    rederive: { type: "boolean", description: "Regenerate the derivation.", default: false },
    message: { type: "string", description: "land: the commit message.", default: "" },
  },
  steps: [
    { id: "prepare", title: "Read the plan and the tree" },
    { id: "implement", title: "One worker, deepseek first", after: ["prepare"] },
    { id: "battery", title: "The obligated rules run", after: ["implement"] },
    { id: "architect", title: "The architect directs", after: ["battery"] },
    { id: "redteam", title: "The red team hunts", after: ["architect"] },
    { id: "remediate", title: "Worker applies the directives", after: ["redteam"] },
    { id: "land", title: "Ledger tick and commit", after: ["architect"] },
  ],
};

const plan = args.plan ?? meta.params.plan.default;
const branch = args.branch ?? meta.params.branch.default;
const today = new Date().toISOString().slice(0, 10);
const autoLand = args.autoLand ?? meta.params.autoLand.default;
const requestedWorker = (args.workerModel ?? meta.params.workerModel.default).trim();
if (/glm-5\.3(?!-flash)/i.test(requestedWorker) && /xhigh/i.test(requestedWorker)) {
  throw new Error("glm-5.3@xhigh is refused for workers; it stays the architect's, single-flight.");
}
const workerOpts = requestedWorker === "auto" ? { tier: "quick", subSessions: false } : { model: requestedWorker, subSessions: false };
const workerName = requestedWorker === "auto" ? "auto (deepseek-flash:xhigh)" : requestedWorker;

const sh = async (command, timeoutMs = 1200000) => {
  const out = await tools.shell_command({ command, timeoutMs, login: false });
  return { code: out.exitCode, text: (out.stdout ?? "") + (out.stderr ? "\n" + out.stderr : "") };
};
const ROOT = (await sh("pwd", 30000)).text.trim().split(/\r?\n/)[0];
const read = async (path) => (await tools.read_file({ path })).content ?? "";
const derivedPath = (id) => `.alta/derived/${id}.md`;
const verdictPath = (id) => `.alta/derived/${id}.verdict.json`;

// Per-pass cost telemetry. Token totals come from the session record the
// child session leaves behind; appends are queued so parallel red-team
// passes cannot drop each other's line. Best-effort: the loop never stops
// for a metrics failure.
let metricsQueue = Promise.resolve();
const withMetrics = (title, extra, run) => {
  const started = Date.now();
  return Promise.resolve(run()).then(
    (result) => {
      metricsQueue = metricsQueue.then(async () => {
        try {
          const list = await alta.session.list({ limit: 40 });
          const items = Array.isArray(list) ? list : (list.items ?? []);
          const hit = items.find((s) => s.title === title && Date.parse(s.createdAt ?? "") > started - 60000);
          if (!hit) return;
          const rep = await alta.session.report({ sessionId: hit.sessionId, include: "metrics" });
          const it = Array.isArray(rep) ? rep.find((x) => x.type === "alta.session.report.item") : rep;
          const po = it?.metrics?.providerOperations ?? {};
          let prior = "";
          try { prior = await read(".alta/derived/loop-metrics.jsonl"); } catch { /* first line */ }
          const enrich = typeof extra === "function" ? extra(result) : (extra ?? {});
          const line = { ts: new Date().toISOString(), title, ...enrich, model: hit.modelSelection?.modelRef, inTok: po.inputTokens ?? 0, cachedIn: po.cachedInputTokens ?? 0, outTok: po.outputTokens ?? 0, reasonTok: po.reasoningTokens ?? 0, durMin: Math.round((it?.metrics?.durationMs ?? 0) / 6000) / 10 };
          await tools.write_file({ path: ".alta/derived/loop-metrics.jsonl", content: prior + JSON.stringify(line) + "\n" });
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
const nextUnchecked = (planText, afterId) => {
  const ids = [...planText.matchAll(/- \[ \] \*\*(P\d+[a-z]?) —/g)].map((m) => m[1]);
  if (!afterId) return ids[0] ?? null;
  const i = ids.indexOf(afterId);
  return i >= 0 ? (ids[i + 1] ?? null) : ids[0] ?? null;
};
const priorRounds = async (id) => {
  try {
    const v = JSON.parse(await read(verdictPath(id)));
    return `A prior review round ended with: ${JSON.stringify({ verdict: v.verdict, directives: v.directives, unverified: v.unverified })}. A directive contradicting a refuted finding WITHOUT new evidence is an error.`;
  } catch { return "(first review round on this step)"; }
};

const IDIOM = `Uninterrupted fluent chains (no create-then-mutate-then-return, no |> ignore chain interrupts); implicit comprehension items, yield! only for collection splices; Option combinators over re-wrap matches; no direct option comparisons (compare inner values); at most 3 tupled parameters, a contextual record beyond; no private in files with an .fsi; comments belong in the .fsi; structured log fields stay separate and every existing debug/success log stays (moving code never deletes an observation); decompose nested controls into named one-concern elements; composition over god objects; no speculative slots. RECURSION: recursive async is async/asyncEx, never a task CE (task CEs cannot evaluate recursively); genuinely tail-recursive functions carry [<TailCall>] so the compiler checks them, and a recursion that cannot be tail stays un-annotated with its bound stated. MATCHES ARE HONEST OR THEY ARE WRONG: a match whose arms are '_ when' predicates is an if/elif chain in disguise — replace it with data (a lookup table whose order is the precedence) or a parsed value; a match over a tuple with discard-heavy arms is validation — parse the inputs into one small DU first (see .agents/skills/parse-dont-validate) and match that value discretely. No comment in code may reference the plan, steps, phases, agents, or any conversation that produced the code.`;

const DERIVE_BRIEF = (id, st) => `You derive the working parameters for one step of the DevNull.Client refactor (repo root ${ROOT}, branch ${branch}). READ-ONLY: no edits, no commits. Read AGENTS.md, ${plan} (step ${id}), and the code the step names — ground everything in file:line evidence you actually read.

## Step ${id} — ${st.title}

${st.body}

Produce, as Markdown:
1. **Worker brief additions** — concrete targets (file:line as they sit TODAY, greps that locate drifted numbers), boundaries it must not cross (which later plan steps own what), stop conditions where the worker halts and reports instead of improvising.
2. **Review parameters** — what the architect must verify FOR THIS STEP beyond the standing rules: likely worker claims needing code-level verification, the structural question the step exists to answer, fail conditions that reject even with green suites.
3. **Regression expectations** — for bug steps: the observable behavior each fix restores and a fail-before/pass-after test through the public surface.

Mandatory, every derivation:
- **Caller census.** For every symbol the step touches: grep across src/ AND tests/, NAME every caller. A caller not listed is a missed consumer.
- **Negative-space parameters.** What must NOT appear when done: new ":> Control" upcasts, new observables or subscriptions, new "let mutable", files outside scope, record fields no consumer reads.
- **Public-surface flag.** Any exported type or .fsi change is named as the structural risk with its consumer ripple.
- **Destructive-claim parity.** For steps that move or delete code wholesale (comment migration, dead-surface removal): state the exact count reconciliation the architect must check — e.g. doc-comment line totals before vs after must account as MOVED, not deleted — and the "git diff --stat" shape the diff should show.
Cap: 40 lines beyond these. Only what is specific to this step and this tree.`;

const WORKER_BRIEF = (id, st, derivation, extra) => `You are the implementer for step ${id} of the work plan in DevNull.Client (repo root ${ROOT}, branch ${branch} — stay on it). Read AGENTS.md (binding), ${plan} (step ${id} — its scope below is binding), and the skills under .agents/skills/ the step names; if none, read mvuish-element-shapes, mvuish-controls, devnull-mvuish-audit.

## Step ${id} — ${st.title}

${st.body}
${derivation ? `\n## Derived targets, boundaries, and stop conditions (binding)\n\n${derivation}\n` : ""}${extra ? `\n## Directives from the architect (binding)\n\n${extra}\n` : ""}
## Idiom rules

${IDIOM}

## Rules

No git commits, no pushes, no gh. USE THE EDIT TOOLS for every file change — no shell text replacement. If a seam fights, STOP that item and report the evidence. Bug fixes follow the testing rule: a regression test that fails before and passes after.

## Done when

dotnet fantomas . leaves no diff (run last); dotnet build DevNull.Client.slnx has 0 errors and no new warnings (a leftover DevNull.Client.exe from the repo bin locks the DLL — MSB3021; the battery stops it); Core (dotnet run --project tests/DevNull.Client.Core.Tests -c Debug --no-build -- --summary) and UI (dotnet run --project tests/DevNull.Client.UI.Tests -c Debug --no-build -- --sequenced --summary) green — composer keys-walk is a known flake, rerun once; pwsh audits/gates.ps1 <changed .fs/.fsi> zero unexplained hits. Report: what changed and why, each fix's shape in one line, fail-before/pass-after evidence per regression test, verification tails with pass counts, deviations.`;

const ARCHITECT_BRIEF = (id, st, derivation, workerReport, battery, changed, prior) => `You are the ARCHITECT-EVALUATOR for step ${id} of the DevNull.Client refactor (repo root ${ROOT}, branch ${branch}). Your role is to DIRECT THE APP'S STRUCTURE, not to line-review: does this step move the codebase toward coherent Shell -> Pages -> Controls -> UI, composition over god objects, content projection over deep threading, the env pattern intact, no C#-dressed-as-F#? A clean diff that cements a wrong boundary is a REJECT; a rough diff that opens the right seam is an APPROVE with notes. Your approval does NOT land the step: the user reviews after you.

Read first: AGENTS.md, ${plan} (step ${id}), .alta/derived/rejections.md (the casebook — every entry is a class; find its next instance), audits/gates.ps1 (what the battery checked; do not re-run it).

${prior}
${derivation ? `\n## Step-specific review parameters (verify each)\n\n${derivation}\n` : ""}
## The worker's report (verify load-bearing claims against the code — the worker is fast but tunnel-visioned)
${String(workerReport).slice(0, 8000)}

## The battery's result (${battery.hard ? (battery.hitsOnAdded.length ? "GREEN with flags to classify" : "ALL GREEN") : "FAILED"}) — DO NOT RE-RUN THE SUITES: the counts below are parsed from the runs' own output tails, verbatim; exit codes back them.
- fantomas: ${battery.fantomas}; build errors: ${battery.buildErrors === 0 ? 0 : JSON.stringify(battery.buildErrors)}
- Core: ${Number.isNaN(battery.core.passed) ? "(unparsed)" : `${battery.core.passed} passed / ${battery.core.failed} failed`} [exit ${battery.core.exit}] tail: ${battery.core.tail}
- UI sequenced${battery.uiRetry ? " (retried once)" : ""}: ${Number.isNaN(battery.ui.passed) ? "(unparsed)" : `${battery.ui.passed} passed / ${battery.ui.failed} failed / ${battery.ui.errored} errored`} [exit ${battery.ui.exit}] tail: ${battery.ui.tail}
- gates: ${battery.gateHits} pre-existing hits; on ADDED lines: ${battery.hitsOnAdded.length ? battery.hitsOnAdded.join(", ") : "none"} — classify each (real / justified / false positive); a real one is a directive.
- standing greps: Components-to-Views ${battery.greps.componentsToViews}; BehaviorSubject<Sub> ${battery.greps.behaviorSubjectSub}; private-in-paired: ${battery.greps.privateInPaired || "none"}

## Your obligated work — judgment first, the checklist is the floor
1. Spend most of your budget on what no rule names: read every new and heavily changed file as its harshest maintainer, ASSUMING the diff games checklists. Every construct must mean what it says. If you catch yourself verifying a rule's letter, ask whether its spirit survived.
2. READ every new/heavily changed file (below; ${changed.length} changed) against the idiom rules — suites and greps have never caught what this read catches.
3. Verify the worker's claims against the code.
4. Judge the STRUCTURE: right layer, right seam, right slot, scaffolding dissolved, no speculative surface, lifetime ownership honest.
5. Bug steps: each regression test asserts behavior through the public surface and its fail-before evidence is real.
6. Derivation audit: caller census, negative-space items, public-surface risk — spot-check one reshaped symbol yourself.
7. Destructive claims are verified against "git diff --stat" line deltas, never against the worker's word: a worker once deleted a thousand comment lines and reported the opposite. Comment counts and deletion deltas must reconcile as moves; a report that contradicts the stat is an automatic reject.

Changed files:
${changed.map((f) => `- ${f}`).join("\n")}

Answer with your verdict. Directives are concrete, file-anchored, one worker pass. The ledger note is one line, no step ids. Say what you could NOT verify rather than guessing. A verdict issued without reading the changed files in THIS pass is void: ground every ruling in code you read now, and say which files you read.`;

const ARCHITECT_SCHEMA = {
  type: "object", required: ["verdict", "directives", "ledgerNote", "unverified"],
  properties: {
    verdict: { type: "string", enum: ["approve", "directives"] },
    directives: { type: "array", items: { type: "string" } },
    ledgerNote: { type: "string" },
    unverified: { type: "array", items: { type: "string" } },
  },
};

const REDTEAM_BRIEF = (id, architect, derivation, changed) => `You are the RED TEAM for step ${id} of the DevNull.Client refactor (repo root ${ROOT}, branch ${branch}). An architect APPROVED this diff. Your only job: find what it rationalized, did not read, or was gamed on. Read the changed code as its harshest maintainer, assuming the diff satisfies checklists while breaking their spirit.

Read first: .alta/derived/rejections.md (the casebook — find each class's next instance), the diff (git diff over the changed files), the architect's verdict.

Architect verdict: ${JSON.stringify(architect)}
Derived parameters: ${(derivation ?? "").slice(0, 4000)}

Changed files:
${changed.map((f) => `- ${f}`).join("\n")}

Look where checklists do not: constructs that do not mean what they say; behavior the suites cannot see; comments or messages leaking process data; claims in the verdict the code does not support. Do NOT re-run the battery and do not restate known justified survivors. Answer: clean, or findings — each file-anchored, concrete, one worker pass. Only what would embarrass us in a human review.`;

const REDTEAM_SCHEMA = {
  type: "object", required: ["verdict", "findings", "notes"],
  properties: {
    verdict: { type: "string", enum: ["clean", "findings"] },
    findings: { type: "array", items: { type: "string" } },
    notes: { type: "string" },
  },
};

const redTeamOpts = (ref) => {
  // Friendly names -> what the runtime accepts: tier quick IS deepseek-flash@xhigh;
  // glm flash takes the full provider:model@effort ref.
  if (/deepseek/i.test(ref) && !/^deepseek:/.test(ref)) return { tier: "quick", subSessions: false };
  const glm = /^glm-5\.3-flash(?::(high|xhigh))?$/.exec(ref.trim());
  if (glm) return { model: `zai:glm-5.3-flash@${glm[1] ?? "high"}`, subSessions: false };
  return { model: ref, subSessions: false };
};
const runRedTeam = async (id, architect, derivation, changed, label) => {
  const refs = (args.redTeamModels ?? meta.params.redTeamModels.default).split(",").map((r) => r.trim()).filter(Boolean);
  for (const ref of refs) {
    if (/glm-5\.3(?!-flash)/i.test(ref) && /xhigh/i.test(ref)) throw new Error(`Red-team model '${ref}' refused: full glm-5.3@xhigh is the architect's, single-flight.`);
  }
  const passes = await parallel(refs.map((ref) => () =>
    withMetrics(`${id} red team [${ref}] ${label ?? ""}`.trim(), { step: id, round: label, role: "redteam", ref }, () =>
      agent(REDTEAM_BRIEF(id, architect, derivation, changed), { ...redTeamOpts(ref), title: `${id} red team [${ref}] ${label ?? ""}`.trim(), schema: REDTEAM_SCHEMA }))
      .then((r) => ({ ref, ...r }))));
  const live = passes.filter(Boolean);
  // An all-dead red team must never read as "clean": that is an infrastructure
  // failure, and approving on it would fake the plurality the user asked for.
  if (!live.length) {
    return { verdict: "findings", findings: [`[red-team] NO red-team pass could run (${refs.join(", ")}) — infrastructure, not a code finding. Do not treat this as clean; re-run the red team before landing.`], notes: "all passes failed", passes: [], failed: refs.length };
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

const battery = async () => {
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
  const grepCount = async (pattern, paths) =>
    Number((await sh(`(Get-ChildItem ${paths} -Recurse -Include *.fs,*.fsi | Select-String -Pattern '${pattern}' | Measure-Object).Count`)).text.match(/\d+/)?.[0] ?? 0);
  const greps = {
    componentsToViews: await grepCount("DevNull\\.Client\\.Views|\\bViews\\.", "src/DevNull.Client.UI/Components"),
    behaviorSubjectSub: await grepCount("BehaviorSubject<Sub", "src"),
    privateInPaired: (await sh("Get-ChildItem src -Recurse -Include *.fs | Where-Object { Test-Path ($_.FullName -replace '\\.fs$','.fsi') } | Select-String -Pattern '\\blet private\\b|\\bmember private\\b|\\btype private\\b' -CaseSensitive | ForEach-Object { \"{0}:{1}\" -f (Split-Path $_.Path -Leaf), $_.LineNumber }")).text.trim(),
  };
  const hard = /files unchanged|No files were changed|0 files needed formatting/i.test(fantomas.text) && !buildErrors && suiteOk(coreRun, core) && suiteOk(uiRun, ui);
  return { pass: hard && hitsOnAdded.length === 0, hard, fantomas: fantomas.text.trim().split("\n").pop(), buildErrors: buildErrors ? build.text.trim().split("\n").filter((l) => /error/i.test(l)).slice(0, 5) : 0, core, ui, uiRetry, changed, fsFiles, gateHits: (gateOut.match(/FINDINGS/g) ?? []).length, hitsOnAdded, greps };
};

const deriveOpts = () => (args.deriveModel ?? "")
  ? { model: args.deriveModel, subSessions: false }
  : { tier: args.deriveTier ?? meta.params.deriveTier.default, subSessions: false };
// Explicit model pins the architect for every round; unset = the tier ladder.
const archTier = (r) => (r === 0 || args.architectTier) ? (args.architectTier ?? meta.params.architectTier.default) : "standard";
const archAgentOpts = (r) => (args.architectModel ?? "")
  ? { model: args.architectModel, subSessions: false }
  : { tier: archTier(r), subSessions: false };
const deriveFor = async (id, st, force) => {
  let existing = null;
  try { existing = await read(derivedPath(id)); } catch { /* none */ }
  if (existing && existing.trim() && !force) return existing;
  const text = await withMetrics(`${id} derivation`, { step: id, round: 0, role: "derive" }, () => agent(DERIVE_BRIEF(id, st), { ...deriveOpts(), title: `${id} derivation`, subSessions: false }));
  await tools.write_file({ path: derivedPath(id), content: `# ${id} derivation — ${today}\n\n${String(text)}` });
  return String(text);
};

const cleanNote = (note) => {
  // A ledger note is review narration; a commit message is a product artifact.
  // Strip verdict/process prefixes, take the first clause, cap the length.
  const stripped = String(note)
    .replace(/^(approved|directives|pending|rework|p\d+[a-z]?)\b[-—:.\s]*/i, "")
    .replace(/\(p\d+[a-z]?\)/gi, "")
    .replace(/^p\d+[a-z]?\s*[-—:.]\s*/i, "")
    .trim();
  const firstClause = stripped.split(/[.;—]|\bdone\b/i)[0].trim();
  return (firstClause.length > 12 ? firstClause : stripped.slice(0, 80)).slice(0, 100);
};
const landStep = async (id, message) => {
  const planText = await read(plan);
  const finalMessage = message || `refactor: ${id.toLowerCase()}`;
  const ticked = planText
    .replace(`- [ ] **${id} `, `- [x] **${id} `)
    .replace(new RegExp(`\\| ${id} \\| pending \\|([^|]*)\\|`), `| ${id} | committed ${today} | ${finalMessage.replace(/\|/g, "/")} |`);
  await tools.write_file({ path: plan, content: ticked });
  await sh("dotnet fantomas .");
  const seq = (args.landTarget ?? meta.params.landTarget.default) === "branch"
    ? `git add -A; git commit -m "${finalMessage.replace(/"/g, "'")}"; git log --oneline -1; git status --porcelain=v1`
    : `git add -A; git commit -m "${finalMessage.replace(/"/g, "'")}"; git switch main; git merge --ff-only ${branch}; git switch ${branch}; git log --oneline -2; git status --porcelain=v1`;
  const out = await sh(seq);
  return { message: finalMessage, land: out.text.trim() };
};

// ---------------- standalone actions ----------------
const action = args.action ?? meta.params.action.default;
const id = args.step;

if (action === "rollback") {
  const head = (await sh("git log --oneline -1")).text.trim();
  if (!new RegExp(`\\(${id}\\)`).test(head)) throw new Error(`HEAD is not the ${id} landing (${head}).`);
  const mainHead = (await sh("git rev-parse main")).text.trim();
  const branchParent = (await sh("git rev-parse HEAD~1")).text.trim();
  const steps = [];
  steps.push((await sh("git reset --soft HEAD~1")).text.trim() || "branch soft-reset: changes staged for review");
  if (mainHead !== branchParent) steps.push((await sh(`git switch main; git reset --hard ${branchParent}; git switch ${branch}`)).text.trim() || `main rewound to ${branchParent.slice(0, 7)}`);
  const planText = await read(plan);
  await tools.write_file({ path: plan, content: planText.replace(`- [x] **${id} `, `- [ ] **${id} `).replace(new RegExp(`\\| ${id} \\| committed [^|]*\\|[^|]*\\|`), `| ${id} | pending | (rolled back for user review) |`) });
  present(`## ${id} rolled back\n\n- ${steps.join("\n- ")}\n- plan unticked. Changes staged on ${branch} for your review.`);
  return { undone: head, steps };
}

if (action === "land") {
  const wrap = (msg) => (msg && /^(feat|fix|refactor|docs|test|chore):/i.test(msg) ? msg : msg ? `refactor: ${msg}` : `refactor: ${id.toLowerCase()}`);
  let note = args.message ?? "";
  if (!note) { try { note = JSON.parse(await read(verdictPath(id))).ledgerNote ?? ""; } catch { /* none */ } }
  const out = await landStep(id, wrap(cleanNote(note)));
  present(`## ${id} landed\n\n\`\`\`\n${out.land.split("\n").slice(0, 4).join("\n")}\n\`\`\``);
  return out;
}

if (action === "battery") {
  const results = await step("battery", battery);
  const count = (c) => (Number.isNaN(c.passed) ? `exit ${c.exit} (unparsed)` : `${c.passed}/${c.failed}`);
  present(`## ${id ?? "tree"} battery: ${results.pass ? "ALL GREEN" : results.hard ? `GREEN with ${results.hitsOnAdded.length} flag(s) for the architect` : "FAILED"}\n- fantomas ${results.fantomas}; build ${results.buildErrors === 0 ? 0 : results.buildErrors} errors\n- Core ${count(results.core)}; UI ${count(results.ui)}${results.uiRetry ? " (retried)" : ""}\n- gates: ${results.gateHits} pre-existing, ${results.hitsOnAdded.length} on added lines${results.hitsOnAdded.length ? " — " + results.hitsOnAdded.join(", ") : ""}`);
  return { battery: results };
}

const planText = await read(plan);
const st = stepText(planText, id) ?? { title: "(current tree)", body: "(no unchecked step text; working with the tree as it stands)" };

if (action === "derive") {
  const derivation = await deriveFor(id, st, args.rederive ?? meta.params.rederive.default);
  present(`## ${id} derivation\n\n${derivation}`);
  return { step: id, derivation };
}

if (action === "architect") {
  const derivation = await deriveFor(id, st, false);
  const results = await step("battery", battery);
  const prior = await priorRounds(id);
  const architect = await step("architect", () => withMetrics(`${id} architect`, (r) => ({ step: id, round: 0, role: "architect", verdict: r?.verdict, findings: r?.directives?.length ?? 0 }), () => agent(ARCHITECT_BRIEF(id, st, derivation, "(no worker report: evaluating the current tree)", results, results.changed, prior), { ...archAgentOpts(0), title: `${id} architect`, subSessions: false, schema: ARCHITECT_SCHEMA })));
  await tools.write_file({ path: verdictPath(id), content: JSON.stringify(architect, null, 2) });
  let redTeam = null;
  if ((args.redTeam ?? meta.params.redTeam.default) && results.hard && architect.verdict === "approve") {
    redTeam = await step("redteam", () => runRedTeam(id, architect, derivation, results.changed));
  }
  present(`## ${id} architect\n\n- battery: ${results.hard ? (results.hitsOnAdded.length ? "GREEN with flags" : "ALL GREEN") : "FAILED"}\n- verdict: **${architect.verdict}**${architect.directives.length ? "\n- directives:\n  - " + architect.directives.join("\n  - ") : ""}${redTeam ? `\n- red team: **${redTeam.verdict}**${redTeam.findings.length ? "\n  - " + redTeam.findings.join("\n  - ") : ""}` : ""}\n- ledger: ${architect.ledgerNote}${architect.unverified.length ? "\n- unverified: " + architect.unverified.join("; ") : ""}\n\n**Nothing lands without your review.**`);
  return { step: id, battery: results, architect, redTeam };
}

// ---------------- the full loop ----------------
if (action !== "run") throw new Error(`Unknown action '${action}'.`);
const wanted = args.count ?? meta.params.count.default;
const maxRemediations = args.maxRemediations ?? meta.params.maxRemediations.default;
const budget = Math.min(maxRemediations, args.maxReviewRounds ?? meta.params.maxReviewRounds.default);
const isTrivial = (ds) => ds.length > 0 && ds.length <= 3 && ds.every((d) => d.length < 220) && ds.every((d) => /\.(fs|fsi|md)/.test(d));

let current = id;
const landed = [];
const stopped = [];

for (let n = 0; n < wanted && current; n++) {
  const pt = await read(plan);
  const st2 = stepText(pt, current);
  if (!st2) { stopped.push({ step: current, why: "not found unchecked in the plan" }); break; }
  const derivation = await deriveFor(current, st2, args.rederive ?? false);
  present(`## ${current} — ${st2.title}\n\nDerivation done. Worker (${workerName}) dispatched, then the battery, then the architect. autoLand ${autoLand ? "ON" : "OFF"}; land target ${args.landTarget ?? meta.params.landTarget.default}.`);

  let workerReport = await step("implement", () => withMetrics(`${current} worker`, (r) => ({ step: current, round: 0, role: "worker", reportChars: String(r).length }), () => agent(WORKER_BRIEF(current, st2, derivation, null), { ...workerOpts, title: `${current} worker` })));
  let results = await step("battery", battery);
  let prior = await priorRounds(current);
  let architect = await step("architect", () => withMetrics(`${current} architect`, (r) => ({ step: current, round: 0, role: "architect", verdict: r?.verdict, findings: r?.directives?.length ?? 0 }), () => agent(ARCHITECT_BRIEF(current, st2, derivation, workerReport, results, results.changed, prior), { ...archAgentOpts(0), title: `${current} architect`, subSessions: false, schema: ARCHITECT_SCHEMA })));
  await tools.write_file({ path: verdictPath(current), content: JSON.stringify(architect, null, 2) });
  let redTeam = null;
  if ((args.redTeam ?? meta.params.redTeam.default) && results.hard && architect.verdict === "approve") {
    redTeam = await step("redteam", () => runRedTeam(current, architect, derivation, results.changed));
  }
  let rounds = 0;
  let previousDirectives = "";

  while ((!results.hard || architect.verdict !== "approve" || redTeam?.verdict === "findings") && rounds < budget) {
    const currentDirectives = [...architect.directives, ...(redTeam?.findings ?? [])].join("||");
    if (currentDirectives && currentDirectives === previousDirectives) {
      stopped.push({ step: current, why: "no progress: the same findings returned twice" });
      present(`### ${current} STOPPED — no progress\n\nThe same findings returned twice; your judgment is next. Nothing landed.`);
      break;
    }
    previousDirectives = currentDirectives;
    rounds++;
    const directives = [...(results.hard ? [] : [`The battery failed: ${JSON.stringify({ build: results.buildErrors, core: results.core, ui: results.ui, hitsOnAdded: results.hitsOnAdded })}. Fix what fails.`]), ...architect.directives, ...(redTeam?.findings ?? [])];
    if (results.hard && rounds >= 1 && isTrivial(directives)) {
      present(`### ${current} round ${rounds}: ${directives.length} trivial directive(s) — fast path`);
      await step("remediate", () => withMetrics(`${current} worker r${rounds} (fast)`, (r) => ({ step: current, round: rounds, role: "worker", fastPath: true, reportChars: String(r).length }), () => agent(WORKER_BRIEF(current, st2, derivation, directives.map((d, i) => `${i + 1}. ${d}`).join("\n")), { ...workerOpts, title: `${current} worker r${rounds} (fast)` })));
      results = await step("battery", battery);
      if (!results.hard) { stopped.push({ step: current, why: "battery failed after the trivial fast path" }); break; }
      present(`### ${current} ready for review (fast path) — battery green`);
      break;
    }
    present(`### ${current} round ${rounds}: ${directives.length} directive(s) back to the worker${redTeam?.verdict === "findings" ? " (red team spoke)" : ""}`);
    workerReport = await step("remediate", () => withMetrics(`${current} worker r${rounds}`, (r) => ({ step: current, round: rounds, role: "worker", reportChars: String(r).length }), () => agent(WORKER_BRIEF(current, st2, derivation, directives.map((d, i) => `${i + 1}. ${d}`).join("\n")), { ...workerOpts, title: `${current} worker r${rounds}` })));
    results = await step("battery", battery);
    prior = await priorRounds(current);
    architect = await step("architect", () => withMetrics(`${current} architect r${rounds}`, (r) => ({ step: current, round: rounds, role: "architect", verdict: r?.verdict, findings: r?.directives?.length ?? 0 }), () => agent(ARCHITECT_BRIEF(current, st2, derivation, workerReport, results, results.changed, prior), { ...archAgentOpts(rounds), title: `${current} architect r${rounds}`, subSessions: false, schema: ARCHITECT_SCHEMA })));
    await tools.write_file({ path: verdictPath(current), content: JSON.stringify(architect, null, 2) });
    redTeam = null;
    if ((args.redTeam ?? meta.params.redTeam.default) && results.hard && architect.verdict === "approve") {
      redTeam = await step("redteam", () => runRedTeam(current, architect, derivation, results.changed, `r${rounds}`));
    }
  }

  if (results.hard && architect.verdict === "approve" && redTeam?.verdict !== "findings") {
    present(`### ${current} APPROVED — architect${redTeam ? " + red team (clean)" : ""}\n\nLedger note: ${architect.ledgerNote}\n\n${autoLand ? "Landing…" : "**Stopped for your review.**"}${architect.unverified.length ? "\n\nUnverified: " + architect.unverified.join("; ") : ""}${redTeam?.notes ? "\n\nRed team notes: " + redTeam.notes : ""}`);
    if (autoLand) {
      const landing = await step("land", () => landStep(current, `refactor: ${cleanNote(architect.ledgerNote)}`));
      landed.push({ step: current, message: landing.message, commit: landing.land.split("\n").find((l) => l.match(/^[0-9a-f]{7} /)) ?? "" });
      current = nextUnchecked(await read(plan), current);
    } else {
      break; // the user reviews every step
    }
  } else if (!stopped.some((s) => s.step === current)) {
    stopped.push({ step: current, why: !results.hard ? "battery failed after remediation" : architect.verdict !== "approve" ? "architect rejected after remediation" : `review budget (${budget} rounds) spent — your judgment is next`, directives: [...architect.directives, ...(redTeam?.findings ?? [])], unverified: architect.unverified });
    present(`### ${current} STOPPED\n${!results.hard ? "The battery still fails." : architect.verdict !== "approve" ? "The architect rejected it." : "The budget is spent."} Nothing landed.`);
    break;
  } else {
    break;
  }
}

return { landed, stopped, plan, branch, note: "Nothing was pushed; the push gate stays with the user." };
