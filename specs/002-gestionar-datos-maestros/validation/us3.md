# US3 — Proveedores

**Fecha**: 2026-11-18. Fuentes: [spec](../spec.md), [plan](../plan.md),
[tareas](../tasks.md), [HTTP](../contracts/api.md) e [integración](../contracts/integration.md).

El catálogo de proveedores está implementado con alta, detalle, edición, búsqueda,
paginación y estados. Email e identificador fiscal son opcionales y se normalizan a null
si están vacíos. El correo no vacío se valida; no se aplica una regla fiscal nacional.
No existe DELETE de proveedores ni envío de pedidos desde esta historia.

La migración `20261117104422_HU08Suppliers` continúa la cadena después de
`HU08RequireArticleFamily`, con código único y rowversion. EF 10.0.13 confirma que el
snapshot coincide con el modelo. No se reescribieron migraciones previas.

`MasterDataSupplierTests` verifica SQL real: normalización, contacto opcional/inválido,
estados, duplicados incluidos inactivos, filtros, ETag, replay, 401/403/CSRF y rechazo de
campos internos. `MasterDataReliabilityTests` comprueba también dos auditorías y dos
resultados para alta y edición, sin duplicados por replay, y rollback ante fallo inducido.
Ambas clases forman parte de las 76 pruebas backend aprobadas el 2026-11-18.

Las tres pruebas de `supplier-form.component.spec.ts` pasan: errores conservando datos,
contacto ausente con aviso, cancelar revisión de estado y permisos de consulta. La suite
frontend completa pasa 36 pruebas. El recorrido de navegador se registra en
[aceptación técnica](acceptance.md).

La conservación de pedidos y sus instantáneas sigue pendiente de HU-07/HU-01 y T076.
Estos resultados no acreditan documentos todavía inexistentes, SC-006 humana ni revisión A+B.
