# VTT Platform — проектная документация

Статус: `Draft / architecture baseline`  
Дата среза: 2026-08-14  
Язык терминов API и доменных событий: английский; пояснения: русский.

Этот каталог фиксирует продуктовые требования и целевую архитектуру браузерной
Virtual Tabletop-платформы. Первая поставка поддерживает D&D через отдельно
версионируемые ruleset/content packages, но ядро не зависит от D&D и допускает
другие настольные ролевые системы.

## Навигация

### Продукт

- [Требования и полный каталог функций](product/requirements.md)
- [Анализ конкурентов](product/competitors.md)
- [Границы MVP и дорожная карта](product/roadmap.md)
- [Подробный исполняемый roadmap разработки](roadmap/README.md)

### Архитектура

- [Верхнеуровневая архитектура](architecture/overview.md)
- [Данные, CQRS, Event Sourcing и события](architecture/data-and-events.md)
- [Нефункциональные требования, безопасность и real-time](architecture/non-functional.md)
- [Нагрузочная модель, SLI/SLO/SLA и стоимость](architecture/capacity-slo-cost.md)
- [Стратегия тестирования](architecture/testing.md)
- [Публичные HTTP и event contracts](architecture/contracts.md)
- [ADR-0001: monorepo и границы сервисов](architecture/adr/0001-monorepo-and-service-boundaries.md)
- [ADR-0002: CQRS, Event Sourcing и надёжная доставка](architecture/adr/0002-cqrs-event-sourcing-and-reliable-messaging.md)

### Разработка и evidence

- [Onboarding и команды local development](development/onboarding.md)
- [Локальные порты и Compose profiles](development/ports.md)
- [Workflow миграций PostgreSQL/Marten](development/migrations.md)
- [Runbook event platform](operations/event-platform.md)
- [Test report шага 01](testing/step-01-test-report.md)
- [Test report шага 02](testing/step-02-test-report.md)

### Микросервисы

- [Карта сервисов и общие правила API](services/README.md)
- [Edge Gateway / BFF](services/edge-gateway.md)
- [Identity & Access](services/identity-access.md)
- [Campaign](services/campaign.md)
- [Ruleset](services/ruleset.md)
- [Compendium](services/compendium.md)
- [Character](services/character.md)
- [Media](services/media.md)
- [Scene](services/scene.md)
- [Session & Realtime](services/session-realtime.md)
- [Gameplay & Encounter](services/gameplay-encounter.md)
- [Chat & Dice](services/chat-dice.md)
- [Search & Projections](services/search-projections.md)

## Ключевые решения

1. DDD задаёт границы владения данными. У каждого bounded context — собственные
   записи, события, модели чтения и жизненный цикл развертывания.
2. CQRS разделяет команды и оптимизированные read models. Клиент никогда не
   восстанавливает экран чтением всех доменных событий.
3. Event Sourcing применяется к бизнес-агрегатам и аудируемым игровым действиям.
   Presence, курсор и промежуточные координаты drag не попадают в event store.
4. Real-time UI использует WebSocket/SignalR, optimistic updates и
   авторитетные версии сервера. Durable integration идёт через NATS JetStream.
5. Игровые правила — неизменяемые версионированные пакеты. Формулы исполняются
   детерминированным sandboxed engine, а не произвольным JavaScript/C#.
6. Карты и производные медиа раздаются напрямую из S3-compatible object storage
   через CDN; API не проксирует тяжёлые файлы.
7. Цель первой production-версии — 1 000 одновременных пользователей в одном
   регионе с горизонтальным масштабированием до 10 000 без смены доменной модели.

## Что эта документация не утверждает

- Оценки нагрузки — capacity hypothesis до получения production-телеметрии.
- Цены — ориентиры без зарплат, налогов и лицензий на коммерческий контент.
- SLA становится договорным обязательством только после юридического утверждения;
  до этого в документе приведены рекомендуемые цели.
- Это архитектурная, а не юридическая консультация. Перед публикацией D&D-контента
  необходима проверка состава SRD, атрибуции, названия продукта и товарных знаков.

## Правило актуализации

Изменение границы владения данными, публичного контракта, семантики события,
SLO или правила совместимости должно оформляться ADR и обновлять затронутые
страницы. API описывается в OpenAPI, события — в AsyncAPI/JSON Schema; Markdown
фиксирует намерение и границы, а генерируемые спецификации становятся
машиночитаемым контрактом.
