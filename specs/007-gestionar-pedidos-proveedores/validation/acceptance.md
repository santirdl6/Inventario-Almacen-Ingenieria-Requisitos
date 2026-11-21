# Matriz de aceptación HU-07

**Estado**: entrega funcional local cerrada el 2026-11-21. SC-007 permanece abierto:
el cierre del corte de implementación no es aceptación integral ni puesta en producción.
[Regresión integrada](integration.md) y [resultados de rendimiento](performance.md).

| Alcance | Evidencia y resultado |
|---|---|
| FR-001–005, Q07-01/02 | [US1](us1.md): alta, edición, precisión/unidades, líneas estables, errores sin cambios parciales. |
| FR-006–008, Q07-07/08, INT-HU07-01/02 | [US1](us1.md) y [US2](us2.md): escritores reales, instantáneas, primera referencia y ambos órdenes de carrera con maestros. |
| FR-009–013, Q07-03/06 | [US2](us2.md): revisión y cambio de contacto, congelación, 202 histórico y aceptación con el documento original. |
| FR-014–017, Q07-04/05/06/09 | [US2](us2.md), [US4](us4.md): progreso, lease, reinicio, fallo conocido y retry sobre la misma entrega. |
| FR-018–020, FR-022, Q07-11/12 | [US3](us3.md): filtros SNAPSHOT, detalle coherente, proyección de recibido y ETag; compras no modifica stock. |
| FR-021, SC-006 | Lector/contrato verificados; recepción real y V-02/V-06 pendientes en [HU-01](hu01-handoff.md). |
| FR-023–028, Q07-05/06/10 | [US4](us4.md), [US5](us5.md): permisos, autoría, recuperación, fallo de commit y restricciones de ejecución SQL. |
| FR-029/030, Q07-02/03/05/11 | [US1](us1.md), [US2](us2.md), [US3](us3.md), [regresión](integration.md): etiquetas/teclado, errores, guardado confirmado y seguimiento. |
| SC-001/002 | Casos válidos e inválidos del alcance local: [US1](us1.md), [US2](us2.md), [US5](us5.md). |
| SC-003/004 | Correo, recuperación y concurrencia: [US2](us2.md), [US4](us4.md), [US5](us5.md). Una copia SMTP posible no crea otro pedido; aceptación no implica entrega ni lectura. |
| SC-005 | Evidencia INT-HU07-01/02 con escritores reales y SQL: [US1](us1.md), [US2](us2.md). |
| SC-007 | [Rendimiento](performance.md): FAIL; 25 sesiones, muestra suficiente, p95 UI de 1,25 s en consulta, 3,65 s en alta y 3,60 s en corrección. T055 abierta para guardados. |
| SC-008 / H5-03 | [Guion preparado](h5-handoff.md); ejecución con compradores reales pendiente. |
| V-14 / H5-02 | [Restauración técnica aprobada](recovery-v14.md); ejercicio operativo y aprobación de RPO/RTO pendientes. |

Revisión [A+B](shared-review.md) realizada según confirmación del usuario. La evidencia está vinculada a
[tareas](../tasks.md) y [guía de ejecución](../quickstart.md).
Los dos [ítems receptores INT-HU07](../../001-reparto-specs-inventario/integration-tasks.md#hu-07--compras--persona-a)
se cierran por las pruebas con escritores reales: `PurchasingMasterDataIntegrationTests`
para crear/retirar y carreras con maestros, y `PurchaseOrderSendConcurrencyTests` para
la primera solicitud frente a desactivación de proveedor/artículo, en ambos órdenes.
La marca de esos ítems no acredita rendimiento, revisión A+B, HU-01 ni H5.
