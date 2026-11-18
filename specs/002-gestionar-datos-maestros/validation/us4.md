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
