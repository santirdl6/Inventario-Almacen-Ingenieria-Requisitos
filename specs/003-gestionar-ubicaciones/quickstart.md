# Validación: HU-02 — Ubicaciones

**Base**: [guía común](../../.specify/memory/project/quickstart.md) (arranque Compose, SQL
de pruebas, datos comunes §5, V-01 y V-03). Esta guía solo añade los pasos de HU-02.
Contratos: [API](contracts/locations-api.md), [UI](contracts/locations-ui.md);
reglas: [data-model.md](data-model.md). Nada de lo descrito aquí está ejecutado todavía.

**Revisión 2026-11-19**: base integrada de A; L-11 pasa a «se impide»; L-13 y L-14 se
retiran (cubiertos por `StockCoreTests` y `OperationProtocolTests`); E2E desbloqueado con el
login de A, solo para el recorrido principal; rendimiento y operarios fuera de esta entrega.

## Prerrequisitos

1. Rama `b/hu-02-ubicaciones-v2` (núcleo de stock y frontend de A integrados).
2. Migración `HU02_Locations` aplicada; antes, comprobar que no hay combinaciones
   pasillo/estantería/nivel repetidas en bases existentes (la migración falla
   explícitamente si las hay).
3. `dotnet ef migrations has-pending-model-changes` sin cambios pendientes.

## Pruebas automatizadas backend

Contenedor del SDK sobre la red de Compose, como en la guía común:

```bash
docker compose up -d sqlserver
docker run --rm --network inventario_default -v "$PWD/backend:/src" -w /src \
  -e Testing__SqlHost=sqlserver -e 'Testing__SqlAdminPassword=LocalSqlAdmin_2026!' \
  mcr.microsoft.com/dotnet/sdk:10.0 sh -c 'dotnet tool restore && dotnet test Inventario.slnx --nologo'
```

Resultado esperado: las 88 pruebas previas siguen pasando y las de `Locations/` cubren la
matriz siguiente. Repetir tres veces las de concurrencia.

| Caso | Preparación y acción | Resultado esperado | Spec |
|------|----------------------|--------------------|------|
| L-01 | Alta REC-01 (Receiving), P01-E01-N01, P01-E01-N02; búsqueda por prefijo y filtros. | 201, activas, vacías, auditoría `Create` con entidad `Location`, sin movimientos; la búsqueda solo devuelve coincidencias. | US1.1, US1.5, FR-001, FR-004, FR-005, FR-015 |
| L-02 | Alta ` p01-e01-n01 ` frente a un código existente y uno inactivo; otro código con P01/E01/N01. | 409 `DuplicateCode` / `DuplicateLocationPlace` con sus campos; nada creado. | US1.2–1.3, FR-002–003 |
| L-03 | Alta con pasillo «   » en tipo Receiving; longitudes excesivas; `page=0`. | 422 con `errors.aisle` / longitud / `errors.page`; nada creado. | US1.4, FR-003 |
| L-04 | 8 altas simultáneas con la misma clave. | Una ubicación; respuestas idénticas. | US5.3, FR-018 |
| L-05 | Recibir con el núcleo 2 y 4 de ART-01/L-001 en N01/N02; consultar el artículo y N02. | Buckets 2 y 4, total 6 desde StockBalances, igual al diario; N02 lista 4 UD. | US2.1, US3.1, SC-003 |
| L-06 | Dos lotes en la misma ubicación; artículo sin existencias; ubicación inactiva con historial; `pageSize=1`. | Lotes por separado sin duplicar total; vacío sin error; inactiva visible con estado; total de todo el artículo. | US2.2–2.5, FR-007 |
| L-07 | Corregir nivel sin movimientos; intentar lo mismo en N01 tras recibir y tras dejarla a cero. | Primero 200 + auditoría `Update`; después 409 `LocationIdentityLocked` en ambos casos. | US4.1–4.2, FR-011 |
| L-08 | Desactivar N02 con existencias y con reserva viva; vaciarla; desactivar; reactivar. | 409 `LocationNotEmpty`; después 200 `Deactivate` y `Reactivate`; Receive/Reserve rechazados mientras inactiva; sigue en las consultas. | US4.3–4.5, FR-012–013 |
| L-09 | Desactivación y Receive simultáneos sobre ubicación vacía (20 repeticiones). | Siempre uno de los dos resultados válidos; nunca saldo en ubicación inactiva. | US5.4, FR-016, SC-004 |
| L-10 | Dos PUT con el mismo ETag; PUT sin If-Match; replay tras éxito con ETag ya obsoleto; misma clave con otro cuerpo. | 200 y 412 `VersionConflict`; 428; replay devuelve el primer resultado; 409 `IdempotencyConflict`. | US5.2–5.3, FR-017–018 |
| L-11 | Desactivar la única Receiving; pasarla a Storage; con dos Receiving, retirarlas a la vez. | 409 `NoActiveReceivingLeft` sin cambios en ambos; en paralelo, exactamente una 200 y otra 409. | US4.7, FR-021, SC-004 |
| L-12 | Cuenta GestorMaestros (solo lectura) intenta POST/PUT; Administrador (sin `Inventory.Read`) consulta; sin sesión; sin antiforgery; DELETE. | 403; 403; 401; 400; 405; consulta del lector permitida. | US5.1, FR-010, FR-014 |
| L-15 | `FailAfterSaveInterceptor` armado en un alta y en una desactivación; después, repetir con la misma clave. | Ni ubicación ni cambio, ni auditoría, ni `OperationResult`; el reintento confirma un solo efecto con una sola auditoría. | FR-015, FR-018, SC-007 |

## Pruebas frontend

Directorio `frontend/`, con la configuración de pruebas de A:

```bash
npm test
npm run build
```

Vitest/TestBed: validación de formulario y conservación de datos tras 422/409; mensaje de
412 con «Ver datos vigentes»; misma clave tras error de red; diálogo de desactivación que
impide retirar la última zona de recepción; textos de estado sin depender del color; foco
tras error; botones de gestión ocultos sin `Locations.Manage`.

## E2E (recorrido principal)

```bash
npm run test:e2e -- e2e/locations.spec.ts
```

Con el fixture de A (`master-data.fixture.ts`, entorno dedicado y cuenta Operario):
iniciar sesión → `/ubicaciones` → crear REC-01 y P01-E01-N01 → error de duplicado sin
perder lo escrito → buscar → abrir el detalle vacío → desactivar con revisión → reactivar;
todo por teclado. Con la cuenta GestorMaestros, la lista se ve y no hay botones de gestión.
Las existencias no se preparan por la interfaz (HU-01 no tiene recepciones HTTP todavía):
L-05 se demuestra por API.

## Evidencia

Registrar aquí fecha, entorno, número de pruebas y resultado de cada ejecución completa.

| Fecha (UTC) | Entorno | Ejecución | Resultado |
|-------------|---------|-----------|-----------|
| 2026-11-19 10:42 | `mcr.microsoft.com/dotnet/sdk:10.0` en la red `inventario_default`, SQL Server 2025 de Compose; rama `b/hu-02-ubicaciones-v2` | Suite backend completa, 3 veces seguidas (compilación con avisos como errores: 0 avisos) | 110/110 las tres veces: 88 previas + 22 de `Locations/` (L-01–L-12 y L-15) |
| 2026-11-19 11:07 | Igual; frontend con Node 26 y la configuración de pruebas de A (Vitest 5.1.0, jsdom 31.0.0) | Verificación final (T067): suite backend, `has-pending-model-changes`, `npm test`, `npm run build` | Backend 110/110; sin cambios de modelo pendientes; frontend 92/92; build de producción sin avisos |
| 2026-11-19 11:05 | Proyecto Compose dedicado `inventario-hu08-e2e` (8082), Chromium de Playwright 1.65.0 | `e2e/locations.spec.ts` (T063) y `e2e/environment.spec.ts` de A | 2/2 y 3/3 |

## Prueba manual

El arranque normal (`docker compose up`, http://localhost:8081) solo crea el administrador,
que no tiene `Inventory.Read`, y no hay API de usuarios. Para probar HU-02 a mano con una
cuenta Operario se puede reutilizar el entorno dedicado de E2E de A sin que se detenga:

```bash
cd frontend
HU08_E2E_KEEP_RUNNING=1 npm run test:e2e -- e2e/environment.spec.ts
```

y preparar después tres cuentas propias con el auxiliar de A (formato obligatorio
`<guid sin guiones>.<rol en minúsculas>@hu08.e2e.test`, las tres a la vez), desde la raíz:

```bash
RUN=$(python -c "import uuid; print(uuid.uuid4().hex)"); PASS='Manual_2026!aA9'
USERS="[{\"email\":\"$RUN.operario@hu08.e2e.test\",\"password\":\"$PASS\",\"role\":\"Operario\"},
{\"email\":\"$RUN.gestormaestros@hu08.e2e.test\",\"password\":\"$PASS\",\"role\":\"GestorMaestros\"},
{\"email\":\"$RUN.administrador@hu08.e2e.test\",\"password\":\"$PASS\",\"role\":\"Administrador\"}]"
DB_NAME=InventoryHu08E2E APP_PORT=8082 MAILPIT_PORT=8026 HU08_E2E_PROVISION=1 \
HU08_E2E_DATABASE=InventoryHu08E2E HU08_E2E_USERS_JSON="$USERS" \
docker compose --project-name inventario-hu08-e2e --profile test run --no-deps --rm \
  --volume "$PWD:/workspace" --workdir /workspace \
  --env HU08_E2E_PROVISION --env HU08_E2E_DATABASE --env HU08_E2E_USERS_JSON \
  --entrypoint sh tests -c 'dotnet restore backend/Inventario.slnx --force && dotnet run --file scripts/provision-e2e-users.cs -- provision'
echo "$RUN.operario@hu08.e2e.test / $PASS"
```

En Git Bash de Windows, anteponer `MSYS_NO_PATHCONV=1` al `docker compose` y montar
`"$(cygpath -w $PWD):/workspace"`. Receta comprobada el 2026-11-19: «Tres cuentas E2E
preparadas», login de Operario 200 y `GET /api/v1/locations` 200.

Abrir http://localhost:8082/ubicaciones e iniciar sesión con la cuenta Operario (alta,
búsqueda, detalle, corrección, desactivar/reactivar y «Localizar un artículo»). La cuenta
GestorMaestros sirve para comprobar la consulta sin gestión. Para detener el entorno:
`docker compose --project-name inventario-hu08-e2e stop`.

## Fuera de alcance de esta entrega

Desviación registrada en [plan.md § Complexity Tracking](plan.md#complexity-tracking).
HU-02 no se da por aceptada hasta aportar:

- **Rendimiento (SC-005)**: con 2.000 ubicaciones, 10.000 artículos y saldos sintéticos
  generados con el núcleo, 25 sesiones concurrentes contra `GET /locations?search=` y
  `GET /articles/{id}/locations`; p95 ≤ 2 s; localizar un artículo en < 30 s desde el inicio.
- **Validación con operarios (SC-006)**: al menos tres operarios completan alta,
  corrección de un campo inválido, localización de un artículo repartido y desactivación
  de una ubicación vacía; documentar participantes, resultados, incidencias y correcciones.

## Pendiente por dependencias

- Conteo abierto (FR-012, US3.3, US4.3, SC-004) con HU-04 (T034, bloqueada; `inOpenCount`
  es `false` y la regla de conteo abierto no se ha demostrado).
- Revisión de A (T001) y cierre documental en la base común (T068), pendientes.
- Reservas reales de pedidos con HU-03/HU-05; mientras, reservas con el núcleo.
- Integración con recepciones reales (V-03) en H3.
