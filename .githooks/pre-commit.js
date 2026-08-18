#!/usr/bin/env node
// Git pre-commit hook: checks `dotnet format` on staged .cs files, then runs
// the fast unit-test subset (Category!=Integration) whenever any .cs is staged.
// Integration tests (Postgres/Testcontainers, tagged [Trait("Category","Integration")])
// are intentionally excluded here — they need Docker and take ~10s+; run them
// via `dotnet test src/Lassie.Tests/Lassie.Tests.csproj` or in CI.
const { spawnSync } = require("node:child_process");

function run(cmd, args, opts = {}) {
  return spawnSync(cmd, args, { encoding: "utf8", ...opts });
}

const repoRoot = run("git", ["rev-parse", "--show-toplevel"]).stdout.trim();
process.chdir(repoRoot);

const diff = run("git", ["diff", "--cached", "--name-only", "--diff-filter=ACMR"]);
if (diff.status !== 0) {
  console.error(diff.stderr || "git diff --cached failed");
  process.exit(1);
}

const stagedCsFiles = diff.stdout
  .split("\n")
  .map((s) => s.trim())
  .filter((f) => f.endsWith(".cs") && !/[\\/](obj|bin)[\\/]/i.test(f));

if (stagedCsFiles.length === 0) {
  process.exit(0);
}

console.log(`pre-commit: checking formatting on ${stagedCsFiles.length} staged .cs file(s)...`);

const testProjectFiles = stagedCsFiles.filter((f) => f.startsWith("src/Lassie.Tests/"));
const mainProjectFiles = stagedCsFiles.filter((f) => !f.startsWith("src/Lassie.Tests/"));

let formatFailed = false;

function checkFormat(project, files) {
  if (files.length === 0) return;
  const args = ["format", project, "--verify-no-changes"];
  for (const f of files) args.push("--include", f);
  const result = run("dotnet", args);
  if (result.status !== 0) {
    formatFailed = true;
    console.error(`\n${result.stdout}${result.stderr}`.trim());
  }
}

checkFormat("src/lassie.csproj", mainProjectFiles);
checkFormat("src/Lassie.Tests/Lassie.Tests.csproj", testProjectFiles);

if (formatFailed) {
  console.error(
    "\npre-commit: formatting issues found. Run `dotnet format src/lassie.csproj` " +
      "(or the test project) and re-stage before committing."
  );
  process.exit(1);
}

console.log("pre-commit: formatting OK. Running unit tests (Category!=Integration)...");

const testResult = run(
  "dotnet",
  ["test", "src/Lassie.Tests/Lassie.Tests.csproj", "--filter", "Category!=Integration"],
  { stdio: "inherit" }
);

if (testResult.status !== 0) {
  console.error("\npre-commit: unit tests failed. Commit aborted.");
  process.exit(1);
}

process.exit(0);
