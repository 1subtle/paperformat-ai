#!/usr/bin/env node

import { createHash } from "node:crypto";
import {
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const root = resolve(scriptDirectory, "..");
const configurationPath = join(
  root,
  "examples",
  "ieee-agent-native",
  "example.json",
);
const configuration = JSON.parse(readFileSync(configurationPath, "utf8"));
const outputArgument = process.argv.indexOf("--output");
if (outputArgument < 0 || !process.argv[outputArgument + 1]) {
  fail("Usage: node scripts/rehearse-agent-native-example.mjs --output NEW_DIR");
}

const workspace = resolve(process.argv[outputArgument + 1]);
if (existsSync(workspace) && readdirSync(workspace).length > 0) {
  fail(`Output directory must be new or empty: ${workspace}`);
}
mkdirSync(dirname(workspace), { recursive: true });

const source = resolve(
  root,
  "examples",
  "ieee-agent-native",
  configuration.source,
);
if (!existsSync(source) || !statSync(source).isFile()) {
  fail(`Example source is missing: ${source}`);
}

const launcher = join(
  root,
  "plugins",
  "paperformat-ai",
  "scripts",
  "paperformat",
);
const transcript = [];

run(
  "run-workflow",
  [
    "run-workflow",
    "--manuscript",
    source,
    "--ieee",
    "--workspace",
    workspace,
  ],
  [0, 3],
);

const report = json(join(workspace, "issue-report.json"));
const analysis = json(join(workspace, "layout-analysis.json"));
const workflow = json(join(workspace, "workflow.json"));
if (!analysis.canConvert || !analysis.frontMatterEndElementId) {
  fail("Controlled example no longer has a safe front-matter boundary.");
}

const breakId = "example-insert-body-section";
const columnsId = "example-set-body-columns";
const proposalPath = join(workspace, "agent-plan-proposal.json");
writeJson(proposalPath, {
  schemaVersion: "2.0",
  sourceReportId: report.reportId,
  sourceSha256: sha256(source),
  providerId: "release-rehearsal",
  model: "checked-ieee-example-v1",
  visualEvidenceUsed: true,
  externalProcessingConsent: false,
  directives: [],
  layoutOperations: [
    {
      operationId: breakId,
      kind: "insertContinuousSectionBreak",
      decision: "apply",
      risk: "medium",
      reason: "Keep the controlled title block full width.",
      dependsOnOperationIds: [],
      rollbackStrategy: "restoreSectionSnapshot",
      afterElementId: analysis.frontMatterEndElementId,
    },
    {
      operationId: columnsId,
      kind: "setSectionColumns",
      decision: "apply",
      risk: "medium",
      reason: "Apply the reviewed two-column body geometry.",
      dependsOnOperationIds: [breakId],
      rollbackStrategy: "restoreSectionSnapshot",
      targetSectionIndex: 1,
      columnCount: configuration.expected.bodyColumnCount,
      columnSpacingTwips: configuration.expected.columnSpacingTwips,
    },
  ],
});

const planPath = join(workspace, "repair-plan.json");
run(
  "plan-validate",
  [
    "plan-validate",
    "--source",
    source,
    "--report",
    join(workspace, "issue-report.json"),
    "--rules",
    join(workspace, "format-spec.json"),
    "--proposal",
    proposalPath,
    "--output",
    planPath,
  ],
  [3],
);

const applyDirectory = join(workspace, "approved-candidate");
run(
  "apply",
  [
    "apply",
    "--input",
    source,
    "--rules",
    join(workspace, "format-spec.json"),
    "--report",
    join(workspace, "issue-report.json"),
    "--plan",
    planPath,
    "--approve",
    `${breakId},${columnsId}`,
    "--confirm-page-changes",
    "--output-dir",
    applyDirectory,
  ],
  [0],
);

const formatted = join(applyDirectory, "formatted.docx");
const afterPages = join(workspace, "after-pages");
run(
  "render-after",
  ["render", "--input", formatted, "--output-dir", afterPages],
  [0],
);

const comparisonPath = join(applyDirectory, "page-comparison.json");
run(
  "compare-pages",
  [
    "compare-pages",
    "--before",
    join(workspace, "before-pages"),
    "--after",
    afterPages,
    "--output",
    comparisonPath,
  ],
  [0, 3],
);

const applyManifest = json(join(applyDirectory, "apply-manifest.json"));
const beforeRender = json(
  join(workspace, "before-pages", "render-manifest.json"),
);
const afterRender = json(join(afterPages, "render-manifest.json"));
const visualSubmissionPath = join(workspace, "visual-review-submission.json");
writeJson(visualSubmissionPath, {
  schemaVersion: "1.0",
  planId: applyManifest.planId,
  operationId: applyManifest.operationId,
  status: "passed",
  providerId: "release-rehearsal",
  model: "checked-ieee-example-v1",
  sourcePageCount: beforeRender.pages.length,
  outputPageCount: afterRender.pages.length,
  findings: [],
  summary:
    "Controlled release fixture matched its previously reviewed visual baseline.",
});

const visualReviewPath = join(
  applyDirectory,
  "validated-visual-review.json",
);
run(
  "visual-review",
  [
    "visual-review",
    "--apply-manifest",
    join(applyDirectory, "apply-manifest.json"),
    "--before-render",
    join(workspace, "before-pages"),
    "--after-render",
    afterPages,
    "--comparison",
    comparisonPath,
    "--submission",
    visualSubmissionPath,
    "--output",
    visualReviewPath,
  ],
  [0],
);

const validationPath = join(applyDirectory, "validation-report.json");
run(
  "validate-output",
  [
    "validate-output",
    "--input-dir",
    applyDirectory,
    "--comparison",
    comparisonPath,
    "--visual-review",
    visualReviewPath,
    "--output",
    validationPath,
  ],
  [0],
);

const exportDirectory = join(workspace, "ready-export");
run(
  "export",
  [
    "export",
    "--input-dir",
    applyDirectory,
    "--output-dir",
    exportDirectory,
  ],
  [0],
);

const postCheck = json(join(applyDirectory, "post-check.json"));
const integrity = json(join(applyDirectory, "integrity-report.json"));
const comparison = json(comparisonPath);
const validation = json(validationPath);
const exportManifest = json(join(exportDirectory, "export-manifest.json"));
const outputInspectionPath = join(workspace, "output-inspection.json");
run(
  "inspect-output",
  ["inspect", "--input", formatted, "--output", outputInspectionPath],
  [0],
);
const outputInspection = json(outputInspectionPath).inspection;

const expected = configuration.expected;
const actualColumns = outputInspection.sections.map(
  (section) => section.pageSettings.columns.count,
);
assert(exportManifest.status === "ready", "Export is not ready.");
assert(validation.status === "passed", "Final validation did not pass.");
assert(integrity.status === "passed", "Content integrity did not pass.");
assert(postCheck.summary.errorCount === 0, "Format errors remain.");
assert(comparison.status === "passed", "Page comparison did not pass.");
assert(
  sha256(source) === workflow.sourceSha256,
  "Source hash changed during the rehearsal.",
);
assert(
  actualColumns.length === expected.sectionCount &&
    actualColumns[0] === expected.frontMatterColumnCount &&
    actualColumns[1] === expected.bodyColumnCount,
  `Unexpected section columns: ${actualColumns.join(",")}`,
);

const summary = {
  schemaVersion: "1.0",
  status: "ready",
  controlledRegressionBaseline: true,
  sourceSha256: workflow.sourceSha256,
  outputSha256: applyManifest.outputSha256,
  planId: applyManifest.planId,
  operationId: applyManifest.operationId,
  approvedReviewOperations: [breakId, columnsId],
  sourcePreserved: applyManifest.originalPreserved,
  packageValid: applyManifest.packageValid,
  postCheck: {
    score: postCheck.summary.score,
    issueCount: postCheck.summary.issueCount,
    errorCount: postCheck.summary.errorCount,
  },
  integrityStatus: integrity.status,
  pageComparisonStatus: comparison.status,
  visualReviewStatus: validation.visualReview.status,
  beforePageCount: beforeRender.pages.length,
  afterPageCount: afterRender.pages.length,
  sectionColumns: actualColumns,
  readyDocx: relative(workspace, join(exportDirectory, "formatted.docx")),
};
const expectedSummary = json(
  join(root, "examples", "ieee-agent-native", "expected-summary.json"),
);
for (const [key, expectedValue] of Object.entries(expectedSummary)) {
  assert(
    JSON.stringify(summary[key]) === JSON.stringify(expectedValue),
    `Release baseline mismatch for ${key}.`,
  );
}
writeJson(join(workspace, "rehearsal-summary.json"), summary);
writeFileSync(
  join(workspace, "command-transcript.jsonl"),
  transcript.map((entry) => JSON.stringify(entry)).join("\n") + "\n",
);
process.stdout.write(`${JSON.stringify(summary, null, 2)}\n`);

function run(label, args, allowedExitCodes) {
  const result = spawnSync(launcher, args, {
    cwd: root,
    encoding: "utf8",
    env: { ...process.env, OPENAI_API_KEY: "" },
  });
  const status = result.status ?? 10;
  transcript.push({
    label,
    command: ["paperformat", ...args],
    exitCode: status,
    stdout: result.stdout.trim(),
    stderr: result.stderr.trim(),
  });
  if (!allowedExitCodes.includes(status)) {
    fail(
      `${label} exited ${status}.\n${result.stdout.trim()}\n${result.stderr.trim()}`,
    );
  }
}

function json(file) {
  return JSON.parse(readFileSync(file, "utf8"));
}

function writeJson(file, value) {
  writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`);
}

function sha256(file) {
  return createHash("sha256").update(readFileSync(file)).digest("hex");
}

function assert(condition, message) {
  if (!condition) fail(message);
}

function fail(message) {
  process.stderr.write(`${message}\n`);
  process.exit(1);
}
