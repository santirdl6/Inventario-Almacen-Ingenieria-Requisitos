# Verificación de fundamentos compartidos

**Fecha**: 2026-11-17. **Tareas**: T006–T023.
**Estado**: implementados y verificados localmente; revisión de B pendiente antes de integrar.
**Código**: HEAD e006513 más cambios locales, sin commit nuevo.
**Fuentes**: [contrato de integración](../contracts/integration.md), [tareas](../tasks.md),
[coordinación](coordination.md) y [baseline previa](baseline.md).

## Base única implementada

OperationResult, RequestFingerprint, OperationResponse, InventoryLocks e
InventoryOperationExecutor residen en Infrastructure/Persistence/Operations. El ejecutor
abre un scope y DbContext nuevos en cada intento, verifica actor/permisos, adquiere el
applock de operación y resuelve replay antes del callback de negocio/precondición.
El callback comparte contexto/transacción y no confirma por su cuenta. El resultado
exitoso y los efectos se conservan juntos; las cabeceras se limitan a ETag/Location.

La huella usa método/ruta canónicos y JSON del DTO normalizado, con propiedades ordenadas
ordinalmente y arrays en el orden definido por cada contrato. La normalización de valores
de negocio pertenece al DTO/handler, no al hasher. If-Match y secretos de sesión no se incluyen.

La auditoría conserva actor, operación, entidad y snapshots con FK restrictivas a usuario/
resultado y sin FK al maestro que impida su borrado legítimo. El rol inventory_runtime
niega UPDATE/DELETE tanto en resultados como en auditoría. El bootstrap añade al usuario
configurable a ese rol sin entregarle DDL.

## Cadena de migraciones observada

1. 202610250001_InitialInfrastructure.
2. 20261029111255_H0Identity.
3. 20261112105657_H0ArticleReference.
4. 20261116172749_H0OperationProtocol.
5. 20261116174100_HU08AuditInfrastructure.
6. 20261116180206_HU08Families, el primer incremento de negocio.

Las tres anteriores se conservan. B debe generar StockCore sobre esta cadena integrada
y consumir las tablas/protocolo existentes, sin recrear resultados ni auditoría.

## Evidencia ejecutada

Comandos desde la raíz:

```bash
docker compose --profile test run --no-deps --rm \
  --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c \
  'dotnet test Inventario.slnx --configuration Release --no-restore --nologo'
```

**Resultado final: 47 pruebas aprobadas, 0 fallidas, 0 omitidas**, incluyendo baseline,
protocolo, validación, metadatos, consulta de resultados y familias. Se usó la imagen SDK
ya disponible montando el código actual; la construcción/publicación completa de la app
también pasó durante las ejecuciones E2E.

- OperationProtocolTests: misma intención una sola vez; respuesta/cabeceras originales;
  rollback de efectos/resultados; conflicto de contenido/actor y reautorización; dos llamadas
  solapadas con espera de APPLICATION lock observada en DMV SQL; fases invertidas rechazadas.
- Un deadlock SQL 1205 real inducido en tablas técnicas demuestra reintento completo con
  ContextId distinto y un máximo de tres intentos por operación; los efectos finales son exactos.
- Fallo inducido de inserción de auditoría revierte cambios previos del contexto y el resultado.
  Auditoría/resultados rechazan UPDATE/DELETE con el principal de ejecución —SQL 229—.
- EntityTagTests: versión fuerte coincidente, obsoleta 412, ausencia 428 y formatos inválidos 400.
- OperationQueryTests: sesión real, 401/403 y outcome 204 accesible con permiso original
  sin depender de que exista una fila del recurso, con no-store y 404 para resultado no observado.
- MasterDataUsageTests: lector real en modelo H0 sin consumidores; una FK no cubierta
  produce DependencyUnavailable. La prueba de metadatos no certifica integración de stock.

`dotnet ef migrations has-pending-model-changes` terminó sin diferencias. Los fixtures
crean bases/logins aleatorios y los eliminan sin modificar InventoryDev.

Desde frontend, Node 24.15.0:

```bash
npm test
npm run test:e2e:types
```

**15 unitarios aprobados** y tipos E2E correctos. Sesión/XSRF nativo, guards, administrador
sin permiso implícito, intención estable, doble clic, error/404 sin borrar referencia,
restauración DELETE con ETag y logout sin payload están cubiertos. La cobertura se refiere
a archivos ejercitados; no implica cobertura completa del inventario.

## Alcance y pendientes

Se verifican el protocolo común y el MVP de familias, no Receive/Reserve/Ship, stock negativo,
historial real ni las reglas completas de B. La reacción de consumidores futuros y la marca
de primera referencia se completan en US2/US4. La revisión A+B y la aceptación humana siguen
pendientes. No se modifican checklists reviewer-owned para acreditar código.

La resolución de intención sin payload tras logout y el alcance del ETag de futuros detalles
de artículo siguen siendo mejoras pendientes del análisis. No afectan al ETag de Family,
cuya representación contiene exclusivamente sus propios datos persistidos.
