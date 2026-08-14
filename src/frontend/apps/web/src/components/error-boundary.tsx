import { Component, type ErrorInfo, type ReactNode } from "react";

interface ErrorBoundaryProps {
  readonly children: ReactNode;
}

interface ErrorBoundaryState {
  readonly hasError: boolean;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  public override state: ErrorBoundaryState = {
    hasError: false,
  };

  public static getDerivedStateFromError(): ErrorBoundaryState {
    return {
      hasError: true,
    };
  }

  public override componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error("Unhandled application error.", {
      error: error.message,
      componentStack: info.componentStack,
    });
  }

  public override render(): ReactNode {
    if (this.state.hasError) {
      return (
        <main className="state-page" role="alert">
          <p className="eyebrow">Application error</p>
          <h1>Интерфейс не смог продолжить работу</h1>
          <p>Обновите страницу. Если ошибка повторится, приложите время возникновения.</p>
          <button type="button" onClick={() => window.location.reload()}>
            Обновить
          </button>
        </main>
      );
    }

    return this.props.children;
  }
}
