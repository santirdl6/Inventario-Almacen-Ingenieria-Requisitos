# Revisión compartida A+B — HU-01

**Estado: pendiente de revisión real de ambas personas antes de integrar.** Este documento
prepara la revisión; no registra aprobación. Base: [plan global](../../../.specify/memory/project-plan.md),
[protocolo de stock](../../../.specify/memory/project/contracts/stock-operations.md),
[integración HU-01](../contracts/integration.md) y [backlog conjunto](../../001-reparto-specs-inventario/integration-tasks.md).

| Punto | Comprobación solicitada | Evidencia existente / pendiente |
|---|---|---|
| Recepción y compra | Orden de locks compra/recepción→ubicaciones→artículos; pendiente y versión; agregación de buckets sin perder identidad de línea; rollback y replay. | [US1](us1.md); completar matriz INT-HU01-01/02 y regresión. |
| Corrección | Única vía del núcleo, `AdjustDown`, dependencia posterior conservadora, límite de la asignación y disponible, no consumir reservas, diario append-only y ETags. | [US4](us4.md); faltan pruebas de reservas, consumo y carreras. |
| Contingencia | Política compuesta tanto en ejecución/replay como en lectura, hash del lock y unicidad de comprobante, evidencia y autorización de reapertura protegida; importación no equivale a reapertura. | [US4](us4.md); completar la matriz y UI. |
| Migración compartida | Combinar snapshot con las migraciones efectivamente integradas, mantener historial y DENY; revisión conjunta de la cadena antes de integrar cambios de B. | Migraciones HU01Receiving, HU01ReceiptCorrections, HU01ReceivingEvidence y HU01CorrectionReference; probar actualización y base vacía. |
| HU-04/HU-06 | No habilitar conteos mientras un consumidor ignore la congelación; incorporar el evaluador real de alertas en la misma transacción al integrar HU-06. | La implementación actual de `IsInOpenCountAsync` retorna falso; no se considera una prueba del gate. |
| Seguridad y recuperación | Autorización por acción, actor histórico y aislamiento de intenciones después del logout. | Completar regresión de permisos, sesiones y respuestas perdidas. |

Registrar revisores reales, versión revisada, incidencias, fecha y resolución cuando se
complete la revisión. La revisión A+B previa de HU-07 no sustituye este gate de HU-01.
