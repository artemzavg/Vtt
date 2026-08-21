import {
  createIdentityClient,
  IdentityApiError,
  type LoginSession,
  type UserProfile,
} from "@vtt/api-client";
import { type FormEvent, useEffect, useMemo, useState } from "react";

interface AccountPageProps {
  readonly apiBaseUrl: string;
  readonly onLoggedOut: () => void;
}

export function AccountPage({ apiBaseUrl, onLoggedOut }: AccountPageProps) {
  const client = useMemo(() => createIdentityClient(apiBaseUrl), [apiBaseUrl]);
  const [user, setUser] = useState<UserProfile>();
  const [sessions, setSessions] = useState<LoginSession[]>([]);
  const [message, setMessage] = useState("");

  useEffect(() => {
    void Promise.all([client.me(), client.sessions()])
      .then(([profile, activeSessions]) => {
        setUser(profile);
        setSessions(activeSessions.filter((session) => !session.isRevoked));
      })
      .catch((error: unknown) => {
        if (error instanceof IdentityApiError && error.status === 401) {
          onLoggedOut();
          return;
        }
        setMessage("Не удалось загрузить профиль.");
      });
  }, [client, onLoggedOut]);

  const updateProfile = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user) return;

    const data = new FormData(event.currentTarget);
    try {
      const updated = await client.updateProfile({
        ...user,
        displayName: String(data.get("displayName")),
        locale: String(data.get("locale")),
        timeZone: String(data.get("timeZone")),
      });
      setUser(updated);
      setMessage("Профиль сохранён.");
    } catch (error) {
      setMessage(
        error instanceof IdentityApiError && error.status === 409
          ? "Профиль уже изменён в другой вкладке. Обновите страницу."
          : "Не удалось сохранить профиль.",
      );
    }
  };

  return (
    <main id="main-content" className="page">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Безопасность аккаунта</p>
          <h1>{user?.displayName ?? "Профиль"}</h1>
        </div>
        <button className="secondary-button" onClick={() => void client.logout().then(onLoggedOut)}>
          Выйти
        </button>
      </div>
      {user && (
        <section className="diagnostic-panel">
          <h2>Профиль</h2>
          <form onSubmit={updateProfile}>
            <label>
              Имя игрока
              <input
                name="displayName"
                defaultValue={user.displayName}
                minLength={2}
                maxLength={80}
                required
              />
            </label>
            <label>
              Язык
              <input
                name="locale"
                defaultValue={user.locale}
                minLength={2}
                maxLength={16}
                required
              />
            </label>
            <label>
              Часовой пояс
              <input name="timeZone" defaultValue={user.timeZone} maxLength={100} required />
            </label>
            <button>Сохранить профиль</button>
          </form>
        </section>
      )}
      <section className="diagnostic-panel">
        <h2>Активные сессии</h2>
        {sessions.map((session) => (
          <div className="session-row" key={session.sessionId}>
            <span>{session.deviceLabel}</span>
            <span>
              {session.isCurrent ? "Текущая" : new Date(session.lastUsedAt).toLocaleString()}
            </span>
            {!session.isCurrent && (
              <button
                className="secondary-button"
                onClick={() =>
                  void client
                    .revokeSession(session.sessionId)
                    .then(() =>
                      setSessions((items) =>
                        items.filter((item) => item.sessionId !== session.sessionId),
                      ),
                    )
                }
              >
                Завершить
              </button>
            )}
          </div>
        ))}
        <button
          onClick={() =>
            void client
              .revokeOtherSessions()
              .then(() => setSessions((items) => items.filter((item) => item.isCurrent)))
          }
        >
          Завершить остальные
        </button>
      </section>
      {message && (
        <p role="status" className="form-message">
          {message}
        </p>
      )}
    </main>
  );
}
