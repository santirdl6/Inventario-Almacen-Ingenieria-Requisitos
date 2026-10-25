# Instrucciones del proyecto de inventario

## Contexto común obligatorio

Antes de crear o modificar specs, planes, tareas o código del proyecto, consultar:

1. `.specify/memory/constitution.md`: principios y gobernanza del proyecto.
2. `.specify/memory/project-plan.md`: stack, arquitectura y acuerdos técnicos globales.
3. La spec, el plan y las tareas de la funcionalidad sobre la que se está trabajando.

La constitución prevalece sobre el diseño técnico. Consultar la base global no amplía
el alcance del comando o de la tarea solicitada.

## Documentación compartida

Los anexos globales residen en `.specify/memory/project/`. Leer los que afecten al trabajo:

- `research.md`: decisiones, alternativas y reglas comunes iniciales.
- `data-model.md`: entidades, relaciones, estados e invariantes.
- `contracts/api.md`: contratos HTTP, permisos y errores.
- `contracts/stock-operations.md`: protocolo de stock, reservas, transacciones e idempotencia.
- `contracts/ui.md`: navegación y comportamiento de la interfaz.
- `quickstart.md`: preparación y validación conjunta.

Cada nueva spec y plan debe enlazar la constitución, el plan global y los anexos que consume,
utilizando enlaces relativos correctos desde su directorio. Mantener los planes de cada
feature en `specs/`; añadir allí decisiones y validación particulares, referenciando los
acuerdos globales en lugar de copiarlos como otra fuente de verdad.

Los requisitos funcionales describen qué se necesita y por qué; los detalles de implementación
corresponden a los planes y contratos técnicos.

## Cambios compartidos y coordinación

Un cambio de stack, entidades compartidas, cálculo de disponibilidad, estados o efectos
de movimientos requiere actualizar la base global y revisar su impacto en las specs
consumidoras, con revisión de ambas personas. No redefinir esas reglas silenciosamente
en un plan local. Si afecta una obligación constitucional, seguir su procedimiento de enmienda.

El catálogo y reparto 4+4 están en `specs/001-reparto-specs-inventario/spec.md`;
los hitos H0–H5 y los puntos de encuentro, en su `plan.md`.

Los comandos Spec Kit mantienen sus rutas convencionales de feature; estas instrucciones
añaden la consulta explícita de la base global. No se presupone que Spec Kit cargue
automáticamente todos los archivos de `.specify/memory/`.
