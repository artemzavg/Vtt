import { describe, expect, it } from "vitest";

import { parseRuntimeConfig } from "./runtime-config";

describe("runtime config", () => {
  it("keeps only allow-listed public fields", () => {
    const config = parseRuntimeConfig({
      apiBaseUrl: "/api",
      environment: "test",
      build: {
        version: "1.2.3",
        commit: "abc123",
      },
      secret: "must-not-leak",
    });

    expect(config).toEqual({
      apiBaseUrl: "/api",
      environment: "test",
      build: {
        version: "1.2.3",
        commit: "abc123",
      },
    });
    expect(config).not.toHaveProperty("secret");
  });

  it("rejects an unsafe shape", () => {
    expect(() =>
      parseRuntimeConfig({
        apiBaseUrl: "",
      }),
    ).toThrow(/invalid shape/i);
  });
});
