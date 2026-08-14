interface DegradedPageProps {
  readonly message: string;
  readonly onContinue: () => void;
}

export function LoadingPage() {
  return (
    <main className="state-page" aria-busy="true" aria-live="polite">
      <span className="spinner" aria-hidden="true" />
      <p className="eyebrow">VTT Platform</p>
      <h1>Подготавливаем игровой стол</h1>
      <p>Загружается публичная конфигурация приложения.</p>
    </main>
  );
}

export function DegradedPage({ message, onContinue }: DegradedPageProps) {
  return (
    <main className="state-page" role="alert">
      <p className="eyebrow">Degraded mode</p>
      <h1>Конфигурация недоступна</h1>
      <p>{message}</p>
      <button type="button" onClick={onContinue}>
        Продолжить с безопасными local defaults
      </button>
    </main>
  );
}
