# /speckit-converge — iteración 3

**Fecha**: 2026-11-19 · **Rama**: `b/hu-02-ubicaciones-v2` (`24a115c`, tras la iteración 2)
**Fuentes de intención**: [spec.md](../spec.md), [plan.md](../plan.md), [tasks.md](../tasks.md),
contratos ([API](../contracts/locations-api.md), [UI](../contracts/locations-ui.md)) y
[constitución](../../../.specify/memory/constitution.md).
**Resultado**: `tasks_appended` — 2 hallazgos, añadidos como «Phase 18: Convergence» (T076–T077).

## Método

Recorrido sistemático de cada fila de la tabla de errores de `locations-api.md` y de cada
obligación de `locations-ui.md`, buscando para cada una el código que la cumple y una prueba
que la ejercite (en `Locations/`, en las specs de `features/locations/` o en la base común).

## Desviaciones ya registradas (no son brechas nuevas)

SC-005, SC-006, T034, T001 y T068, sin cambios respecto a las iteraciones 1 y 2.

## Comprobación de los hallazgos anteriores

F1–F4 (iteración 1) y F5–F6 (iteración 2) siguen resueltos; sus pruebas pasan.

## Tabla de errores del contrato API

| Estado / code | Código | Prueba |
|---------------|--------|--------|
| 400 InvalidRequest | `LocationEndpoints.Key`, `LocationWrite.Read` | L-12 |
| 403 Forbidden | `RequireAuthorization` + ejecutor | T018, T024, T030, L-12 |
| 409 DuplicateCode / DuplicateLocationPlace | Mapper de A + comprobación previa | L-02 (también la carrera) |
| 409 LocationIdentityLocked / LocationNotEmpty / NoActiveReceivingLeft | `UpdateAsync` | L-07, L-08, L-11 |
| 409 CountScopeFrozen | `UpdateAsync` vía `ILocationUsage` | Pendiente de HU-04 (T034, registrada) |
| 409 IdempotencyConflict | Ejecutor de A | L-10 |
| 412 VersionConflict / 428 PreconditionRequired | `EntityTags` | L-10 |
| 422 ValidationFailed / StateChangeMustBeSeparate | `Validate`, `Paging`, `UpdateAsync` | L-03, T030 |
| **503 LockTimeout + Retry-After** | `InventoryLocks` (5 s) + `ApiExceptionHandler` (`Retry-After: 1`) | **Ninguna en el repositorio** → **F7** |

## Obligaciones del contrato UI

Todas tienen código. Sin prueba quedan: «Longitudes máximas visibles (32 y 16)», «mensajes de
error asociados con `aria-describedby`» y «`asOf` visible» en las ubicaciones de un artículo
→ **F8**. El resto (estados con texto, foco tras error y en diálogos, teclado, revisión antes
de desactivar, resultado pendiente, enlaces) está cubierto por specs o por el E2E.

## Hallazgos

| ID | Tipo | Severidad | Fuente | Evidencia | Trabajo pendiente |
|----|------|-----------|--------|-----------|-------------------|
| F7 | partial | MEDIUM | Contrato API (503 LockTimeout con `Retry-After`), FR-016, constitución V (concurrencia) | El comportamiento existe, pero ninguna prueba retiene `location:{id}` y comprueba el 503 y la cabecera; la replanificación retiró ese caso de la antigua T036 sin sustituirlo. | Prueba: con `location:{id}` retenido en otra transacción, un PUT devuelve 503 `LockTimeout` con `Retry-After` y no cambia nada. |
| F8 | partial | LOW | Contrato UI (longitudes visibles, `aria-describedby`, `asOf` visible), FR-019 | Implementado en `location-form.page.ts` y `article-locations.page.ts`; ninguna spec lo comprueba. | Aserciones en las specs del formulario y de las ubicaciones del artículo. |

## Métricas

- Filas del contrato API revisadas: 9; obligaciones del contrato UI revisadas: 14.
- Requisitos y escenarios: 21 FR, 7 SC, 25 escenarios, 9 casos límite (sin cambios de estado).
- Hallazgos por tipo: missing 0 · partial 2 · contradicts 0 · unrequested 0.
- Hallazgos por severidad: CRITICAL 0 · HIGH 0 · MEDIUM 1 · LOW 1.

## Resolución (`/speckit-implement` de T076–T077)

| Tarea | Hallazgo | Verificación |
|-------|----------|--------------|
| T076 | F7 | `LocationsReliabilityTests.A_held_location_lock_returns_503_LockTimeout_with_RetryAfter_and_changes_nothing`: con `location:{id}` retenido por `InventoryLocks` en otra transacción, el PUT espera los 5 s y devuelve 503 `LockTimeout` con `Retry-After`; la ubicación sigue activa y sin auditoría `Deactivate`. |
| T077 | F8 | `location-form.page.spec.ts`: «Máximo 32/16 caracteres», `maxlength`, `aria-describedby` con la ayuda y con el error, «(obligatorio)» en la etiqueta. `article-locations.page.spec.ts`: «Datos a 18/11/2026 hh:mm:ss». |

Pruebas tras la iteración: backend 112/112, frontend 98/98, build de producción sin avisos.
