# US1 — Borradores de compra y MVP

**Fecha**: 2026-11-20. **Tareas**: T010–T019 de [tasks.md](../tasks.md).
Base técnica y soporte en [baseline.md](baseline.md), [setup.md](setup.md) y
[foundation.md](foundation.md). Implementación local; revisión A+B pendiente antes de integrar.

## Corte implementado

- POST/GET/PUT de pedido y contexto separado, con Purchasing.Manage/Inventory.Read,
  sesión/antiforgery, resultado idempotente, Location y ETag fuerte del agregado.
- Alta con número SQL, líneas independientes/repetidas, precisión exacta y fechas civiles.
  Solo Draft/None admite corrección; identidad de línea/artículo no se sustituye silenciosamente.
- Instantáneas capturadas al asignar, lectura SNAPSHOT, locks del pedido/proveedores/artículos,
  Tracker real y auditoría/resultado en el commit común. Las consultas SNAPSHOT están envueltas
  por la estrategia de resiliencia SQL, sin transacciones manuales incompatibles con ella.
- /compras provisional desde el menú, alta/corrección/detalle, moneda visible y selectores
  paginados. GET /purchase-orders/settings lee la moneda de la misma configuración del alta;
  no se presupone EUR en el navegador ni se cambia moneda de documentos anteriores.
- Transporte/almacén comunes ampliados por permiso y grupo de pedido; referencia recuperable,
  datos retirados al logout, 409 IdempotencyConflict no descarta la intención original.

## Pruebas SQL/HTTP

La primera ejecución de PurchaseOrderDraftTests falló **8/8 por 404** antes de mapear las
rutas. Tras implementar y ampliar casos, pasan **28/28** entre PurchaseOrderDraftTests y
PurchasingMasterDataIntegrationTests:

```bash
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --no-restore --filter "FullyQualifiedName~PurchaseOrderDraftTests|FullyQualifiedName~PurchasingMasterDataIntegrationTests" --nologo'
```

| Escenarios | Resultado acreditado |
|------------|----------------------|
| Q07-01/02; FR-001–005 | Alta/edición, artículo repetido, precio cero, herencia/fecha pasada, precisión inválida, campos no editables, IDs repetidas/ajenas y cambio de artículo con ID retenida rechazados. |
| Q07-05/12; FR-025/026 | Replay de cantidad normalizada devuelve cuerpo/ETag originales; corrección solo de líneas cambia versión; precondición ausente/obsoleta 428/412. |
| INT-HU07-01; FR-006–008 | Línea de borrador real marca definición; retirar el último uso de un artículo mantiene otra línea, conserva marca y permite reevaluar borrado. Con referencia viva, DELETE rechazado. |
| Carreras INT-HU07-01 | Primera referencia vs edición de definición/DELETE en ambos órdenes, con barreras y espera de applock observada. Instantánea usa la definición confirmada o se rechaza la referencia. |
| Parte de INT-HU07-02 | Editar/desactivar/reactivar proveedor preserva snapshot; nuevas compras se rechazan inactivo y vuelven a admitirse al reactivar. Alta vs desactivación de proveedor/artículo en ambos órdenes. |
| Q07-10; FR-022/024/028 | Fallo después de escribir agregado revierte cabecera/líneas/marca/auditoría/resultado; repetir clave completa una sola compra. Crear/editar no añade diario ni saldos. |

La prueba de reordenación se amplió con intercambio de dos posiciones y pasó **1/1**:
mismos IDs, precios/fechas ligados a sus líneas y nueva versión, sin colisión transitoria
del índice único. No se modificaron aserciones para admitir errores del escritor.

La regresión backend completa previa al auxiliar de moneda obtuvo **163 aprobadas,
0 fallidas y 1 omitida V-14**. El pase selectivo posterior incluye GET settings y conserva
28/28. El conjunto integral y EF se volverán a comprobar al modificar el ejecutor para correo.

## Pruebas frontend y navegador

- Primera prueba del almacén de compras: **2/2 fallidas** antes de ampliar rutas/permisos;
  después, recuperación/grupo/retirada de datos quedan verificadas.
- Suite frontend integral: **120/120**, 34 archivos. Tras añadir moneda desde configuración,
  selección de compras/almacén: **12/12**, build y tipos E2E correctos.
- Cobertura técnica: FormArray por ID, datos 422 conservados, referencias inactivas fuera
  de página, pedido congelado, comparación 412 por línea y aceptación explícita de revisión,
  cabeceras HTTP, contexto desalineado, grupo PUT/send, recarga y cambio de actor/logout.

Desde frontend:

```bash
npm test
npm run build
npm run test:e2e:types
INVENTORY_E2E_PROFILE=hu07 HU08_E2E_PROJECT=inventario-hu07-e2e-implementation HU08_E2E_BASE_URL=http://localhost:8085 HU08_E2E_BROWSER_CHANNEL=chrome npm run test:e2e -- purchasing-draft.spec.ts
```

**E2E 1/1 aprobado**, última ejecución 5,5 s de recorrido/2,2 min con entorno: compra desde
menú y teclado, moneda EUR real, artículo UD que rechaza 1,5 conservando campos, alta de 10,
reapertura y corrección a 6, recibido cero/pendiente 6. Compilación de contenedores con
Node 24, API real, SQL aislado y Chrome; cuentas retiradas y contenedores propios detenidos.
Un intento anterior no pudo iniciar el fixture porque Docker estaba apagado: se arrancó
Docker Desktop y se repitió; ese intento no se cuenta como validación funcional.

## Estado y fronteras

MVP US1 verificado. INT-HU07-01 tiene evidencia propia; INT-HU07-02 sigue parcial hasta
probar primer envío contra desactivación en US2. Sus checkboxes globales permanecen pendientes.
No se acredita aún solicitud SMTP/worker, rendimiento SC-007, recepción HU-01 ni H5 humano/
restauración. Las rutas de esos incrementos se implementarán con sus propios checkpoints.
