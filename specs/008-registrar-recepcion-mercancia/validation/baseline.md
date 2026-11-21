# Línea base previa a HU-01

2026-11-21 · Rama `a/hu08-cierre-coordinacion`. HU-07 está en el corte funcional local,
junto con HU-02/HU-08 y StockCore; no se integran aquí las ramas receptoras de B.
Última migración del corte: `20261121025020_HU07ArticleLookupIndex`. Docker 29.1.3,
SQL Server 2025, .NET 10/EF 10.0.13, Angular 22.2.3. Node local 25.9.0 con advertencia
de versión no LTS; la imagen de publicación usa Node 24.

- `docker compose config --quiet`: correcto.
- SDK Docker: `dotnet ef migrations has-pending-model-changes`: sin diferencias.
- `dotnet test Inventario.slnx --configuration Release --no-restore --nologo`:
  196 aprobadas, 1 omitida (V-14 requiere el perfil de restauración), tres fallos de
  inicialización de bases en `DatabaseBootstrapper` (sesión SQL abortada). Al repetir
  `MasterDataSupplierTests|PurchasingRuntimeIntegrityTests` de forma aislada, pasaron 3/3.
  El primer pase integral no fue verde; este incidente precede a los cambios de HU-01.
- `npm test && npm run build && npm run test:e2e:types`: 139/139, compilación y tipos correctos.
