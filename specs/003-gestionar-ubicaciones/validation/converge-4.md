# /speckit-converge — iteración 4

**Fecha**: 2026-11-19 · **Rama**: `b/hu-02-ubicaciones-v2` (`c609656`, tras la iteración 3)
**Fuentes de intención**: [spec.md](../spec.md), [plan.md](../plan.md), [tasks.md](../tasks.md),
contratos ([API](../contracts/locations-api.md), [UI](../contracts/locations-ui.md)) y
[constitución](../../../.specify/memory/constitution.md).
**Resultado**: ✅ `converged` — la implementación cumple la spec, el plan y las tareas.
`tasks.md` no se modifica en esta iteración.

## Desviaciones ya registradas (no son brechas)

| Elemento | Situación |
|----------|-----------|
| SC-005 | Rendimiento fuera de esta entrega (plan § Complexity Tracking, D-13). HU-02 no se da por aceptada sin él. |
| SC-006 | Validación con operarios fuera de esta entrega (ídem; constitución III). |
| T034 | Conteo abierto bloqueado por HU-04: la parte de conteos de FR-012, US3.3, US4.3 y SC-004 sigue sin demostrar; el código responde `CountScopeFrozen` cuando `ILocationUsage` lo indique. |
| T001 | Revisión de A de `ReceivingZonesAsync` y de los cambios en sus archivos (commit `9be62e3`); bloquea la fusión. |
| T068 | Cierre documental en la base común, tras la aprobación de A. |

## Hallazgos anteriores

| Iteración | Hallazgos | Estado |
|-----------|-----------|--------|
| [1](converge-1.md) | F1 enlace al duplicado inactivo · F2 paginación del contenido · F3 enlace al contenido en el formulario · F4 artículo inactivo | Resueltos (T070–T073) |
| [2](converge-2.md) | F5 404 del detalle · F6 foco al cerrar el diálogo | Resueltos (T074–T075) |
| [3](converge-3.md) | F7 503 LockTimeout con Retry-After · F8 longitudes, `aria-describedby`, `asOf` | Resueltos (T076–T077) |

## Comprobación final

- **FR-001…FR-021**: todos con código y prueba (FR-012 salvo conteos, T034).
- **Escenarios US1.1…US5.4**: cada uno ejercitado por una prueba de API
  (`Locations/`, 23 pruebas), una spec de Vitest o el E2E.
- **Casos límite (9)**: cubiertos. El de «existencias a cero con reservas pendientes» no
  puede darse: `CK_StockBalances_Reserved` exige `Reserved <= Physical`, así que una
  reserva viva implica físico; L-08 comprueba el rechazo con una reserva viva.
- **SC-001, SC-002, SC-003, SC-007**: cubiertos; SC-004 salvo conteos (T034).
- **Tabla de errores del contrato API (9 filas)**: todas con prueba salvo `CountScopeFrozen`
  (T034).
- **Contrato UI (14 obligaciones)**: todas con spec o E2E.
- **Plan (D-01…D-13)**: la estructura y las decisiones están en el código; los cambios en
  archivos de A van en su propio commit.
- **Constitución I–V**: sin violaciones; III con la desviación registrada de SC-006.
- **Código no pedido**: ninguno. La búsqueda de artículos usa `GET /articles` de A, sin
  endpoints nuevos.

## Métricas

- Requisitos y escenarios comprobados: 21 FR, 7 SC, 25 escenarios, 9 casos límite.
- Contratos: 9 filas de errores API y 14 obligaciones UI. Decisiones del plan: 13.
- Hallazgos: 0 (missing 0 · partial 0 · contradicts 0 · unrequested 0).
- Estado de las pruebas: backend 112/112, frontend 98/98, build de producción sin avisos;
  E2E con la imagen reconstruida en `c609656`: ubicaciones 2/2 y entorno de A 3/3.

## Siguiente paso

Revisión y PR. No hace falta otra pasada de implementación para el alcance especificado.
