# US2 — Revisión, solicitud y entrega de correo

**Fecha**: 2026-11-20. Implementación local; revisión A+B pendiente antes de integrar.
T020–T032 completadas localmente. Listado completo y retry humano continúan en US3/US4.

## T020/T021 — Pruebas antes del escritor

PurchaseOrderSendTests falló inicialmente **2/2 por 404** al no existir preview/send.
Se añadieron después concurrencia, worker y documento sobre SQL Server real; los casos
de worker/documento tienen base independiente por prueba para no compartir colas pendientes.

```bash
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --no-restore --filter "FullyQualifiedName~PurchaseOrderSendTests|FullyQualifiedName~PurchaseOrderSendConcurrencyTests|FullyQualifiedName~PurchaseDeliveryWorkerTests|FullyQualifiedName~PurchaseOrderDocumentTests" --nologo'
```

Primer pase selectivo **18/18 aprobadas**, ampliado a **20 casos específicos** en el pase
integral final. Cubren:

- Una entrega/snapshot/MIME/hashes conservados y replay 202 histórico; edición congelada.
- Documento HTML escapado, adjunto original/no-sniff y condiciones/precios/fechas revisados.
- Cambio de contacto exige nueva revisión; falta de correo permite borrador, no solicitud.
- Rollback de solicitud sin congelación parcial ni resultado de éxito; misma clave recupera.
- Dos initial o PUT frente a send, y send/desactivación de proveedor/artículo en ambos
  órdenes mediante barreras y espera SQL observada. Completa la parte de primer envío de INT-HU07-02.
- Dos propietarios concurrentes no reciben dos concesiones, replay no incrementa intentos,
  generación anterior no finaliza después de otra concesión y autoridad inexistente se rechaza.
- Rechazo permanente/incertidumbre no comunican Sent; progreso saneado no expone lease/owner.
- SMTP real contra un servidor TCP controlado acepta DATA y cierra antes de QUIT: permanece
  Accepted. Esta prueba verifica el adaptador MailKit, no una entrega externa al proveedor.

Las seis pruebas de worker se repitieron tras añadir logout real del solicitante y el
rechazo HTTP 404 del identificador lógico interno: **6/6 aprobadas**, sin petición HTTP
ficticia ni SMTP para una autoridad inválida.
Después se añadieron recuperación tras recrear la aplicación con un claim abandonado y
fallo inducido del finish después de escribir SQL: **8/8 de worker aprobadas**, con un único
SMTP y una sola aceptación confirmada. El replay HTTP de la solicitud original se verificó
también tras Accepted, haciendo login real con el mismo actor después del logout: devuelve
el 202/cuerpo histórico sin otro mensaje aunque la precondición ya sea obsoleta.

## T023–T028 — Implementación y límites

- MailKit 5.0.0 exacta/MimeKit.Core 5, sin biblioteca PDF; se usa SendAsync con FormatOptions,
  SmtpSendRequest y SendResult, no la firma retirada de MailKit 4. Compilación sin avisos.
- SMTP/opciones/currency validados; TLS obligatorio en producción, plazos y presupuesto
  acotados. Render y MIME fijados una vez, MessageId estable y snapshots sin credenciales.
- Preview con digest de documento/pedido/maestros/contactos/plantilla; initial revalida
  bajo los locks comunes, guarda solicitud/evidencia/resultado y solo comunica Pending.
- InventoryOperationExecutor conserva la entrada HTTP y factoriza un único núcleo privado;
  la entrada interna solo admite comandos tipados y verifica la autorización humana
  persistida/actor/recurso/entrega. No recibe un ActorId o callback de escritura arbitrarios.
- Claim/SMTP/finish separados; leases/fencing y reloj SQL, eventos append-only, resultados
  técnicos reproducibles y recuperación del mismo finish sin repetir SMTP por fallo SQL.
- Worker registrado para ejecución ordinaria, con guardas de inicialización/mantenimiento;
  Enabled=false pausa. Compose pasa opciones y el perfil E2E puede acotar sus tiempos.
- GET de progreso y adjunto originales, coherencia por snapshot/versión y permiso actual.

Se corrigió durante la integración el identificador lógico a
`/api/v1/internal/purchase-deliveries/...` para respetar la canonización existente sin
modificar RequestFingerprint ni publicar esos endpoints. También se explicitó que el
comparador ordinal sitúa purchase-order-delivery antes de purchase-order (`-` < `:`),
solicitando ambos documentos juntos, como ya hace InventoryLocks.

## Regresión del ejecutor y modelo

```bash
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --no-restore --nologo && dotnet tool restore && dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api'
```

Primer corte **181 aprobadas**, ampliado a **183 aprobadas, 0 fallidas, 1 omitida V-14**
y **EF sin diferencias pendientes**. Incluye H0/Identity, replay/locks/resultados,
StockCore, HU-08 y HU-02 tras el factoring común; compilación sin avisos.

## T022/T029/T030 — Revisión y progreso web

Implementada /compras/:id/enviar con permiso Purchasing.Send, revisión estructurada completa,
teclado, ETag/token vinculados, impedimentos, confirmación Pending y actualización del estado
vigente. No se usa el 202 histórico como proyección actual. Progreso se consulta cada 5 s
mientras está pendiente, con limpieza por destrucción/cambio de actor y recarga al variar
la versión. Si el primer refresco falla tras registrar la solicitud, conserva la referencia
y vuelve a comprobar, sin mostrarla de nuevo como una intención editable.

GET delivery expone recipientEmail/senderEmail **congelados**, no los del contexto actual;
la consulta y descarga originales requieren Inventory.Read. Esto evita confundir un cambio
posterior del maestro con el destinatario del compromiso anterior.

Se reforzó la reutilización del componente al navegar entre IDs y las respuestas tardías
tras logout: generación de vista/sesión impide aplicar datos viejos, retiene la referencia
sin payload/ETag y un 401 anterior no expira una cuenta nueva. Los tres casos nuevos de
transporte se reprodujeron en rojo antes de corregirse. Son garantía temprana de FR-027;
US4 ampliará el recorrido de recuperación, sin darlos como aceptación de todo US4.

Desde frontend:

```bash
npm test
npm run build
npm run test:e2e:types
```

**128/128 frontend**, 35 archivos; build y tipos correctos. Incluye cuatro casos de envío,
comparación de contacto, impedimentos y respuesta tras logout, además de navegación entre
documentos con la misma instancia y regresión de maestros/ubicaciones/transporte común.

## T032 — Chrome, API/SQL y Mailpit reales

```bash
INVENTORY_E2E_PROFILE=hu07 HU08_E2E_PROJECT=inventario-hu07-e2e-implementation HU08_E2E_BASE_URL=http://localhost:8085 HU08_E2E_BROWSER_CHANNEL=chrome npm run test:e2e -- purchasing-draft.spec.ts purchasing-send.spec.ts
```

**2/2 aprobadas**, 2,4 min con preparación; borrador 5,9 s y envío 6,9 s. Compilación Node 24
en contenedores, Chrome, API/SQL y buzón exclusivos del proyecto. Se verificó:

1. Cambiar correo desde la revisión antigua provoca 409 sin otro envío; revisar de nuevo
   muestra el nuevo destinatario y permite confirmar por teclado.
2. Respuesta inicial 202 Draft/Pending; aceptación posterior visible como Sent/Accepted.
3. DeliveryId/MessageId y destinatario corresponden al mensaje capturado en Mailpit.
4. El adjunto HTML decodificado de Mailpit es **idéntico byte a byte** al documento conservado
   por la API. Cambio posterior de contacto no sustituye destinatario/documento anteriores.
5. Pedido enviado no ofrece corrección. Se revalidó el recorrido de borrador tras ajustar
   navegación y ciclo de vida; cuentas retiradas y contenedores propios detenidos al terminar.

No acredita todavía retry humano US4, recepción HU-01, carga SC-007, revisión A+B ni H5.
