// Per-role model evaluation for the DevNull dev loop. Runs every candidate
// model over the SAME frozen dossier for one role (architect | redteam |
// derive), captures each pass's token metrics from the session records, then
// a glm-5.3@xhigh judge scores anonymized answers on accuracy, agent
// awareness, completeness and efficiency against the repo and the known
// findings. Appends rows to .alta/derived/role-eval.jsonl and a section to
// .alta/derived/role-eval-report.md.
//
// Ground truth comes from the loop's own history: directives the architect
// recorded but that landed unfixed are REAL findings still present in the
// frozen dossier diff. Candidates are not told about them.
export const meta = {
  name: "devnull-role-eval",
  title: "DevNull role-eval: models per workflow task",
  description: "Runs the candidate pool for one loop role over one frozen dossier, then one judge pass. Objective metrics (tokens, minutes, directive counts) come from session records; the judge scores accuracy, awareness, completeness, efficiency.",
  whenToUse: "To decide which models belong in which seat of the devnull-step loop (architect, red team, derivation), or to re-run that measurement after a model or brief changes.",
  params: {
    role: { type: "string", description: "architect | redteam | derive | worker (worker edits: runs in a per-candidate git worktree).", default: "architect" },
    dossier: { type: "string", description: "P19 | P21 (landed-step review dossiers) | P22 (derive, the next unchecked step).", default: "P21" },
    models: { type: "string", description: "CSV override; default = the pool for the role.", default: "" },
    judge: { type: "string", description: "Judge model ref.", default: "zai:glm-5.3@xhigh" },
  },
  steps: [
    // NOTE: no `each: true` here on purpose: the engine's per-item recorder
    // assumes string items; this step returns {ref, answer} objects.
    { id: "candidates", title: "Every candidate over the dossier" },
    { id: "metrics", title: "Token metrics from session records" },
    { id: "judge", title: "Anonymized judge pass" },
  ],
};

const POOLS = {
  architect: ["zai:glm-5.3@xhigh", "zai:glm-5.3@high", "zai:glm-5.3-flash@high", "zai:glm-5.3-flash@xhigh", "deepseek:deepseek-flash@xhigh"],
  redteam: ["zai:glm-5.3-flash@high", "zai:glm-5.3-flash@xhigh", "deepseek:deepseek-flash@xhigh"],
  derive: ["zai:glm-5.3@high", "zai:glm-5.3-flash@high", "zai:glm-5.3-flash@xhigh", "deepseek:deepseek-flash@xhigh"],
  // The worker edits files: candidates run in their own git worktree so the
  // repo tree stays untouched. Efforts are low/high/xhigh only; glm-5.3
  // concurrency is 30 (flash 50) and this harness never exceeds 3 concurrent.
  worker: ["zai:glm-5.3-flash@high", "deepseek:deepseek-flash@xhigh"],
};

// Known findings per dossier, recovered from the loop's recorded verdicts.
// These directives landed unfixed, so they are present in the dossier diff.
const KNOWN = {
  P19: [
    "NotificationsView.fs line 4: `open System.Collections.Generic` is dead (its only user, a HashSet in toRows, was removed by this step) and must be deleted. (Also a trap: an earlier review round wrongly demanded RESTORING this open.)",
  ],
  P21: [
    "RemotesWire.fs ~line 499-500: the doc comment 'One tab's next page: a moved pin rebuilds the depth instead.' appears twice on consecutive lines above pageAfter; one copy must be deleted.",
  ],
  P22: [],
};

// Session records that hold the real artifacts each dossier needs.
const SOURCES = {
  P21: {
    workerReport: "01a1a17b-3690-7d08-9a75-0cce031d7d15", // P21 worker r2 final report
    approval: "01a1a14f-79bd-7111-9ca1-033b6df44d47", // P21 architect r0 'approve' that missed the duplicate line
    commit: "b0fd50e",
    derivation: ".alta/derived/P21.md",
    step: "P21",
  },
  P19: {
    workerReport: null,
    approval: null,
    commit: "2d3e9fd",
    derivation: ".alta/derived/P19.md",
    step: "P19",
  },
  P22: { step: "P22" },
};

const sh = async (command, timeoutMs = 120000) => {
  const out = await tools.shell_command({ command, timeoutMs, login: false });
  return { code: out.exitCode, text: (out.stdout ?? "") + (out.stderr ? "\n" + out.stderr : "") };
};
const ROOT = (await sh("pwd", 30000)).text.trim().split(/\r?\n/)[0];
const read = async (path) => (await tools.read_file({ path })).content ?? "";
const answerOf = async (sessionId) => {
  try {
    const rep = await alta.session.report({ sessionId, include: "result" });
    const it = Array.isArray(rep) ? rep.find((x) => x.type === "alta.session.report.item") : rep;
    return String(it?.finalAnswer?.text ?? "");
  } catch { return ""; }
};

const role = args.role ?? meta.params.role.default;
const dossier = args.dossier ?? meta.params.dossier.default;
const judgeRef = args.judge ?? meta.params.judge.default;
const pool = (args.models ?? "").trim()
  ? (args.models ?? "").split(",").map((s) => s.trim()).filter(Boolean)
  : POOLS[role];
if (!POOLS[role]) throw new Error(`Unknown role '${role}' (architect | redteam | derive | worker).`);
if (!KNOWN[dossier]) throw new Error(`Unknown dossier '${dossier}' (P19 | P21 | P22).`);

const planText = await read("audits/phase1b-workplan.md");
const stepBlock = (id) => {
  const m = new RegExp(`- \\[.\\] \\*\\*${id} —([^*]*)\\*\\*(.*?)(?=\\n- \\[ |\\n## |$)`, "s").exec(planText);
  return m ? { title: m[1].trim(), body: m[2].trim() } : { title: "(missing)", body: "(step text not found)" };
};

const IDIOM = `Uninterrupted fluent chains (no create-then-mutate-then-return, no |> ignore chain interrupts); implicit comprehension items, yield! only for collection splices; Option combinators over re-wrap matches; no direct option comparisons (compare inner values); at most 3 tupled parameters, a contextual record beyond; no private in files with an .fsi; comments belong in the .fsi; structured log fields stay separate and every existing debug/success log stays (moving code never deletes an observation); decompose nested controls into named one-concern elements; composition over god objects; no speculative slots. RECURSION: recursive async is async/asyncEx, never a task CE; genuinely tail-recursive functions carry [<TailCall>]; a recursion that cannot be tail stays un-annotated with its bound stated. MATCHES ARE HONEST: a match whose arms are '_ when' predicates is an if/elif chain in disguise — replace with data or a parsed value; a match over a tuple with discard-heavy arms is validation — parse into one small DU first. No comment in code may reference the plan, steps, phases, agents, or any conversation that produced the code.`;

const src = SOURCES[dossier];
if (role === "worker" && !src.step) throw new Error("The worker role needs a plan step to implement (dossier P22).");
const st = stepBlock(src.step);
const derivation = src.derivation ? await read(src.derivation) : "";
const changed = src.commit
  ? (await sh(`git show --name-only --format= ${src.commit}`)).text.split(/\r?\n/).filter((l) => l.trim())
  : [];
const workerReport = src.workerReport ? await answerOf(src.workerReport) : "(no worker report available; judge the diff as it stands)";
const approval = src.approval ? await answerOf(src.approval) : "";
const batteryBlock = src.commit
  ? `- fantomas: unchanged; build: 0 errors (warnings only in untouched test files)
- Core suite: green, 0 failed [exit 0]; UI suite (sequenced): green, 0 failed [exit 0]
- gates: pre-existing baseline hits only; every flag on added lines was classified (moved construct or pattern false positive)
(diff of landed commit ${src.commit}; suite summary reconstructed from the recorded battery of that round)`
  : "";

const ARCH_SCHEMA = {
  type: "object", required: ["verdict", "directives", "ledgerNote", "unverified"],
  properties: {
    verdict: { type: "string", enum: ["approve", "directives"] },
    directives: { type: "array", items: { type: "string" } },
    ledgerNote: { type: "string" },
    unverified: { type: "array", items: { type: "string" } },
  },
};
const RED_SCHEMA = {
  type: "object", required: ["verdict", "findings", "notes"],
  properties: {
    verdict: { type: "string", enum: ["clean", "findings"] },
    findings: { type: "array", items: { type: "string" } },
    notes: { type: "string" },
  },
};

const briefFor = {
  architect: `You are the ARCHITECT-EVALUATOR for step ${src.step} of the DevNull.Client refactor (repo root ${ROOT}). Your role is to DIRECT THE APP'S STRUCTURE, not to line-review: does this step move the codebase toward coherent Shell -> Pages -> Controls -> UI, composition over god objects, content projection over deep threading, the env pattern intact, no C#-dressed-as-F#? A clean diff that cements a wrong boundary is a REJECT; a rough diff that opens the right seam is an APPROVE with notes.

Read first: AGENTS.md, audits/phase1b-workplan.md (step ${src.step}), .alta/derived/rejections.md, audits/gates.ps1. The step's diff is \`git show ${src.commit}\`; read the changed files in the tree as they are.

(first review round on this step)
${derivation ? `\n## Step-specific review parameters (verify each)\n\n${derivation.slice(0, 4000)}\n` : ""}
## The worker's report (verify load-bearing claims against the code — the worker is fast but tunnel-visioned)
${String(workerReport).slice(0, 8000)}

## The battery's result (ALL GREEN) — DO NOT RE-RUN THE SUITES; the counts are from the runs' own output tails.
${batteryBlock}

## Your obligated work — judgment first, the checklist is the floor
1. Spend most of your budget on what no rule names: read every new and heavily changed file as its harshest maintainer, ASSUMING the diff games checklists. Every construct must mean what it says.
2. READ every changed file (below; ${changed.length} changed) against the idiom rules.
3. Verify the worker's claims against the code.
4. Judge the STRUCTURE: right layer, right seam, right slot, scaffolding dissolved, no speculative surface, lifetime ownership honest.
5. Derivation audit: caller census, negative-space items, public-surface risk — spot-check one reshaped symbol yourself.

Changed files:
${changed.map((f) => `- ${f}`).join("\n")}

Answer with your verdict. Directives are concrete, file-anchored, one worker pass. The ledger note is one line, no step ids. Say what you could NOT verify rather than guessing.`,
  redteam: `You are the RED TEAM for step ${src.step} of the DevNull.Client refactor (repo root ${ROOT}). An architect APPROVED this diff. Your only job: find what it rationalized, did not read, or was gamed on. Read the changed code as its harshest maintainer, assuming the diff satisfies checklists while breaking their spirit.

Read first: .alta/derived/rejections.md, the diff (git show ${src.commit}), the architect's verdict.

Architect verdict: ${approval || "(approved; no further detail recorded)"}

Changed files:
${changed.map((f) => `- ${f}`).join("\n")}

Look where checklists do not: constructs that do not mean what they say; behavior the suites cannot see; comments or messages leaking process data; claims in the verdict the code does not support. Do NOT re-run the battery and do not restate known justified survivors. Answer: clean, or findings — each file-anchored, concrete, one worker pass. Only what would embarrass us in a human review.`,
  derive: `You derive the working parameters for one step of the DevNull.Client refactor (repo root ${ROOT}, branch main). READ-ONLY: no edits, no commits. Read AGENTS.md, audits/phase1b-workplan.md (step ${src.step}), and the code the step names — ground everything in file:line evidence you actually read.

## Step ${src.step} — ${st.title}

${st.body}

Produce, as Markdown:
1. **Worker brief additions** — concrete targets (file:line as they sit TODAY, greps that locate drifted numbers), boundaries it must not cross (which later plan steps own what), stop conditions where the worker halts and reports instead of improvising.
2. **Review parameters** — what the architect must verify FOR THIS STEP beyond the standing rules: likely worker claims needing code-level verification, the structural question the step exists to answer, fail conditions that reject even with green suites.
3. **Regression expectations** — for bug steps: the observable behavior each fix restores and a fail-before/pass-after test through the public surface.

Mandatory, every derivation:
- **Caller census.** For every symbol the step touches: grep across src/ AND tests/, NAME every caller. A caller not listed is a missed consumer.
- **Negative-space parameters.** What must NOT appear when done: new ":> Control" upcasts, new observables or subscriptions, new "let mutable", files outside scope, record fields no consumer reads.
- **Public-surface flag.** Any exported type or .fsi change is named as the structural risk with its consumer ripple.
Cap: 40 lines beyond these. Only what is specific to this step and this tree.`,
};

const brief = briefFor[role];
const schema = role === "architect" ? ARCH_SCHEMA : role === "redteam" ? RED_SCHEMA : undefined;
const short = (ref) => String(ref ?? "unknown").replace(/[:@]/g, "-");
const titleOf = (ref) => `role-eval ${role} [${short(ref)}] ${dossier}`;

// Per-token prices the operator pays (USD per 1M). deepseek is metered;
// GLM runs on a flat $80/month token plan, so its marginal dollar cost is
// ~zero and its seats are judged on minutes, plan-token burn and catch
// quality instead. costUsd is null for flat-plan models.
const PRICES = {
  deepseek: { miss: 0.15, hit: 0.003, out: 0.6 },
  glm: null,
};
const costUsdOf = (m) => {
  const p = /deepseek/i.test(m.ref ?? "") ? PRICES.deepseek : PRICES.glm;
  if (!p || !m.inTok) return null;
  return Math.round((((m.inTok - m.cachedIn) / 1e6) * p.miss + ((m.cachedIn) / 1e6) * p.hit + ((m.outTok) / 1e6) * p.out) * 1000) / 1000;
};

const WORKER_TASK = `You are the implementer for step ${src.step} of the work plan in DevNull.Client. You are in YOUR OWN git worktree of the repo (a branch of your own): do every change there. Read AGENTS.md (binding), audits/phase1b-workplan.md (step ${src.step} — its scope below is binding), and the skills under .agents/skills/ named mvuish-element-shapes, mvuish-controls, devnull-mvuish-audit.

## Step ${src.step} — ${st.title}

${st.body}

## Idiom rules

${IDIOM}

## Rules

No git commits, no pushes, no gh. USE THE EDIT TOOLS for every file change — no shell text replacement. If a seam fights, STOP that item and report the evidence.

## Done when

dotnet fantomas . leaves no diff (run last); dotnet build DevNull.Client.slnx has 0 errors and no new warnings; Core (dotnet run --project tests/DevNull.Client.Core.Tests -c Debug -- --summary) and UI (dotnet run --project tests/DevNull.Client.UI.Tests -c Debug -- --sequenced --summary) green — composer keys-walk is a known flake, rerun once. Report: what changed and why, each fix's shape in one line, verification tails with pass counts, deviations.`;

present(`## role-eval: ${role} over ${dossier}\n\n${pool.length} candidates: ${pool.map(short).join(", ")}\nJudge: ${short(judgeRef)} (anonymized). Known findings held back: ${(KNOWN[dossier] ?? []).length}.${role === "worker" ? "\nWorker candidates edit in per-candidate git worktrees; the repo tree is untouched." : ""}`);

// ---- candidates: deepseek is ungated (the provider allows ~2.5k
// concurrent requests); every GLM model (5.3 and Flash) goes through 2
// slots. Eval slices are launched one at a time by policy. A checkpoint
// after the metrics step means an engine failure in the judge phase never
// costs the candidate passes again. ----
const PARTIAL = `.alta/derived/role-eval-partial.json`;
let resumed = null;
try {
  const p = JSON.parse(await read(PARTIAL));
  if (p.role === role && p.dossier === dossier) resumed = p;
} catch { /* none */ }
const started = Date.now();
const isGlm = (ref) => !/deepseek/i.test(ref);
let glmActive = 0;
const glmWaiters = [];
const takeGlmSlot = () => (glmActive < 2 ? (glmActive++, Promise.resolve()) : new Promise((res) => glmWaiters.push(res)));
const freeGlmSlot = () => { const next = glmWaiters.shift(); if (next) next(); else glmActive = Math.max(0, glmActive - 1); };
const runCandidate = async (ref) => {
  const opts = { model: ref, title: titleOf(ref), subSessions: false, ...(role === "worker" ? { worktree: true } : {}) };
  if (schema) opts.schema = schema;
  const briefText = role === "worker" ? WORKER_TASK : brief;
  const gated = isGlm(ref);
  if (gated) await takeGlmSlot();
  try {
    const a = await agent(briefText, opts);
    return { ref, answer: typeof a === "object" && a !== null ? JSON.stringify(a, null, 1) : String(a) };
  } catch (e) {
    return { ref, answer: `(candidate failed: ${String(e?.message ?? e).slice(0, 200)})` };
  } finally { if (gated) freeGlmSlot(); }
};
let answers, met;
if (resumed) {
  answers = resumed.answers; met = resumed.met;
} else {
  answers = await step("candidates", () => Promise.all(pool.map((ref) => runCandidate(ref))));
  met = await step("metrics", async () => {
    const list = await alta.session.list({ limit: 40 });
    const items = Array.isArray(list) ? list : (list.items ?? []);
    return Promise.all(pool.map(async (cand) => {
      const hit = items.find((s) => s.title === titleOf(cand.ref) && s.createdAt && Date.parse(s.createdAt) > started - 60000);
      if (!hit) return { ref: cand.ref, inTok: null };
      try {
        const rep = await alta.session.report({ sessionId: hit.sessionId, include: "metrics" });
        const it = Array.isArray(rep) ? rep.find((x) => x.type === "alta.session.report.item") : rep;
        const po = it?.metrics?.providerOperations ?? {};
        return { ref: cand.ref, inTok: po.inputTokens ?? 0, cachedIn: po.cachedInputTokens ?? 0, outTok: po.outputTokens ?? 0, durMin: Math.round((it?.metrics?.durationMs ?? 0) / 6000) / 10, tools: it?.metrics?.toolCallCount ?? 0, worktree: hit.workingDirectory ?? null };
      } catch { return { ref: cand.ref, inTok: null }; }
    }));
  });
  try { await tools.write_file({ path: PARTIAL, content: JSON.stringify({ role, dossier, answers, met }) }); } catch { /* checkpoint is best-effort */ }
}
const byRef = {}; for (const m of met) if (m) byRef[m.ref] = m;

// ---- anonymized judge ----
const order = pool.map((_, i) => i).sort(() => Math.random() - 0.5);
const labels = ["A", "B", "C", "D", "E"];
const knownBlock = (KNOWN[dossier] ?? []).map((k, i) => `${i + 1}. ${k}`).join("\n") || "(none on record for this dossier)";
const judged = await step("judge", () => agent(
  `You judge a model-evaluation run for the DevNull dev loop. Role under test: ${role === "architect" ? "ARCHITECT-EVALUATOR (reviews a landed refactor step)" : role === "redteam" ? "RED TEAM (adversarial pass over an approved diff)" : role === "worker" ? "WORKER (implements the next plan step; each candidate worked in its own git worktree)" : "DERIVATION (extracts worker brief additions and review parameters for the next step)"}.
Dossier: ${dossier} — ${role === "worker" || role === "derive" ? `the unchecked plan step ${dossier} (${st.title})` : `landed commit ${src.commit} (${changed.length} files)`}. ${role === "worker" ? "Each candidate's uncommitted work sits in the worktree listed under it; inspect with git -C <worktree> diff / status and by reading files. You MAY run dotnet fantomas or a build inside a worktree when a claim is decisive; otherwise read." : "Every candidate below received the SAME brief and worked in the same repo."}

## Known ground-truth findings (from the loop's recorded review history; candidates were NOT told)
${knownBlock}

Score each candidate 0-10 on:
- **accuracy**: its claims are true in the code. VERIFY the load-bearing claims by reading the repo yourself${role === "worker" ? " (for a worker: its report's battery claims vs what its worktree actually shows)" : ""}. A wrong claim scores low.
- **awareness**: no tunnel vision; it saw the file's neighborhood, callers, ripple, lifetime issues — extras beyond the obvious checklist${role === "worker" ? "; for a worker: it swept every file the step names, not just the obvious ones" : ""}.
- **completeness**: nothing load-bearing missed for this role${role === "worker" ? "; for a worker: the step's whole scope done" : ""}; for reviewers, mark in caughtKnown every ground-truth finding it actually caught.
- **efficiency**: value per unit cost. Its measured cost is given (tokens are mostly cache reads). Penalty for work a later round must redo${role === "worker" ? " or scope the worker skipped" : " or invented directives that would cost a worker round"}.

${answers.map((c, i) => {
  const j = order.indexOf(i);
  const m = byRef[c?.ref] ?? {};
  return `## Candidate ${labels[j]} — measured cost: ${(m.inTok ?? 0).toLocaleString()} input tokens (${(m.cachedIn ?? 0).toLocaleString()} cached), ${(m.outTok ?? 0).toLocaleString()} output, ${m.durMin ?? "?"} min, ${m.tools ?? "?"} tool calls${m.worktree ? `\nWorktree (uncommitted changes): ${m.worktree}` : ""}

${String(c?.answer ?? "(no answer)").slice(0, 9000)}`;
}).join("\n\n")}

Answer as JSON. ranking = labels best-to-worst FOR THIS ROLE. notes: a few lines, the decisive differences. Do not reward volume or confidence; reward being right and hard to fool.`,
  { model: judgeRef, title: `role-eval judge ${role} ${dossier}`, subSessions: false, schema: {
    type: "object", required: ["candidates", "ranking", "notes"],
    properties: {
      candidates: { type: "array", items: { type: "object", required: ["label", "accuracy", "awareness", "completeness", "efficiency", "caughtKnown", "falsePositives"], properties: {
        label: { type: "string" }, accuracy: { type: "number" }, awareness: { type: "number" }, completeness: { type: "number" }, efficiency: { type: "number" },
        caughtKnown: { type: "array", items: { type: "string" } }, falsePositives: { type: "array", items: { type: "string" } },
      } } },
      ranking: { type: "array", items: { type: "string" } },
      notes: { type: "string" },
    },
  } }));

// ---- report ----
const rows = answers.map((c, i) => {
  const label = labels[order.indexOf(i)];
  const m = byRef[c?.ref] ?? {};
  const jc = (judged.candidates ?? []).find((x) => x.label === label) ?? {};
  let nFind = 0, verdict = role === "worker" ? "report" : "(no schema)";
  try { const j = JSON.parse(c?.answer ?? ""); verdict = j.verdict; nFind = (j.directives ?? j.findings ?? []).length; } catch { verdict = "text"; }
  const effIn = (m.inTok ?? 0) - (m.cachedIn ?? 0);
  return { ts: new Date().toISOString(), role, dossier, model: c.ref, label, verdict, nFind, caughtKnown: jc.caughtKnown ?? [], falsePositives: jc.falsePositives ?? [], scores: { accuracy: jc.accuracy, awareness: jc.awareness, completeness: jc.completeness, efficiency: jc.efficiency }, inTok: m.inTok, cachedIn: m.cachedIn, outTok: m.outTok, durMin: m.durMin, uncachedIn: effIn, worktree: m.worktree ?? null, costUsd: costUsdOf(m) };
});
let jsonl = "";
try { jsonl = await read(".alta/derived/role-eval.jsonl"); } catch { /* new */ }
await tools.write_file({ path: ".alta/derived/role-eval.jsonl", content: jsonl + rows.map((r) => JSON.stringify(r)).join("\n") + "\n" });

const table = rows.map((r) => `| ${r.label} | \`${r.model}\` | ${r.verdict} (${r.nFind}) | ${(r.scores.accuracy ?? "?")}/${r.scores.awareness ?? "?"}/${r.scores.completeness ?? "?"}/${r.scores.efficiency ?? "?"} | ${(r.caughtKnown ?? []).length}/${(KNOWN[dossier] ?? []).length} | ${(r.falsePositives ?? []).length} | ${(r.inTok ?? 0).toLocaleString()} (${(r.uncachedIn ?? 0).toLocaleString()} unc) | ${r.durMin ?? "?"}m |`).join("\n");
let report = "";
try { report = await read(".alta/derived/role-eval-report.md"); } catch { /* new */ }
const section = `## ${new Date().toISOString().slice(0, 16)} — ${role} over ${dossier}

| # | model | verdict (findings) | acc/aware/comp/eff | known caught | false pos | input tokens | min |
|---|--------|--------------------|--------------------|--------------|-----------|--------------|-----|
${table}

Judge ranking: ${(judged.ranking ?? []).map((l) => `${l} = ${rows.find((r) => r.label === l)?.model ?? "?"}`).join("  >  ")}
Judge notes: ${judged.notes ?? ""}

`;
await tools.write_file({ path: ".alta/derived/role-eval-report.md", content: report + section });

present(`## role-eval ${role} / ${dossier} — done\n\n| # | model | verdict | acc/aware/comp/eff | known | FP | in-tok | min |\n|---|--------|---------|--------------------|-------|----|--------|-----|\n${table}\n\nRanking: ${(judged.ranking ?? []).join(" > ")}\nJudge: ${judged.notes ?? ""}`);
return { role, dossier, judge: judgeRef, rows, ranking: judged.ranking, judgeNotes: judged.notes };
