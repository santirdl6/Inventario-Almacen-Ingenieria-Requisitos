# /speckit-converge — iteración 2

**Fecha**: 2026-11-19 · **Rama**: `b/hu-02-ubicaciones-v2` (`da17931`, tras la iteración 1)
**Fuentes de intención**: [spec.md](../spec.md), [plan.md](../plan.md), [tasks.md](../tasks.md),
contratos ([API](../contracts/locations-api.md), [UI](../contracts/locations-ui.md)) y
[constitución](../../../.specify/memory/constitution.md).
**Resultado**: `tasks_appended` — 2 hallazgos de verificación, añadidos como «Phase 17: Convergence» (T074–T075).

## Alcance evaluado

El mismo que en la [iteración 1](converge-1.md), con los cambios de T070–T073. Además de
volver a contrastar cada requisito, esta iteración revisa que cada obligación marcada como
hecha en `tasks.md` tenga una prueba que la ejercite, no solo código.

## Desviaciones ya registradas (no son brechas nuevas)

SC-005 y SC-006 (fuera de esta entrega, plan § Complexity Tracking), T034 (conteos,
bloqueada por HU-04), T001 (revisión de A) y T068 (cierre documental tras la aprobación de
A). Sin cambios respecto a la iteración 1.

## Comprobación de los hallazgos de la iteración 1

| Hallazgo | Estado | Evidencia |
|----------|--------|-----------|
| F1 (FR-002, US1.2–1.3) | Resuelto | La lista lee `search`, `status` y `kind` de la URL; spec de lista |
| F2 (FR-008, US3.1) | Resuelto | Paginación del contenido en el detalle; spec del detalle |
| F3 (US4.3) | Resuelto | Enlace al contenido en `LocationNotEmpty`/`CountScopeFrozen`; spec de edición |
| F4 (US2.4) | Resuelto | Aserción del artículo inactivo en `ArticleLocationsTests` |

## Verificación por requisito

Sin cambios respecto a la iteración 1 salvo lo resuelto: FR-001…FR-021 se cumplen (FR-012 y
SC-004 salvo conteos, T034); US1…US5 se cumplen; SC-001, SC-002, SC-003 y SC-007 se cumplen;
SC-005 y SC-006 son desviaciones registradas; constitución sin violaciones.

## Hallazgos

| ID | Tipo | Severidad | Fuente | Evidencia | Trabajo pendiente |
|----|------|-----------|--------|-----------|-------------------|
| F5 | partial | LOW | Contrato API (`GET /locations/{id}` → 404 si no existe), T022 | El endpoint devuelve `ApiProblems.Status(404)`, pero ninguna prueba de `Locations/` lo ejercita (solo el 404 de `/stock` y de `/articles/{id}/locations`). | Prueba del 404 del detalle. |
| F6 | partial | LOW | Contrato UI § Accesibilidad («foco … al abrir/cerrar diálogos»), T058 | `location-detail.page.ts` devuelve el foco al botón de estado al cerrar o confirmar el diálogo, pero ninguna spec lo comprueba. | Prueba de la vuelta del foco. |

## Métricas

- Requisitos y escenarios comprobados: 21 FR, 7 SC, 25 escenarios, 9 casos límite; 4 hallazgos previos revisados.
- Decisiones del plan comprobadas: 13. Principios de la constitución: 5 + gobernanza.
- Hallazgos por tipo: missing 0 · partial 2 · contradicts 0 · unrequested 0.
- Hallazgos por severidad: CRITICAL 0 · HIGH 0 · MEDIUM 0 · LOW 2.

## Resolución (`/speckit-implement` de T074–T075)

| Tarea | Hallazgo | Verificación |
|-------|----------|--------------|
| T074 | F5 | `LocationsCreateTests.L03…`: `GET /api/v1/locations/{id}` inexistente → 404, `application/problem+json`, `code` NotFound. |
| T075 | F6 | `location-detail.page.spec.ts`: abrir el diálogo y cancelar devuelve el foco al botón «Desactivar», sin enviar nada. |

Pruebas tras la iteración: backend 111/111, frontend 97/97, build de producción sin avisos.
