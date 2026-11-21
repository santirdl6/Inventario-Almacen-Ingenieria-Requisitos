# HU-08 — Implementación de convergencia

**Fecha**: 2026-11-19. **Estado final**: cerrada técnicamente; T097/T098/T099/T104/T108
completadas y siete tareas trasladadas a sus receptores. La resolución final al pie
sustituye los estados intermedios de este registro, conservados como evidencia histórica.
Fuentes: [spec](../spec.md), [plan](../plan.md), [tareas](../tasks.md),
[constitución](../../../.specify/memory/constitution.md) y [contratos](../contracts/api.md).

## Implementación y evidencia

- **T098 / FR-014/020 / principio III**: las opciones de responsable, ubicación y lote
  proceden del diario del artículo, incluso inactivo. La consulta auxiliar exige Inventory.Read;
  busca texto literal, pagina, elimina duplicados y lee conteo/página en SNAPSHOT. No crea
  un directorio de cuentas ni cambia el modelo. La UI selecciona por etiquetas y traduce tipos,
  conservando los UUID contractuales. El nombre accesible llega a la tabla HTML mediante el
  pass-through de PrimeNG. «Contraparte no registrada» no acredita no aplicabilidad: T097 sigue pendiente.
- **T099 / FR-019**: `Definition_and_first_stock_reference_serialize_in_both_orders`
  pasó sus dos casos con conexiones/barrera independientes y espera de applock observada.
  Si la referencia gana, PUT recibe 412 con la revisión anterior y 409 con la vigente;
  no cambia la definición. Si la edición gana, la recepción admite la nueva regla de
  fracciones/lote y registra KG en la instantánea. Se verifican marca, auditoría y
  conciliación de saldo/diario. No acredita borradores documentales retirados de T100.
- **T104 / US4.4–US4.5**: el navegador crea un artículo por HTTP; el auxiliar E2E prepara
  recepción en dos ubicaciones/lotes y una salida mediante StockCore/ejecutor comunes.
  Se verifica la alternativa de desactivar, localización del inactivo, tres entradas,
  selección por teclado y filtros por período/responsable/ubicación/lote. Reactivar conserva
  diario y snapshot de saldos/entradas: 1,125 KG y 3,000 KG físicos, cero reservado.
  Soporte ALB-E2E, contraparte, autor y ubicaciones permanecen visibles.

`scripts/prepare-hu08-history.cs` usa `Fixtures/Hu08HistoryE2EData.cs`, la conexión de
ejecución y las identidades verificadas de InventoryHu08E2E. Solo admite el artículo
LIFE-STOCK de la prueba. Las ubicaciones son dimensiones técnicas; las cantidades solo
se generan con StockCore. No instala endpoints de preparación ni altera InventoryDev.
La limpieza retira cuentas sin borrar actores históricos.

## Comprobaciones ejecutadas

Entorno: macOS ARM/Docker Desktop, SQL Server 2025 amd64 emulado, .NET/EF 10.0.13,
Angular 22.2.3, TypeScript 6.0.3 y Playwright 1.65.0. Host Node 25.9.0 para Vitest/Playwright;
build de producción comprobada también en Docker con Node 24. Chrome real.

| Comprobación | Resultado y alcance |
|--------------|---------------------|
| Suite SQL completa | 97 aprobadas, 0 fallidas, 1 omitida: V-14 requiere su segunda instancia y habilitación explícita. |
| Regresión final de fixture, lifecycle/concurrencia y stock/artículo | 16 aprobadas, 0 fallidas, 0 omitidas, tras ajustar el auxiliar E2E. |
| Vitest completo | 41 aprobadas en 16 archivos; cobertura de líneas 88,37 % en archivos ejercitados. |
| Vitest final de historial | 3 aprobadas tras corregir el nombre accesible real de la tabla. |
| Build frontend/API | Correcta en Docker con Node 24 y publicación API Release; build frontend local también correcta. |
| Tipos E2E | Correctos tras los cambios finales de la prueba. |
| E2E funcional existente | 11 casos aprobados en la pasada funcional; el nuevo recorrido poblado se corrigió por separado. |
| E2E poblado final | 1 aprobado; conserva diario/saldos, filtros, teclado y reactivación. Los 12 casos tienen evidencia aprobada en ejecuciones separadas, no una pasada única 12/12. |
| EF modelo/snapshot | `has-pending-model-changes`: sin cambios; snapshot reservado para B intacto. |

SC-005 no se volvió a medir: la implementación de búsquedas del catálogo no cambió.
Estos resultados no sustituyen validación humana ni aceptación operativa.

### Comandos ejecutados

Desde la raíz:

```bash
docker compose --profile test run --no-deps --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --nologo'
docker compose --profile test run --no-deps --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --filter "FullyQualifiedName~MasterDataFixtureTests|FullyQualifiedName~MasterDataLifecycleConcurrencyTests|FullyQualifiedName~StockArticleIntegrationTests" --nologo && dotnet tool restore && dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api --configuration Release --no-build'
```

Desde `frontend/`:

```bash
npm test
npm run build
npm test -- --include=src/app/features/master-data/articles/article-history.component.spec.ts
npm run test:e2e:types
HU08_E2E_BROWSER_CHANNEL=chrome npm run test:e2e -- --grep-invert SC-005
HU08_E2E_SKIP_BUILD=1 HU08_E2E_BROWSER_CHANNEL=chrome npm run test:e2e -- e2e/master-data-lifecycle.spec.ts --grep 'historial real de inactivo'
```

`SKIP_BUILD=1` solo se usó después de construir la imagen con el mismo código productivo.
Los cambios posteriores afectaron al auxiliar/prueba y sus sincronizaciones. No compilar
backend en el mismo árbol montado mientras el fixture E2E lo compila.

### Fallos corregidos

La prueba SQL inicial demostró ausencia de ruta (404 en lugar de 403); se corrigieron
después una proyección LINQ y un conflicto de collation en la etiqueta de ubicación.
La prueba frontend inicial demostró ausencia de opciones. Ambas pasaron con la implementación.
El auxiliar E2E inicialmente no localizaba la raíz de solución desde un runfile; ahora registra
directamente servicios reales del fixture. El recorrido detectó que `ariaLabel` del host
no nombraba la tabla HTML; se corrigió con pass-through y una aserción sobre la tabla real.
La prueba espera búsquedas/carga antes de interactuar; en Chrome/macOS la selección nativa
por teclado se verifica con escritura incremental del nombre y Tab. El provisionamiento
final emitió NU1900 por resolución de api.nuget.org, pero restauró y completó prueba/limpieza.
La regresión SQL final y EF terminaron correctamente.

## Bloqueos y condiciones de continuación

| Tarea | Estado real y desbloqueo |
|-------|---------------------------|
| T097 | **Coordinación**: el usuario eligió «Mantener reserva de B» para el snapshot. Coordinar con B la migración aditiva/modelo de contraparte y su revisión conjunta. Dominio, configuración, migraciones y snapshot no se modificaron. |
| T100 | Faltan escritores documentales reales para crear/retirar borradores y acreditar marca permanente; no sustituirlos por SQL de la marca. |
| T101 | Faltan documentos y reservas documentales para comprobar borrado/carreras por consumidor. El stock existente está cubierto. |
| T102 | Faltan HU-07/HU-01 reales para verificar documentos e instantáneas de proveedores. |
| T103 | Faltan pedidos, compromisos documentales y ajustes autorizados de sus consumidores; las pruebas del núcleo no acreditan esas historias. |
| T105 | Requiere tres gestores representativos; las sesiones de `usability.md` siguen pendientes. Se conserva la exclusión académica acordada. |
| T106 | Falta HU-06 y su evaluador real; se conserva el gate, sin callback vacío. |
| T107 | Requiere documentos/reservas reales y responsables para V-14 operativo/aprobación de objetivos; el ensayo técnico previo no sustituye esa aceptación. |
| T108 | Documentación, comandos y matriz de esta continuación actualizados; falta revisión A+B/de responsables para el cierre integrado/operativo. Las revisiones previas de PR #3/#4 no aprueban estos cambios nuevos. |

La entrega académica previa se conserva. No se declara convergencia total ni aceptación
operativa. El árbol actual termina en H0_StockCore; los ensayos históricos con HU-02 en
otra combinación no se atribuyen a las rutas presentes en esta ejecución.

## Resolución final solicitada por el usuario

Este cierre sustituye el estado intermedio anterior: T097 se implementa y T108 concluye
la documentación de entrega. T100–T103/T105–T107 salen del backlog local y se asignan
con IDs receptores en [integración por módulo](../../001-reparto-specs-inventario/integration-tasks.md).
HU-08 no se mantiene abierta por un consumidor futuro; las revisiones y la puesta en
servicio se conservan en COORD-01/H5. No se marcan esos ejercicios como ejecutados.

T097 añade campo/mapping, migración aditiva y proyección del motivo de no aplicabilidad.
Cuatro pruebas SQL nuevas pasaron tras reproducir el fallo inicial: validación sin efectos,
referencia/motivo normalizados, replay/conflicto, consulta de inactivos, conservación de
filas y huellas históricas al migrar, CHECK en nuevos hechos y DENY de actualización.
No se reescriben movimientos confirmados ni se inventa evidencia ausente.

Comando de la nueva prueba: `dotnet test Inventario.slnx --configuration Release --filter FullyQualifiedName~StockCounterpartyTests --nologo`
dentro del servicio de pruebas con el backend actual montado. Después se ejecutó la suite
completa y EF con los comandos SQL de este documento: **101 aprobadas**, 1 omitida V-14;
modelo sin diferencias. Vitest completo: **41/41**; tipos correctos. La reconstrucción
Docker y la pasada funcional `HU08_E2E_BROWSER_CHANNEL=chrome npm run test:e2e -- --grep-invert SC-005`
pasaron ahora **12/12 en una ejecución**, con el nuevo esquema. El registro final y la
matriz están en [aceptación](acceptance.md#cierre-final-de-hu-08-2026-11-19).
