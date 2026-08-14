import { readdir, readFile } from "node:fs/promises";
import { extname, join } from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const sourceRoot = fileURLToPath(new URL("../src/frontend/apps/web/src/", import.meta.url));
const violations = [];

async function visit(directory) {
  const entries = await readdir(directory, { withFileTypes: true });

  for (const entry of entries) {
    const path = join(directory, entry.name);

    if (entry.isDirectory()) {
      await visit(path);
      continue;
    }

    if (![".ts", ".tsx"].includes(extname(entry.name))) {
      continue;
    }

    const content = await readFile(path, "utf8");

    if (/\b(?:interface|type|class)\s+\w*Dto\b/.test(content)) {
      violations.push(`${path}: handwritten *Dto declaration`);
    }

    if (/from\s+["'][^"']*\/Services\//.test(content)) {
      violations.push(`${path}: frontend imports backend service source`);
    }
  }
}

await visit(sourceRoot);

if (violations.length > 0) {
  console.error(["Frontend boundary violations:", ...violations].join("\n"));
  process.exitCode = 1;
} else {
  console.log("Frontend boundary rules passed.");
}
