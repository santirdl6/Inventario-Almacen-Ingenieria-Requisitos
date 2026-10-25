#!/bin/sh
set -eu

# Ejecutar desde la raíz. Usa la misma configuración .env/entorno que docker compose up.
docker compose config --quiet
docker info > /dev/null
docker compose up --detach --build --wait --wait-timeout 300
docker compose exec -T app curl --fail --silent http://localhost:8080/api/v1/health
docker compose exec -T app curl --fail --silent --output /dev/null http://localhost:8080/comprobacion/ruta-spa

sql() {
  docker compose exec -T sqlserver sh -c '
    export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d "$INVENTORY_DATABASE" -Q "$1"
  ' sh "$1"
}

sql "
  IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NULL THROW 51000, 'Falta el historial de migraciones.', 1;
  IF OBJECT_ID(N'dbo.__ContainerPersistenceCheck') IS NULL
    CREATE TABLE dbo.__ContainerPersistenceCheck (Id int NOT NULL PRIMARY KEY);
  IF NOT EXISTS (SELECT 1 FROM dbo.__ContainerPersistenceCheck WHERE Id = 1)
    INSERT INTO dbo.__ContainerPersistenceCheck (Id) VALUES (1);
"

# Fuerza contenedores nuevos sin borrar sus volúmenes, incluido SQL Server.
docker compose stop app sqlserver
docker compose up --detach --force-recreate --wait --wait-timeout 300
docker compose exec -T app curl --fail --silent http://localhost:8080/api/v1/health
sql "
  IF NOT EXISTS (SELECT 1 FROM dbo.__ContainerPersistenceCheck WHERE Id = 1)
    THROW 51001, 'Se ha perdido el dato persistido tras recrear contenedores.', 1;
  DROP TABLE dbo.__ContainerPersistenceCheck;
"

printf '\nArranque, migración, ruta SPA y persistencia comprobados.\n'
