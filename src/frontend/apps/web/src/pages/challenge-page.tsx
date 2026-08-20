import { createIdentityClient } from "@vtt/api-client";
import { type FormEvent, useEffect, useMemo, useRef, useState } from "react";

interface ChallengePageProps {
  readonly apiBaseUrl: string;
  readonly purpose: "verify" | "reset";
}

export function ChallengePage({ apiBaseUrl, purpose }: ChallengePageProps) {
  const client = useMemo(() => createIdentityClient(apiBaseUrl), [apiBaseUrl]);
  const token = new URLSearchParams(window.location.search).get("token") ?? "";
  const [status, setStatus] = useState<"idle" | "busy" | "succeeded" | "failed">(() =>
    purpose === "verify" ? "busy" : "idle",
  );
  const verificationStarted = useRef(false);

  useEffect(() => {
    if (purpose !== "verify" || verificationStarted.current) {
      return;
    }

    verificationStarted.current = true;
    void client.verifyEmail(token).then(
      () => setStatus("succeeded"),
      () => setStatus("failed"),
    );
  }, [client, purpose, token]);

  const resetPassword = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setStatus("busy");
    const data = new FormData(event.currentTarget);
    try {
      await client.completeReset(token, String(data.get("password")));
      setStatus("succeeded");
    } catch {
      setStatus("failed");
    }
  };

  return (
    <main id="main-content" className="page auth-layout">
      <section className="auth-panel" aria-labelledby="challenge-title">
        <p className="eyebrow">Identity & Access</p>
        <h1 id="challenge-title">
          {purpose === "verify" ? "Подтверждение email" : "Новый пароль"}
        </h1>
        {token.length === 0 || status === "failed" ? (
          <p role="alert" className="form-message">
            Ссылка недействительна или устарела.
          </p>
        ) : status === "succeeded" ? (
          <p role="status" className="form-message">
            {purpose === "verify"
              ? "Email подтверждён. Теперь можно войти."
              : "Пароль изменён. Все прежние сессии завершены."}
          </p>
        ) : purpose === "reset" ? (
          <form onSubmit={resetPassword}>
            <label>
              Новый пароль
              <input
                name="password"
                type="password"
                autoComplete="new-password"
                minLength={12}
                required
              />
            </label>
            <button disabled={status === "busy"}>
              {status === "busy" ? "Сохраняем…" : "Изменить пароль"}
            </button>
          </form>
        ) : (
          <p role="status">Проверяем ссылку…</p>
        )}
        {(status === "succeeded" || status === "failed" || token.length === 0) && (
          <a href="/auth">Перейти ко входу</a>
        )}
      </section>
    </main>
  );
}
