import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

import { App } from "./app";
import { ErrorBoundary } from "./components/error-boundary";
import { DegradedPage, LoadingPage } from "./components/state-pages";
import { getFallbackRuntimeConfig, loadRuntimeConfig } from "./config/runtime-config";
import "./styles.css";

const container = document.querySelector<HTMLDivElement>("#root");

if (!container) {
  throw new Error("Root container was not found.");
}

const root = createRoot(container);

function renderApplication(config = getFallbackRuntimeConfig()) {
  root.render(
    <StrictMode>
      <ErrorBoundary>
        <App config={config} />
      </ErrorBoundary>
    </StrictMode>,
  );
}

root.render(
  <StrictMode>
    <LoadingPage />
  </StrictMode>,
);

loadRuntimeConfig()
  .then(renderApplication)
  .catch((error: unknown) => {
    const message = error instanceof Error ? error.message : "Неизвестная ошибка конфигурации.";

    root.render(
      <StrictMode>
        <DegradedPage message={message} onContinue={() => renderApplication()} />
      </StrictMode>,
    );
  });
