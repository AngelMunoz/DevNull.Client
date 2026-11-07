// The overnight runner: chains devnull-step over every unchecked plan step,
// inside the off-peak window for BOTH providers, landing to the work branch
// only (main untouched), keeping the changelog honest per AGENTS.md, and
// finishing with a pull request for the user's morning review. It never
// merges and never pushes main.
//
// Peak windows (weekdays): deepseek 01:00-04:00 and 06:00-10:00 UTC; GLM
// 14:00-18:00 UTC+8 = 06:00-10:00 UTC (inside deepseek's). Combined peaks:
// 01:00-04:00 and 06:00-10:00 UTC, Monday-Friday. Weekends: all off-peak.
export const meta = {
  name: "devnull-overnight",
  title: "DevNull overnight loop (off-peak, branch-only, PR at the end)",
  description: "Runs the dev-loop over every unchecked step while both providers are off-peak: derive, worker (its battery is the record), collect, architect, red team, bounded remediation, land to the branch, changelog pass, and a PR when steps run out or the window closes.",
  whenToUse: "When the user has authorized an unattended run ending in a PR for their review. Never for merging.",
  params: {
    plan: { type: "string", description: "The plan file, repo-relative.", default: "audits/phase1b-workplan.md" },
    branch: { type: "string", description: "The work branch; every commit lands here.", default: "phase1b/mvuish" },
    stopUtc: { type: "string", description: "Wall-clock UTC 'HH:MM' after which no new step starts and the PR opens.", default: "13:30" },
    maxSteps: { type: "integer", description: "Hard cap on steps attempted this run.", default: 12 },
    dryRun: { type: "boolean", description: "Check the window and list the steps without running anything.", default: false },
  },
  steps: [
    { id: "window", title: "Check the off-peak window" },
    { id: "run", title: "Run each unchecked step", after: ["window"] },
    { id: "changelog", title: "Changelog pass per landed step", after: ["run"] },
    { id: "pr", title: "Open the pull request", after: ["changelog"] },
  ],
};

const plan = args.plan ?? meta.params.plan.default;
const branch = args.branch ?? meta.params.branch.default;
const stopUtc = args.stopUtc ?? meta.params.stopUtc.default;
const maxSteps = args.maxSteps ?? meta.params.maxSteps.default;

const sh = async (command, timeoutMs = 1800000) => {
  const out = await tools.shell_command({ command, timeoutMs, login: false });
  return { code: out.exitCode, text: (out.stdout ?? "") + (out.stderr ? "\n" + out.stderr : "") };
};
const read = async (path) => (await tools.read_file({ path })).content ?? "";

// --- the window guard -------------------------------------------------------
const nowUtc = () => { const d = new Date(); return { h: d.getUTCHours(), m: d.getUTCMinutes(), day: d.getUTCDay(), date: d }; };
const inPeak = ({ h, day }) => day >= 1 && day <= 5 && ((h >= 1 && h < 4) || (h >= 6 && h < 10));
const minutesUtc = ({ h, m }) => h * 60 + m;
const parseHm = (s) => { const [h, m] = s.split(":").map(Number); return h * 60 + (m || 0); };

const t0 = nowUtc();
if (inPeak(t0)) {
  present(`## Overnight run refused: inside a peak window\n\nNow ${String(t0.h).padStart(2, "0")}:${String(t0.m).padStart(2, "0")} UTC (weekday). Combined peaks: 01:00-04:00 and 06:00-10:00 UTC. Re-arm for the next off-peak window.`);
  return { blocked: true, at: t0.date.toISOString() };
}

const planText = await read(plan);
const ids = [...planText.matchAll(/- \[ \] \*\*(P\d+[a-z]?) —/g)].map((m) => m[1]);
if (!ids.length) {
  present("## No unchecked steps\n\nThe plan has nothing to run. Opening the PR for what already landed.");
}

if (args.dryRun ?? meta.params.dryRun.default) {
  present(`## Dry run\n\nWindow: OFF-PEAK ok (${String(t0.h).padStart(2, "0")}:${String(t0.m).padStart(2, "0")} UTC). Steps queued: ${ids.slice(0, maxSteps).join(", ") || "(none)"}. Stop-at ${stopUtc} UTC.`);
  return { dryRun: true, offPeak: true, queued: ids.slice(0, maxSteps) };
}

// --- the run loop -----------------------------------------------------------
const landed = [];
const failed = [];
let stoppedForWindow = false;

for (const id of ids.slice(0, maxSteps)) {
  const t = nowUtc();
  if (inPeak(t)) { stoppedForWindow = true; present(`### Window closed mid-run (peak at ${String(t.h).padStart(2, "0")}:${String(t.m).padStart(2, "0")} UTC)`); break; }
  if (minutesUtc(t) >= parseHm(stopUtc)) { stoppedForWindow = true; present(`### Stop-at reached (${stopUtc} UTC)`); break; }

  present(`## ${id}: launching the step loop (off-peak, branch-only landing)`);
  const stepArgs = {
    step: id, action: "run", count: 1, plan, branch,
    workerModel: "auto", deriveTier: "quick", architectTier: "complex",
    autoLand: false, maxRemediations: 2,
    rederive: false, redTeam: true, redTeamModels: "deepseek-flash:xhigh,glm-5.3-flash:high", message: "",
  };
  const result = await step("run", () => workflow("devnull-step", stepArgs));

  // The step loop stops at approval (autoLand off); the verdict file says which.
  // The runner lands approved steps itself — branch commit only, main untouched.
  let verdict = null;
  try { verdict = JSON.parse(await read(`.alta/derived/${id}.verdict.json`)); } catch { /* none */ }

  if (verdict?.verdict === "approve") {
    // A ledger note is review narration; a commit message is a product artifact:
    // strip verdict/process prefixes, first clause only, hard length cap.
    const raw = String(verdict.ledgerNote ?? "")
      .replace(/^(approved|directives|pending|rework|p\d+[a-z]?)\b[-—:.\s]*/i, "")
      .replace(/\(p\d+[a-z]?\)/gi, "")
      .replace(/^p\d+[a-z]?\s*[-—:.]\s*/i, "")
      .trim();
    const note = (raw.split(/[.;—]|\bdone\b/i)[0].trim() || id.toLowerCase()).slice(0, 100);
    const planNow = await read(plan);
    const ticked = planNow
      .replace(`- [ ] **${id} `, `- [x] **${id} `)
      .replace(new RegExp(`\\| ${id} \\| pending \\|([^|]*)\\|`), `| ${id} | committed ${new Date().toISOString().slice(0, 10)} | refactor: ${note.replace(/\|/g, "/")} |`);
    await tools.write_file({ path: plan, content: ticked });
    await sh("dotnet fantomas .");
    const landing = await sh(`git add -A; git commit -m "refactor: ${note.replace(/"/g, "'")}"; git log --oneline -1`);
    const commit = landing.text.trim().split("\n").find((l) => /^[0-9a-f]{7} /.test(l)) ?? "";
    const message = `refactor: ${note}`;
    landed.push({ step: id, commit, message });

    // Changelog pass: one entry only when the step changed something a user
    // can feel (AGENTS rules; internal refactors carry no bullet).
    const commitStat = commit ? (await sh(`git show --stat ${commit.split(" ")[0]}`)).text : "(commit id unavailable)";
    const change = await step("changelog", () => agent(
      `Read AGENTS.md's Changelog Management section (format, categories, writing style — the changelog is for developers upgrading, no internal paths, bold area prefix). Then read the landed commit:\n\n${commitStat}\n\nCommit message: ${message}\n\nQuestion: does this change ANYTHING a user of the app can observe? If yes, write ONE bullet for CHANGELOG.md under [Unreleased] in the right category (Added/Changed/Fixed/Deprecated/Removed/Security), following every AGENTS rule. If it is purely internal (refactor, tests, structure), the entry is null — KeepAChangelog documents notable changes, not every commit.\n\nReturn: category and entry (the bullet text, starting with the bold area prefix), or category "none" and entry null.`,
      { tier: "quick", title: `${id} changelog`, subSessions: false, schema: { type: "object", required: ["category", "entry"], properties: { category: { type: "string", enum: ["Added", "Changed", "Fixed", "Deprecated", "Removed", "Security", "none"] }, entry: { type: ["string", "null"] } } } }));

    if (change && change.category !== "none" && change.entry) {
      const log = await read("CHANGELOG.md");
      const anchor = `### ${change.category}`;
      const at = log.indexOf(anchor);
      const updated = at >= 0
        ? log.slice(0, at + anchor.length) + "\n\n" + change.entry + log.slice(at + anchor.length)
        : log.replace("## [Unreleased]", `## [Unreleased]\n\n${anchor}\n\n${change.entry}`);
      await tools.write_file({ path: "CHANGELOG.md", content: updated });
      await sh(`git add CHANGELOG.md; git commit -m "docs: changelog entry for the ${message.replace(/^refactor: /, "").slice(0, 60)} work"`);
      present(`### ${id} landed + changelog (${change.category})`);
    } else {
      present(`### ${id} landed (internal: no changelog entry)`);
    }
  } else {
    failed.push({ step: id, why: verdict ? `architect verdict: ${verdict.verdict}` : (result?.stopped?.[0]?.why ?? "step loop returned without a verdict") });
    present(`### ${id} FAILED after its budget — stopping the overnight run.\n\nThe tree and dossier are left for the morning review.`);
    break;
  }
}

// --- the pull request -------------------------------------------------------
const pr = await step("pr", async () => {
  const ledgerRows = (await read(plan)).split("\n").filter((l) => new RegExp(`\\| P\\d+ \\| committed ${new Date().toISOString().slice(0, 10)}`).test(l));
  const logText = (await sh(`git log --oneline main..${branch}`)).text.trim();
  const changes = (await sh("git diff main...HEAD --stat")).text.trim().split("\n").slice(-3).join("\n");
  const body = [
    `# Phase 1b — the polish and bug-ledger loop`,
    ``,
    `Unattended run of the dev-loop workflow, off-peak, ${new Date().toISOString().slice(0, 16).replace("T", " ")} UTC.`,
    landed.length ? `\n## Landed this run\n\n${landed.map((l) => `- ${l.commit} ${l.message}`).join("\n")}` : "",
    failed.length ? `\n## Stopped on\n\n\`${failed[0].step}\`: ${failed[0].why}` : "",
    stoppedForWindow ? `\nStopped by the window/stop-at guard; the remaining steps stay unchecked in the plan.` : "",
    ``,
    `## Full branch log (main..${branch})`,
    ``,
    "```", logText, "```",
    ``,
    `## Diff size`, ``, "```", changes, "```",
    ``,
    `Every step passed its worker battery (fantomas, build, both suites — tails on record), the idiom gates, and the architect + red-team review before landing. Review per commit or per area — the ledger in \`${plan}\` carries one line per step.`,
    ``,
    `_This pull request was written by [CodeAlta](https://codealta.github.io), an AI coding agent._`,
  ].filter((s) => s !== "").join("\n");
  await tools.write_file({ path: ".alta/derived/PR-body.md", content: body });
  const push = await sh(`git push -u origin ${branch}`);
  const created = await sh(`gh pr create --base main --head ${branch} --title "Phase 1b: polish and bug ledger (dev-loop)" --body-file .alta/derived/PR-body.md`);
  return { push: push.text.trim().split("\n").slice(-2).join(" "), pr: created.text.trim() || created.code === 0 ? created.text.trim() : `gh exit ${created.code}` };
});

present(`## Overnight run done\n\nLanded: ${landed.length} step(s)${failed.length ? `; failed: ${failed[0].step}` : ""}. PR: ${pr.pr}`);
return { landed, failed, stoppedForWindow, pr };
