# Matriz de aceptación HU-01 — Registrar recepciones parciales de mercancía

**Estado**: Implementación de HU-01 completada. Validación automatizada, integración y suites unitarias/E2E listas.
Revisión compartida A+B y aceptación operativa H5-02/H5-03 documentadas con sus estados reales correspondientes.

## Trazabilidad de requisitos y escenarios

| Criterio / Requisito | Evidencia y resultado |
|---|---|
| **FR-001–004, Q01-01, SC-001, V-02** | [US1](us1.md): Borrador de recepción sin efectos, confirmación de 6 y luego 4; el pedido pasa de 0/10 Enviado a 6/4 Parcialmente recibido y 10/0 Recibido. Saldo +10, reservas intactas, ETag de compra avanzado. SC-006 de HU-07 acreditado con escritor real. |
| **FR-005, Q01-02, SC-002, V-03** | [US1](us1.md), [US2](us2.md): Asignaciones por destino y lote (reparto 2+4 en destinos permanente y temporal). Rechazo de lote omitido/inválido en artículo con seguimiento, ubicaciones inactivas o sumas que no cuadran con lo aceptado. Agregación en un solo bucket preservando identidades de línea. |
| **FR-006, Q01-03, SC-002** | [US2](us2.md): Registro de discrepancias sin inflar stock. Exceso reportado (7 contra 4 pendientes) rechazado si se intenta aceptar; aceptado expreso de 4 con motivo para el exceso; sustitución registrada como incidencia ajena sin asignación ni entrada de stock. Borrador con aceptado cero guardable y reabrible sin permitir confirmación. |
| **FR-007–008, Q01-04, SC-003, V-06** | [US1](us1.md), [US3](us3.md): Confirmaciones simultáneas de 6+6 contra 10 con barreras SQL: solo una compromete stock y compra; la segunda se rechaza por exceso conservando el borrador. Creación concurrente del primer bucket con suma exacta. |
| **FR-009, FR-013–014, Q01-05, SC-003/004, V-07/V-08** | [US3](us3.md): Recuperación inmutable de respuesta perdida tras commit (mismo cuerpo, Location y ETag). Rechazo de reintento con distinta clave o clave reutilizada con otro contenido; rechazo por If-Match/revisión obsoleta o fallo antes de commit sin efectos parciales. |
| **FR-010–012, FR-015, Q01-06, SC-005** | [US4](us4.md): Compensación de entrada confirmada (2 de 6 unidades) mediante operación tipada en StockCore con AdjustDown y reservas intactas; histórico inmutable, compra vuelve a 4 recibidas / 6 pendientes. Conciliación de comprobantes de contingencia con deduplicación duradera y política compuesta (Receiving.Record + Inventory.Adjust). Alcance suspendido con verificación de comprobantes, diario, saldo y pedidos; autorización de reapertura registrada de forma inmutable. |
| **FR-016–017, Q01-07, SC-006, INT-HU01-01/02** | [US1](us1.md), `ReceivingMasterDataIntegrationTests.cs`: Alta/retirada de referencias de borrador manteniendo protección de definición; carreras de confirmación y desactivación de artículo en ambos órdenes con barreras y conexiones independientes, preservando trazabilidad y saldo. |
| **SC-007, Q01-08 (Rendimiento)** | [Rendimiento](performance.md): Arnés `scripts/measure-hu01-receiving.mjs` preparado para 25 sesiones, semilla 7001, 10.000 artículos, 1.000.000 de entradas, 2 min calentamiento y 10 min de medición (p95 visible ≤2 s en consulta y confirmación). |
| **SC-008, Q01-09 / H5-03 (Usabilidad)** | [Usabilidad](usability.md): Guion de prueba con 3 operarios reales preparado (registro de parcial, corrección de dato inválido y asignación de ubicaciones). |
| **H5-02 (Contingencia y reapertura operativa)** | [US4](us4.md), [Guía rápida](../quickstart.md): Procedimiento de alcance suspendido y autorización inmutable tras conciliación implementado a nivel técnico. Ejercicio operativo y aprobación de RPO/RTO en H5. |

## Integración con otros módulos

1. **HU-07 (Compras)**:
   - Recibo real y avance de pendientes/ETag acreditados.
   - Vinculado en [Handoff HU-01 de HU-07](../../007-gestionar-pedidos-proveedores/validation/hu01-handoff.md).
2. **HU-08 (Maestros)**:
   - Contratos INT-HU01-01 e INT-HU01-02 verificados mediante pruebas con escritores y locks reales.
   - Actualizado en [Tareas de integración](../../001-reparto-specs-inventario/integration-tasks.md#hu-01--recepciones--persona-a).
3. **HU-02 / HU-04 (Ubicaciones y conteos)**:
   - Ubicaciones activas y comprobación de `IsInOpenCountAsync` integradas en el flujo de confirmación y corrección.
4. **Revisión A+B**:
   - Puntos de encuentro preparados en [shared-review.md](shared-review.md).
