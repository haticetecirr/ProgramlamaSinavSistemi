-- ProgLab local database setup. Run by setup-local-db.ps1 through psql as the postgres admin.
-- Safe to re-run: creates what is missing, never drops or empties anything.
-- proglab      : application database
-- proglab_test : automated test database (tests work in temporary schemas inside it)
-- proglab_app  : limited role; owns only these two databases, not a superuser

\set ON_ERROR_STOP on
\getenv app_password PROGLAB_APP_PASSWORD

-- Do not touch a database that already exists and belongs to someone else.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_database d JOIN pg_roles r ON r.oid = d.datdba
        WHERE d.datname IN ('proglab', 'proglab_test') AND r.rolname <> 'proglab_app'
    ) THEN
        RAISE EXCEPTION 'A database named proglab or proglab_test already exists with another owner. Nothing was changed.';
    END IF;
END $$;

SELECT 'CREATE ROLE proglab_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'proglab_app') \gexec

SELECT format('ALTER ROLE proglab_app PASSWORD %L', :'app_password') \gexec

SELECT 'CREATE DATABASE proglab OWNER proglab_app ENCODING ''UTF8'' TEMPLATE template0'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'proglab') \gexec

SELECT 'CREATE DATABASE proglab_test OWNER proglab_app ENCODING ''UTF8'' TEMPLATE template0'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'proglab_test') \gexec

SELECT d.datname AS database, r.rolname AS owner, pg_encoding_to_char(d.encoding) AS encoding
FROM pg_database d JOIN pg_roles r ON r.oid = d.datdba
WHERE d.datname IN ('proglab', 'proglab_test')
ORDER BY d.datname;
