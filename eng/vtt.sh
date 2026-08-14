#!/usr/bin/env sh
set -eu

command_name="${1:-doctor}"
script_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
repository_root="$(CDPATH= cd -- "$script_dir/.." && pwd)"
compose_file="$repository_root/deploy/compose/compose.yaml"

compose() {
  if [ -f "$repository_root/.env" ]; then
    docker compose --env-file "$repository_root/.env" -f "$compose_file" "$@"
  else
    docker compose -f "$compose_file" "$@"
  fi
}

doctor() {
  for tool in dotnet node pnpm docker; do
    command -v "$tool" >/dev/null 2>&1 || {
      echo "Missing '$tool'. See docs/development/onboarding.md." >&2
      exit 1
    }
  done

  dotnet --version | grep -Eq '^10\.0\.' || {
    echo "Expected .NET SDK 10.0.x." >&2
    exit 1
  }
  node -e "const [major, minor] = process.versions.node.split('.').map(Number); if (major < 22 || major >= 25 || (major === 22 && minor < 12)) process.exit(1)"
  pnpm --version | grep -Eq '^11\.' || {
    echo "Expected pnpm 11.x." >&2
    exit 1
  }
  docker compose version
  docker info --format '{{.ServerVersion}}'
}

restore_repository() {
  dotnet restore "$repository_root/Vtt.slnx" --configfile "$repository_root/NuGet.config"
  pnpm install --frozen-lockfile
}

build_backend() {
  dotnet build "$repository_root/Vtt.slnx" --configuration Release --no-restore
}

test_backend() {
  dotnet test "$repository_root/Vtt.slnx" --configuration Release --no-build
}

test_frontend() {
  pnpm lint
  pnpm typecheck
  pnpm test
  pnpm build
}

cd "$repository_root"

case "$command_name" in
  doctor)
    doctor
    ;;
  bootstrap)
    doctor
    restore_repository
    ;;
  restore)
    restore_repository
    ;;
  build)
    build_backend
    pnpm build
    ;;
  test)
    build_backend
    test_backend
    pnpm test
    ;;
  frontend)
    test_frontend
    ;;
  verify)
    doctor
    restore_repository
    dotnet format "$repository_root/Vtt.slnx" --verify-no-changes --no-restore
    build_backend
    test_backend
    test_frontend
    pnpm format:check
    pnpm security:scan
    compose --profile "*" config --quiet
    ;;
  core-up)
    compose up --detach --wait postgres nats redis minio
    compose run --rm nats-init
    compose run --rm minio-init
    ;;
  app-smoke)
    compose --profile apps up --detach --build --wait edge web
    ;;
  apps-up)
    compose --profile apps up --detach --build --wait edge identity campaign ruleset compendium character media scene session gameplay chat-dice search web
    ;;
  observability-up)
    compose --profile apps --profile observability up --detach --build --wait edge grafana
    ;;
  dev-tools-up)
    compose --profile dev-tools up --detach --wait mailpit
    ;;
  down)
    compose --profile "*" down --remove-orphans
    ;;
  reset)
    if [ "${VTT_CONFIRM_RESET:-}" != "vtt" ]; then
      echo "Set VTT_CONFIRM_RESET=vtt to delete only the named VTT Compose volumes." >&2
      exit 1
    fi
    compose --profile "*" down --volumes --remove-orphans
    ;;
  images)
    compose --profile apps build edge identity campaign ruleset compendium character media scene session gameplay chat-dice search web
    ;;
  *)
    echo "Unknown command: $command_name" >&2
    exit 1
    ;;
esac
