-- Creates per-service databases on first boot.
-- NOTE: Runs only when the Postgres data volume is empty.

DO
$$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_database WHERE datname = 'worksheet_v2') THEN
    CREATE DATABASE worksheet_v2;
  END IF;

  IF NOT EXISTS (SELECT FROM pg_database WHERE datname = 'auth_db') THEN
    CREATE DATABASE auth_db;
  END IF;

  IF NOT EXISTS (SELECT FROM pg_database WHERE datname = 'badge') THEN
    CREATE DATABASE badge;
  END IF;

  IF NOT EXISTS (SELECT FROM pg_database WHERE datname = 'catalog') THEN
    CREATE DATABASE catalog;
  END IF;

  IF NOT EXISTS (SELECT FROM pg_database WHERE datname = 'finance_db') THEN
    CREATE DATABASE finance_db;
  END IF;

  IF NOT EXISTS (SELECT FROM pg_database WHERE datname = 'keycloak') THEN
    CREATE DATABASE keycloak;
  END IF;
END
$$;
