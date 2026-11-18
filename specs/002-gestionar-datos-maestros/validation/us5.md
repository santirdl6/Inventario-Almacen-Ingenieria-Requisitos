# US5 — Fiabilidad de cambios de maestros

**Fecha**: 2026-11-18. Fuentes: [spec](../spec.md), [plan](../plan.md),
[tareas](../tasks.md), [HTTP](../contracts/api.md) y [UI](../contracts/ui.md).

## SQL real

Las 76 pruebas backend pasan. `MasterDataReliabilityTests` ejecuta alta y edición en los
tres catálogos: recuperación por OperationId, replay con respuesta/ETag originales,
conflicto por actor/contenido/ruta diferentes y 412 para una nueva intención obsoleta.
Cada maestro conserva una fila, dos auditorías y dos resultados tras alta y edición.

Un trigger de prueba induce fallo de auditoría antes del commit para cada catálogo.
Se comprueban cero maestros nuevos, cero auditorías y cero resultados para la intención
fallida; retirado el trigger, la misma clave confirma el alta. No hay sondas productivas.

`MasterDataOperationAuthorizationTests` comprueba 401/403 y reautorización del permiso
original al consultar un resultado después del borrado; outcome 204/body null, 404 para
una operación no observada y replay anterior a comprobar existencia/versión.

## Cliente

Las 36 pruebas frontend pasan. Se conserva clave/payload/ETag originales ante doble envío,
timeout, 404 de consulta, recarga y reintento. Se respeta Retry-After y la reautenticación
no envía automáticamente. Al cerrar sesión se retira el contenido y solo queda la referencia.

Regresiones añadidas en esta continuación:

- Una edición pendiente impide iniciar un borrado del mismo recurso hasta resolverla.
- Un objeto antiguo conservado antes del logout no permite reenviar el payload retirado,
  incluso volviendo a autenticar la misma cuenta.
- El transporte recupera la intención vigente del almacén antes de enviar: una copia del
  llamador no cambia método, payload ni ETag. Una intención ya resuelta no vuelve a enviarse.

Los tres formularios incorporan consulta/reintento explícitos y comparación de versión
preservando el borrador. Los recorridos de conflicto entre dos sesiones y respuesta perdida
tras commit se registran en [aceptación técnica](acceptance.md).

Esto acredita los escenarios ejecutados de maestros H1. La aceptación humana, integración
con stock/documentos, revisión cruzada y restauración no están sustituidas por estas suites.
