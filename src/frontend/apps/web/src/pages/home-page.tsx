export function HomePage() {
  return (
    <main id="main-content" className="page">
      <section className="hero" aria-labelledby="hero-title">
        <div>
          <p className="eyebrow">Foundation · Step 01</p>
          <h1 id="hero-title">Один стол для историй, правил и живой игры</h1>
          <p className="hero-copy">
            Базовый контур готовит платформу к персонажам, сценам, компендиумам и realtime — без
            преждевременной реализации бизнес-функций.
          </p>
        </div>
        <div className="sigil" aria-hidden="true">
          <span>20</span>
        </div>
      </section>

      <section className="foundation-grid" aria-label="Состояние базового контура">
        <article>
          <span className="card-index">01</span>
          <h2>Bounded contexts</h2>
          <p>Сервисы физически разделены и защищены автоматическими dependency rules.</p>
        </article>
        <article>
          <span className="card-index">02</span>
          <h2>Fast feedback</h2>
          <p>Строгий TypeScript, unit и integration smoke-проверки запускаются локально и в CI.</p>
        </article>
        <article>
          <span className="card-index">03</span>
          <h2>Observable by default</h2>
          <p>Health endpoints и OTLP подключены до появления первой доменной команды.</p>
        </article>
      </section>
    </main>
  );
}
