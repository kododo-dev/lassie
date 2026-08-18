#!/usr/bin/env node
// PostToolUse (Write|Edit) hook: runs `dotnet format --verify-no-changes` on the
// edited .cs file's project. Exits 2 (and lets asyncRewake wake Claude) when the
// file isn't formatted per the project's style rules.
const { spawnSync } = require("node:child_process");
const path = require("node:path");

let raw = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", (chunk) => (raw += chunk));
process.stdin.on("end", () => {
  let filePath;
  try {
    filePath = JSON.parse(raw || "{}").tool_input?.file_path;
  } catch {
    process.exit(0);
  }
  if (!filePath || !filePath.endsWith(".cs")) process.exit(0);
  if (/[\\/](obj|bin)[\\/]/i.test(filePath)) process.exit(0);

  const project = filePath.includes("Lassie.Tests")
    ? "src/Lassie.Tests/Lassie.Tests.csproj"
    : "src/lassie.csproj";

  // dotnet format's --include only matches when given a project-root-relative
  // path with forward slashes; absolute or backslash paths silently match
  // nothing (exit 0 with no diagnostics), so this conversion is load-bearing.
  const relativePath = path
    .relative(process.cwd(), filePath)
    .split(path.sep)
    .join("/");

  const result = spawnSync(
    "dotnet",
    ["format", project, "--verify-no-changes", "--include", relativePath],
    { encoding: "utf8", cwd: process.cwd() }
  );

  const output = `${result.stdout ?? ""}${result.stderr ?? ""}`.trim();
  if (result.status !== 0 && output) {
    process.stderr.write(
      `dotnet format found style issues in ${filePath}:\n${output}\n`
    );
  }
  process.exit(result.status ?? 0);
});
