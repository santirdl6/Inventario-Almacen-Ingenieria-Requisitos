# Quickstart: HU-04 — Validar conteos y ajustes

Guía de validación de la feature. Preparación del entorno, cuentas y receta Docker: la
[guía común](../../.specify/memory/project/quickstart.md) y la receta de specs/003 (no hay SDK local).
Rutas y errores: [contrato API](contracts/cycle-counts-api.md); pantallas:
[contrato UI](contracts/cycle-counts-ui.md); invariantes: [data-model](data-model.md#invariantes-comprobables-quickstart-c-14).

Nada de lo siguiente está ejecutado: es la matriz que las tareas deben dejar en verde.

## Comandos

- Backend: suite completa con la receta de specs/003 sobre `git archive HEAD`; línea base 291
  aprobadas, 1 omitida (V-14), 0 fallidas (HU-03 tras C4).
- `dotnet ef migrations has-pending-model-changes`: sin cambios.
- Frontend: `ng test --watch=false`; línea base 187/187. `tsc` de app, specs y E2E y `npm run build`.
- E2E: Playwright `--grep-invert SC-005`, proyecto `inventario-hu08-e2e`; línea base 17/17.

## Pruebas automatizadas (backend)

| ID | Escenario | Resultado esperado | Spec | V común |
|----|-----------|--------------------|------|---------|
| C-01 | Esquema: tablas, índice filtrado, `DENY DELETE`, triggers 51401–51407 con `inventory_runtime`, `HasTrigger`, conjunto exacto de FK a `Article` con `CountLine` | Cada trigger rechaza con su código y no con 3609; la conexión sigue utilizable | FR-013, D-07 | — |
| C-02 | `StockCore` `Adjust`: subida con lote nuevo, bajada, bajada por debajo del reservado, bajada > físico, artículo inactivo | Entradas `AdjustUp`/`AdjustDown`; `ReservedStockConflict`; `InsufficientStock`; inactivo admitido | FR-010, FR-025 | — |
| C-03 | Crear y editar borrador; ubicación inactiva o repetida; sin permiso | 201/200 sin congelar; 422; 403 | FR-001, US1.1 | — |
| C-04 | Iniciar con físico 10 de A y 5 de B en U1 | `Counting`, U1 congelada, dos líneas con su teórico; saldos sin cambios; primera referencia marcada | FR-002, US1.2 | V-09 |
| C-05 | Iniciar con U1 ya en otro conteo abierto | 409 `CountScopeFrozen` con el código del otro conteo | FR-003, US1.6 | — |
| C-06 | Contar 8 y 5; cero explícito; fracción no admitida; enviar con una línea sin contar | Diferencias −2 y 0; 422 por línea; `AwaitingApproval` solo con todas contadas | FR-004–FR-007, US1.3–US1.5 | V-09 |
| C-07 | Hallazgo de C (3) en U1; fuera del alcance; sin lote; retirar hallazgo; retirar esperada | Línea con teórico 0; 422; retirada con referencia retirada; 422/51403 | FR-006, US5 | V-10 |
| C-08 | Revisar y confirmar: sin motivo, con motivo, sin diferencias | 422; un movimiento con entradas −2 (y +3), físico = contado, aprobación registrada, alcance liberado; sin diferencias, sin movimiento | FR-008–FR-010, US2.1–US2.5 | V-09 |
| C-09 | Contado 2 con reservado 3: confirmar; liberar 1 durante el conteo; confirmar de nuevo | 409 `ReservedStockConflict` conservando la congelación; liberar admitido; luego confirmado | FR-011, FR-015, US2.4, US3.3 | V-10 |
| C-10 | Físico cambiado sin pasar por la congelación (escritura directa de prueba) | 409 `CountTheoreticalChanged`, sin cambios | FR-011, D-05 | — |
| C-11 | Cancelar desde cada estado con motivo; sin motivo; Operario | Líneas conservadas, saldos sin cambios, alcance liberado; 422; 403 | FR-012, US2.6–US2.7 | V-10 |
| C-12 | Con U1 congelada: confirmar salida, reservar, desactivar U1 vacía y cambiar su tipo; `inOpenCount`; borrador sin congelar | 409 `CountScopeFrozen` sin cambios; `inOpenCount = true`; el borrador no bloquea; tras cerrar, todo admitido | FR-014, FR-016, US3 | V-09 |
| C-13 | Carreras con `LockBarrier` en ambos órdenes: iniciar frente a confirmar salida, reservar y desactivar U1; confirmar frente a liberar | Uno completo primero; teórico = físico tras la operación ganadora; nada perdido ni duplicado (20 rondas) | FR-017, SC-003 | — |
| C-14 | Invariantes 1–7 del data-model en cada confirmación de la batería | Todas se cumplen | SC-001 | — |
| C-15 | Añadir y retirar un hallazgo frente a edición de definición y borrado del artículo (ambos órdenes) | Serializados por `article:{id}`; sin artículo borrado con referencias; tras la retirada, edición y borrado admitidos si no hay otras referencias | FR-024, INT-HU04-01 | — |
| C-16 | Conteo de un artículo inactivo con diferencia | Ajuste con motivo y autorización; historial y unidades conservados | FR-025, INT-HU04-02 | V-12 |
| C-17 | Replay de cada escritura; misma clave con otro cuerpo; `If-Match` viejo o ausente | Mismo resultado sin segundo ajuste; 409 `IdempotencyConflict`; 412/428 | FR-026, FR-027, SC-004 | V-07 |
| C-18 | Fallo inducido tras guardar en la confirmación (`FailAfterSaveInterceptor`) | Nada aplicado o todo aplicado; `GET /operations/{id}` coherente | FR-028 | V-08 |
| C-19 | Corrección: salida de 6 desde U1 cuando salieron 4; contar U1 = físico teórico + 2; vincular y confirmar | Físico +2; línea vinculada; salida, `ShippedQuantity`, consumos y remanentes sin cambios | FR-018, FR-019, US4, SC-005 | — |
| C-20 | Vínculo a salida en borrador, inexistente o que no tocó el bucket; en una línea sin diferencia; candidatas del bucket | 422 por línea; candidatas solo confirmadas y del bucket | FR-020, US4.4 | — |

## Frontend y E2E

- Vitest: lista, nuevo, detalle por estado (contar, hallazgo, enviar, revisar, vincular, confirmar,
  cancelar), foco al primer error, 412 con relectura sin perder lo escrito, 409 con relectura,
  operación visible al recuperar, todas las páginas en los selectores.
- E2E-03: operario crea e inicia un conteo, cuenta con una diferencia; responsable revisa con motivo y
  confirma; la consulta de existencias muestra el físico ajustado y U1 deja de estar en conteo.

## Fuera de alcance de esta entrega

- SC-006: 25 sesiones, conteo de 50 líneas, p95 ≤ 3 s. Requiere carga y datos sintéticos.
- SC-007: validación con trabajadores representativos (principio III). Guion: un operario cuenta una
  ubicación con una diferencia y un hallazgo; el responsable revisa y autoriza; se anota si
  terminaron sin ayuda técnica y sin errores no detectados.

## Evidencia

Se completará por tarea con fecha, commit, entorno, resultado e incidencias.
