#!/usr/bin/env bash
set -Eeuo pipefail

contexts=(
  identity
  campaign
  ruleset
  compendium
  character
  media
  scene
  session
  gameplay
  chatdice
  search
  engineering
)

for context in "${contexts[@]}"; do
  role_name="vtt_${context}"
  database_name="vtt_${context}"

  psql     --set=ON_ERROR_STOP=1     --set=role_name="${role_name}"     --set=role_password="${VTT_LOCAL_DB_PASSWORD}"     --username "${POSTGRES_USER}"     --dbname "${POSTGRES_DB}" <<-'SQL'
SELECT format(
  'CREATE ROLE %I LOGIN PASSWORD %L',
  :'role_name',
  :'role_password')
WHERE NOT EXISTS (
  SELECT 1
  FROM pg_catalog.pg_roles
  WHERE rolname = :'role_name')
\gexec
SQL

  if [[ "$(psql --username "${POSTGRES_USER}" --dbname "${POSTGRES_DB}" --tuples-only --no-align --command "SELECT 1 FROM pg_database WHERE datname = '${database_name}'")" != "1" ]]; then
    createdb       --username "${POSTGRES_USER}"       --owner "${role_name}"       "${database_name}"
  fi
done
