# US4 — Retry conocido y recuperación de incertidumbre

**Fecha**: 2026-11-20 · T040–T048 verificadas localmente.

RetryPurchaseOrderDelivery exige sesión/Purchasing.Send, antiforgery, clave nueva y ETag
actual; bloquea delivery+pedido en orden común y solo reactiva Failed. Actualiza cabecera
antes de cambiar la FK del outbox a la nueva OperationResult, que se confirma junto a
evento/auditoría/cola. No regenera documento/MIME ni consulta maestros para cambiar destino.

El primer caso de retry se reprodujo en rojo (422 por deliveryId no admitido todavía).
Ahora pasan **6 casos SQL/SMTP** de PurchaseDeliveryRetryTests/PurchaseDeliveryRecoveryTests:
dos retries simultáneos abren un ciclo, replay histórico no envía otra vez, cinco intentos
con backoff 5/30/120/300 quedan acotados, pérdida real del acuse TCP deja incertidumbre,
claims abandonados agotan presupuesto con incidente público y host deshabilitado conserva
cola/documento congelado que un host nuevo habilitado reanuda sin sesión HTTP.

La interfaz diferencia comprobar/repetir intención incierta de reintentar un error conocido;
usa entrega/contactos congelados y refresca estado vigente tras recuperar 202 histórico.
Pruebas frontend cubren clave/cuerpo/ETag, 404 no definitivo, Retry-After, recarga, logout,
otra cuenta y respuestas tardías. Se preservan rutas de maestros/ubicaciones en el mismo
transporte; ningún callback tardío expira una sesión nueva ni pierde su referencia anterior.

```bash
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --no-restore --filter "FullyQualifiedName~PurchaseDeliveryRetryTests|FullyQualifiedName~PurchaseDeliveryRecoveryTests" --nologo'
npm test -- --include='src/app/features/purchasing/**/*.spec.ts' --include=src/app/core/operations/purchasing-pending-operation.spec.ts
```

Pase selectivo de cinco casos más pausa/reanudación 1/1; pase integral final:
**192 backend aprobadas, 1 omitida V-14**, **135 frontend**, build/tipos correctos y
`docker compose config --quiet` correcto. Opciones efectivas documentadas en README y
.env.example; el perfil HU-07 fija SMTP/Currency propios para no heredar credenciales externas.

## E2E real con pérdidas de respuesta después del commit

```bash
INVENTORY_E2E_PROFILE=hu07 HU08_E2E_PROJECT=inventario-hu07-e2e-implementation HU08_E2E_BASE_URL=http://localhost:8085 HU08_E2E_BROWSER_CHANNEL=chrome HU08_E2E_SKIP_BUILD=1 npm run test:e2e -- purchasing-recovery.spec.ts
```

**1/1 aprobado**, recorrido 19,9 s / entorno 2,2 min. Usa imagen compilada inmediatamente
antes con el código actual; el primer intento falló por un selector que encontraba tanto
resumen global como estado de formulario. Se acotó al main y se repitió sin cambiar aserciones.

El interceptor del navegador deja que el servidor confirme y pierde únicamente su respuesta.
Alta, PUT, send initial y retry conservan referencia tras recarga y se comprueban mediante
GET operations; no se fabrica otra petición. Se detiene/restaura solo SMTP del proyecto,
se alcanza Failed y se reintenta con clave distinta sobre la misma Delivery/MessageId/MIME/
destinatario. Un cambio posterior del correo maestro no cambia el envío anterior. Al final
hay una compra, documento idéntico y un mensaje de ese recorrido en Mailpit; no se promete
ausencia de copias en los casos de acuse incierto acreditados por SQL/SMTP controlado.

Cobertura FR-014–017/025/027, Q07-04/05/06/09 y pausa de T048. No acredita restauración
operativa H5-02 ni recepción HU-01. La referencia de esas aceptaciones permanece compartida.
