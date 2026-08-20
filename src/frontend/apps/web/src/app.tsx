import { useEffect, useState } from "react";

import type { RuntimeConfig } from "./config/runtime-config";
import { HomePage } from "./pages/home-page";
import { AccountPage } from "./pages/account-page";
import { AuthPage } from "./pages/auth-page";
import { ChallengePage } from "./pages/challenge-page";
import { SystemPage } from "./pages/system-page";

interface AppProps {
  readonly config: RuntimeConfig;
}

export function App({ config }: AppProps) {
  const [path, setPath] = useState(() => window.location.pathname);

  useEffect(() => {
    const updatePath = () => setPath(window.location.pathname);
    window.addEventListener("popstate", updatePath);

    return () => window.removeEventListener("popstate", updatePath);
  }, []);

  const navigate = (nextPath: string) => {
    if (nextPath === window.location.pathname) {
      return;
    }

    window.history.pushState({}, "", nextPath);
    setPath(nextPath);
    window.scrollTo({ top: 0 });
  };

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Перейти к содержимому
      </a>
      <header className="app-header">
        <a
          href="/"
          className="brand"
          aria-label="VTT Platform — главная"
          onClick={(event) => {
            event.preventDefault();
            navigate("/");
          }}
        >
          <span className="brand-mark" aria-hidden="true">
            V
          </span>
          <span>
            VTT <small>Platform</small>
          </span>
        </a>

        <nav aria-label="Основная навигация">
          <a
            href="/"
            aria-current={path === "/" ? "page" : undefined}
            onClick={(event) => {
              event.preventDefault();
              navigate("/");
            }}
          >
            Обзор
          </a>
          <a
            href="/system"
            aria-current={path === "/system" ? "page" : undefined}
            onClick={(event) => {
              event.preventDefault();
              navigate("/system");
            }}
          >
            Система
          </a>
          <a
            href="/account"
            aria-current={path === "/account" ? "page" : undefined}
            onClick={(event) => {
              event.preventDefault();
              navigate("/account");
            }}
          >
            Аккаунт
          </a>
          <a
            href="/auth"
            aria-current={path === "/auth" ? "page" : undefined}
            onClick={(event) => {
              event.preventDefault();
              navigate("/auth");
            }}
          >
            Войти
          </a>
        </nav>
      </header>

      {path === "/system" ? (
        <SystemPage config={config} />
      ) : path === "/auth" ? (
        <AuthPage apiBaseUrl={config.apiBaseUrl} onAuthenticated={() => navigate("/account")} />
      ) : path === "/account" ? (
        <AccountPage apiBaseUrl={config.apiBaseUrl} onLoggedOut={() => navigate("/auth")} />
      ) : path === "/verify-email" ? (
        <ChallengePage apiBaseUrl={config.apiBaseUrl} purpose="verify" />
      ) : path === "/reset-password" ? (
        <ChallengePage apiBaseUrl={config.apiBaseUrl} purpose="reset" />
      ) : (
        <HomePage />
      )}

      <footer>
        <span>Step 03 · Identity & Access</span>
        <span>Сессии защищены BFF</span>
      </footer>
    </div>
  );
}
