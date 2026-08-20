import { createIdentityClient } from "@vtt/api-client";
import { type FormEvent, useState } from "react";

interface AuthPageProps {
  readonly apiBaseUrl: string;
  readonly onAuthenticated: () => void;
}

export function AuthPage({ apiBaseUrl, onAuthenticated }: AuthPageProps) {
  const client = createIdentityClient(apiBaseUrl);
  const [mode, setMode] = useState<"login" | "register" | "reset">("login");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setBusy(true);
    setMessage("");
    const data = new FormData(event.currentTarget);
    const email = String(data.get("email"));
    try {
      if (mode === "login") {
        await client.login(email, String(data.get("password")));
        onAuthenticated();
      } else if (mode === "register") {
        await client.register({
          email,
          password: String(data.get("password")),
          displayName: String(data.get("displayName")),
          locale: "ru-RU",
          timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
          termsVersion: "2026-01",
          privacyVersion: "2026-01",
        });
        setMessage("Проверьте почту: если адрес доступен, письмо уже отправлено.");
      } else {
        await client.requestReset(email);
        setMessage("Если аккаунт существует, инструкция уже отправлена.");
      }
    } catch {
      setMessage(
        mode === "login"
          ? "Не удалось войти. Проверьте email и пароль."
          : "Не удалось выполнить запрос.",
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <main id="main-content" className="page auth-layout">
      <section className="auth-panel" aria-labelledby="auth-title">
        <p className="eyebrow">Identity & Access · Step 03</p>
        <h1 id="auth-title">
          {mode === "login"
            ? "Войти в VTT"
            : mode === "register"
              ? "Создать аккаунт"
              : "Восстановить пароль"}
        </h1>
        <div className="auth-tabs" role="tablist" aria-label="Способ входа">
          <button type="button" className="secondary-button" onClick={() => setMode("login")}>
            Вход
          </button>
          <button type="button" className="secondary-button" onClick={() => setMode("register")}>
            Регистрация
          </button>
          <button type="button" className="secondary-button" onClick={() => setMode("reset")}>
            Сброс
          </button>
        </div>
        <form onSubmit={submit}>
          <label>
            Email
            <input name="email" type="email" autoComplete="email" required />
          </label>
          {mode !== "reset" && (
            <label>
              Пароль
              <input
                name="password"
                type="password"
                autoComplete={mode === "login" ? "current-password" : "new-password"}
                minLength={12}
                required
              />
            </label>
          )}
          {mode === "register" && (
            <label>
              Имя игрока
              <input name="displayName" minLength={2} maxLength={80} required />
            </label>
          )}
          <button disabled={busy}>{busy ? "Подождите…" : "Продолжить"}</button>
        </form>
        {message && (
          <p role="status" className="form-message">
            {message}
          </p>
        )}
      </section>
    </main>
  );
}
