# /speckit-converge — iteración 5

**Fecha**: 2026-11-19 · **Rama**: `b/hu-02-ubicaciones-v2` (`1cbd392`)
**Fuentes de intención**: [spec.md](../spec.md) (con la tercera sesión de clarify), [plan.md](../plan.md),
[tasks.md](../tasks.md), contratos ([API](../contracts/locations-api.md), [UI](../contracts/locations-ui.md))
y [constitución](../../../.specify/memory/constitution.md).
**Resultado**: ✅ `converged` — la implementación cumple la spec, el plan y las tareas. `tasks.md`
no se modifica en esta iteración.

## Motivo de la iteración

Correcciones tras la [prueba manual del 2026-11-19](manual-2026-11-19.md):

- **Caso 2** («  nay - a2  » guardado con espacios interiores): ambigüedad resuelta en la
  tercera sesión de clarify (FR-002, FR-003, US1.6, SC-002 y casos límite) e implementada en
  T078–T082 (Phase 19).
- **Caso 12** («Localizar un artículo»): no reproducible por el equipo; la base de E2E no tiene
  artículos. No se ha cambiado la ruta; se añadieron `locations.routes.spec.ts` (navegación
  real con el router, comprobado que falla si se rompe la ruta) y un paso en el E2E.

## Desviaciones ya registradas (no son brechas)

SC-005 y SC-006 (fuera de esta entrega), T034 (conteos, HU-04), T001 (revisión de A, que ahora
incluye también la regla de caracteres, D-14) y T068 (cierre documental). Sin cambios.

## Verificación de lo nuevo

| Clave | Estado | Evidencia |
|-------|--------|-----------|
| FR-002 (caracteres del código) | Cumple | `Location.Normalize`/`HasOnlyAllowedCharacters`, CHECK `CK_Locations_Code_Chars`; L-16 |
| FR-003 (caracteres de pasillo, estantería y nivel) | Cumple | Mismos mecanismos en las tres columnas; L-16 (aisle, rack, level) |
| US1.6 (alta o corrección rechazada en el campo, lo escrito se conserva, «p01-e01» → «P01-E01») | Cumple | L-16 (alta y PUT); spec del formulario (caso 2, minúsculas válidas, 422 del servidor) |
| SC-002 (caracteres no admitidos impiden el guardado) | Cumple | L-16: nada creado ni modificado |
| Caso límite: espacio interior rechazado, no convertido | Cumple | L-16 y spec del formulario con «  nay - a2  » |
| Caso límite: acentos y Ñ rechazados aunque la base los ordene en A–Z | Cumple | Lista ASCII en dominio e interfaz; CHECK con `Latin1_General_BIN2` (L-16 con INSERT directo de «ÁREA») |
| Caso límite: «ı» que `ToUpperInvariant` convertiría en «I» | Cumple | Comprobación antes de pasar a mayúsculas, también en `LocationWrite.Read`; L-16 |
| Caso límite: base con valores que incumplen → la actualización se detiene con mensaje claro | Cumple | `HU02_LocationCharacters` (THROW 50003 con las ubicaciones), demostrado en una base aparte; registrado en quickstart § Evidencia |
| FR-019 (cómo corregir) | Cumple | Mensaje con los caracteres admitidos y ayuda de cada campo (spec del formulario) |
| US2.1 / caso 12 | Cumple | `locations.routes.spec.ts` y E2E (clic y Enter) |
| Plan D-14 y punto para A | Cumple | research D-14; plan § Lista de puntos (pendiente de A) |

## Comprobación del resto

- FR-001…FR-021, 26 escenarios (US1.6 nuevo), 10 casos límite, 9 filas de errores del contrato
  API (la de 422 ampliada) y 15 obligaciones del contrato UI (la de caracteres nueva): código y
  prueba, salvo la parte de conteos (T034).
- F1–F8 de las iteraciones 1–3 siguen resueltos; sus pruebas pasan.
- Constitución I–V: sin violaciones; III con la desviación registrada de SC-006.
- Código no pedido: ninguno.

## Métricas

- Requisitos y escenarios comprobados: 21 FR, 7 SC, 26 escenarios, 10 casos límite.
- Contratos: 9 filas de errores API y 15 obligaciones UI. Decisiones del plan: 14 (D-01…D-14).
- Hallazgos: 0 (missing 0 · partial 0 · contradicts 0 · unrequested 0).
- Pruebas: backend 114/114; frontend 103/103; build de producción sin avisos; cadena de
  migraciones desde cero, revertir y reaplicar correctas; E2E con la imagen reconstruida:
  ubicaciones 2/2 y entorno de A 3/3.

## Siguiente paso

Revisión y PR. Pendiente de A: T001 (incluida la regla de caracteres) y, tras su aprobación, T068.
