# Implementation Plan: HU-01 — Registrar recepciones parciales de mercancía

**Feature**: `008-registrar-recepcion-mercancia` · **Rama Git**: `a/hu08-cierre-coordinacion`  
**Fecha**: 2026-11-21 · **Spec**: [HU-01](spec.md) · **Responsable**: Persona A · **Hito**: H3

## Summary

Registrar entregas parciales, aceptar únicamente lo que corresponde al pendiente de cada
línea de compra y localizar las cantidades por ubicación/lote. El ejecutor común confirma
recepción, diario, saldo y acumulado del pedido en una sola transacción. El diseño incluye
incidencias sin efectos en stock, compensaciones trazables y conciliación de comprobantes.

Base: [constitución](../../.specify/memory/constitution.md), [plan global](../../.specify/memory/project-plan.md),
[reglas comunes](../../.specify/memory/project/research.md), [modelo común](../../.specify/memory/project/data-model.md),
[HTTP](../../.specify/memory/project/contracts/api.md), [stock](../../.specify/memory/project/contracts/stock-operations.md),
[UI](../../.specify/memory/project/contracts/ui.md), [validación conjunta](../../.specify/memory/project/quickstart.md),
[entrega HU-07](../007-gestionar-pedidos-proveedores/validation/hu01-handoff.md) y
[backlog receptor](../001-reparto-specs-inventario/integration-tasks.md#hu-01--recepciones--persona-a).

## Technical Context

| Aspecto | Decisión |
|---|---|
| Lenguajes | C# 14/.NET 10 y TypeScript 6; Node 24 para frontend. |
| Dependencias | ASP.NET Core, EF Core, Identity y OpenAPI 10.0.13; Angular 22.2.3 y PrimeNG 22.1.4. Sin nueva dependencia de aplicación. |
| Persistencia | SQL Server 2025; un contexto, una cadena EF, UUID, rowversion de agregado, UTC y decimal(18,3). Lecturas compuestas en SNAPSHOT. |
| Pruebas | xUnit, HTTP/SQL real aislado, Vitest y Playwright; pruebas humanas separadas según SC-008. |
| Plataforma y tipo | SPA en español sobre el monolito modular existente; Docker de desarrollo, SQL x64 soportado para validación representativa. |
| Rendimiento | SC-007: p95 visible ≤2 s por separado en consulta paginada y confirmación de hasta 20 líneas, con 25 sesiones, 10.000 artículos y un millón de entradas. |
| Restricciones | Sin efectos de borrador, negativo, exceso ni duplicado. Revisión del borrador y revalidación del pendiente bajo locks; no modificar reservas ni historial original. |
| Alcance | Un almacén, compras HU-07, ubicaciones HU-02 y catálogo HU-08. Cuatro historias, 17 FR y ocho SC. |

En el corte inspeccionado existen el ejecutor, StockCore.Receive, el lector de artículos y la
escritura de HU-07. La compensación no tiene todavía acción en el núcleo; contingencia y
recepción tampoco están implementadas. El lector de conteo actual no aporta evidencia de HU-04.

## Constitution Check

| Principio | Antes de investigar | Tras el diseño |
|---|---|---|
| I. Correspondencia | Aceptación real e incidencias | Stock solo por cantidades confirmadas, corrección y contingencia con conciliación documentada. SC-001/002/005. |
| II. Ubicación | Destino para todas las unidades | Asignaciones con ubicación activa y lote aplicable; suma exacta por línea. SC-002. |
| III. Uso intuitivo | Revisión y confirmación comprensibles | Formulario accesible, errores sin pérdida, revisión y prueba con tres operarios. SC-008. |
| IV. Trazabilidad | Documento, actor, tiempos y soporte | Diario append-only y compensación enlazada a asignación original. SC-005/006. |
| V. Integridad | Commit común y comprobación de resultado | Revisión vigente, evidencia conjunta, replay, límites de compensación, carreras y recuperación. SC-003/004/005; restauración operativa H5-02. |

**Gate previo y posterior: conforme en diseño, sin excepciones.** La aprobación requiere la
[evidencia prevista](quickstart.md) y los responsables reales. Siguen pendientes la revisión
A+B de extensiones de HU-01 antes de integrar y la aceptación operativa aplicable en H5.

## Project Structure

### Documentation (this feature)

`spec.md`, `plan.md`, [research.md](research.md), [data-model.md](data-model.md),
[contracts/api.md](contracts/api.md), [contracts/ui.md](contracts/ui.md),
[contracts/integration.md](contracts/integration.md), [quickstart.md](quickstart.md).
`tasks.md` se genera en la fase siguiente.

### Source Code (repository root)

| Área | Rutas previstas |
|---|---|
| Dominio y corte vertical | `backend/src/Inventario.Api/Domain/Receiving/`, `backend/src/Inventario.Api/Features/Receiving/` (consultas, comandos y rutas). |
| Infraestructura | `backend/src/Inventario.Api/Infrastructure/Persistence/` (mappings y migración aditiva); reutilizar `StockCore`, ejecutor y resultados. Correcciones y contingencia quedan conectadas a sus rutas transversales comunes. |
| UI | `frontend/src/app/features/receiving/`; navegación, permisos y recuperación en infraestructura compartida. |
| Verificación | `backend/tests/Inventario.IntegrationTests/Receiving/`, specs Vitest de la feature y `frontend/e2e/receiving*.spec.ts`. |

**Structure Decision**: corte vertical dentro de los proyectos existentes; mantener un solo
escritor de stock y un único ejecutor. No duplicar catálogos, compras ni resultados.

## Phase 0 — Outline & Research

[Investigación](research.md) resuelve identidad de línea, revisión frente a cambios vivos,
incidencias sin stock, límites de compensación, deduplicación duradera del comprobante y
reglas de coordinación con ubicaciones, artículos, conteo y alertas. Sin dudas bloqueantes
de diseño; las extensiones compartidas quedan trazadas para revisión A+B.

## Phase 1 — Design & Contracts

1. Modelo y migración aditiva; lectores y escritor de borrador con referencias protegidas.
2. Confirmación real: revalidar bajo locks, asignar ubicación/lote y reflejar el recibido de HU-07 en el mismo commit. Verificar V-02/V-03/V-06, INT-HU01-01/02 y recuperación básica desde este corte.
3. Documentar discrepancias, reabrir/revisar borradores y presentar resultados confirmados; ensayar V-07/V-08 y permisos con API y navegador reales aislados.
4. Agregar compensación tipada y conciliación de contingencia bajo el protocolo común; probar rechazo por dependencias, cantidades y duplicados. La autorización de reapertura se registra por separado bajo revisión del alcance y se rechaza si faltan comprobantes, evidencia o resolución de diferencias. Capacidades de ajuste no se conceden al permiso de recibir.
5. Regresión de consumidores presentes, evidencia de carga SC-007 y guion con operarios SC-008. Coordinar integración pendiente de conteo/alertas con sus historias; conservar aceptación operativa H5.

Mapa de cobertura para `/speckit.tasks`: US1 FR-001–004/006–009; US2 FR-003–006; US3
FR-007/008/010/013/014/017; US4 FR-009–012/015. FR-016/017 requieren, además, **INT-HU01-01/02**.
El [quickstart](quickstart.md) vincula la evidencia por criterio.

## Complexity Tracking

Sin excepciones constitucionales. Transacción, locks, protección contra duplicados y
compensación responden a invariantes explícitas de la spec y del protocolo común.
