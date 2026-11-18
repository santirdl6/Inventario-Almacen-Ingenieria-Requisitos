# US2 — Catálogo de artículos: corte H1 verificado

**Fecha**: 2026-11-17. **Rama**: `a/h0-identity-article`, HEAD `e006513` más
cambios locales sin publicar. Fuentes: [spec](../spec.md), [tareas](../tasks.md),
[plan](../plan.md), [HTTP](../contracts/api.md) e [integración](../contracts/integration.md).

## Resultado y alcance

- **64/64 pruebas backend** aprobadas con SQL Server 2025 real en Docker Desktop macOS
  ARM, SQL amd64 emulado, contextos y bases/logins aleatorios del fixture.
- **22/22 unitarias frontend**, nueve archivos, con Node 24.15.0, Angular 22.2.3,
  Vitest 5.1.0 y jsdom 31.0.0. La cobertura corresponde a archivos ejercitados, no a HU-08 completa.
- **7/7 E2E** con Playwright 1.65.0: tres HTTP y cuatro recorridos Chromium;
  dos de navegador corresponden a artículos y dos a familias.
- Build de producción frontend y publicación de API en imagen Docker correctos.
  Tipado E2E correcto; EF 10.0.13 informa modelo sin cambios pendientes.
- No hay documentos, reservas, saldos, lotes ni movimientos integrados en el modelo H1.
  El alta solo crea Article y su StockPolicy inicial, además de evidencia y resultado;
  la prueba no sustituye una comparación de existencias de un núcleo futuro.

## Evidencia por escenario

| Escenario | Evidencia ejecutada | Resultado |
|-----------|---------------------|-----------|
| Q-01/Q-02 | `MasterDataArticleTests`: alta/detalle/PUT/listas; permiso real, administrador sin privilegios implícitos, CSRF; código normalizado, inválidos/internos, familia inexistente/inactiva y política cero. | Aprobado para Article H1. |
| Q-02/Q-10 | `article-form.component.spec.ts`, `articles-api.service.spec.ts`, `article-list.component.spec.ts` y `master-data-articles.spec.ts`: familia obligatoria, unidad/booleanos, selector buscable, controles bloqueados incluidos en PUT, readonly, errores preservados, filtros, cambio de definición sin uso y revisión de estado. | Aprobado; sin ubicación, cantidad ni mínimo editables en alta. |
| Q-03/Q-04 | SQL: 428/412, replay de alta/PUT con ETag original y conteo de evidencia/resultados. Unitario: rechazar contexto de otra revisión. Renombrar/desactivar familia conserva ETag propio del artículo. | Aprobado para los casos ejecutados; recorrido completo de fiabilidad sigue en US5. |
| Q-05 | `MasterDataFirstReferenceTests`: ausencia de consumidores acreditada por FK del modelo, rechazo de enlace ausente aun con lector declarado y marca monotónica de dominio. `MasterDataArticleTests`: edición sin uso y rechazo con marca histórica preparada. | H1 aprobado; **primer uso integrado pendiente**. La marca preparada por SQL no acredita crear/retirar un borrador real ni su carrera con edición. |
| Q-06 | Trigger de fallo de auditoría durante alta HTTP: rollback de Article, política, auditoría y OperationResult; reintento de la misma clave confirma. Fallo equivalente durante preparación H0 conserva FamilyId null y ausencia de política/resultado. | Aprobado en SQL real, sin sondas productivas. |
| Q-07 | Cuatro casos de `MasterDataConcurrencyTests`: alta y reasignación frente a desactivación de familia, ambos órdenes de commit; conexiones/peticiones independientes y barrera tras SaveChanges antes del commit. DMV confirma espera real por applock antes de liberar la primera transacción. | Aprobado: asignación anterior se conserva; posterior se rechaza 422; no desactiva Article en cascada. |
| Q-09 | `MasterDataFamilyTests` y `MasterDataArticleTests`: renombrar/desactivar familia conserva UUID, asociación, estado y política del artículo; editar otros campos admite la familia actual inactiva. | Aprobado para maestros. Operaciones de stock/alertas futuras pendientes. |
| Q-11 | `MasterDataMigrationTests`: reaplicación desde H0 vacío; expansión con FamilyId nullable y TargetModel concordante; fila H0 con FK técnica externa; cierre falla 51002 sin preparar; mapa autenticado, 401/403/CSRF/actor falso/lote inválido/fallo/replay; cierre conserva UUID, datos y vínculo. | Aprobado H0 técnico. FK de prueba no representa documentos/stock integrados. Revisión A+B pendiente. |
| Q-11 | Mantenimiento sin rutas ordinarias, health 503 incluso con esquema final; ruta ausente fuera de su ventana; conexión de ejecución sin permiso ALTER de base. | Aprobado; DDL ejecutado únicamente por migrador externo al proceso API. |

Un contexto con política ausente devuelve 503 `DependencyUnavailable`, nunca un mínimo cero
inventado. ArticleDetail representa únicamente campos propios con ETag; ArticleContext
se lee bajo SNAPSHOT, sin ETag, con `articleVersion`. El frontend rechaza revisiones distintas.

## Migraciones e integración

Tras `20261116180206_HU08Families` se añadieron, en la misma cadena:

1. `20261117095524_HU08MasterDataExpand`: FamilyId nullable, marca de definición y StockPolicy.
2. `20261117095527_HU08RequireArticleFamily`: precomprobación de asociaciones/políticas y cierre NOT NULL.

La prueba aplica y revierte el cierre; la expansión describe también en su Designer la
FK opcional. No se modificaron migraciones H0 previas. El snapshot final exige familia.
La preparación usa `PrepareHu08FamilyMapHandler`, sesión real, actor del scope del intento,
bloqueos familia→artículo, proyección nullable y transacción del ejecutor compartido.

`ArticleReferenceTracker` y `ArticleDefinitionReadiness` se registran para los futuros
escritores. Ninguno de esos escritores existe todavía: cada feature deberá aportar lector,
enlace de marca y sus pruebas reales antes de habilitarse. T045 acredita el gate H1;
T035 conserva pendiente su parte integrada de US2.8, junto con T075/T076.

## Comandos ejecutados

Desde la raíz, sobre el código montado y SQL de pruebas saludable:

```bash
docker compose --profile test run --no-deps --rm \
  --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c \
  'dotnet test Inventario.slnx --configuration Release --no-restore --nologo'
```

Resultado final 64/64. En la ejecución previa del mismo modelo productivo, después de
62/62 pruebas, se ejecutó también:

```bash
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api --configuration Release --no-build
```

Desde `frontend/`, con Node 24.15.0 en PATH y Chromium instalado:

```bash
npm run build
npm test
npm run test:e2e:types
npm run test:e2e
```

Build/tipos correctos; última suite unitaria 22/22. E2E 7/7 en 1,7 min, reconstruyendo la
imagen del mismo código productivo. Después solo se añadieron pruebas backend/unitarias.
Se provisionaron y retiraron cuentas temporales mediante Identity y se detuvo exclusivamente
el proyecto `inventario-hu08-e2e`, conservando volúmenes y actores históricos.

## Incidencias corregidas y límites de cierre

- Una búsqueda de opciones retiraba visualmente la familia seleccionada; un test reprodujo
  el fallo antes de corregirlo. Se conservan selección y familia actual al cambiar resultados.
- El listado filtraba sobre un constructor de record no traducible por EF y devolvía 500;
  filtros/orden/paginación ahora preceden a la proyección DTO. Se comprobó en SQL real.
- Un filtro UUID inválido llegaba como BadHttpRequestException y se convertía en 500;
  el manejador común conserva su estado HTTP y mensaje genérico seguro (400 en este caso).
- El modo mantenimiento no declara readiness normal, incluso si se inicia por error después
  del cierre de esquema. La ruta de preparación sigue ausente en ese estado.

No se declara aceptación completa de FR-009/019 ni del conjunto HU-08. Continúan pendientes
referencias reales y sus carreras, proveedores, retirada/historial, fiabilidad completa,
rendimiento, SC-006 humana, recuperación y revisión A+B. No se recibió aprobación de B.
