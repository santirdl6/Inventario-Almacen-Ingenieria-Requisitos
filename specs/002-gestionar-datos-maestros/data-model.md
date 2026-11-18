# Modelo de implementación de HU-08

**Fecha de diseño**: 2026-11-12. Familias, auditoría y Article/StockPolicy implementados
en H1 el 2026-11-17; migraciones y evidencia en [US2](validation/us2.md). Supplier y
consumidores documentales/stock siguen pendientes.
**Fuentes**: [spec](spec.md), [research](research.md),
[modelo canónico](../../.specify/memory/project/data-model.md) y
[protocolo compartido](../../.specify/memory/project/contracts/stock-operations.md).
Las definiciones compartidas permanecen en la base global; aquí se concretan sus mappings,
validaciones y transición desde H0. No se redefinen saldos, reservas ni movimientos.

## Entidades de maestros

| Entidad | Campos y mapping particular de HU-08 |
|---------|--------------------------------------|
| Family | Id UUID generado en aplicación; Code nvarchar(64), Name nvarchar(200), IsActive bit, Version rowversion. Code y Name obligatorios. |
| Supplier | Id UUID; Code nvarchar(64), Name nvarchar(200), Email nvarchar(256) nullable, TaxIdentifier nvarchar(64) nullable, IsActive bit, Version rowversion. Code y Name obligatorios; correo no vacío válido. |
| Article | Ampliar la fila H0 conservando sus mappings: Id, Sku(64), Name(200), BaseUnit(32), AllowsFractional, TracksLot, IsActive y Version. Añadir FamilyId UUID obligatorio al cerrar transición e IsDefinitionLocked bit inicialmente false. |
| MasterDataAudit | Id UUID, EntityType nvarchar(32), EntityId UUID histórico, Action nvarchar(24), Before/After nvarchar(max) JSON nullable según acción, ActorId UUID, RecordedAt datetimeoffset UTC, OperationId UUID. Solo inserciones. |

- Code/Sku: Trim + ToUpperInvariant, comparación única SQL `Latin1_General_100_CI_AS`,
  con registros inactivos incluidos. Mantener PK UUID distinta del código mutable.
- Name: Trim, no vacío, sin exigir unicidad. Opcionales vacíos se normalizan a null;
  email se conserva como contacto normalizado con Trim, sin volverlo una clave única.
- Longitudes se validan antes de persistir; SQL conserva NOT NULL, restricciones no vacío,
  índices únicos y FK. La normalización del comando y la huella de operación son coherentes.
- Article→Family: muchos a uno, FK restrictiva. Nueva asociación exige familia activa;
  conservar la familia inactiva actual permite cambiar otros campos.
- Cambiar Code/Sku/Name no sustituye la identidad ni reescribe instantáneas de documentos.

## Dependencias compartidas consumidas

| Entidad | Uso y límites |
|---------|---------------|
| StockPolicy | PK/FK ArticleId con relación uno a uno; MinimumAvailable decimal(18,3) ≥ 0 y Version rowversion. HU-08 crea la fila a cero. HU-06 mantiene configuración y evaluación. |
| OperationResult | Entidad común del ejecutor, no tabla específica de maestros. Contrato completo en el modelo global: resultado, cabeceras reproducibles y permiso original; sin FK a Article que impida borrarlo. |
| ApplicationUser | ActorId verificado mediante ICurrentActor. Las FK de auditoría/resultados a usuarios históricos son restrictivas. |
| Documentos, reservas, StockMovement/StockEntry | Los define cada feature consumidora. HU-08 consulta referencias e historial reales y no crea otra versión de ellos. |

StockPolicy se elimina junto con un Article cuyo borrado sea legítimo, dentro de la misma
transacción; no se deja una política huérfana. Esa relación de propiedad no cuenta como uso
documental ni impide por sí sola el borrado. No hay cascadas a diario, reservas o documentos.
Lot y StockBalance no se crean desde el alta de HU-08; su creación corresponde al núcleo.

## Marca de definición y ciclo de vida

- `IsDefinitionLocked`: false → true únicamente. La marca cambia en el commit que incorpora
  la primera referencia, incluso de borrador, y actualiza la rowversion de Article.
- Una retirada posterior de líneas no vuelve a false. Estado inactivo y saldo cero tampoco
  la restablecen. No es un campo aceptado en POST/PUT de maestros.
- Unidad base, lotes o fracciones: solo editables con marca false y ausencia de uso real,
  comprobadas bajo el bloqueo de artículo. Un payload que mantiene los mismos valores
  no constituye cambio de definición y puede editar los restantes campos.
- IsActive: activo ↔ inactivo, con auditoría y permisos. Los efectos sobre operaciones y
  alertas siguen la base global; no se cambian cantidades al editar el estado.
- Borrado físico: solo Article sin documentos, reservas ni movimientos actuales/históricos
  referenciados. La marca de definición sola no es motivo de prohibición. Audit/OperationResult
  quedan como evidencia aun cuando la fila del artículo ya no exista.
- Family y Supplier no tienen borrado HTTP en esta feature.

## Auditoría y resultado de operación

Acciones: Create, Update, Deactivate, Reactivate y Delete. Un PUT con cambio de estado
registra la acción de estado y todos los cambios de su snapshot en una única evidencia;
una operación exitosa genera una evidencia por maestro afectado, no otra al reproducirla.
Before es null en Create y After es null en Delete. Un PUT válido sin diferencias puede
registrarse como Update; conserva su propio resultado sin inventar cambios de stock.

EntityId **no tiene FK al maestro**. ActorId y OperationId usan referencias restrictivas
a Identity y OperationResult. Before/After preservan identidad, código y datos del maestro;
no contienen contraseñas, tokens ni cookies. Índices: entidad/ID/fecha e identificador de
operación; una misma operación no duplica la evidencia de la misma entidad/acción.

El rol de ejecución puede INSERT/SELECT, pero tiene DENY UPDATE/DELETE sobre auditoría
y resultados confirmados; la migración prepara restricciones para `inventory_runtime`
y el inicializador añade al usuario de ejecución. Los permisos se verifican con ese
usuario, no solo con una conexión administrativa de pruebas.

## Transición desde H0

La expansión se puede materializar en incrementos seriales por historia: `HU08Families`
para el MVP de familias, `HU08MasterDataExpand` para la ampliación Article/política y
`HU08Suppliers` para proveedores, además de la infraestructura común de auditoría.
No se obliga al MVP de familias a introducir el modelo de Supplier/Article aún sin implementar.
El cierre de familia obligatoria siempre ocurre después de su preparación explícita.

1. Antes de generar migraciones, integrar el último snapshot común y resolver con B las
   entidades transversales. Conservar `H0Identity` y `H0ArticleReference` ya existentes.
2. Migración `HU08MasterDataExpand`: crear catálogos/auditoría y política si no está ya
   integrada; añadir FamilyId temporalmente nullable e IsDefinitionLocked. Evitar crear
   otra StockPolicy u OperationResult si la cadena integrada ya las incluye.
3. Preparación administrativa explícita mediante handler HTTP de mantenimiento autenticado:
   completar asociaciones H0 a familias válidas, crear políticas ausentes a cero y marcar
   uso previo real con ICurrentActor y el ejecutor común. El mapa no contiene ActorId; cada
   lote guarda asignaciones, políticas, auditoría y resultado conjuntamente. Conservar
   marcas de primer uso ya activadas, UUID, definiciones y datos de stock. El contrato de
   mantenimiento está en [HTTP](contracts/api.md#mantenimiento-autenticado-de-la-transición-h0).
4. Migración `HU08RequireArticleFamily`: comprobar cobertura y aplicar NOT NULL/FK restrictiva.
   Si hay referencias sin clasificar o adaptadores de uso ausentes, abortar; no completar
   con valores ficticios. Los dos pasos de esquema son entregas coordinadas, no un arranque
   que aplique ambas antes de permitir la preparación cuando esta sea necesaria.
5. Habilitar los comandos de Article únicamente con esquema final y registro de consumidores
   completos. Antes de admitir cambios de definición, verificar para cada consumidor presente
   lector y enlace probado de primera referencia; si falta alguno, rechazar el cambio con
   DependencyUnavailable. Sin consumidores, acreditar esa ausencia; los futuros se integran
   antes de habilitar sus escritores. Actualizar fixtures H0 de A/B para preparar familia y política.

En una base vacía los pasos 3–4 no requieren crear maestros de negocio. En una base con
artículos, la transición se ejecuta en mantenimiento y con respaldo; no se presupone que
`docker compose up` pueda resolver por sí solo las asociaciones. El flujo de desarrollo
de una nueva base vacía conserva el arranque con un comando del plan global.

En el modo de mantenimiento se conservan sesión/antiforgery de Identity y la conexión de
ejecución sin DDL; no se registran los endpoints ordinarios de maestros ni otros escritores
de negocio. Las migraciones se aplican externamente con las credenciales administrativas.
Para leer las asociaciones todavía nulas, el handler usa una proyección de mantenimiento
con FamilyId nullable sobre la misma conexión/transacción, sin materializar Article como
si ya cumpliera el esquema final ni crear otro InventoryDbContext. Las actualizaciones son
parametrizadas, respetan los bloqueos comunes y dejan Before/After históricos correctos.

## Consultas y consistencia

Listas por Code/Sku e Id estables; índices de código existentes y de FamilyId/IsActive
según las consultas. Conteo+items y detalle+capacidades usan una lectura SNAPSHOT coherente
si requieren varias sentencias. No se usan proyecciones diferidas ni lecturas sucias.
La búsqueda por nombre es parametrizada; el presupuesto se comprueba con 10.000 artículos.
ETag representa rowversion, no fecha; los permisos y capacidades se revalidan al guardar.

## Pruebas de integridad específicas

Preservar UUID y referencias al migrar; rechazar datos sin familia; unicidad simultánea;
marca permanente incluso retirando una línea; ambos órdenes referencia/edición y
referencia/borrado; cambio de estado frente a nuevas operaciones; atomicidad de maestro,
política, auditoría y resultado; replay tras borrar; DENY efectivo de evidencias; historial
intacto y ausencia de efectos sobre saldos. Ejecución en [quickstart](quickstart.md).
