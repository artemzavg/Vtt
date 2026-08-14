export interface RuntimeConfig {
  readonly apiBaseUrl: string;
  readonly environment: string;
  readonly build: {
    readonly version: string;
    readonly commit: string;
  };
}

const fallbackConfig: RuntimeConfig = {
  apiBaseUrl: "/api",
  environment: "local",
  build: {
    version: "0.0.0-local",
    commit: "development",
  },
};

export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  const response = await fetch("/runtime-config.json", {
    cache: "no-store",
    headers: {
      Accept: "application/json",
    },
  });

  if (!response.ok) {
    throw new Error(`Runtime config request failed with status ${response.status}.`);
  }

  return parseRuntimeConfig(await response.json());
}

export function parseRuntimeConfig(value: unknown): RuntimeConfig {
  if (!isRecord(value) || !isRecord(value.build)) {
    throw new Error("Runtime config has an invalid shape.");
  }

  const apiBaseUrl = readNonEmptyString(value, "apiBaseUrl");
  const environment = readNonEmptyString(value, "environment");
  const version = readNonEmptyString(value.build, "version");
  const commit = readNonEmptyString(value.build, "commit");

  if (!apiBaseUrl.startsWith("/") && !URL.canParse(apiBaseUrl)) {
    throw new Error("Runtime config apiBaseUrl must be relative or a valid URL.");
  }

  return {
    apiBaseUrl,
    environment,
    build: {
      version,
      commit,
    },
  };
}

export function getFallbackRuntimeConfig(): RuntimeConfig {
  return fallbackConfig;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function readNonEmptyString(record: Record<string, unknown>, key: string): string {
  const value = record[key];

  if (typeof value !== "string" || value.trim().length === 0) {
    throw new Error(`Runtime config field "${key}" must be a non-empty string.`);
  }

  return value;
}
