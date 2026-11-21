# Agrupamiento y numeración de las specs

**Acuerdo**: 2026-11-19. A confirma la propuesta de B: B reserva 004–006 y A
007–009. El número de directorio es independiente del identificador de historia HU.
001 conserva la coordinación; cada persona mantiene cuatro historias.

Fuentes: [catálogo de historias](../specs/001-reparto-specs-inventario/spec.md),
[hitos](../specs/001-reparto-specs-inventario/plan.md),
[constitución](../.specify/memory/constitution.md) y
[plan global](../.specify/memory/project-plan.md).

## Persona A — Abastecimiento y datos maestros

| Número | Historia | Nombre completo | Directorio confirmado |
|--------|----------|-----------------|-----------------------|
| 002 | HU-08 | Mantener artículos, familias y proveedores | `specs/002-gestionar-datos-maestros/` |
| 007 | HU-07 | Crear, enviar y seguir pedidos a proveedores | `specs/007-gestionar-pedidos-proveedores/` |
| 008 | HU-01 | Registrar la recepción de mercancía | `specs/008-registrar-recepcion-mercancia/` |
| 009 | HU-06 | Configurar alertas de stock mínimo | `specs/009-configurar-alertas-stock-minimo/` |

002 está implementada y cerrada técnicamente. 007 tiene su
[entrega funcional local](../specs/007-gestionar-pedidos-proveedores/spec.md) con el objetivo
de rendimiento de guardados pendiente. 008 tiene su [especificación HU-01 en borrador](../specs/008-registrar-recepcion-mercancia/spec.md),
creada el 2026-11-21. 009 está [completada en su alcance funcional](../specs/009-configurar-alertas-stock-minimo/spec.md),
con el ensayo completo de rendimiento diferido.

## Persona B — Operación y disponibilidad

| Número | Historia | Nombre completo | Directorio / reserva |
|--------|----------|-----------------|---------------------|
| 003 | HU-02 | Gestionar las ubicaciones de los artículos | `specs/003-gestionar-ubicaciones/`, PR #6 |
| 004 | HU-05 | Consultar existencias y reservas | `specs/004-consultar-existencias/`, rama de B |
| 005 | HU-03 | Registrar la salida de mercancía | Prefijo reservado por B; sufijo de directorio a concretar por B |
| 006 | HU-04 | Realizar inventarios cíclicos y ajustar diferencias | Prefijo reservado por B; sufijo de directorio a concretar por B |

004 se ha contrastado con el borrador publicado en `b/hu-05-consultar-existencias`.
La tabla no presupone que esos directorios estén integrados en la rama base local.

## Frontera confirmada entre HU-05 y HU-03

- **HU-05 / 004** implementa consulta de existencias y compromisos, alta y consulta
  del pedido de cliente mínimo y comandos de reservar/liberar. El pedido es soporte
  de reservas y salidas: referencia externa, cliente y líneas; sin precios, facturación
  ni módulo comercial. Su alta usa Shipping.Record, su consulta Inventory.Read y
  reservar/liberar Reservations.Manage, conforme al contrato común.
- **HU-03 / 005** consume ese pedido y sus reservas para registrar/confirmar salidas,
  actualizar cantidades servidas y estado derivado y consumir la reserva propia según
  el cálculo compartido. No crea otro pedido mínimo ni otra implementación de reservas.
- El cambio adelanta el soporte de pedido a H2/HU-05. Conserva entidades, estados,
  permisos, disponibilidad e idempotencia comunes; no cambia las reglas de stock.
- Editar o cancelar pedidos queda fuera del alta/consulta mínima de esta entrega;
  no se eliminan por ello los estados ni contratos de evolución del modelo común.

La revisión de A del cambio de responsabilidad queda registrada aquí y en el plan 001,
research/modelo/API comunes y las tareas receptoras. La aceptación integrada de salidas
se realiza en HU-03 sobre lo entregado por HU-05.

## Coordinación

Antes de crear una spec, usar la reserva de esta tabla y consultar el
[backlog receptor](../specs/001-reparto-specs-inventario/integration-tasks.md).
HU-08 no se reabre para implementar escritores futuros. Cada módulo incorpora sus
lectores, marca de primera referencia, bloqueos y pruebas antes de habilitar su escritor.

Revisión de HU-02: [informe del PR #6](revisiones/pr-006-hu02.md).
