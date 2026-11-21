# US5 — Autorización, autoría e integridad

**Fecha**: 2026-11-20 · T049–T054 verificadas localmente.

Pruebas transversales selectivas:

```bash
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --no-restore --filter "FullyQualifiedName~PurchasingAuthorizationTests|FullyQualifiedName~PurchasingAtomicityAuditTests|FullyQualifiedName~PurchasingConcurrencyTests|FullyQualifiedName~PurchasingRuntimeIntegrityTests" --nologo'
```

**5/5 aprobadas**. Matriz de rutas (anónimo 401/Administrador 403), lectura/gestión/envío
separados, antiforgery real 400 y otra identidad que no reutiliza una clave confirmada.
La prueba específica añade claims controlados en servidor a cuentas de fixture persistidas:
Manage sin Send crea pero no revisa/envía; Send sin Manage revisa/solicita pero no crea.
No hay roles nuevos ni API pública que acepte esos claims/ActorId.

El fallo después del SQL del grafo completo (resultado, entrega, evento y auditoría) revierte
todos los hechos; repetir la misma clave termina una sola solicitud. El mismo punto aplicado
al claim revierte contador/lease/evento/resultado y permite recuperar la operación original.
Dos ediciones con revisión común, una con reasignación a otro proveedor/artículo, conservan
la primera y rechazan 412 la segunda; el descubrimiento se hace bajo el lock del pedido,
sin adquirir proveedores/artículos antiguos desde una lectura previa no autoritativa.

El runtime rechaza contadores fuera de rango y Processing sin concesión completa (SQL547).
La suite de migración ya acredita SQL229 de UPDATE/DELETE en cada tabla inmutable y FK/
JSON/cantidades/estados sin permisos administrativos; no se duplicó esa preparación.
Complementos y carreras INT propias en [US1](us1.md), [US2](us2.md) y [US4](us4.md).

Frontend `purchasing-authorization.spec.ts`: **1/1**, rutas con Inventory.Read no conceden
gestión/envío y Users.Manage no concede compras. Se contrastó con denegaciones del servidor.

```bash
INVENTORY_E2E_PROFILE=hu07 HU08_E2E_PROJECT=inventario-hu07-e2e-implementation HU08_E2E_BASE_URL=http://localhost:8085 HU08_E2E_BROWSER_CHANNEL=chrome HU08_E2E_SKIP_BUILD=1 npm run test:e2e -- purchasing-permissions.spec.ts
```

**1/1 E2E aprobado**, 5,3 s de recorrido / 1,9 min con entorno. Después de perder respuesta
de un PUT ya confirmado, logout retira payload/ETag; una cuenta lectora distinta no ve
la intención ni puede entrar en edición. Volver a la identidad original permite comprobar
el resultado, sin reconstruir petición ni ofrecer replay sin payload. Cantidad confirmada
8 se recupera. El mismo almacenamiento/servicio común atiende maestros/ubicaciones/compras.

No se comunica éxito sin commit. La ejecución automática se enlaza a autorización humana
persistida y actor histórico, con permiso nuevo para retry manual; la aceptación incierta
no se convierte en otra compra. Se mantienen pendientes revisión A+B, SC-007 representativo,
recepciones reales HU-01 y aceptación/restauración operativas H5.
