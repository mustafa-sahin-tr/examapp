-- Creates per-service databases on first boot.
-- NOTE: Runs only when the Postgres data volume is empty.

\set ON_ERROR_STOP on

-- `CREATE DATABASE` cannot be executed inside a transaction/function/DO block.
-- Use psql's \gexec to conditionally execute CREATE DATABASE statements.

SELECT format('CREATE DATABASE %I;', 'worksheet_v2')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'worksheet_v2');
\gexec

SELECT format('CREATE DATABASE %I;', 'auth_db')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'auth_db');
\gexec

SELECT format('CREATE DATABASE %I;', 'badge')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'badge');
\gexec

SELECT format('CREATE DATABASE %I;', 'catalog')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'catalog');
\gexec

SELECT format('CREATE DATABASE %I;', 'finance_db')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'finance_db');
\gexec

SELECT format('CREATE DATABASE %I;', 'keycloak')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'keycloak');
\gexec
