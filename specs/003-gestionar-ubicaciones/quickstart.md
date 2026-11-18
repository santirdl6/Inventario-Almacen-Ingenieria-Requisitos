# Validación: HU-02 — Ubicaciones

**Base**: [guía común](../../.specify/memory/project/quickstart.md) (arranque Compose, SQL
de pruebas, datos comunes §5, V-01 y V-03). Esta guía solo añade los pasos de HU-02.
Contratos: [API](contracts/locations-api.md), [UI](contracts/locations-ui.md);
reglas: [data-model.md](data-model.md). Nada de lo descrito aquí está ejecutado todavía.

## Prerrequisitos

1. Rama `b/hu-02-ubicaciones` con `b/h0-frontend-base` integrada (ruta diferida y `ApiError`).
2. Migración `HU02_Locations` aplicada por el inicializador; antes, comprobar que no hay
   combinaciones pasillo/estantería/nivel repetidas en bases existentes (la migración falla
   explícitamente si las hay).
3. `dotnet ef migrations has-pending-model-changes` sin cambios pendientes.

## Pruebas automatizadas backend

Mismo comando que el núcleo H0 (guía común, «Núcleo de stock H0»), desde la raíz:

```bash
docker compose up -d sqlserver
docker run --rm --network inventario_default -v "$PWD/backend:/src" -w /src \
  -e Testing__SqlHost=sqlserver -e 'Testing__SqlAdminPassword=LocalSqlAdmin_2026!' \
  mcr.microsoft.com/dotnet/sdk:10.0 sh -c 'dotnet tool restore && dotnet test Inventario.slnx --nologo'
```

Resultado esperado: las pruebas previas siguen pasando y `LocationsTests` cubre la matriz
siguiente. Repetir tres veces las de concurrencia, como en `StockCoreTests`.

| Caso | Preparación y acción | Resultado esperado | Spec |
|------|----------------------|--------------------|------|
| L-01 | Alta REC-01 (Receiving), P01-E01-N01, P01-E01-N02. | 201, activas, vacías, auditoría `Create`, sin movimientos. | US1.1, FR-004, FR-015 |
| L-02 | Alta ` p01-e01-n01 ` y otra con P01/E01/N01 y otro código, también contra una inactiva. | 409 `DuplicateLocationCode` / `DuplicateLocationPlace` con campo e id existente; nada creado. | US1.2–1.3, FR-002–003 |
| L-03 | Alta con pasillo «   » en tipo Receiving. | 422 con `errors.aisle`; nada creado. | US1.4 |
| L-04 | 8 altas simultáneas con la misma clave; 2 altas simultáneas del mismo código con claves distintas. | Una ubicación; replays idénticos; la segunda alta 409. | US5.3, FR-018 |
| L-05 | Recibir con `StockCore` 2 y 4 de ART-01/L-001 en N01/N02; consultar artículo y N02. | Buckets 2 y 4, total 6 desde StockBalances; N02 lista 4 UD. | US2.1, US3.1, SC-003 |
| L-06 | Dos lotes en la misma ubicación; artículo sin existencias; ubicación inactiva con historial. | Lotes por separado sin duplicar total; vacío sin error; inactiva visible con estado. | US2.2–2.4 |
| L-07 | Corregir nivel de ubicación sin movimientos; intentar lo mismo en N01 tras recibir y tras dejarla a cero. | Primero 200 + auditoría; después 409 `LocationIdentityLocked` en ambos casos. | US4.1–4.2, FR-011 |
| L-08 | Desactivar N02 con existencias; reservar y liberar con `StockCore` y reintentar con reserva viva; vaciarla con Ship; desactivar; reactivar. | 409 `LocationNotEmpty`; después 200 `Deactivate` y `Reactivate`; Receive/Reserve rechazados mientras inactiva. | US4.3–4.5, FR-012–013 |
| L-09 | Desactivación y Receive simultáneos sobre ubicación vacía (20 repeticiones). | Siempre uno de los dos resultados válidos; nunca saldo en ubicación inactiva. | US5.4, FR-016, SC-004 |
| L-10 | Dos PUT con el mismo ETag; PUT sin If-Match; replay tras éxito con ETag ya obsoleto. | 200 y 412; 428; replay devuelve el primer resultado. | US5.2, FR-017 |
| L-11 | Desactivar la única Receiving sin y con reconocimiento; dos Receiving desactivadas a la vez. | 409 `NoActiveReceivingLeft`, luego 200; en paralelo, como mucho una pasa sin aviso. | US4.7, FR-021 |
| L-12 | Cuenta GestorMaestros (solo lectura) intenta POST/PUT; sin sesión; sin antiforgery; DELETE. | 403, 401, 400, 405; consulta permitida. | US5.1, FR-010, FR-014 |
| L-13 | Equivalencia D-03: tras cada operación del núcleo, «tiene saldo» ⇔ «tiene entradas». | Coinciden para todas las ubicaciones de la prueba. | FR-011 |
| L-14 | Usuario de ejecución intenta UPDATE/DELETE en MasterDataAudit. | Error SQL 229. | FR-015 |

## Pruebas frontend

Directorio `frontend/`:

```bash
npx --no-install ng test --watch=false
npx --no-install ng build --configuration production
```

Vitest/TestBed: validación de formulario y conservación de datos tras 422/409, mensaje
de 412 con «Ver datos vigentes», reutilización de la clave tras error de red, diálogo de
desactivación con advertencia de última zona de recepción, textos de estado sin depender
del color. Playwright (L-01, L-05, L-08 por la UI) queda pendiente del login de la interfaz.

## Rendimiento (SC-005)

Con 2.000 ubicaciones, 10.000 artículos y saldos sintéticos generados con el núcleo,
25 sesiones concurrentes ejecutan `GET /locations?search=` y `GET /articles/{id}/locations`.
Objetivo: p95 ≤ 2 s. Registrar fecha, entorno y resultados; sin descartar errores.

## Validación con operarios (SC-006)

Al menos tres operarios: alta de una ubicación, corrección de un campo inválido,
localizar un artículo repartido (< 30 s desde inicio) y desactivar una ubicación vacía.
Documentar participantes, resultado, incidencias y correcciones (constitución III).

## Pendiente por dependencias

- Conteo abierto (FR-012) con HU-04; reservas reales de pedidos con HU-03/HU-05.
- Búsqueda de artículo por código o nombre (US2.1) con `GET /articles` de HU-08.
- Integración con recepciones reales (V-03) en H3.
