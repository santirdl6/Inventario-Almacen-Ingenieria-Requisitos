# Implementation Plan: HU-06 — Alertas de stock mínimo

**Feature**: `009-configurar-alertas-stock-minimo` · **Rama Git**: `a/hu08-cierre-coordinacion`

**Fecha**: 2026-11-21 · **Spec**: [HU-06](spec.md) · Persona A · Hito H4.

**Estado**: implementación y validación funcional local completadas. El ensayo SC-004 y el registro verificable de la revisión A+B siguen pendientes.
La consulta usa `Alerts.Read` y la preselección editable en compras fue confirmada por el usuario.

## Summary

Reutilizar la política por artículo y evaluar el disponible del inventario para mantener
un episodio de alerta interno y una sugerencia de reposición. El evaluador debe participar
en la transacción del escritor de stock o del cambio de política, sin crear compras por sí solo.
La revisión y escritura se basan en los contratos compartidos existentes.

**Contexto obligatorio**: [constitución](../../.specify/memory/constitution.md),
[plan global](../../.specify/memory/project-plan.md), [reglas](../../.specify/memory/project/research.md),
[modelo](../../.specify/memory/project/data-model.md), [HTTP](../../.specify/memory/project/contracts/api.md),
[stock](../../.specify/memory/project/contracts/stock-operations.md),
[UI](../../.specify/memory/project/contracts/ui.md), [validación común](../../.specify/memory/project/quickstart.md),
[HU-07](../007-gestionar-pedidos-proveedores/spec.md) y
[INT-HU06-01](../001-reparto-specs-inventario/integration-tasks.md#hu-06--mínimos-y-alertas--persona-a).

## Technical Context

| Aspecto | Base existente |
|---|---|
| Lenguajes | C# 14/.NET 10, TypeScript 6, Node 24 para frontend. |
| Dependencias | ASP.NET Core/EF Core/Identity 10.0.13, Angular 22.2.3 y PrimeNG 22.1.4. |
| Almacenamiento | SQL Server 2025, un contexto y cadena EF; cantidades decimal(18,3), ETag fuerte y resultado común de operación. |
| Pruebas | xUnit sobre SQL real aislado, Vitest, Playwright y validación operativa separada. |
| Alcance | Política y alerta por artículo, bandeja interna y navegación a compras; no reservas propias ni pedido automático. |
| Objetivos | Consulta/confirmación bajo el objetivo global y evaluación medida conforme SC-004, sin atribuir resultados no obtenidos. |

## Constitution Check

**Gate previo y posterior: PASS en diseño**, no aprobación de implementación. La spec y
contratos locales se alinearon con el saldo no negativo y el cálculo global; se mantuvo
`Alerts.Read` y se adoptó `/compras/nuevo` con propuesta editable por decisión del usuario.
Se conservaron permisos actuales, resultado idempotente y ausencia de compras automáticas.

| Principio | Evidencia y control previsto |
|---|---|
| I y II | La alerta no altera ni inventa stock o ubicaciones; el disponible procede del saldo común. V-11 y reconciliación de diferencias. |
| III | Estados textuales, cantidades/unidad y revisión humana en compras; accesibilidad y validación operativa donde corresponda. |
| IV | Episodios históricos conservados; actor, antes/después y resultado para el cambio de política. |
| V | Evaluación en la transacción del escritor bajo lock del artículo; unicidad, rollback, replay, concurrencia y lectura consistente. H5 conserva restauración operativa. |
| Gobernanza | INT-HU06-01 y revisión A+B antes de integrar cambios comunes; no declarar aprobados los controles por este diseño. |

## Project Structure

### Documentación

El directorio contiene [spec](spec.md), [research](research.md), [modelo local](data-model.md),
[HTTP](contracts/api.md), [UI](contracts/ui.md), [integración](contracts/integration.md),
[quickstart](quickstart.md) y este plan. `tasks.md` se generará en la siguiente fase.

### Source Code (repository root)

| Área | Destino previsto |
|---|---|
| Backend | `backend/src/Inventario.Api/Features/Replenishment/`, `Domain/Stock/`, configuraciones y migraciones compartidas. |
| Integración | `Features/Stock/Core/StockCore.cs`, `Features/MasterData/Commands/ArticleLifecycleCoordinator.cs` y único ejecutor transaccional. |
| Frontend | `frontend/src/app/features/` y componentes comunes de navegación, permisos y recuperación. |
| Validación | `backend/tests/Inventario.IntegrationTests/`, Vitest y `frontend/e2e/` con SQL/Mailpit aislados. |

## Phase 0 — Investigación y dependencias

La [investigación](research.md) propone índice único filtrado por alerta activa y
evaluación síncrona. La evaluación agregada
debe incluir los cambios pendientes del contexto de la misma transacción; una consulta SQL
que ignore saldos aún no guardados no demuestra correspondencia. El coordinador
`IArticleStateEffects` existe, pero INT-HU06-01 exige su implementación real y pruebas de
desactivar/reactivar, rollback y efectos de escritores. No usar un interceptor genérico que
evalúe operaciones ajenas sin un punto de integración explícito y verificable.

## Phase 1 — Diseño y secuencia

1. Crear entidad `StockAlert`, migración aditiva e índice activo único; añadir cobertura
   de referencias y manejo explícito de borrado de artículo con historial de alertas.
2. Implementar política versionada con el ejecutor y evaluación común sobre estado
   transaccional resultante. Reutilizar `IArticleStateEffects` para INT-HU06-01.
3. Conectar el evaluador a cada escritor presente (Receive/Reserve/Release/Ship/corrección,
   mínimo y ciclo de vida) y entregar contrato de integración a HU-04/HU-05 pendientes.
4. Construir consulta paginada y ruta de alertas; extender el formulario HU-07 con
   propuesta opcional editable y validada, sin guardar ni enviar al navegar.
5. Comprobar V-11, concurrencia/rollback/replay, INT-HU06-01, no compra automática y
   el presupuesto SC-004 mediante el [quickstart](quickstart.md). La revisión A+B real
   es previa a integrar cambios compartidos; H5 conserva aceptación operativa.

## Complexity Tracking

No se propone ninguna excepción constitucional. La alerta y evaluación síncrona atienden
las garantías de disponibilidad, unicidad y correspondencia del proyecto.
