# Specification Quality Checklist: HU-02 — Gestionar las ubicaciones de los artículos

**Purpose**: Validar la completitud y calidad de la especificación antes de planificar.
**Created**: 2026-11-16
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

- Revisión 1 (2026-11-16): todos los criterios satisfechos en la primera iteración.
- Los enlaces del encabezado apuntan a acuerdos normativos (constitución, modelo, contratos);
  la spec no fija tecnologías, rutas ni estructuras de datos.
- Cobertura de la HU original: código único con pasillo/estantería/nivel (US1, FR-002/003);
  artículo en varias ubicaciones y cantidad por artículo y ubicación (US2, US3, FR-006);
  consulta de todas las ubicaciones de un artículo (US2, FR-007).
- Revisión 2 (2026-11-18, `/speckit-clarify`): las cuatro decisiones tomadas por defecto quedan
  resueltas en la sección Clarifications: combinación física única incluso entre inactivas (FR-003),
  identificación inmutable desde el primer movimiento confirmado (FR-011), sin borrado de
  ubicaciones (FR-014) y almacén único sin zonas. Se añade FR-021 (aviso al retirar la última zona
  de recepción activa). Los 16 criterios siguen satisfechos.
- Pendiente de validación de A en el PR: FR-003 añade una restricción al modelo común y FR-014
  endurece "no se borra con historial" a "no se borra nunca"; ambos requieren actualizar
  `data-model.md` y `contracts/api.md` con revisión de A al planificar, según AGENTS.md.
