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

  it("renders an accessible campaign dashboard", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve([
            {
              campaignId: "018f0000-0000-7000-8000-000000000010",
              ownerId: "018f0000-0000-7000-8000-000000000001",
              name: "Арракис",
              description: "",
              locale: "ru-RU",
              timeZone: "Europe/Moscow",
              rulesetVersionId: "dnd5e-srd@1.0.0",
              status: "Draft",
              automationLevel: "Assisted",
              dicePolicy: "ServerAuthoritative",
              version: 1,
              policyRevision: 1,
              role: "Owner",
              effectiveCapabilities: ["campaign.read"],
            },
          ]),
      }),
    );
    const user = userEvent.setup();
    const { container } = render(<App config={config} />);

    await user.click(screen.getByRole("link", { name: "Кампании" }));

    expect(await screen.findByText("Арракис")).toBeInTheDocument();
    expect((await axe.run(container)).violations).toHaveLength(0);
  });
});
