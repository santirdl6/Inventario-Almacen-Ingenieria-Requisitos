# Specification Quality Checklist: HU-05 — Consultar existencias y reservas

**Purpose**: Validar la completitud y calidad de la especificación antes de planificar.
**Created**: 2026-11-19
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

- Revisión 1 (2026-11-19): falla «Requirements are testable and unambiguous». El caso límite de
  dos asignaciones en el mismo bucket admitía «se rechaza o se agrupa». Se fija el rechazo.
- Revisión 2 (2026-11-19): los 16 criterios se cumplen. No quedan marcadores
  [NEEDS CLARIFICATION]: la asignación manual de reservas, el motivo obligatorio y las
  liberaciones parciales los fijan R-05 y los contratos comunes. Las demás decisiones abiertas
  (ocultar filas con físico cero, un artículo por pedido, pedido sin edición ni cancelación,
  motivo de liberación en texto libre) se documentan en Assumptions como valores por defecto
  para `/speckit-clarify`.
- La entrada del usuario nombra componentes y permisos técnicos. La línea **Input** la reproduce
  literalmente. El cuerpo solo cita los permisos del catálogo común en Assumptions y remite los
  componentes de H0 al contrato de stock, sin fijar tecnologías, rutas ni estructuras de datos.
- Cobertura de la HU original: búsqueda por artículo con filtros de ubicación o lote (US1, FR-001,
  FR-004); físico, reservado y disponible (US1, FR-002/003); resultados confirmados (US1.5, FR-005).
- Coordinación pendiente para el plan: R-10 (research.md, línea 261) y el plan 001 (plan.md,
  línea 114) sitúan el pedido de cliente mínimo en HU-03. HU-05 lo incorpora por decisión de B
  sobre la frontera HU-03/HU-05; queda pendiente de revisión de A y de actualizar ambos
  documentos, según AGENTS.md.
