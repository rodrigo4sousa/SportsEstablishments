-- Run once in the Supabase SQL editor, BEFORE the first migration, connected as `postgres`.
-- See docs/deployment/runbook.md. Safe to run again (it also updates the password).
--
-- Two roles use the database:
--   postgres           owns the schema. Migrations and the prod-data commands connect as it (GitHub secret DB_OWNER_CONNECTION_STRING).
--   sportsbooking_app  the API on Render. Reads and writes data, and cannot change the schema.
-- Both connect through the session pooler (port 5432), with the user name <role>.<project-ref>.

-- 1. The application role. Replace the password here, in this one place, before running.
DO $$
DECLARE
  app_password text := 'REPLACE_WITH_A_LONG_RANDOM_PASSWORD';
BEGIN
  IF app_password = 'REPLACE_WITH_A_LONG_' || 'RANDOM_PASSWORD' THEN
    RAISE EXCEPTION 'Set app_password at the top of this script before running it.';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sportsbooking_app') THEN
    EXECUTE format('ALTER ROLE sportsbooking_app LOGIN PASSWORD %L', app_password);
  ELSE
    EXECUTE format('CREATE ROLE sportsbooking_app LOGIN PASSWORD %L', app_password);
  END IF;
END
$$;

ALTER ROLE sportsbooking_app NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;

-- 2. It may connect and use the schema, and never create anything in it.
GRANT CONNECT ON DATABASE postgres TO sportsbooking_app;
GRANT USAGE ON SCHEMA public TO sportsbooking_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM sportsbooking_app;

-- 3. Read and write what exists now, and what future migrations create as `postgres`. This covers every table of the app: Identity
--    (users, roles, user_roles, ...), bookings, outbox_messages, data_protection_keys, refresh_tokens and the rest. No TRUNCATE, no DDL.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO sportsbooking_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO sportsbooking_app;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO sportsbooking_app;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO sportsbooking_app;

-- 4. Keep the app's tables away from Supabase's Data API. New objects in `public` can be granted to these roles by default, and the tables
--    have no row level security, so without this anyone with the project's public key could read `users`.
REVOKE USAGE ON SCHEMA public FROM anon, authenticated;
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM anon, authenticated, service_role;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM anon, authenticated, service_role;
REVOKE ALL ON ALL ROUTINES IN SCHEMA public FROM anon, authenticated, service_role;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public REVOKE ALL ON TABLES FROM anon, authenticated, service_role;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public REVOKE ALL ON SEQUENCES FROM anon, authenticated, service_role;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public REVOKE ALL ON ROUTINES FROM anon, authenticated, service_role;

-- btree_gist and the exclusion constraint on bookings are created by the migrations, as `postgres`. The application role only needs the
-- table privileges above for the constraint to apply to its inserts and updates.
