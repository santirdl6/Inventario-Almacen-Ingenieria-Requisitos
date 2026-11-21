# Integración transaccional — HU-06

Base: [plan](../plan.md), [modelo común](../../../.specify/memory/project/data-model.md),
[protocolo de stock](../../../.specify/memory/project/contracts/stock-operations.md),
[escritores receptores](../../001-reparto-specs-inventario/integration-tasks.md) y
[HU-07](../../007-gestionar-pedidos-proveedores/contracts/integration.md).

## Evaluación y transacción

- `IStockAlertEvaluator` recibe el `OperationContext` del intento y IDs de artículo. No
  abre contexto, transacción ni commit propios. Ejecutar tras validar y aplicar efectos
  de cada escritor confirmado, y en cambios de mínimo y ciclo de vida bajo el mismo lock
  de artículo, después de releer los valores decisorios.
- Agregar **todos** los buckets del artículo mediante el cálculo compartido: suma física,
  suma reservada y disponible = suma física − suma reservada. Incorporar buckets nuevos y
  deltas EF todavía sin guardar exactamente una vez; no confundir valores originales con
  actuales ni hacer una consulta SQL sobre datos pendientes sin conciliarlos. Comprobar
  límites y devolver error recuperable sin commit si un total o sugerencia no es representable.
- `Active` existe solo si el artículo está activo y disponible < mínimo. Crear o actualizar
  una sola alerta activa con snapshots consistentes; resolverla al recuperar el umbral o al
  desactivar. Reactivar reevalúa inmediatamente. Un mínimo cero y disponible cero no alertan.
  Histórico `Resolved` se conserva. Índice filtrado único y locks son defensas complementarias.
- El escritor de StockCore llama explícitamente al evaluador después de sus efectos; también
  deben integrarse los escritores de corrección y los que se incorporen después. La operación
  de política y `IArticleStateEffects` usan el mismo evaluador. Las alertas y los efectos de
  stock/política/resultado se guardan en el mismo commit; replay no reevalúa otra vez.
- El esquema añade FK restrictiva de `StockAlert` a `Article`: un `IArticleUsageContributor`
  autoritativo debe impedir borrar mientras exista cualquier episodio, incluso resuelto.
  Una alerta derivada solo del mínimo no es por sí misma documento, reserva ni movimiento:
  no debe bloquear permanentemente la definición de un artículo que nunca tuvo esos usos.
  El validador actual reutiliza la misma lista de FKs para uso y primer enlace; separar sus
  conjuntos explícitamente, excluyendo `StockAlert` **solo del gate de primer enlace** y
  manteniéndolo en cobertura de uso. Probar mínimo positivo con stock cero, edición de
  definición permitida, borrado rechazado por historial de alerta y posterior primer uso
  real que sí fija la marca. Este cambio compartido requiere revisión A+B previa a integrar;
  no usar respuesta constante ni ocultar errores de base de datos.

## Coordinación

`INT-HU06-01`: conectar `IArticleStateEffects` con pruebas reales de desactivación,
reactivación, bloqueo compartido, rollback y evaluaciones en todos los escritores presentes.
HU-01/03/04/05 deben consumir el mismo evaluador al integrarse; no modificar su disponibilidad
ni asumir que las ramas no integradas están terminadas. Para HU-04 la congelación de conteo
debe estar resuelta por su propio gate, no sustituida por un lector constante.
En el corte actual, `StockCore.ApplyAsync` cubre los efectos ordinarios y `StockCore.CorrectReceiptAsync` la compensación de HU-01. Un escritor futuro que no use esos puntos debe llamar al mismo evaluador tras aplicar todos sus efectos, con el lock de artículo ya adquirido y antes del commit; no volver a llamarlo cuando el núcleo ya lo hizo.

HU-07 añade la precarga opcional de `/compras/nuevo`: selección y cantidad son editables,
se exige `Purchasing.Manage`, se comprueba que el artículo sigue activo y no se crea pedido
al navegar. Si falta permiso o el artículo cambió de estado, explicar el caso sin guardar.

La [revisión A+B confirmada por el usuario](../validation/shared-review.md) abarca modelo, cadena EF, cálculo, escritores, historial, borrado, política y permisos. Los consumidores futuros mantienen su propia verificación de integración.
