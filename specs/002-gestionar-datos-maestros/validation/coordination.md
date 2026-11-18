# Coordinación de implementación HU-08

**Fecha**: 2026-11-12. **Tarea**: T001, inventario de entregas.
**Rama de trabajo**: `a/h0-identity-article`; HEAD `e006513`.
**Fuentes**: [plan](../plan.md), [integración](../contracts/integration.md) y
[plan global](../../../.specify/memory/project-plan.md).

## Estado comprobado

- H0 de A está presente: sesión Identity, ICurrentActor, Article mínima y su mapping.
- Cadena observada: `202610250001_InitialInfrastructure`,
  `20261029111255_H0Identity`, `20261112105657_H0ArticleReference`.
- No hay OperationResult, ejecutor de operaciones, applocks ni migración H0_StockCore
  en el árbol actual. No hay pantallas/login/layout comunes de B integrados en frontend.
- `git ls-remote --heads origin` solo muestra `main` en `04a1e17` y
  `a/h0-identity-article` en `e006513`; no hay una entrega de B publicada en ese remoto
  que pueda incorporarse durante esta comprobación. Esto no afirma nada sobre su trabajo local.

## Responsabilidades e interfaces

| Entrega | Responsable | Estado/ruta |
|---------|-------------|-------------|
| Article e ICurrentActor H0 | A | Implementados en Domain e Infrastructure existentes. |
| Resultado común | B, H0 | Ausente. Ruta prevista: `backend/src/Inventario.Api/Infrastructure/Persistence/Operations/OperationResult.cs`; confirmar con B al integrar. |
| Ejecutor y bloqueos | B, H0 | Ausentes. Rutas previstas: `Infrastructure/Persistence/Operations/InventoryOperationExecutor.cs` e `InventoryLocks.cs` dentro del proyecto API; confirmar con B. |
| Maestros y política inicial | A, HU-08 | Preparación todavía pendiente de fundamentos. |
| Sesión/layout frontend común | B, H0 | Pendiente de entrega o de acuerdo explícito para adelantarlo. |

## Orden y bloqueo

Completar la preparación T001–T005. **T006/T007 están bloqueadas por la ausencia de la
entrega común de B**; no habilitar escrituras de maestros ni crear una implementación
privada del protocolo. No se toma una ausencia de entrega como aprobación para sustituirla.

Después de integrar H0_StockCore sobre la cadena real, verificar resultado/cabeceras/
permiso, orden de bloqueos, scope de actor y restricciones SQL antes de continuar.
Las migraciones de A se generan de una en una sobre el snapshot integrado.

**Revisión A+B**: pendiente, sin aprobación registrada. Se mantiene también pendiente la
confirmación de rutas definitivas del componente compartido y de la base frontend.

## Reanudación 2026-11-16

Se repitió la consulta remota después de recuperar Docker: siguen disponibles únicamente
main en 04a1e17 y a/h0-identity-article en e006513. Se verificó otra vez la ausencia de
OperationResult/ejecutor y H0_StockCore en el árbol actual. T001–T005 quedan preparadas y
verificadas según [baseline](baseline.md); T006 es el siguiente bloqueo secuencial real.

## Aportación común autorizada, 2026-11-16

Ante la ausencia de entrega de B, el usuario eligió explícitamente «Adelantar base común»:
A implementa el único OperationResult, ejecutor y bloqueos en las rutas compartidas previstas,
con pruebas SQL reales, para desbloquear familias. Se levanta el bloqueo de autoría de
T006/T007; no se acredita por ello revisión o integración conjunta de B.

B consumirá esta misma base y continuará sus operaciones/entidades de stock sin duplicar
resultados ni protocolo. Las ampliaciones y migraciones permanecen pendientes de revisión
cruzada antes de fusionar. La base frontend mínima para el corte vertical también se prepara
como contribución común si sigue ausente, manteniendo su revisión con B pendiente.

## Estado verificado 2026-11-17

La base compartida y el MVP de familias están implementados en las rutas previstas,
con seis migraciones en la cadena y 47 pruebas backend, 15 unitarias frontend y 5 E2E
aprobadas. [Foundation](foundation.md) y [US1](us1.md) delimitan esa evidencia.
T006/T007 ya no esperan una implementación externa: B debe consumir esta base, revisar
sus interfaces/migraciones y añadir su StockCore sobre ella, sin otra tabla de resultados.
La revisión cruzada continúa pendiente y no se ha fusionado ni publicado un nuevo commit.

## Corte Article H1, 2026-11-17

La cadena añade HU08MasterDataExpand y HU08RequireArticleFamily sobre HU08Families,
con expansión/preparación autenticada/cierre comprobados. Catálogo de artículos y contexto
auxiliar disponibles con UI; 64 pruebas backend, 22 unitarias frontend y 7 E2E aprobadas.
El gate de primera referencia acredita ausencia de consumidores en el modelo H1 y rechaza
metadatos incompletos. No hay escritores documentales/stock reales integrados; su enlace,
retirada y carreras siguen pendientes en T035/T075/T076. Evidencia en [US2](us2.md).
La revisión A+B de interfaces, migraciones y fuente compartida continúa pendiente.


## Continuación H1, 2026-11-18

Proveedores y su migración HU08Suppliers están presentes; retirada sin uso y recuperación
de intenciones se verifican en SQL/frontend. Resultados consolidados en
[aceptación técnica](acceptance.md), con los pendientes integrados explícitos.
No hay aprobación nueva de B ni de responsables operativos. La cadena y el snapshot
continúan necesitando revisión cruzada; no se realizó commit, push ni fusión.
