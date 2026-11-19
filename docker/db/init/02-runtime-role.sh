#!/usr/bin/env bash
# Idempotent API runtime role. Does not print POSTGRES_APP_PASSWORD.
# Does not create the vector extension.
# psql does not substitute variables inside dollar quotes, so role changes go
# through format() and \gexec. If __ef_migrations_history does not exist yet,
# run this script again after the first migration so the write revoke applies.
# Default privileges cover tables and sequences created later by POSTGRES_USER.
(
  set -euo pipefail

  : "${POSTGRES_USER:?POSTGRES_USER is required}"
  : "${POSTGRES_DB:?POSTGRES_DB is required}"
  : "${POSTGRES_APP_USER:?POSTGRES_APP_USER is required}"
  : "${POSTGRES_APP_PASSWORD:?POSTGRES_APP_PASSWORD is required}"

  psql \
    --username "$POSTGRES_USER" \
    --dbname "$POSTGRES_DB" \
    -v ON_ERROR_STOP=1 \
    -q \
    --no-psqlrc <<'SQL'
\set ECHO none
\getenv app_user POSTGRES_APP_USER
\getenv app_password POSTGRES_APP_PASSWORD
\getenv admin_user POSTGRES_USER
\getenv db_name POSTGRES_DB

SELECT format(
  'CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT PASSWORD %L',
  :'app_user',
  :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_user')
\gexec

SELECT format(
  'ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT PASSWORD %L',
  :'app_user',
  :'app_password')
\gexec

SELECT format('REVOKE %I FROM %I', :'admin_user', :'app_user')
WHERE EXISTS (
  SELECT 1
  FROM pg_auth_members AS membership
  JOIN pg_roles AS granted ON granted.oid = membership.roleid
  JOIN pg_roles AS member ON member.oid = membership.member
  WHERE granted.rolname = :'admin_user'
    AND member.rolname = :'app_user')
\gexec

REVOKE CREATE ON SCHEMA public FROM :"app_user";
GRANT CONNECT ON DATABASE :"db_name" TO :"app_user";
GRANT USAGE ON SCHEMA public TO :"app_user";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"app_user";
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO :"app_user";

SELECT format(
  'REVOKE INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON TABLE public.__ef_migrations_history FROM %I',
  :'app_user')
WHERE to_regclass('public.__ef_migrations_history') IS NOT NULL
\gexec

SELECT format(
  'GRANT SELECT ON TABLE public.__ef_migrations_history TO %I',
  :'app_user')
WHERE to_regclass('public.__ef_migrations_history') IS NOT NULL
\gexec

ALTER DEFAULT PRIVILEGES FOR ROLE :"admin_user" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app_user";
ALTER DEFAULT PRIVILEGES FOR ROLE :"admin_user" IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO :"app_user";
SQL
)
