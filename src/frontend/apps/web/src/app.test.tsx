import axe from "axe-core";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { App } from "./app";
import type { RuntimeConfig } from "./config/runtime-config";

const config: RuntimeConfig = {
  apiBaseUrl: "/api",
  environment: "test",
  build: {
    version: "0.0.0-test",
    commit: "test-commit",
  },
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  window.history.replaceState({}, "", "/");
});

describe("application shell", () => {
  it("renders the foundation overview and has no detectable accessibility violations", async () => {
    window.history.replaceState({}, "", "/");
    const { container } = render(<App config={config} />);

    expect(
      screen.getByRole("heading", {
        name: /один стол для историй/i,
      }),
    ).toBeInTheDocument();

    const results = await axe.run(container);
    expect(results.violations).toHaveLength(0);
  });

  it("navigates to diagnostics and reports Edge readiness", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
      }),
    );
    const user = userEvent.setup();

    render(<App config={config} />);
    await user.click(screen.getByRole("link", { name: "Система" }));

    expect(await screen.findByText("Edge API готов принимать запросы.")).toBeInTheDocument();
    expect(screen.getByText("test-commit")).toBeInTheDocument();
  });
});
