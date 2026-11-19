# US4 — Retirada de artículos: alcance H1

**Fecha**: 2026-11-18. Fuentes: [spec](../spec.md), [plan](../plan.md),
[tareas](../tasks.md) y [contrato de integración](../contracts/integration.md).

`MasterDataLifecycleTests` pasa con SQL Server real dentro de la suite de 76 pruebas:

- Borrado con If-Match, 428 sin versión y 412 con versión obsoleta.
- Eliminación atómica del artículo y su política; auditoría anterior/posterior conservada,
  dos resultados de operación y replay 204 incluso después de desaparecer el recurso.
- La marca histórica de definición por sí sola no impide borrar. La marca preparada en
  esta prueba no acredita un documento real creado y retirado.
- Desactivación conservando identidad y acceso a movimientos; sin diario integrado,
  GET movements devuelve 503 DependencyUnavailable. Un artículo inexistente devuelve 404.
- Fechas sin zona o intervalos invertidos se rechazan; no se presenta un historial vacío
  como sustituto del diario ausente.

Las pruebas unitarias de retirada e historial pasan: revisión de identidad/consecuencia,
cancelación sin envío, rechazo con opción de desactivar y distinción entre vacío confirmado
y dependencia ausente. El recorrido de navegador se registra en [aceptación](acceptance.md).

El endpoint dispone de contrato de lectura y DTO con cantidades decimales exactas como
cadenas. Falta el lector real de StockMovement/StockEntry: T069 permanece pendiente.
No hay referencias documentales/stock en el modelo H1 aparte de la política propia.
T064/T066/T070/T075/T076/T077/T078 conservan sus partes integradas pendientes:
referencias/historial reales, carreras con escritores, dimensiones de stock, compromisos
existentes, instantáneas de compras/recepciones y efectos de alertas. No se sustituyen con
dobles ni se declara completa US4.

## Validación con el stock H0, 2026-11-19

La rama local `work/hu08-stock-history` combina la base con la PR #4 ya fusionada y
el commit `25d6df6` de la PR #3. El historial lee el diario real y mantiene las
cantidades decimales, la unidad histórica, el responsable, documento, soporte,
contraparte registrada, ubicación y lote. La consulta de un artículo inactivo
respeta filtros, paginación y permisos. El detalle de la proyección está en el
[contrato de integración](../contracts/integration.md#historial-con-el-núcleo-h0-de-stock).

La suite completa pasó 95/95 sobre SQL Server. Comprueba el 412 con el ETag
anterior al primer movimiento y el 409 con el vigente, el bloqueo de borrado con
historial, y ambos órdenes de commit entre recepción y borrado o desactivación,
observando la espera del applock. También comprueba que el núcleo reutiliza los
bloqueos adquiridos por el consumidor de recepción. El núcleo rechaza nuevas recepciones y
reservas tras desactivar, permite liberar y expedir la reserva existente y
conserva la conciliación con el diario. EF no detectó cambios de modelo pendientes.

El recorrido Chromium de ciclo de vida pasó sobre SQL Server en el proyecto E2E
aislado. Comprueba desactivación, historial vacío confirmado, reactivación, borrado
y recuperación del resultado. Los casos con movimientos se verifican a través
del endpoint HTTP con datos creados por el ejecutor y el núcleo de stock.

En este corte se puede verificar el efecto de stock en T035, T064, T066, T070,
T075, T077 y T078. Su cierre integrado sigue sujeto a los escenarios documentales
y consumidores indicados en [tareas](../tasks.md).

## Prueba cruzada de consumidores, 2026-11-19

El corte publicado de HU-02 (`3d32162`) pasó 112/112 pruebas contra SQL Server.
Al combinarlo con HU-08 (`cddfa4f`) en `work/hu08-hu02-validation` (`ae3db49`), pasaron
119/119. EF confirmó el snapshot de la cadena que termina en
`20261119101936_HU02_Locations`.

`ArticleLocationsTests.Hu08_deactivation_preserves_hu02_balances_and_hu08_history_for_the_same_article`
da de alta el artículo con el endpoint de HU-08, crea su ubicación con el de
HU-02 y registra recepción, reserva y salida mediante el ejecutor común.
Al desactivar el artículo con HU-08, el lector de HU-02 mantiene físico 4 y
reservado 1. El historial filtrado por ubicación conserva las tres entradas
y el DELETE del artículo responde `409 HistoryPreventsDeletion`. Se comprobaron
las cantidades contra los deltas del diario.

El resto de Q-05/Q-07/Q-09 requiere las implementaciones de compras (HU-07),
recepciones (HU-01), pedidos y salidas (HU-03), reservas (HU-05),
conteos (HU-04) y alertas (HU-06). El núcleo ejercita los efectos de stock
disponibles. Las ramas publicadas durante el ensayo todavía carecían de esos
consumidores documentales.
