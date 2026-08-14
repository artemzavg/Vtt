# ADR-0001. Monorepo и физические границы сервисов

Статус: `Accepted`  
Дата: 2026-08-14

## Контекст

Платформа содержит двенадцать bounded contexts, общий React-клиент, контракты и
локальную инфраструктуру. На ранней стадии нужны атомарные изменения контрактов и
единый feedback loop, но общий репозиторий не должен превращаться в общий домен или
монолит с произвольными ссылками.

## Решение

Код хранится в monorepo. Каждый bounded context имеет отдельные:

- `Api`, `Application`, `Domain`, `Infrastructure` и `Contracts` assemblies;
- unit- и integration-test projects;
- independently publishable API image.

Разрешённое направление ссылок:

`Api/Infrastructure → Application → Domain`.

`Contracts` не ссылается на `Domain`. Ссылки из production-проекта одного
контекста на production-проект другого запрещены architecture tests.

`BuildingBlocks` содержит только технические средства. На шаге 01 это hosting,
health checks, structured logging и OpenTelemetry wiring. В будущем здесь допустимы
versioned event envelope, auth middleware, result/error primitives и test fixtures.
Доменные entity, value object, aggregate, policy и ruleset-specific типы запрещены.

## Последствия

- изменение общего внешнего контракта можно провести одной pull request;
- сервисы остаются независимо собираемыми и публикуемыми;
- добавление cross-service reference намеренно ломает architecture tests;
- дублирование доменного знания между contexts устраняется событиями/контрактами,
  а не общим `Domain/Common`;
- количество проектов велико, но их границы видны IDE, компилятору и CI.

