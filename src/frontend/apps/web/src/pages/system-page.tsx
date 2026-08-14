import { generatedClientStatus } from "@vtt/api-client";
import { useCallback, useEffect, useState } from "react";

import type { RuntimeConfig } from "../config/runtime-config";

interface SystemPageProps {
  readonly config: RuntimeConfig;
}

type ApiHealth =
  | { readonly kind: "checking"; readonly message: string }
  | { readonly kind: "healthy"; readonly message: string }
  | { readonly kind: "degraded"; readonly message: string };

export function SystemPage({ config }: SystemPageProps) {
  const [health, setHealth] = useState<ApiHealth>({
    kind: "checking",
    message: "Проверяем Edge API…",
  });

  const checkHealth = useCallback(
    async (signal?: AbortSignal) => {
      setHealth({
        kind: "checking",
        message: "Проверяем Edge API…",
      });

      try {
        const request: RequestInit = {
          headers: {
            Accept: "application/json",
          },
        };

        if (signal) {
          request.signal = signal;
        }

        const response = await fetch(
          `${config.apiBaseUrl.replace(/\/$/, "")}/health/ready`,
          request,
        );

        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }

        setHealth({
          kind: "healthy",
          message: "Edge API готов принимать запросы.",
        });
      } catch (error) {
        if (error instanceof DOMException && error.name === "AbortError") {
          return;
        }

        setHealth({
          kind: "degraded",
          message: error instanceof Error ? error.message : "Неизвестная ошибка сети.",
        });
      }
    },
    [config.apiBaseUrl],
  );

  useEffect(() => {
    const controller = new AbortController();
    void checkHealth(controller.signal);

    return () => controller.abort();
  }, [checkHealth]);

  return (
    <main id="main-content" className="page system-page">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Diagnostics</p>
          <h1>Состояние системы</h1>
        </div>
        <button type="button" className="secondary-button" onClick={() => void checkHealth()}>
          Проверить снова
        </button>
      </div>

      <section className="diagnostic-panel" aria-labelledby="runtime-heading">
        <div className="panel-heading">
          <h2 id="runtime-heading">Публичная runtime-конфигурация</h2>
          <span className="safe-label">allow-listed</span>
        </div>
        <dl>
          <div>
            <dt>Environment</dt>
            <dd>{config.environment}</dd>
          </div>
          <div>
            <dt>Version</dt>
            <dd>{config.build.version}</dd>
          </div>
          <div>
            <dt>Commit</dt>
            <dd>{config.build.commit}</dd>
          </div>
          <div>
            <dt>API base URL</dt>
            <dd>{config.apiBaseUrl}</dd>
          </div>
          <div>
            <dt>Generated client</dt>
            <dd>{generatedClientStatus}</dd>
          </div>
        </dl>
      </section>

      <section className="diagnostic-panel" aria-labelledby="health-heading">
        <div className="panel-heading">
          <h2 id="health-heading">Readiness</h2>
          <span className={`health-pill health-${health.kind}`}>{health.kind}</span>
        </div>
        <p aria-live="polite">{health.message}</p>
      </section>
    </main>
  );
}
