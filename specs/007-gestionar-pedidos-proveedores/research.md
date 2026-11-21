# Investigación HU-07 — Compras y correo

**Fecha**: 2026-11-20 · **Estado**: decisiones aplicadas localmente en US1–US5;
evidencia en [plan](plan.md) y `validation/`; [revisión A+B](validation/shared-review.md) confirmada.
Base: [spec](spec.md), [constitución](../../.specify/memory/constitution.md),
[plan global](../../.specify/memory/project-plan.md) y
[decisiones comunes](../../.specify/memory/project/research.md).

## R07-01. Reutilización de la base ejecutable

**Decisión**: mantener una API por funcionalidades, CQRS ligero por DI, Angular standalone
con formularios tipados/signals y un InventoryDbContext. Añadir Purchasing y Messaging a
la estructura existente. Consumir StockCore únicamente desde la futura recepción HU-01.

**Fundamento**: ya existen sesión, permisos Purchasing, maestros, bloqueos, resultados
idempotentes y transporte de intenciones. El código consultado usa net10.0/EF 10.0.13 y
Angular/CLI/CDK 22.2.3, PrimeNG 22.1.4, temas 3.1.0, TS 6.0.3 y RxJS 7.8.2.
Al iniciar la investigación no existían pedido, outbox ni adaptador SMTP; se entregaron
localmente en US1/US2. Compose ya disponía de Mailpit persistente.

**Alternativas**: otro servicio/base, MediatR, broker, repositorio genérico y store global
añadirían coordinación sin resolver una necesidad de esta entrega.

## R07-02. Identidad, precisión y datos históricos

**Decisión**: UUID por pedido/línea; secuencia SQL BIGINT no reiniciable y número
`PO-` + valor con al menos ocho dígitos. Admite huecos; no expresa año ni obligación contable.
Las líneas reciben UUID del cliente antes del primer guardado, sin usar ArticleId como
identidad. Cantidades/precios son cadenas decimales normalizadas a tres/dos decimales.
Fechas previstas son fechas civiles ISO `YYYY-MM-DD` (DateOnly/SQL date), no instantes;
la autoría y los eventos sí usan datetimeoffset UTC. Moneda única configurable,
`Purchasing:Currency` ISO 4217, default local EUR, congelada en cada pedido.

Las instantáneas se capturan al asignar proveedor o artículo, bajo bloqueos. Ediciones de
cantidad/precio/fecha no las refrescan. Una reasignación explícita captura los datos del
nuevo maestro. Cambiar artículo equivale a retirar línea y añadir otra identidad.
La fecha de línea omitida se materializa con la general al guardar; cambiar la general no
reescribe fechas de líneas ya guardadas sin una edición explícita de estas.

**Fundamento**: preserva condiciones históricas y líneas repetidas, sin cálculo binario
en el navegador ni reinterpretación horaria. El contacto vigente se usa solo para la
primera solicitud, separado de la instantánea histórica del proveedor.

**Alternativas**: contador MAX+1, identidad por artículo, refresco automático de snapshots
y conversión de fecha a medianoche UTC introducen carreras o cambios silenciosos.

## R07-03. ETag y revisión de envío

**Decisión**: detalle propio en SNAPSHOT, ETag fuerte de rowversion de cabecera. Cada cambio
representado, incluidas líneas, recepción y SendStatus, toca un campo RevisionStamp de
cabecera para avanzar rowversion. Contexto de maestros y progreso técnico del outbox se
consultan separadamente sin ETag, con no-store y purchaseOrderVersion.

La revisión de envío se obtiene en `/purchase-orders/{id}/send-preview`: muestra documento
exacto y destinatario; entrega `reviewToken` SHA-256 del contenido canónico, versión del
pedido, rowversion de proveedor y versiones de artículos, contactos y configuración de
renderizado/remitente. No es una autorización. El primer `/send` recalcula y compara ese
token después de bloquear pedido/proveedor/artículos y exige If-Match. Un cambio de correo
o documento exige otra revisión. El token nunca permite elegir un correo arbitrario.

**Fundamento**: rowversion del pedido no detecta cambios del proveedor; un refresh previo
al POST tampoco elimina la carrera. Separar contexto evita un ETag fuerte invariable para
una representación que cambió por datos relacionados, problema ya corregido en HU-02.

**Alternativas**: ETag hash de toda la representación es válido, pero acopla la edición a
todos los datos derivados; solo If-Match del pedido no protege al destinatario revisado.

## R07-04. SMTP y documento reproducible

**Decisión**: BackgroundService en la API siempre activa y **MailKit 5.0.0 exacta**, estable
y con target net10.0 en sus metadatos oficiales. Usar la API 5.x de SendResult/SmtpSendResult,
verificada al implementar. Documento HTML UTF-8 autocontenido adjunto y cuerpo de texto
con resumen e identidad del pedido. Escapar datos de maestros; sin recursos remotos ni
contenido HTML del usuario. Persistir bytes MIME completos, documento, hashes y versión
de plantilla; MessageId, Date, límites MIME y contenido se generan una sola vez.

**Fundamento**: cumple documento consultable/repetible sin dependencia PDF. Un retry usa
el mensaje conservado, nunca maestros/plantillas actuales. SMTP queda fuera de la
transacción; SendAsync aceptado no significa entrega final. Un fallo al desconectar
después de la aceptación no revoca ese resultado.

**Alternativas**: System.Net.Mail no se recomienda para nuevo desarrollo; PDF no es
requisito; correo dentro del commit y trabajo solo en memoria no permiten recuperación.

**Fuentes**:
- [Metadatos MailKit 5.0.0](https://api.nuget.org/v3-flatcontainer/mailkit/5.0.0/mailkit.nuspec).
- [Metadatos MimeKit.Core 5.0.0](https://api.nuget.org/v3-flatcontainer/mimekit.core/5.0.0/mimekit.core.nuspec).
- [SendAsync](https://mimekit.net/docs/html/M_MailKit_Net_Smtp_SmtpClient_SendAsync.htm),
  [SmtpSendResult](https://mimekit.net/docs/html/T_MailKit_Net_Smtp_SmtpSendResult.htm),
  [MessageId](https://mimekit.net/docs/html/P_MimeKit_MimeMessage_MessageId.htm).
- [Servicios alojados](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0).

## R07-05. Una entrega, varias acciones autorizadas

**Decisión**: primera solicitud crea una Delivery inmutable y un OutboxMessage mutable
1:1; índice único de pedido, sin segunda versión de envío en este alcance. Respuesta 202
histórica inmutable. Recuperar respuesta HTTP perdida repite clave/cuerpo/ETag originales.
Reintentar Failed es **otra operación HTTP**, nueva clave y permiso Purchasing.Send,
que reactiva la misma Delivery/MessageId/documento/destinatario, incluso si cambiaron los
maestros después. Automatismos se vinculan a la última autorización persistida.

**Fundamento**: reutilizar la clave original devolvería siempre su 202 y no reactivaría
un error. La identidad de la entrega no debe confundirse con la intención HTTP concreta.
No se cambia destinatario ni se desbloquea edición tras un fallo.

**Alternativas**: crear otra entrega para un retry, modificar OperationResult para reflejar
el progreso o regenerar el documento destruyen la trazabilidad del compromiso original.

## R07-06. Autoridad de segundo plano sin sesión ficticia

**Decisión**: factorizar el núcleo privado transaccional de InventoryOperationExecutor
sin cambiar su entrada HTTP. Añadir entrada interna tipada, limitada a claim/finalización
de compras, que resuelve actor y autoridad desde Delivery + OperationResult confirmado
de solicitud/reintento. No recibe ActorId/permiso/callback arbitrarios desde transporte.
Conserva scopes limpios, tres intentos SQL, applocks, huella, rollback y resultado juntos.
Los hechos automáticos indican ExecutionKind Worker y autorización de origen.

**Fundamento**: el ejecutor actual exige HttpContext.User **además** de ICurrentActor;
no funciona desde BackgroundService. Sesión expirada o permiso revocado después del
commit no cancela una compra ya autorizada; otro reintento manual sí exige permiso actual.
Una operación interna que registra un fallo tiene resultado técnico 2xx: el fallo del
correo se registró, sin afirmar aceptación SMTP. No se crea otro ejecutor ni otra tabla.

**Alternativas**: simular HttpContext, actor sistema predeterminado, bandera pública
skipAuthorization o transacciones privadas del worker debilitan el protocolo común.

**Fuentes**:
- [Scopes de BackgroundService](https://learn.microsoft.com/en-us/dotnet/core/extensions/scoped-service).
- [HttpContext fuera de la petición](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-context?view=aspnetcore-10.0).
- [Resiliencia y commit incierto EF](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency).

## R07-07. Claim, fencing y aceptación incierta

**Decisión**: claim en transacción corta, SMTP sin transacción, finalización en otra
transacción. Lease de 120 s, plazo total SMTP de 30 s, polling 5 s, un envío concurrente
por instancia; generación monotónica y AttemptId comprueban propietario vigente.
Cinco claims por ciclo autorizado como máximo, backoff 5/30/120/300 s; rechazo SMTP
permanente termina el ciclo inmediatamente. Los fallos transitorios de SQL reintentan
solo su comando local, nunca la llamada SMTP. Reloj de lease y elegibilidad: SQL UTC.

Un lease vencido hace incierto el intento incompleto; una observación tardía no cambia la
proyección de una generación posterior. Tras aceptación externa con commit local incierto,
primero se recupera/repite la finalización con su clave estable. Tras caída irrecuperable,
otro intento puede enviar una copia: conserva MessageId y muestra incertidumbre.
No se promete exactly-once SMTP ni que Failed demuestre rechazo externo si hubo incertidumbre.

**Fundamento**: permite multiproceso y recuperación sin transacciones largas. Fencing protege
SQL, no un servidor SMTP que no conoce ese token. La prueba debe cubrir procesos pausados,
no solo apagado limpio. Detalle operativo en [contrato de integración](contracts/integration.md).

**Alternativas**: mutex en memoria no coordina instancias; lease sin generación permite
finalizar desde propietario obsoleto; un broker no elimina el problema del acuse SMTP perdido.

**Fuentes**:
- [sp_getapplock](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql?view=sql-server-ver17).
- [Concurrencia EF](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).
- [Opciones TLS MailKit](https://mimekit.net/docs/html/T_MailKit_Security_SecureSocketOptions.htm).

## R07-08. UI y ampliación mínima de infraestructura compartida

**Decisión**: reutilizar MasterDataMutationsService/PendingOperationStore/OperationStatusComponent;
ampliar permisos por método/ruta, agrupación PUT/send por pedido y navegación a Compras.
No renombrar transversalmente estos servicios solo por su nombre histórico. Mantener
petición inmutable en sessionStorage, retiro de payload/ETag en logout y aislamiento por actor.
Un resultado histórico recuperado dirige al recurso y refresca el detalle; no sobrescribe
un estado actual Accepted con el Pending histórico. Conflictos se comparan por ID de línea.

**Fundamento**: el store actual solo conoce maestros/ubicaciones y bloquea por URL exacta;
la recuperación necesita impedir acciones incompatibles entre detalle y /send.
El servidor mantiene las garantías entre pestañas/usuarios; la UI no es el cerrojo global.

**Alternativas**: transporte/store de compras independiente duplicaría recuperación y
separación de identidad; deshabilitar solo el botón no protege recarga ni otra pestaña.

## Cierre e impacto

Resueltas las incógnitas técnicas: dependencia, documento, fechas/moneda/número, revisión,
autoridad worker, lease/retry, ETag y transporte. No quedan aclaraciones bloqueantes.
Las concreciones compartidas se enlazan desde la base global y requieren revisión A+B
antes de integrar; **no se registra como realizada**. INT-HU07-01/02 se prueban en este
módulo con escritores reales. Recepciones reales y sus carreras pertenecen a HU-01;
restauración/aceptación operativa permanecen en H5.
