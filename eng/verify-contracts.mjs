import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { renderGeneratedClient } from "./generate-contracts.mjs";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const readJson = async (path) => JSON.parse(await readFile(resolve(repositoryRoot, path), "utf8"));

function operations(contract) {
  return Object.values(contract.paths).flatMap((path) => Object.values(path));
}

export function findCompatibilityViolations(contract, baseline) {
  const violations = [];
  const byId = new Map(operations(contract).map((operation) => [operation.operationId, operation]));

  for (const [operationId, responseCodes] of Object.entries(baseline.operations)) {
    const operation = byId.get(operationId);
    if (!operation) {
      violations.push(`removed operation ${operationId}`);
      continue;
    }
    for (const responseCode of responseCodes) {
      if (!(responseCode in operation.responses)) {
        violations.push(`removed response ${operationId}:${responseCode}`);
      }
    }
  }

  for (const [schemaName, expected] of Object.entries(baseline.schemas)) {
    const schema = contract.components.schemas[schemaName];
    if (!schema) {
      violations.push(`removed schema ${schemaName}`);
      continue;
    }
    for (const property of expected.required) {
      if (!schema.required?.includes(property)) {
        violations.push(`required response field became optional ${schemaName}.${property}`);
      }
    }
    for (const [property, type] of Object.entries(expected.propertyTypes)) {
      if (!schema.properties?.[property]) {
        violations.push(`removed field ${schemaName}.${property}`);
      } else if (JSON.stringify(schema.properties[property].type) !== JSON.stringify(type)) {
        violations.push(`changed type ${schemaName}.${property}`);
      }
    }
  }

  return violations;
}

function validatePublicFieldPolicy(document, source) {
  const forbidden = /^(?:password|secret|token|email|rawBody)$/i;
  const violations = [];

  function visit(value, path) {
    if (Array.isArray(value)) {
      value.forEach((item, index) => visit(item, `${path}[${index}]`));
    } else if (value && typeof value === "object") {
      for (const [key, child] of Object.entries(value)) {
        if (key === "properties" && child && typeof child === "object") {
          for (const propertyName of Object.keys(child)) {
            if (forbidden.test(propertyName)) {
              violations.push(`${source}:${path}.properties.${propertyName}`);
            }
          }
        }
        visit(child, `${path}.${key}`);
      }
    }
  }

  visit(document, "$");
  return violations;
}

const openapi = await readJson("contracts/openapi/engineering-fixture.v1.json");
const identityOpenapi = await readJson("contracts/openapi/identity-bff.v1.json");
const asyncapi = await readJson("contracts/asyncapi/engineering-fixture.v1.json");
const envelope = await readJson("contracts/events/event-envelope.v1.schema.json");
const eventData = await readJson("contracts/events/engineering-probe-incremented.v1.schema.json");
const baseline = await readJson("contracts/baseline/engineering-fixture.v1.compatibility.json");
const generatedPath = resolve(
  repositoryRoot,
  "src/frontend/packages/api-client/src/generated/engineering-fixture.ts",
);
const generated = await readFile(generatedPath, "utf8");

const violations = [
  ...(openapi.openapi?.startsWith("3.1.") ? [] : ["OpenAPI must be in the 3.1 line"]),
  ...(asyncapi.asyncapi === "3.0.0" ? [] : ["AsyncAPI must be 3.0.0"]),
  ...(envelope.$schema?.endsWith("2020-12/schema")
    ? []
    : ["Event envelope must use JSON Schema 2020-12"]),
  ...(asyncapi.channels.probeIncremented.address ===
  "vtt.engineering-fixture.engineering-probe.incremented.v1"
    ? []
    : ["JetStream subject does not follow the versioned naming convention"]),
  ...findCompatibilityViolations(openapi, baseline),
  ...validatePublicFieldPolicy(openapi, "openapi"),
  ...validatePublicFieldPolicy(envelope, "event-envelope"),
  ...validatePublicFieldPolicy(eventData, "event-data"),
  ...(identityOpenapi.openapi?.startsWith("3.1.")
    ? []
    : ["Identity BFF OpenAPI must be in the 3.1 line"]),
  ...[
    "registerUser",
    "login",
    "refreshBrowserSession",
    "logout",
    "getMyProfile",
    "listMySessions",
    "revokeSession",
  ].filter(
    (operationId) =>
      !operations(identityOpenapi).some((operation) => operation.operationId === operationId),
  ),
  ...["sessionToken", "accessToken", "refreshToken"].filter(
    (field) => field in (identityOpenapi.components.schemas.BrowserSession.properties ?? {}),
  ),
];

if (generated !== renderGeneratedClient(openapi)) {
  violations.push("generated TypeScript client is stale; run pnpm contracts:generate");
}

const breakingFixture = structuredClone(openapi);
delete breakingFixture.components.schemas.IncrementProbeResponse.properties.eventId;
if (findCompatibilityViolations(breakingFixture, baseline).length === 0) {
  violations.push("compatibility check failed its built-in breaking-change fixture");
}

if (violations.length > 0) {
  console.error(["Contract verification failed:", ...violations].join("\n- "));
  process.exitCode = 1;
} else {
  console.log("Contracts, compatibility baseline, and generated client are valid.");
}
