-- Crea la base y los roles de OtaLink (entorno de desarrollo).
-- Uso (en la VM): ver docs/DATABASE.md
--   sudo -u postgres psql -v ON_ERROR_STOP=1 \
--     -v mig_pw="$(sudo cat /etc/otalink/db-migrator-password)" \
--     -v app_pw="$(sudo cat /etc/otalink/db-app-password)" \
--     -f 01-create-database.sql
-- Las contraseñas llegan por variables de psql; no se guardan en este archivo.

CREATE ROLE otalink_migrator LOGIN PASSWORD :'mig_pw';
CREATE ROLE otalink_app      LOGIN PASSWORD :'app_pw';

CREATE DATABASE otalink_dev OWNER otalink_migrator;

-- Nadie fuera de estos roles puede conectarse a la base.
REVOKE CONNECT ON DATABASE otalink_dev FROM PUBLIC;
GRANT  CONNECT ON DATABASE otalink_dev TO otalink_app;

\connect otalink_dev

-- El esquema public pertenece al migrador; la app solo lo usa.
ALTER SCHEMA public OWNER TO otalink_migrator;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO otalink_app;

-- Lo que cree el migrador en el futuro queda accesible a la app solo en modo DML.
ALTER DEFAULT PRIVILEGES FOR ROLE otalink_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO otalink_app;
ALTER DEFAULT PRIVILEGES FOR ROLE otalink_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO otalink_app;
