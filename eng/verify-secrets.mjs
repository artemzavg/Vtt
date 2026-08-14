import { readdir, readFile } from "node:fs/promises";
import { extname, join, relative } from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const repositoryRoot = fileURLToPath(new URL("../", import.meta.url));
const ignoredDirectories = new Set([
  ".git",
  ".idea",
  "bin",
  "dist",
  "node_modules",
  "obj",
  "playwright-report",
  "test-results",
  "TestResults",
]);
const ignoredFiles = new Set([".env.example", "pnpm-lock.yaml"]);
const textExtensions = new Set([
  "",
  ".cs",
  ".csproj",
  ".css",
  ".env",
  ".html",
  ".js",
  ".json",
  ".md",
  ".mjs",
  ".props",
  ".ps1",
  ".sh",
  ".targets",
  ".ts",
  ".tsx",
  ".xml",
  ".yaml",
  ".yml",
]);
const secretPatterns = [
  { name: "private key", pattern: /-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/ },
  { name: "AWS access key", pattern: /\bAKIA[0-9A-Z]{16}\b/ },
  { name: "GitHub token", pattern: /\bgh[oprsu]_[A-Za-z0-9_]{30,}\b/ },
  { name: "Slack token", pattern: /\bxox[baprs]-[A-Za-z0-9-]{20,}\b/ },
];

const violations = [];

async function visit(directory) {
  const entries = await readdir(directory, { withFileTypes: true });

  for (const entry of entries) {
    if (entry.isDirectory() && ignoredDirectories.has(entry.name)) {
      continue;
    }

    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      await visit(path);
      continue;
    }

    if (ignoredFiles.has(entry.name) || !textExtensions.has(extname(entry.name))) {
      continue;
    }

    const content = await readFile(path, "utf8");
    for (const secretPattern of secretPatterns) {
      if (secretPattern.pattern.test(content)) {
        violations.push(`${relative(repositoryRoot, path)}: possible ${secretPattern.name}`);
      }
    }
  }
}

await visit(repositoryRoot);

if (violations.length > 0) {
  console.error(["Possible committed secrets:", ...violations].join("\n"));
  process.exitCode = 1;
} else {
  console.log("High-confidence secret scan passed.");
}
