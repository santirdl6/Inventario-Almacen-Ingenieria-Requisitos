# Contrato HTTP particular de HU-08

**Fecha de diseño**: 2026-11-12. Implementados el 2026-11-17: familias, GET/POST/PUT
de artículos, contexto auxiliar, resultados y mantenimiento transitorio. Proveedores,
DELETE e historial siguen pendientes. Evidencia en [US2](../validation/us2.md).
Extiende el [contrato común](../../../.specify/memory/project/contracts/api.md),
sin sustituirlo. Datos en [data-model](../data-model.md); coordinación en
[integration](integration.md). Base `/api/v1`, JSON UTF-8 y mismo origen.

## Sesión y permisos

Reutilizar csrf/login/me/logout existentes y cookie Identity. Lectura: Inventory.Read.
Toda mutación: MasterData.Manage y antiforgery X-XSRF-TOKEN. Los cuerpos no aceptan ActorId,
RecordedAt, Version, OperationId o IsDefinitionLocked como datos editables. El identificador
de operación procede de Idempotency-Key; la autoría y fecha las asigna el servidor.
No hay registro público ni gestión de usuarios nueva.

## Rutas y resultados

| Método/ruta | Entrada | Resultado exitoso |
|-------------|---------|-------------------|
| GET /families | Filtros comunes | 200 Page de FamilySummary. |
| GET /families/{id} | UUID | 200 FamilyDetail y ETag. |
| POST /families | FamilyWrite de alta | 201 MutationResult y Location/ETag. |
| PUT /families/{id} | FamilyWrite completo | 200 MutationResult y nuevo ETag. |
| GET /suppliers | Filtros comunes | 200 Page de SupplierSummary. |
| GET /suppliers/{id} | UUID | 200 SupplierDetail y ETag. |
| POST /suppliers | SupplierWrite de alta | 201 MutationResult y Location/ETag. |
| PUT /suppliers/{id} | SupplierWrite completo | 200 MutationResult y nuevo ETag. |
| GET /articles | Filtros comunes y familyId opcional | 200 Page de ArticleSummary. |
| GET /articles/{id} | UUID | 200 ArticleDetail y ETag. |
| GET /articles/{id}/context | UUID | 200 contexto auxiliar de familia, política y capacidades, sin ETag. |
| POST /articles | ArticleWrite de alta | 201 MutationResult y Location/ETag. |
| PUT /articles/{id} | ArticleWrite completo | 200 MutationResult y nuevo ETag. |
| DELETE /articles/{id} | Sin cuerpo | 204; conserva auditoría y resultado recuperable. |
| GET /articles/{id}/movements | Filtros de historial | 200 Page de movimientos comunes. |
| GET /operations/{operationId} | UUID de la intención | 200 OperationOutcome autorizado; independiente de que el maestro siga existiendo. |

No se crean rutas DELETE de familias/proveedores ni endpoints para sobrescribir saldos.
Desactivar/reactivar se expresa con PUT cambiando isActive, no con borrado lógico ambiguo.

## Esquemas de maestros

- **FamilyWrite**: `{ code, name, isActive }`.
- **SupplierWrite**: `{ code, name, email, taxIdentifier, isActive }`.
- **ArticleWrite**: `{ sku, name, familyId, baseUnit, allowsFractional, tracksLot, isActive }`.
- En POST, isActive es omisible o true; false es validación 422 porque las altas son activas.
  En PUT se exigen todos los campos no opcionales. Email/taxIdentifier admiten null.
- Los límites y la normalización son los de [data-model](../data-model.md). Los campos
  desconocidos que pretendan fijar autoría, versión o marca interna se rechazan con 422.
- **Summary**: identificador y campos del maestro correspondientes; Article incluye
  family `{ id, code, name, isActive }`, baseUnit, flags y estado. No lleva una versión
  editable: abrir detalle para revisar y obtener ETag antes de guardar.
- **Detail**: en Family/Supplier, los datos propios persistidos del maestro. Article devuelve
  `{ id, sku, name, familyId, baseUnit, allowsFractional, tracksLot, isActive, isDefinitionLocked }`.
  Su ETag fuerte representa exclusivamente su fila, sin nombres ni capacidades derivados.
- **ArticleContext**: `{ articleVersion, family, minimumAvailable, capabilities }`, sin ETag y
  con no-store. family contiene id/code/name/isActive; minimumAvailable es cadena decimal
  exacta. capabilities contiene canEditDefinition/definitionLockReason/canDelete/deleteBlockReason
  y describe dominio, no permisos. articleVersion permite comprobar que el contexto corresponde
  al ETag del detalle; si difiere, el cliente recarga antes de editar.
- definitionLockReason es null o `FirstReference`; deleteBlockReason es null,
  `History` o `References`. Cuando faltan lectores o el enlace de primer uso necesario para
  acreditar capacidades, no se inventan: se devuelve 503 DependencyUnavailable. Capacidad
  y detalle se leen de forma consistente.

Esta separación resuelve el alcance del validador: renombrar la familia o cambiar metadatos
de uso no cambia artificialmente la revisión propia del artículo. Los listados pueden incluir
familia derivada porque no llevan ETag editable. La interfaz combina detalle y contexto;
el servidor vuelve a validar al guardar bajo los bloqueos comunes.
- **MutationResult**: `{ operationId, resource }`, siendo resource el Detail confirmado.
  Para DELETE el resultado HTTP es 204, sin cuerpo; OperationId ya es conocido por el cliente.

Los campos de definición bloqueados pueden enviarse con sus valores actuales para editar
el resto. Para cambiar cualquiera de ellos se comprueba primero el gate de consumidores:
incompleto devuelve 503 DependencyUnavailable; con gate completo, IsDefinitionLocked=true
o referencias reales devuelve 409 sin modificar otros campos. No se exige que un cliente
adivine si tiene saldo.

## Búsqueda, paginación e historial

- `q` opcional: Trim, hasta 200 caracteres, búsqueda literal parcial por código o nombre,
  sin distinguir mayúsculas. No se interpreta como SQL ni como expresión de comodines.
- `state=active|inactive|all`, por defecto active. Cualquier otro valor: 422.
- page desde 1, pageSize por defecto 25 y máximo 100; orden por Code/Sku e Id estable.
  Page: `{ items, page, pageSize, totalCount }`, incluyendo items=[] y totalCount=0.
- familyId limita el listado de artículos; se admite una familia inactiva para consulta.
  Un filtro UUID mal formado es 400; un UUID válido sin coincidencias da página vacía.
- Historial: `from`, `to` ISO 8601 con zona; intervalo [from,to), `actorId`, `locationId`,
  `lotId`, page/pageSize. Orden recordedAt descendente e Id como desempate estable.
  Rango temporal inválido: 422. Los filtros no modifican el estado del artículo.
- El DTO de movimientos proyecta el diario común: identidad, tipo, cantidad decimal como
  cadena, unidad histórica, occurredAt/recordedAt, actor, motivo, documento, soporte,
  contraparte, origen/destino y lote aplicables. No usar la unidad actual para reescribir hechos.
- Con artículo existente y proveedor de diario integrado sin movimientos: página vacía.
  Artículo inexistente: 404. Diario aún no integrado: 503 DependencyUnavailable.

## Precondiciones, idempotencia y resultado incierto

1. Toda mutación exige Idempotency-Key UUID no vacío. OperationId es ese mismo UUID.
2. Para una operación nueva, PUT y DELETE exigen un único If-Match fuerte; el ETag se codifica
   como Base64 de la rowversion entre comillas. No admitir W/, listas o `*` como sustitutos
   de una versión revisada. Ausencia: 428; formato inválido: 400; versión obsoleta: 412.
3. Tras autorizar y bloquear la operación, un replay coincidente se resuelve **antes** de
   cargar el recurso y validar If-Match. Se comparan actor, método, ruta canónica y payload
   normalizado. La clave no puede reutilizarse para otro contenido o recurso: 409.
4. El replay devuelve estado, cuerpo y ETag/Location históricos de ese resultado, aunque el
   maestro haya cambiado o ya no exista; no ejecuta el handler ni duplica evidencia.
5. La respuesta de alta tiene Location al GET del recurso; PUT entrega el nuevo ETag.
   Su persistencia requiere guardar el resultado después de obtener la rowversion y antes
   del commit, dentro de la misma transacción de maestro/auditoría/política.
6. Un fallo antes del commit no deja OperationResult de éxito. Un commit incierto se
   comprueba por operación o se reintenta con la misma clave; un 404 de operación no prueba
   por sí solo rollback. No se exige crear un resultado de error persistente.

Normalización para la huella: método en mayúsculas, ruta del contrato con UUID D en
minúsculas y JSON UTF-8 con propiedades ordenadas ordinalmente tras normalizar el DTO; códigos/unidad en mayúsculas
tras Trim, nombres/contacto con Trim, opcionales vacíos a null y defaults de alta explícitos.
RequestHash es SHA-256 de método, ruta y ese JSON separados por salto de línea. If-Match,
cookies y cabeceras de autenticación no forman parte de la huella; el actor se compara aparte.

**OperationOutcome**:

```json
{
  "operationId": "40000000-0000-0000-0000-000000000001",
  "method": "DELETE",
  "resource": "/api/v1/articles/10000000-0000-0000-0000-000000000001",
  "status": 204,
  "body": null,
  "headers": {},
  "recordedAt": "2026-11-12T12:00:00Z"
}
```

La consulta exige el permiso original guardado en el resultado —MasterData.Manage para
estas mutaciones— y autenticación actual; no depende de una FK al recurso eliminado.
No expone cookies, tokens, trazas ni cabeceras arbitrarias. Headers solo permite ETag/Location.

## Mantenimiento autenticado de la transición H0

Contrato técnico de la preparación de FamilyId, no un alta provisional de artículos.
`POST /maintenance/hu08/family-map` solo se registra cuando
`Maintenance:Hu08FamilyPreparation=true` y el esquema expandido está aplicado sin la
migración de cierre de familia obligatoria. Con el modo desactivado o esquema final en el
arranque, la ruta no existe —404— ni se anuncia en OpenAPI. Si cambia el estado de esquema
durante la petición, se rechaza con 503 DependencyUnavailable sin efectos parciales.

El modo mantiene las rutas de sesión y la conexión de ejecución sin DDL, pero no registra
los endpoints ordinarios de maestros ni otros escritores o workers/jobs de negocio. Las migraciones se
aplican fuera de la API y el modo de mantenimiento no anuncia disponibilidad operativa normal.

- Exige sesión Identity, MasterData.Manage, X-XSRF-TOKEN e Idempotency-Key UUID.
- Entrada: `{ entries: [{ articleId, familyId }] }`, de 1 a 100 entradas con articleId único.
  Ambos identificadores deben ser UUID no vacíos. No acepta ActorId, RecordedAt, Version,
  OperationId, cantidades ni definición del artículo en el cuerpo.
- Solo prepara artículos existentes y familias activas existentes. Una asociación ya
  completada al mismo destino se conserva; una diferente se rechaza para no sobrescribir
  decisiones previas. Referencias o campos inválidos devuelven 422 con errors por entrada.
- El responsable procede exclusivamente de ICurrentActor resuelto dentro del scope de
  cada intento. Usuario presente en la base sin sesión no basta para ejecutar la preparación.
- Usa el ejecutor común: operación → familias destino ordenadas → artículos ordenados.
  Relee y valida después de los bloqueos. Cada lote guarda asociaciones, políticas ausentes
  a cero, marca de uso previo, auditoría y OperationResult en una transacción. No resetea
  políticas existentes ni marcas de primer uso activadas, UUID, definición o stock.
- La proyección de mantenimiento admite FamilyId null en la lectura de filas H0 sobre la
  misma conexión/transacción; no crea otro DbContext ni materializa una Article incompleta
  con el mapping final. Actualiza mediante comandos parametrizados del mismo contexto.
- Respuesta 200: `{ operationId, preparedArticleIds }`; ResultHeaders vacío y permiso
  original MasterData.Manage. El replay devuelve ese resultado sin otra auditoría.
  La huella normaliza UUID y ordena entries por articleId; una clave con otro mapa da 409.

Después de comprobar cobertura, detener el proceso de mantenimiento, aplicar la migración
de cierre externamente y volver al modo ordinario. No hay actor configurado de respaldo,
credencial administrativa entregada a la API ni script de datos que acepte autoría manual.
Pruebas: 401/403, token ausente, intento de fijar actor, lote inválido, rollback inducido,
replay sin duplicados y ruta ausente fuera de su ventana de esquema/configuración.

## Errores particulares

Todos siguen ProblemDetails del contrato común, con code, traceId y errors por campo cuando
apliquen. El mismo rechazo no cambia el maestro, su auditoría ni su resultado confirmado.

| HTTP | Code | Caso |
|------|------|------|
| 400 | InvalidRequest | JSON, UUID, clave idempotente o formato If-Match inválidos/ausentes según corresponda. |
| 400 | AntiforgeryValidationFailed | Token ausente, ajeno o inválido. |
| 401 / 403 | Unauthenticated / Forbidden | Sesión/actor inexistente o permiso insuficiente. |
| 404 | NotFound | Recurso o resultado no observado; no sustituye el replay de una operación confirmada. |
| 409 | DuplicateCode | Código ocupado, también por un registro inactivo; errors indica code o sku. |
| 409 | ArticleDefinitionLocked | Intento de cambiar unidad, lotes o fracciones después del primer uso. |
| 409 | HistoryPreventsDeletion | Borrado con movimientos, documentos o reservas referenciados. |
| 409 | IdempotencyConflict | Misma clave con actor, método, recurso o contenido diferente. |
| 412 / 428 | VersionConflict / PreconditionRequired | Versión obsoleta / If-Match requerido. |
| 422 | ValidationFailed | Campos vacíos, longitudes, correo o referencias de alta/asignación inválidas; familia inactiva nueva incluida. |
| 503 | LockTimeout / DependencyUnavailable / ServiceUnavailable | Conflicto agotado, dependencia de integración ausente o indisponibilidad; Retry-After cuando sea recuperable. |

Una excepción de concurrencia no se devuelve como 500; índices únicos y FK restrictivas
se traducen sin exponer SQL. Un error inesperado mantiene el tratamiento genérico H0.
