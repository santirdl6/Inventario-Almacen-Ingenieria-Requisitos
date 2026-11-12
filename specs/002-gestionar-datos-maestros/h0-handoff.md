# Preparación H0 de A para desbloquear el núcleo de B

**Fecha**: 2026-11-12. **Estado**: implementado y verificado localmente; revisión de B pendiente.
**Alcance**: Article mínima, referencia de actor y configuración de persistencia compartida.
Esta preparación no equivale a la implementación completa de [HU-08](spec.md).

## Base y plan acotado

- [Constitución](../../.specify/memory/constitution.md), especialmente II, IV y V.
- [Plan global](../../.specify/memory/project-plan.md) y [coordinación H0–H5](../001-reparto-specs-inventario/plan.md).
- [Decisión R-10](../../.specify/memory/project/research.md#r-10-desbloqueo-de-referencias-e-identidad-del-núcleo-h0).
- [Modelo común](../../.specify/memory/project/data-model.md), [contrato HTTP](../../.specify/memory/project/contracts/api.md)
  y [protocolo de stock](../../.specify/memory/project/contracts/stock-operations.md).
- [Validación conjunta](../../.specify/memory/project/quickstart.md) y [spec HU-08](spec.md).

Secuencia de preparación realizada:

1. Adelantar una única entidad Article referenciable, con configuración separada y migración
   aditiva sobre Identity. Conservar las migraciones previas y la definición completa de HU-08.
2. Exponer un contrato de actor independiente del transporte, implementado con Identity y
   sin ActorId suministrado por el cliente ni actor predeterminado fuera de HTTP.
3. Comprobar persistencia y concurrencia optimista del artículo, actor autenticado y ausencia
   de regresiones de Identity en SQL real; registrar aquí evidencia y obligaciones de B.

## Contrato disponible para B

### Article

- Clase pública: `Inventario.Api.Domain.MasterData.Article`.
- Archivo: `backend/src/Inventario.Api/Domain/MasterData/Article.cs`.
- Contexto: `InventoryDbContext.Articles`; tabla `Articles`; clave `Id` de tipo `Guid`.
- Constructor: `new Article(sku, name, baseUnit, allowsFractional, tracksLot)`.
- Propiedades: `Id`, `Sku`, `Name`, `BaseUnit`, `AllowsFractional`, `TracksLot`, `IsActive`, `Version`.
- Identificador generado por la aplicación; activos al crear. SKU único sin distinguir
  mayúsculas, normalizado con Trim y mayúsculas; unidad también normalizada. Límites:
  SKU 64, nombre 200, unidad 32. `Version` es rowversion generada por SQL.
- Configuración: `Infrastructure/Persistence/Configurations/ArticleConfiguration.cs`.

Ejemplo de preparación controlada de pruebas:

```csharp
var article = new Article("ART-01", "Artículo de prueba", "UD", false, true);
database.Articles.Add(article);
await database.SaveChangesAsync(cancellationToken);
// Usar article.Id, no un Guid inventado sin fila Article.
```

Las altas de negocio con familia obligatoria, StockPolicy inicial y auditoría se implementan
en HU-08. No hay endpoint de alta provisional ni seed automático de artículos. La futura
migración de familia debe asignar una familia válida a referencias existentes sin borrarlas.
El constructor de preparación no valida permisos ni genera auditoría de negocio.

### Actor

- Interfaz pública: `Inventario.Api.Domain.Identity.ICurrentActor`.
- Método: `Task<Guid> GetRequiredActorIdAsync(CancellationToken cancellationToken = default)`.
- Implementación: `Infrastructure/Identity/IdentityCurrentActor.cs`, registrada scoped junto
  con `IHttpContextAccessor` por `AddInventoryIdentity`.
- Usa el principal autenticado y su claim `NameIdentifier`; exige Guid no vacío y usuario
  existente en Identity dentro del mismo `InventoryDbContext` del comando.
- Sin identidad válida lanza `UnauthenticatedActorException`; HTTP devuelve 401,
  `application/problem+json`, code `Unauthenticated` y traceId.
- `/api/v1/auth/me` ya consume esta implementación. El servicio proporciona identidad;
  el permiso de cada operación sigue siendo obligatorio en su endpoint/handler.

Consumo desde un handler:

```csharp
var actorId = await currentActor.GetRequiredActorIdAsync(cancellationToken);
// Utilizar actorId al crear el documento/diario; nunca el valor recibido del cliente.
```

En pruebas del núcleo puede sustituirse por una implementación fija:

```csharp
sealed class TestActor(Guid userId) : ICurrentActor
{
    public Task<Guid> GetRequiredActorIdAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(userId);
}
```

Cuando se prueben claves foráneas de actor, `userId` debe corresponder a un usuario creado
en la base aislada de Identity. No registrar esta sustitución en la aplicación real ni
reemplazar con ella las pruebas HTTP de sesión y permisos.

## Persistencia y migraciones

Cadena actual:

1. `202610250001_InitialInfrastructure`.
2. `20261029111255_H0Identity`.
3. `20261112105657_H0ArticleReference`.

La última migración solo crea Articles, sus restricciones e índice; no altera Identity.
El snapshot incluye ambos modelos. `InventoryDbContext.OnModelCreating` conserva
`base.OnModelCreating` y aplica las configuraciones del ensamblado.

Para el trabajo de B:

- Integrar esta base antes de generar `H0_StockCore`. Si ya generó una migración local no
  compartida sobre otro snapshot, regenerarla después de integrar; no borrar migraciones
  aplicadas ni resolver el snapshot solo por concatenación de conflictos.
- Añadir Lot, StockBalance y StockEntry con FK a `Article.Id` y borrado restringido.
  La relación artículo/lote del saldo debe conservar la FK compuesta del modelo global.
- Mantener configuraciones por entidad en archivos separados. `Set<T>()` permite trabajar
  con entidades nuevas sin obligar a añadir un DbSet por cada una al archivo compartido.
- Compartir contexto y transacción con los documentos; el núcleo no hace commit autónomo.
- Obtener el actor validado, autorizar y seguir el orden operación idempotente → documentos
  → ubicaciones → artículos. Los órdenes, nombres de bloqueo y reglas siguen el contrato
  global de stock, sin crear una variante local.
- Conservar UUID de artículo/actor en los fixtures. Los datos de inventario se preparan por
  operaciones del núcleo y diario, nunca insertando saldos manuales.

Los cambios están en el árbol de trabajo local. B necesita incorporarlos desde la entrega
integrada del equipo antes de generar su migración; no se presupone que ya estén en main.
La revisión cruzada de entidades compartidas, modelo y snapshot queda pendiente de B.

## Verificación realizada

**Entorno**: Docker Desktop, SDK .NET 10 en contenedor y SQL Server real de Compose.
Sin SDK local. Bases y logins aleatorios, aislados de InventoryDev, eliminados al finalizar.

**Resultado**: 18 pruebas aprobadas, 0 fallidas, 0 omitidas:

- 8 pruebas previas de Identity: sesión, antiforgery, permisos, bootstrap repetible,
  protección de cookies y sesión recuperable por otra instancia.
- 3 pruebas de Article: persistencia/normalización/unicidad SQL, rowversion con dos contextos
  y rechazo de edición obsoleta, fallo inducido después de escribir y antes del commit
  que revierte el alta técnica.
- 5 casos de actor inválido: anónimo, claim ausente, malformado, Guid vacío y usuario inexistente.
- 1 caso sin contexto HTTP y 1 recorrido HTTP con sesión real, ActorId ajeno ignorado en
  login y rechazo después de borrar al usuario de Identity.

`dotnet ef migrations has-pending-model-changes` confirma que modelo y snapshot coinciden.
La lista de migraciones sin conexión confirma la secuencia anterior; la aplicación efectiva
de la cadena y su reinicialización se verifican en las bases SQL aisladas del fixture.

El intento de `docker compose --profile test run --build --rm tests` agotó el tiempo
descargando una nueva imagen base. La compilación y las 18 pruebas se completaron usando
el código actual montado en la imagen SDK de pruebas ya existente; comando exacto en la
[guía común](../../.specify/memory/project/quickstart.md#desbloqueo-a--b-article-e-icurrentactor).
No se actualizó InventoryDev ni se acredita una reconstrucción completa de la imagen app.

## Siguiente entrega de B

Esqueleto de stock y Receive/Reserve/Ship mínimos para demostrar en SQL Server real:
reintento sin duplicados, rollback sin escrituras parciales y operaciones solapadas que
conservan físico no negativo y reservas no superiores al físico. Documento, diario, saldos,
reservas aplicables y OperationResult se confirman conjuntamente.

Estas pruebas de protocolo aún corresponden a B: las pruebas de Article no demuestran
idempotencia ni concurrencia del stock. B concreta CustomerOrder mínimo en HU-03; las
reglas completas de operaciones y conteos permanecen en las historias correspondientes.
