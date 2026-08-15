import process from "node:process";
import { randomUUID } from "node:crypto";
import { performance } from "node:perf_hooks";

const baseUrl = process.env.VTT_ENGINEERING_URL ?? "http://127.0.0.1:55120";
const iterations = Number.parseInt(process.env.VTT_BENCHMARK_ITERATIONS ?? "100", 10);
if (!Number.isSafeInteger(iterations) || iterations < 10 || iterations > 10_000) {
  throw new Error("VTT_BENCHMARK_ITERATIONS must be an integer from 10 to 10000.");
}

function percentile(values, fraction) {
  const ordered = [...values].sort((left, right) => left - right);
  return ordered[Math.min(ordered.length - 1, Math.ceil(ordered.length * fraction) - 1)];
}

async function append(probeId, version, key) {
  const started = performance.now();
  const response = await fetch(`${baseUrl}/platform/probes/${probeId}/increments`, {
    method: "POST",
    headers: {
      "content-type": "application/json",
      "if-match": `"${version}"`,
      "idempotency-key": key,
    },
    body: JSON.stringify({ amount: 1 }),
  });
  if (!response.ok) {
    throw new Error(`Append failed with ${response.status}: ${await response.text()}`);
  }
  await response.arrayBuffer();
  return performance.now() - started;
}

async function waitForOperation(location) {
  const deadline = Date.now() + 60_000;
  while (Date.now() < deadline) {
    const response = await fetch(new URL(location, baseUrl));
    const operation = await response.json();
    if (["completed", "failed", "cancelled"].includes(operation.status)) {
      if (operation.status !== "completed") {
        throw new Error(`Rebuild finished as ${operation.status}: ${operation.errorCode}`);
      }
      return operation;
    }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  throw new Error("Projection rebuild timed out.");
}

const probeId = randomUUID();
for (let warmup = 0; warmup < 5; warmup += 1) {
  await append(probeId, warmup, `benchmark-warmup-${probeId}-${warmup}`);
}

const latencies = [];
const appendStarted = performance.now();
for (let index = 0; index < iterations; index += 1) {
  latencies.push(await append(probeId, index + 5, `benchmark-${probeId}-${index}`));
}
const appendElapsed = performance.now() - appendStarted;

const rebuildStarted = performance.now();
const rebuildResponse = await fetch(`${baseUrl}/platform/projections/probes/rebuild`, {
  method: "POST",
});
if (rebuildResponse.status !== 202) {
  throw new Error(`Rebuild start failed with ${rebuildResponse.status}.`);
}
const location = rebuildResponse.headers.get("location");
if (!location) throw new Error("Rebuild response has no Location header.");
const operation = await waitForOperation(location);
const rebuildElapsed = performance.now() - rebuildStarted;

console.log(
  JSON.stringify(
    {
      environment: { baseUrl, node: process.version, sequential: true },
      append: {
        events: iterations,
        rps: Number(((iterations * 1000) / appendElapsed).toFixed(2)),
        p50Ms: Number(percentile(latencies, 0.5).toFixed(2)),
        p95Ms: Number(percentile(latencies, 0.95).toFixed(2)),
        maxMs: Number(Math.max(...latencies).toFixed(2)),
      },
      replay: {
        durationMs: Number(rebuildElapsed.toFixed(2)),
        checksum: operation.resultChecksum,
      },
    },
    null,
    2,
  ),
);
