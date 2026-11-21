# /speckit-converge — iteración 1

**Fecha**: 2026-11-19 · **Rama**: `b/hu-02-ubicaciones-v2` (tras fusionar `b/h0-stock-service-v2`, `6f2cce9`)
**Fuentes de intención**: [spec.md](../spec.md), [plan.md](../plan.md), [tasks.md](../tasks.md),
contratos ([API](../contracts/locations-api.md), [UI](../contracts/locations-ui.md)) y
[constitución](../../../.specify/memory/constitution.md).
**Resultado**: `tasks_appended` — 4 hallazgos accionables, añadidos como «Phase 16: Convergence» (T070–T073).

## Alcance evaluado

Código en el alcance que fijan plan y tasks: `backend/src/Inventario.Api/Features/Locations/`,
`Domain/Locations/Location.cs`, migración `HU02_Locations`, los cambios en archivos de A
(`InventoryLocks`, `MasterDataProblemMapper`, `MasterDataAudit`), las pruebas de
`backend/tests/Inventario.IntegrationTests/Locations/`, `frontend/src/app/features/locations/`
y `frontend/e2e/locations.spec.ts`. Estado de partida: backend 111/111, frontend 92/92,
E2E 2/2.

## Desviaciones ya registradas (no son brechas nuevas)

| Elemento | Situación | Dónde consta |
|----------|-----------|--------------|
| SC-005 (rendimiento, localizar en < 30 s) | Fuera de esta entrega; HU-02 no se da por aceptada sin él. | plan.md § Complexity Tracking; research D-13; quickstart § Fuera de alcance |
| SC-006 (validación con operarios, constitución III) | Igual que SC-005. | Ídem |
| T034 (conteo abierto: FR-012, US3.3, US4.3, SC-004 en su parte de conteos) | Bloqueada por HU-04; `inOpenCount` es `false`. | tasks.md T034; quickstart § Pendiente |
| T001 (revisión de A de `ReceivingZonesAsync` y cambios en sus archivos) | Pendiente; bloquea la fusión, no la implementación. | tasks.md T001; plan.md § Estado de la revisión de A |
| T068 (cierre documental en la base común) | Pendiente de la aprobación de A. | tasks.md T068 |

## Verificación por requisito

| Clave | Estado | Evidencia |
|-------|--------|-----------|
| FR-001 | Cumple | `LocationCommands.CreateAsync/UpdateAsync`, `Location`; L-01, L-07 |
| FR-002 | Cumple en API; **parcial en UI** | Índice de `Code` + `DuplicateCode` (L-02). El enlace al duplicado no muestra inactivas → **F1** |
| FR-003 | Cumple | `UX_Locations_Aisle_Rack_Level`, comprobación previa que nombra la existente (L-02, carrera) |
| FR-004 | Cumple | L-01: sin `StockBalances` ni `StockEntries` |
| FR-005 | Cumple | `ListAsync` (prefijo de código o pasillo, tipo, estado, etiquetas); spec de lista |
| FR-006, FR-007 | Cumple | `ArticleLocationsAsync` (L-05/L-06); búsqueda de artículos con `GET /articles` de A (T055) |
| FR-008 | Cumple en API; **parcial en UI** | `StockAsync` paginado (L-05); el detalle solo muestra la primera página → **F2** |
| FR-009 | Cumple | Solo `StockBalances`; sin edición de cantidades |
| FR-010 | Cumple | `RequireAuthorization` + ejecutor; L-12, 403 por historia; `permissionGuard` en UI |
| FR-011 | Cumple | `LocationIdentityLocked` con saldo, también a cero (L-07); campos de solo lectura en UI |
| FR-012 | Cumple salvo conteos (T034) | `LocationNotEmpty` (L-08) |
| FR-013 | Cumple | `StockCore` rechaza Receive/Reserve en inactivas (L-08); listas `status=active` por defecto |
| FR-014 | Cumple | `DELETE` → 405 (T030); sin acción Eliminar en UI |
| FR-015 | Cumple | `MasterDataAuditWriter` con `Location` (L-01, L-07, L-08, L-15) |
| FR-016 | Cumple | `location:{id}` compartido con el núcleo (L-09) |
| FR-017 | Cumple | `EntityTags`, 412/428 (L-10); panel «Ver datos vigentes» |
| FR-018 | Cumple | Ejecutor de A (L-04, L-10, L-15); `idempotent-intent` y «Comprobar» |
| FR-019 | Cumple | Etiquetas, obligatoriedad, unidades, estados con texto, foco; E2E por teclado |
| FR-020 | Cumple | Éxito tras 2xx; diálogo de revisión con consecuencia |
| FR-021 | Cumple | `ReceivingZonesAsync` + `NoActiveReceivingLeft` (L-11, concurrente); diálogo sin confirmar |
| US1.1–US1.5 | Cumple (US1.2–1.3 con **F1** en UI) | T018, T047, E2E |
| US2.1–US2.5 | Cumple; US2.4 **sin prueba del artículo inactivo** → **F4** | T024, T050 |
| US3.1–US3.3 | Cumple salvo conteos (T034); contenido largo → **F2** | T027, T054 |
| US4.1–US4.7 | US4.3 **parcial en el formulario** → **F3** | T030, T059, edit spec |
| US5.1–US5.4 | Cumple | T035, T062, dialog spec |
| SC-001, SC-002, SC-003, SC-007 | Cumple | Pruebas de API, Vitest y E2E |
| SC-004 | Cumple salvo conteos (T034) | L-08, L-09, L-11 |
| SC-005, SC-006 | Desviación registrada | Ver tabla anterior |
| Constitución I–V | Sin violaciones | III con la desviación registrada de SC-006 |
| Plan (InventoryLocks, ejecutor, auditoría, InventoryProblemException/EntityTags, frontend sobre A, idempotent-intent como excepción) | Cumple | Estructura y decisiones D-04–D-07, D-11 presentes en el código |

## Hallazgos

| ID | Tipo | Severidad | Fuente | Evidencia | Trabajo pendiente |
|----|------|-----------|--------|-----------|-------------------|
| F1 | partial | MEDIUM | FR-002, US1.2–US1.3, contrato UI («enlaza a la lista filtrada») | `location-form.page.ts` enlaza a `/ubicaciones?search=…&status=all`, pero `location-list.page.ts` solo lee `search` de la URL: el filtro vuelve a «Activas» y un duplicado inactivo no aparece. | La lista lee también `status` (y `kind`) de la URL; prueba con un duplicado inactivo. |
| F2 | partial | MEDIUM | FR-008, US3.1 | `location-detail.page.ts` pide solo la página 1 de `/stock` y no tiene paginación: con más de 25 existencias el resto no se ve. | Paginación del contenido en el detalle, con prueba. |
| F3 | partial | MEDIUM | US4.3 («se rechaza explicando la circunstancia y enlazando a su contenido») | En el formulario, un `LocationNotEmpty`/`CountScopeFrozen` al cambiar el tipo solo muestra el mensaje junto al campo, sin enlace al contenido (el diálogo sí lo enlaza). | Enlace «Ver el contenido» a `/ubicaciones/:id` en ese error, con prueba. |
| F4 | partial | LOW | US2.4, T024 («artículo inactivo … siguen apareciendo con su estado») | `ArticleLocationsTests` comprueba la ubicación inactiva pero no el artículo inactivo. El código no filtra por estado. | Añadir la aserción del artículo inactivo. |

## Métricas

- Requisitos y escenarios comprobados: 21 FR, 7 SC, 25 escenarios de aceptación, 9 casos límite.
- Decisiones del plan comprobadas: 13 (D-01–D-13).
- Principios de la constitución comprobados: 5 + revisión y gobernanza.
- Hallazgos por tipo: missing 0 · partial 4 · contradicts 0 · unrequested 0.
- Hallazgos por severidad: CRITICAL 0 · HIGH 0 · MEDIUM 3 · LOW 1.

## Siguiente paso

Implementar T070–T073 (`/speckit-implement`), volver a ejecutar las suites y repetir converge.

## Resolución (`/speckit-implement` de T070–T073)

| Tarea | Hallazgo | Cambio | Verificación |
|-------|----------|--------|--------------|
| T070 | F1 | La lista inicializa `search`, `status` y `kind` desde la URL (valores desconocidos vuelven a «Activas»/«Todos»). | `location-list.page.spec.ts`: enlace de duplicado con `status=all` muestra una inactiva; estado desconocido → `active`. |
| T071 | F2 | El detalle pagina el contenido (Anterior/Siguiente, «Página X de Y · N existencias») y conserva la página al recargar. | `location-detail.page.spec.ts`: 30 existencias → paginación y petición de la página 2; una sola página → sin paginación. |
| T072 | F3 | `LocationNotEmpty`/`CountScopeFrozen` en el formulario muestran «Ver el contenido de la ubicación» (`/ubicaciones/:id#contenido-ubicacion`). | `location-form.edit.spec.ts`: enlace con existencias y con conteo abierto. |
| T073 | F4 | Prueba: artículo inactivo con sus 2 ubicaciones y total 5.000. | `ArticleLocationsTests.L05_L06…` |

Además, al ejecutar las pruebas apareció una intermitencia ajena a estos cambios:
`operation-areas.spec.ts` («con permiso carga la página de cada área») superó una vez el
límite de 5 s al cargar cuatro módulos diferidos con la caché fría. Se le da un límite de
20 s sin tocar sus aserciones; verde con la caché borrada y en caliente.

Pruebas tras la iteración: backend 111/111, frontend 96/96 (dos ejecuciones, una con la
caché de Angular borrada), build de producción sin avisos.
