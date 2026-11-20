# HU-05 — /speckit-analyze, iteración 1

**Fecha**: 2026-11-20. **Autora**: Persona B. **Rama**: `b/hu-05-consultar-existencias`, tras
fusionar `origin/a/h0-identity-article` (incluye PR #6 y la corrección de A `dfc1fd1`).
**Artefactos analizados**: [spec.md](../spec.md), [plan.md](../plan.md), [tasks.md](../tasks.md),
[research.md](../research.md), [data-model.md](../data-model.md), [contratos](../contracts/) y
[quickstart.md](../quickstart.md), frente a la [constitución v1.0.0](../../../.specify/memory/constitution.md),
el [plan global](../../../.specify/memory/project-plan.md) y sus anexos.

**Fuentes externas a la rama**, leídas sin fusionar:

- PR #7 de A (`origin/a/hu08-cierre-coordinacion`, abierto, aprobado por B):
  `specs/001-reparto-specs-inventario/integration-tasks.md`, la regla de contraparte
  (`StockCore.cs`, `StockMovementConfiguration.cs`, `StockTestHarness.cs`) y los cambios de los
  documentos comunes (`research.md` R-05/R-10, `data-model.md`, `api.md`, `stock-operations.md`,
  `ui.md`, `project-plan.md`, `AGENTS.md`, plan 001).
- Código de intención idempotente ya fusionado: `frontend/src/app/core/operations/`,
  `core/http/master-data-mutations.service.ts` y `features/locations/data/`.

Las líneas citadas «(#7)» se refieren a `origin/a/hu08-cierre-coordinacion`. El resto, a esta rama.

**Análisis de solo lectura**: no se ha modificado ningún archivo de spec, plan, tareas ni
contratos. Las propuestas no añaden tareas: el tope es de 20.

---

## Resumen

| Gravedad | Hallazgos |
|----------|-----------|
| Crítica | 2 (C1, C2) |
| Alta | 4 (H1–H4) |
| Media | 6 (M1–M6) |
| Baja | 5 (L1–L5) |

**Veredicto**: **no conviene lanzar `/speckit-implement` sin resolver antes C1 y C2.**

- **C1**: el frontend reutilizaría un mecanismo de intención que A retiró por los defectos R1 y R2.
- **C2**: la preparación de existencias del E2E no cumple la trazabilidad del principio IV y,
  después de #7, el núcleo la rechazaría.

Ambas se corrigen editando tareas que ya existen (T016 y T017). Las altas solo afectan a pruebas
y coordinación.

---

## Hallazgos

| ID | Categoría | Gravedad | Ubicación | Resumen | Propuesta |
|----|-----------|----------|-----------|---------|-----------|
| C1 | Constitución V / Inconsistencia | CRÍTICA | research.md:273-279; tasks.md:303-304, 382-384; contracts/stock-ui.md:66; plan.md:171, 216-218 | D-15 y T016 reutilizan `idempotent-intent.ts` y descartan `PendingOperationStore` porque «solo admite maestros». Tras `dfc1fd1` esto ya no es cierto: el almacén admite ubicaciones (`pending-operation.store.ts:10-14`) y HU-02 abandonó esas claves por R1 (el estado pendiente se pierde al recargar) y R2 (la clave se puede reutilizar con otra identidad). Lo registra `specs/003-gestionar-ubicaciones/research.md`, «Revisión de A en PR #6». Sin cambios, HU-05 reintroduciría ambos defectos e incumpliría US5.3/FR-026 y el principio V («permitir comprobar su resultado antes de repetirla»). | Ver [C1](#c1--intención-idempotente). |
| C2 | Constitución IV | CRÍTICA | research.md:293-300 (D-17); tasks.md:331-337 (T017) | El `Receive` de preparación no indica referencia de soporte ni contraparte, ni por qué no aplican. El principio IV exige que «esa circunstancia DEBE quedar explícita». Con #7, `ApplyAsync` responde 422 y `CK_StockMovements_Counterparty` rechaza la fila, así que T017 y el E2E-01 (T019) fallarían. | En T017, pasar `SupportNotApplicableReason` («Preparación de pruebas E2E sin documento de compra») y `CounterpartyNotApplicableReason` («Recepción de prueba sin proveedor»). La prueba de T017 comprueba ambos en el movimiento `Receipt`. T017 pasa a depender de la fusión de #7, porque el parámetro no existe antes. |
| H1 | Cobertura (INT-HU05-01/02) | ALTA | tasks.md:183-191, 221-232, 258-273, 386-388; integration-tasks.md:18-27, 46-47 (#7) | Faltan del contrato de escritores: carreras alta/reserva frente a desactivación, edición de la definición y borrado (conexiones independientes, ambos órdenes); borrado rechazado con compromisos reales; reserva rechazada tras desactivar; conciliación documental. S4 (tasks.md:386-388) aplaza S-19 alegando que la suite de HU-08 lo cubre, lo que contradice «cada módulo prueba sus escritores reales, no un doble de otro módulo». | Ver [H1](#h1--cobertura-de-int-hu05-01-e-int-hu05-02). |
| H2 | Gobernanza | ALTA | tasks.md (sin IDs INT); AGENTS.md (#7) | AGENTS.md de #7 obliga a incorporar a `tasks.md` las tareas receptoras con sus IDs. Ninguna tarea cita INT-HU05-01, INT-HU05-02 ni COORD-01. | Etiquetar con su ID las tareas afectadas: INT-HU05-01 en T004, T008, T010, T011 y T012; INT-HU05-02 en T010, T012 y T014. Citar COORD-01 en T001. Marcar el checkbox del backlog solo con evidencia (integration-tasks.md:8-9). |
| H3 | Inconsistencia / orden | ALTA | plan.md:60; research.md:253-258; data-model.md:112; quickstart.md:12; tasks.md:92-95, 396 | La migración se planifica sobre `20261119163347_HU02_LocationCharacters`. Con #7 la cadena termina en `20261119171332_HU08MovementCounterparty`, y por decisión de coordinación T004 no se hace hasta fusionar #7. El grafo de dependencias no recoge ese bloqueo. | T004: «Depende de T003 y de la fusión de #7; base `HU08MovementCounterparty`». Añadir la arista #7 → T004 (y #7 → T017, C2) en el grafo. La parte de T004 que no es migración (`StockDocuments.cs`, contributor, `StockEndpoints.cs`) puede adelantarse sin generar la migración. |
| H4 | Coherencia con #7 (P-01…P-08) | ALTA | tasks.md:42-55; plan.md:231-341 (P-07 en 313-321); spec.md:339-345 | #7 ya aplica P-07 (R-10, R-05 y plan 001:114) y parte de P-01 (HU-05 crea y consulta el pedido; `Cancelled` se conserva). T001 y T002 siguen proponiendo «P-01…P-08» completos, y T002 editaría líneas que #7 ya reescribió: conflicto o texto duplicado. | Ver [la sección c](#c--coherencia-de-tasks-con-p-01p-08). |
| M1 | Inconsistencia (ETag) | MEDIA | contracts/stock-api.md:121-135; research.md:136-154 (D-07) | `GET /customer-orders/{id}` y `GET /reservations/{id}` devuelven un ETag fuerte de la `rowversion` propia, pero la representación incluye datos de otros agregados: SKU, nombre, unidad e `isActive` del artículo, y código de ubicación y de lote. Desactivar o renombrar el artículo cambia el cuerpo sin cambiar el ETag. Es la misma clase de defecto que R3 en PR #6. | Elegirlo en T001 con A. **Recomendado**: el precedente fusionado de HU-02 (`LocationEntityTags`, digest SHA-256 de representación + rowversion; `EntityTags` ya admite longitudes distintas de 8), recalculado bajo bloqueo en `RequireMatch` dentro de T011 y T013. **Alternativa**: el precedente de HU-08 (`/articles/{id}/context`: ETag solo de datos propios y datos ajenos en otra lectura). Se implementa dentro de T008, T011 y T013. |
| M2 | Inconsistencia (contraparte) | MEDIA | research.md:99 frente a 119; data-model.md:103-104 | Las dos operaciones cumplen la regla de #7, pero la contraparte es el cliente al reservar (`CustomerReference`) y la referencia del pedido al liberar (`ExternalReference`, que ya es el soporte de la reserva). Para el mismo concepto hay dos valores distintos. | En T013, `CounterpartyReference = CustomerReference`: el pedido ya está bloqueado y leído al liberar. |
| M3 | Cobertura FR-023 / SC-005 | MEDIA | tasks.md:183-191, 221-232, 258-273 | Ninguna prueba cubre «reutilizar una intención con contenido distinto DEBE rechazarse» (409 `IdempotencyConflict`). La reserva tampoco tiene prueba de replay. T014 prueba otra clave con el mismo `releaseId`, que es otro caso. | Añadir en T012: misma clave y cuerpo → replay idéntico; misma clave con otra cantidad → 409 `IdempotencyConflict`. En T014, lo mismo con otro motivo. |
| M4 | Cobertura FR-020 | MEDIA | spec.md:294-296; tasks.md:380-381 (S2) | FR-020 exige que el sistema muestre los compromisos por artículo. La API entra (T011/T012), pero la pestaña «Reservas» está en «Si hay tiempo». La lista de desviaciones de tasks.md la recoge, no la de plan.md. | Registrarla en Complexity Tracking del plan como desviación de interfaz, o aceptar que la API cubre FR-020 y decirlo así en la cobertura de tasks. |
| M5 | UX / almacén común | MEDIA | `pending-operation.store.ts:21, 34-38` | El almacén identifica cada intención por recurso. Todas las reservas son `POST /api/v1/reservations`, así que una reserva incierta bloquea cualquier otra del mismo usuario, incluso de otro pedido, hasta comprobarla. Es coherente con A («la intención pendiente bloquea otro envío»), pero el contrato UI de HU-05 no lo dice. | Documentarlo en `stock-ui.md` al aplicar C1 y mostrar el enlace «Revisar esta operación pendiente» del layout. No hace falta otra tarea. |
| M6 | Cobertura FR-016 / US3.7 | MEDIA | tasks.md:221-232 | El rechazo de reserva por artículo inactivo no se prueba (S-06 está aplazada). La congelación por conteo no se puede probar hasta HU-04 (`IsInOpenCountAsync` devuelve `false`; INT-HU04-03). | Inactivo: incluido en H1/T012. Conteo: anotar en T012 que lo cubre INT-HU04-03. |
| L1 | Desactualización | BAJA | plan.md:288-292 (P-05) | La fila 409 de `api.md:77` ya incluye los códigos de HU-02. El diff P-05 parte de la versión anterior. | Rehacer P-05 añadiendo `CustomerOrderNotOpen` después de `NoActiveReceivingLeft`. |
| L2 | Desactualización | BAJA | spec.md:339-345; plan.md:99, 224, 338-341 | La frontera HU-03/HU-05 figura como «pendiente de revisión de A». #7 la registra como «acuerdo de alcance revisado por A el 2026-11-19» (research.md R-10, #7). | Al fusionar #7, actualizar la spec y el estado de revisión del plan, anotando solo la aprobación de P-07. |
| L3 | Desactualización | BAJA | tasks.md:371, 426 (T020) | T020 dependía «preferiblemente de la fusión del PR #6», que ya está hecha. A cambió `LocationQueries.DetailAsync` (SNAPSHOT + `LocationEntityTags`). | T020 queda desbloqueada. Delegar solo `ArticleLocationsAsync` sin tocar `DetailAsync` ni el digest. Ejecutar además `LocationReviewTests`. |
| L4 | Terminología | BAJA | research.md:264 frente a tasks.md:439 | D-15 dice que se reutiliza `article-search` de HU-02. La tabla de desviaciones de tasks dice que se usa un selector propio. | Alinear D-15 con lo que dicen las tareas. |
| L5 | Desactualización | BAJA | plan.md:126 | «tasks.md # /speckit-tasks (no generado aquí)», pero tasks.md ya existe. | Retocar el comentario. |

---

## C1 · Intención idempotente

**Estado del código fusionado**:

- `MasterDataMutationsService.mutate()` (`core/http/master-data-mutations.service.ts:22-62`):
  - abre la intención en `PendingOperationStore` con actor, método, recurso, cuerpo congelado y
    ETag;
  - envía `Idempotency-Key` e `If-Match`;
  - resuelve con 2xx y con 4xx (salvo 401/403);
  - respeta `Retry-After`.
- `check()` consulta `GET /operations/{id}` y `retry()` repite la intención original.
- `PendingOperationStore` (`core/operations/pending-operation.store.ts`):
  - persiste en sessionStorage y restaura al recargar (R1);
  - filtra por `actorId` (R2);
  - al cerrar sesión retira cuerpo y ETag y conserva solo la referencia, por diseño.
- `mutationPermission()` (`:10-14`) solo reconoce maestros y `/api/v1/locations(/{id})?`.
  Los recursos de HU-05 se descartarían al restaurar (`:51`) y `begin()` lanzaría una excepción.
- `OperationStatusComponent.route()` (`operation-status.component.ts:35-40`) llevaría
  `/api/v1/customer-orders` a `/maestros/undefined/...`.
- En HU-02, `idempotent-intent.ts` solo sigue en uso por `isDefinitiveStatus`
  (`features/locations/shared/operation.ts:4`). Sus funciones de clave ya no se usan.

**Propuesta, sin tareas nuevas**:

1. **T016**: `customer-orders-api.service.ts` usa `MasterDataMutationsService.mutate()`, como
   `locations-api.service.ts:50-54`:
   - `POST /api/v1/customer-orders`;
   - `POST /api/v1/reservations` con el ETag del pedido;
   - `POST /api/v1/reservations/{id}/release` con el ETag de la reserva.

   El `releaseId` va en el cuerpo congelado de la intención, así que sobrevive a recargas y
   reintentos sin lógica propia. El formulario y los diálogos usan `pending.forResource()`,
   `check()`, `retry()` y `recovered`, como `location-form.page.ts:201-292`.
2. **T016, con ⚑ commit aparte (archivos de A)**:
   - `mutationPermission()`:
     - `/api/v1/customer-orders` → `Shipping.Record`;
     - `/api/v1/reservations` → `Reservations.Manage`;
     - `/api/v1/reservations/{uuid}/release` → `Reservations.Manage`. La expresión actual no
       admite el sufijo `/release`.
   - `OperationStatusComponent.route()`:
     - pedidos → `/existencias/pedidos/nuevo`;
     - reservas y liberaciones → `/existencias/pedidos/{orderId}`. Requiere una ruta por
       reserva o guardar el pedido en el cuerpo; elegirlo en T016.

   Con su prueba unitaria: restaurar tras recargar y no reutilizar la intención con otro
   usuario, como `location-form.review.spec.ts`. Añadir estos archivos a T001(b).
3. Retirar S3 («trasladar `idempotent-intent.ts`») de «Si hay tiempo». Corregir D-15,
   plan.md:171, 216-218 y stock-ui.md:66 para que citen el transporte común.

## H1 · Cobertura de INT-HU05-01 e INT-HU05-02

| Exigencia del backlog (#7) | Hoy | Falta |
|----------------------------|-----|-------|
| Lector autoritativo y enlace a `ArticleReferenceTracker` en el mismo bloqueo y transacción | T004 (contributor, D-13); T008 (`MarkAsync` bajo bloqueo de artículos) | Nada en diseño |
| Definición bloqueada desde la primera línea de pedido | T010: `IsDefinitionLocked` true tras S-08 | Probar que el `PUT` de unidad, lote o fracciones del artículo devuelve 409 después de la primera línea |
| Bloqueo tras retirar referencias «cuando proceda» | — | **No aplica**: HU-05 no retira líneas ni asignaciones (sin edición ni cancelación, `DENY DELETE`). Hay que hacerlo constar en T010 |
| Borrado rechazado con compromisos reales, aun sin movimientos ni saldo | — (S-19 en S4) | `DELETE /articles/{id}` con solo una línea de pedido → rechazo por referencias, sin `DependencyUnavailable` |
| Referencia/edición y referencia/borrado con conexiones y barreras independientes, en ambos órdenes | — | Alta frente a `PUT` de la definición y alta frente a `DELETE` del artículo; reserva frente a `DELETE` |
| Rechazar pedidos nuevos tras desactivar | T010: artículo inactivo → 409 | Nada |
| Rechazar reservas nuevas tras desactivar | — (S-06 en S4) | Reserva sobre artículo desactivado → 409 `ArticleInactive` |
| Carreras alta y reserva frente a desactivación | — (T012 solo hace reserva frente a reserva, V-05) | Ambos órdenes, con conexiones independientes |
| Liberar compromisos existentes tras desactivar, con permiso y motivo | T014: S-15 | Nada |
| Conciliación documental, diario y saldo | T012/T014: `AssertJournalMatchesBalanceAsync` (diario y saldo) | Documental: Σ remanente de `ReservationAllocations` = `StockBalances.Reserved` por bucket (núcleo de S-07) |
| Publicar pedido, servido, pendiente y reservas para HU-03 | T008: `ICustomerOrderReader` sin prueba (S-18 en S4) | Una aserción mínima: el lector dentro del ejecutor da el mismo pendiente y remanente que el GET |
| No sustituir `Reservation` por el efecto `Reserve` | T011 crea `Reservation` + `ReservationAllocation` | Nada |

**Incorporación sin tareas nuevas**: las carreras reutilizan el patrón de barreras de
`MasterDataLifecycleConcurrencyTests` (#7) y `StockTestHarness`, sin modificarlo.

- **T010** (INT-HU05-01/02, alta):
  - `PUT` de la definición → 409 tras la primera línea;
  - `DELETE` del artículo con una línea → rechazo por referencias;
  - carreras alta/desactivación, alta/`PUT` de la definición y alta/`DELETE`, cada una en ambos
    órdenes;
  - nota de «retirada no aplicable».
- **T012** (INT-HU05-01/02, reserva):
  - reserva con el artículo inactivo → 409;
  - carreras reserva/desactivación y reserva/`DELETE`, en ambos órdenes;
  - conciliación documental tras S-10 y V-05;
  - aserción mínima de `ICustomerOrderReader`;
  - replay y `IdempotencyConflict` (M3).
- **T014** (INT-HU05-02, liberación):
  - S-15 ya cubre liberar con el artículo inactivo;
  - añadir que tras liberarlo todo (`Released`) el `DELETE` del artículo sigue rechazado,
    porque las líneas permanecen;
  - conciliación documental al final de S-14 y tras V-08.
- **S4**: retirar S-19, S-06 y el núcleo de S-07, que quedan dentro de T010, T012 y T014. S-04,
  S-13 y S-18 completo siguen como «si hay tiempo».

**Coste estimado**: entre 4 y 6 h dentro del presupuesto de 50 h. Las carreras son
deterministas con barreras, no repeticiones como V-05.

## b · Regla de contraparte de #7

- **Regla**: `StockCore.ApplyAsync` rechaza con 422 un hecho nuevo sin `CounterpartyReference` ni
  `CounterpartyNotApplicableReason` (StockCore.cs, #7). Además existe la CHECK
  `CK_StockMovements_Counterparty`, con `WITH NOCHECK` para las filas históricas.
- **Reservar (T011)**: `CounterpartyReference = CustomerReference`. Es obligatoria, se recorta y
  admite 1–200 caracteres, igual que `StockMovement.ReferenceMaxLength = 200`. **Cumple.**
- **Liberar (T013)**: `CounterpartyReference = ExternalReference`, de 1–64 caracteres. **Cumple**,
  con la salvedad semántica de M2.
- **Huella idempotente**: HU-05 usa el DTO HTTP normalizado como `NormalizedBody` (D-05). Los
  valores de contraparte se derivan en el servidor bajo bloqueo de datos inmutables del pedido.
  Por eso la huella no cambia y la regla de #7 de «incluir el campo solo con valor» no afecta a
  HU-05.
- **Preparación E2E (T017)**: **no cumple**. Es C2.
- **Harness de pruebas**:
  - #7 cambia `StockTestHarness.Context()` para que pase por defecto
    `CounterpartyNotApplicableReason: "Operación interna de prueba sin contraparte externa"`.
  - Las existencias de T007/T010/T012/T014 preparadas con `harness.Context()` cumplen sin
    tocarlo.
  - Si `StockApiSupport.cs` (T007) construye su propio `StockCommandContext`, debe pasar el motivo.
    **Recomendación**: preparar siempre con `StockTestHarness.Context()`.
  - Antes de fusionar #7 el código compila igual, porque la firma de `Context()` no cambia y
    reservar y liberar usan `CounterpartyReference`. Solo T017 necesita el parámetro nuevo.

## c · Coherencia de tasks con P-01…P-08

| Diff | Estado tras #7 | Acción en T001/T002 |
|------|----------------|---------------------|
| P-01 | **Parcial**. #7 (data-model.md:178-183) añade que HU-05 crea y consulta, que no edita ni cancela y que `Cancelled` se conserva. | Quedan pendientes: la unicidad `(CustomerOrderId, ArticleId)`, la comparación de `ExternalReference` sin mayúsculas ni espacios y «cada cambio de reservas o servido toca la cabecera». |
| P-02 | Pendiente | Se mantiene. |
| P-03 | Pendiente. #7 solo añade el párrafo de asignación en `api.md`. | Se mantiene, más la decisión de M1 sobre el ETag. |
| P-04 | Pendiente | Se mantiene. Es opcional, porque S1 está recortada. |
| P-05 | Pendiente | Rehacer sobre la fila actual (L1). |
| P-06 | Pendiente | Se mantiene. Conviene añadir los valores de contraparte de reservar y liberar (b, M2). |
| P-07 | **Aplicado en #7** (research.md R-05 y R-10; plan 001:81 y 114) | **Retirar** de T001 y T002. Quitar `research.md` (R-10) y `specs/001…/plan.md` de la lista de archivos de T002. |
| P-08 | Pendiente (evidencia tras T014) | Se mantiene. |

T001(a) queda como «P-01 (lo pendiente), P-02…P-06 y P-08». T001(b) añade los archivos de A de
C1 y la decisión de M1. T002 no toca R-10 ni el plan 001.

## d · Plan (D-05, D-15) y tareas T015/T016 frente al código fusionado

- **D-05**: sigue siendo válido. Usa el ejecutor común y el DTO normalizado, y lo ya fusionado no
  le afecta.
- **D-15, T016 y T015**:
  - D-15 y T016 se basan en `idempotent-intent.ts` y descartan el almacén común con un motivo que
    ya no es cierto (C1).
  - T015 solo lee (`GET /stock`), así que no depende del transporte de mutaciones.
  - Tanto T015 como T016 deben usar `apiProblem()` y, para el estado 7, el componente
    `OperationStatusComponent` con `[resource]`, como hacen los formularios de maestros
    (`article-form.component.ts:30`).
- **`features/locations/data`**:
  - `locations-api.service.ts` es el modelo que conviene copiar (`mutate` + `pendingUntilEvent`).
  - `isDefinitiveStatus` sigue disponible si `shared/operation.ts` se reutiliza para clasificar
    errores.

## e · Coherencia spec ↔ plan ↔ tasks ↔ contratos

### Cobertura de requisitos

| Requisito | ¿Tarea? | Tareas | Notas |
|-----------|---------|--------|-------|
| FR-001 | Sí | T006, T015 | |
| FR-002 | Sí | T006, T007, T015 | |
| FR-003 | Sí | T006, T020 | |
| FR-004 | Sí | T006, T007 | |
| FR-005 | Parcial | T006 | S-04 aplazada. HU-05 no tiene borradores propios. |
| FR-006 | Sí | T006, T015 | |
| FR-007 | Parcial | T006, T015 | S-06 aplazada (H1 la recupera en T012). |
| FR-008 | Sí | T015 | |
| FR-009 | Sí | T008, T010, T016 | |
| FR-010 | Sí | T008, T009, T010 | |
| FR-011 | Sí | T008, T010 | |
| FR-012 | Sí | T008, T010 | |
| FR-013 | Sí | T008, T016 | |
| FR-014 | Sí | T011, T013, T012, T014 | |
| FR-015 | Sí | T011 | |
| FR-016 | Parcial | T011, T012 | M6 |
| FR-017 | Sí | T011, T013, T012, T014 | |
| FR-018 | Sí | T013, T014, T016 | |
| FR-019 | Sí | T004, T013, T014 | |
| FR-020 | Parcial | T011, T012 | Solo API (M4) |
| FR-021 | Sí | T006–T014 | |
| FR-022 | Sí | T014 (V-08) | |
| FR-023 | Parcial | T010, T014 | M3 |
| FR-024 | Parcial | T012 (V-05) | S-13 aplazada |
| FR-025 | Sí | T012, T014 | |
| FR-026 | **En riesgo** | T016 | C1 |
| FR-027 | Sí | T016 (contrato stock-ui) | |
| FR-028 | Sí | T015, T016 | |
| SC-001 a SC-005 | Sí | T007, T012, T014 | SC-005 parcial (M3) |
| SC-006, SC-007 | No | — | Desviación registrada (plan, Complexity Tracking) |

**Tareas sin requisito**: ninguna. T001 y T002 son de coordinación, T017 y T018 de preparación
del E2E y T020 cubre D-02/FR-003.

### Constitución

- **I, II**: conformes en el diseño.
- **III**: conforme con desviación registrada. SC-007 no se valida con trabajadores; el plan
  impide dar HU-05 por aceptada sin esa validación (plan.md:101-103). No es una excepción
  informal, porque la aceptación queda pendiente y no se renuncia a ella.
- **IV**: incumplimiento de diseño en D-17/T017 (C2). Reservar y liberar cumplen.
- **V**: riesgo en el frontend (C1). En el backend es conforme: ejecutor común, bloqueos,
  atomicidad y V-05/V-08.

### Métricas

- Requisitos: 28 FR y 7 SC (5 exigen trabajo construible).
- Tareas: 20.
- Cobertura: 28/28 FR con al menos una tarea (100 %). 6 son parciales y FR-026 está en riesgo.
- Ambigüedades: 0. No hay marcadores pendientes; `<ts>` en plan.md:158 es el nombre generado de
  la migración.
- Duplicaciones: 1 (P-07, ya aplicado en #7).
- Críticos: 2.

---

## Próximas acciones

1. Resolver C1 y C2 editando T016, T017, D-15, D-17 y stock-ui.md. Pasar después por
   `/speckit-analyze` (iteración 2).
2. Aplicar H1–H4 en T001, T002, T004, T010, T012 y T014, sin tareas nuevas.
3. Decidir M1 (estrategia de ETag) con A dentro de T001. M2–M6 y L1–L5 pueden corregirse en la
   misma edición.
4. T004 (migración) y T017 esperan a la fusión de #7. T003, el resto de T004, T006–T008 y T015
   pueden empezar después de los puntos 1 y 2.

Ninguno de estos cambios se ha aplicado en esta iteración: el informe es de solo lectura.

## Resolución

Correcciones aplicadas el 2026-11-20 según las decisiones de B, en tres commits de documentación y
uno de pruebas de HU-02. Siguen siendo 20 tareas (T001–T020) y los enlaces relativos de la feature
se han comprobado.

| Hallazgo | Decisión | Commit |
|----------|----------|--------|
| C1 | Transporte común de A (`MasterDataMutationsService` / `PendingOperationStore`) en D-15, el plan, stock-ui y T015/T016. Ampliación ⚑ de `mutationPermission()` y `OperationStatusComponent.route()` en T016 y T001(b). S3 retirada. | `ba47d56` |
| C2 | D-17/T017 con `SupportNotApplicableReason` y `CounterpartyNotApplicableReason` («Preparación de pruebas E2E»). T017 depende de #7. | `ba47d56` |
| H1 | Contrato de escritores, carreras en ambos órdenes, desactivación y conciliación dentro de T010, T012 y T014. «Retirar referencias» anotado como no aplicable. S4 corregido. | `81ae533` |
| H2 | Etiquetas INT-HU05-01/02 y COORD-01. | `81ae533` |
| H3 | Base `20261119171332_HU08MovementCounterparty`. T004 (migración) y T017 dependen de #7. | `81ae533` |
| H4 | T001/T002 reducidas a P-01 (lo que falta), P-02…P-06 y P-08. T002 no toca R-10 ni el plan 001. | `81ae533` |
| M1 | **Resuelto en la revisión de A, no como se propuso.** No se adoptó la recomendación del informe (digest) ni la primera decisión de B (`81ae533`: mantener D-07 con los datos ajenos como «informativos»), que dejaba el ETag fuerte igual con representaciones distintas. A pidió y B aceptó el **precedente de HU-08**: el detalle versionado del pedido y la reserva lleva solo datos propios, con la cabecera renovada en cada reserva, liberación o salida (D-07). Los datos actuales de artículos, ubicaciones y lotes van en `GET /customer-orders/{id}/context`, sin ETag y con `orderVersion` (D-11). T010 prueba que el ETag no cambia al renombrar el artículo ni al desactivar la ubicación, y sí con cada escritor. | `81ae533` (primera decisión, sustituida); revisión de A en el commit «HU-05 (B): ETag del pedido y la reserva, datos actuales en un contexto sin ETag» |
| M2 | `CustomerReference` como contraparte al reservar y al liberar. | `ba47d56` |
| M3 | Replay e `IdempotencyConflict` en T012 y T014. | `81ae533` |
| M4 | FR-020 solo en API, desviación en el plan (Complexity Tracking) y en tasks. | `81ae533` |
| M5 | Bloqueo de reservas por una reserva incierta, documentado en stock-ui. | `81ae533` |
| M6 | Regla de conteo pendiente de INT-HU04-03 (T012, desviaciones y dependencias del plan). | `81ae533` |
| L1 | P-05 parte de la fila 409 vigente. | este commit («correcciones del análisis, bajos») |
| L2 | Frontera HU-03/HU-05 aprobada por A en #7, en la spec y en el estado de revisión del plan; sin cambio normativo. | este commit |
| L3 | T020 desbloqueada, sin tocar `DetailAsync` ni `LocationEntityTags`. | este commit |
| L4 | Selector propio en D-15 y stock-ui. | `ba47d56` |
| L5 | Comentario de tasks.md en la estructura del plan. | este commit |
| Observación (Vitest) | `locations.routes.spec.ts` estabilizado en un commit aparte de HU-02. | commit siguiente, «HU-02 (B): estabiliza locations.routes.spec.ts» |

## Observación fuera del análisis

`npm test` en esta rama, antes y después del merge, da 1 fallo de 107:
`locations.routes.spec.ts`, «Localizar un artículo abre la búsqueda…», por timeout de 5 s. El
archivo pasa aislado (2/2). Es un problema de aislamiento entre archivos de prueba de HU-02, no
lo introduce la fusión, e impedirá los checkpoints de T015/T016 («`npm test` en verde») si no se
corrige.
