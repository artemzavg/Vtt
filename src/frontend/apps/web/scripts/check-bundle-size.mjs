import { readdir, stat } from "node:fs/promises";
import { join } from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const assetsDirectory = fileURLToPath(new URL("../dist/assets/", import.meta.url));
const maximumJavaScriptBytes = 350_000;

async function collectJavaScriptFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const path = join(directory, entry.name);

    if (entry.isDirectory()) {
      files.push(...(await collectJavaScriptFiles(path)));
    } else if (entry.name.endsWith(".js")) {
      files.push(path);
    }
  }

  return files;
}

const files = await collectJavaScriptFiles(assetsDirectory);
const sizes = await Promise.all(files.map(async (file) => (await stat(file)).size));
const totalBytes = sizes.reduce((total, size) => total + size, 0);

if (totalBytes > maximumJavaScriptBytes) {
  console.error(
    `JavaScript bundle is ${totalBytes} bytes; baseline is ${maximumJavaScriptBytes} bytes.`,
  );
  process.exitCode = 1;
} else {
  console.log(`JavaScript bundle: ${totalBytes} / ${maximumJavaScriptBytes} bytes.`);
}
