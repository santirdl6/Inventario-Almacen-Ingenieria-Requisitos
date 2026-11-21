# Specification Quality Checklist: HU-03 — Registrar la salida de mercancía

**Purpose**: Validar la completitud y calidad de la especificación antes de planificar.
**Created**: 2026-11-20
**Feature**: [spec.md](../spec.md)

`[x]` indica un criterio de calidad documental revisado y satisfecho; no acredita
implementación, pruebas ejecutadas ni aceptación operativa.

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Revisión 1 (2026-11-20): 15 de 16 criterios se cumplen. Quedan 3 marcadores
  [NEEDS CLARIFICATION], dentro del límite: descartar un borrador (FR-004), elección de orígenes
  al preparar (FR-006) y corrección de una salida confirmada (FR-013). Se resuelven con
  `/speckit-clarify` antes de `/speckit-plan`.
- Revisión 2 (2026-11-20, `/speckit-clarify`): los 16 criterios se cumplen. Los 3 marcadores se
  resuelven en la sesión de clarificación: eliminar un borrador completo (FR-004, US1.9),
  orígenes propuestos desde las reservas propias y editables (FR-006a, US1.10) y salida confirmada
  sin reversión en HU-03, que se corrige con el ajuste de HU-04 (FR-013, Alcance y dependencias).
  Pregunta extra: el pedido se localiza por su referencia externa exacta (FR-001a, US1.11), porque
  HU-05 no construyó la lista de pedidos (S1). Siguen cumpliéndose los 16 criterios.
- Las demás decisiones abiertas se documentan en Assumptions como valores por defecto: contraparte
  siempre el cliente del pedido (nunca el motivo de no aplicación), motivo implícito de expedición,
  ciclo borrador → confirmada del contrato común, varios borradores y salidas parciales por pedido,
  responsables distintos de borrador y confirmación, fecha del hecho en el pasado admitida y futura
  rechazada, y confirmar sin el permiso de gestión de reservas.
- La entrada del usuario nombra componentes y permisos técnicos. La línea **Input** la reproduce
  literalmente. El cuerpo solo cita los permisos del catálogo común en Assumptions y remite los
  componentes de H0, el lector del pedido y el protocolo de bloqueos al contrato de stock y al
  contrato de integración de HU-05, sin fijar tecnologías, rutas ni estructuras de datos. La tabla
  «Integraciones recibidas» conserva el vocabulario de las tareas receptoras para su trazabilidad.
- Cobertura de la HU original: vinculación a pedido (US1, FR-001); artículos, cantidades y orígenes
  (US1, FR-002/003); bloqueo considerando reservas ajenas (US2, FR-007/010); descuento al confirmar
  (US1.3, FR-008); movimiento trazable (US4, FR-015–FR-018).
- Integraciones incorporadas: INT-HU03-01, INT-HU03-02, reglas 3 y 4 del contrato HU-05 → HU-03
  (consumo de reservas y renovación de la versión de la cabecera), S-18 completo y V-04/V-05 con sus
  extensiones.
