# Feature Specification: HU-06 — Configurar alertas de stock mínimo y sugerencia de reposición

**Feature Branch**: Sin rama nueva asociada; el directorio es independiente de la rama Git.

**Created**: 2026-11-21

**Status**: Completada — alcance funcional. El ensayo de rendimiento SC-004 se difiere por decisión del usuario; no forma parte del cierre.

**Input**: «HU-06: mínimo alerta interna y sugerencia»

**Origen**: [catálogo de historias](../001-reparto-specs-inventario/spec.md) · Persona A ·
[Hito H4 (Control)](../001-reparto-specs-inventario/plan.md) · [Reserva 009](../../docs/agrupamiento.md).

**Base normativa y acuerdos**: [constitución](../../.specify/memory/constitution.md),
[plan global](../../.specify/memory/project-plan.md), [reglas de negocio](../../.specify/memory/project/research.md#r-05-reglas-comunes-de-negocio-para-no-bloquear-las-specs),
[modelo compartido](../../.specify/memory/project/data-model.md#reposición-y-mensajería--persona-a--hu-06-hu-07),
[contratos comunes](../../.specify/memory/project/contracts/api.md#hu-06--a-mínimos-y-alertas),
  [stock](../../.specify/memory/project/contracts/stock-operations.md),
[interacción](../../.specify/memory/project/contracts/ui.md), [validación conjunta](../../.specify/memory/project/quickstart.md) y
[tareas receptoras de HU-06](../001-reparto-specs-inventario/integration-tasks.md#hu-06--mínimos-y-alertas--persona-a).

## Clarifications

### Sesión 2026-11-21

- La consulta de alertas requiere `Alerts.Read`, conforme al contrato común; `Inventory.Read` por sí solo no la habilita.
- La acción de reposición abre el formulario existente de HU-07 con artículo y sugerencia editables. No crea ni envía un pedido al navegar.
- La disponibilidad negativa no es un estado confirmado válido: se conservan las invariantes comunes y las diferencias se investigan sin sobrescribir saldos.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configurar el umbral de stock mínimo por artículo (Priority: P1) · MVP

Como responsable de almacén o aprovisionamiento, quiero definir o actualizar el stock mínimo de disponibilidad de un artículo activo, para que el sistema conozca el umbral a partir del cual se requiere reposición.

**Why this priority**: Es la base configurativa de todo el subsistema de reposición y control. Sin umbral definido, no es posible evaluar si las existencias son insuficientes.

**Independent Test**: Partir de un artículo activo con política inicial a cero (creada en su alta por HU-08). Modificar su umbral a 10 unidades mediante control de concurrencia optimista (ETag/versión). Consultar la política y verificar que persiste el umbral exacto y que una petición con versión obsoleta se rechaza.

**Acceptance Scenarios**:

1. **Given** un artículo activo, **When** el usuario autorizado consulta su política de stock, **Then** obtiene el umbral de disponibilidad mínima actual y su identificador de versión (ETag).
2. **Given** un artículo activo y una versión válida, **When** el usuario establece un umbral mínimo positivo (p. ej. 15,000 unidades), **Then** el umbral queda registrado, la versión avanza y se desencadena la evaluación inmediata del artículo contra su stock disponible actual.
3. **Given** un artículo que no permite fraccionario, **When** el usuario intenta configurar un umbral con decimales (p. ej. 10,500), **Then** el sistema rechaza la operación sin guardar cambios, explicando que el artículo se gestiona en unidades enteras.
4. **Given** un intento de configurar un umbral negativo, **When** se envía la solicitud, **Then** el sistema la rechaza exigiendo un valor mayor o igual a cero con hasta tres decimales.
5. **Given** un intento de actualización concurrente con una versión desactualizada (`If-Match` no coincidente), **When** se procesa, **Then** se rechaza con error de precondición (412) y no se sobrescriben los cambios realizados por otro usuario.

---

### User Story 2 - Detección automática y ciclo de vida de alertas internas (Priority: P1) · MVP

Como responsable de inventario, quiero que el sistema detecte automáticamente cuando el stock disponible cae por debajo del mínimo configurado y gestione un único episodio de alerta activo por artículo, para visibilizar el riesgo de rotura sin duplicar registros.

**Why this priority**: La detección automática debe ser inmediata, atómica e íntegra. Todo movimiento de stock que reduzca la disponibilidad debe reflejar su impacto en alertas dentro de la misma transacción para garantizar correspondencia estricta con la realidad.

**Independent Test**: Para un artículo con mínimo 5, físico 12 y reservado 0 (disponible 12), registrar una reserva de 8 unidades (físico 12, reservado 8, disponible 4). Verificar que se abre un episodio de alerta activo con instantánea (físico 12, reservado 8, disponible 4, mínimo 5 y sugerencia 1). Registrar una recepción de 3 unidades (disponible 7) y comprobar que el episodio pasa a resuelto.

**Acceptance Scenarios**:

1. **Given** un artículo con stock mínimo 5 y disponible 10, **When** una operación confirmada (salida, reserva o ajuste negativo) reduce el disponible a 4, **Then** en la misma transacción se genera un episodio de alerta en estado Activo con las instantáneas de stock físico, reservado, disponible, mínimo y fecha/hora del hecho.
2. **Given** un artículo con físico 12, reservado 8 y mínimo 5 (disponible 4), **When** el sistema evalúa la alerta, **Then** detecta que el disponible (4) es inferior al mínimo (5) y mantiene la alerta activa, demostrando que la regla se rige por disponibilidad real (`Físico − Reservado`) y no solo por existencias físicas (escenario V-11).
3. **Given** un artículo que ya tiene una alerta activa, **When** un nuevo movimiento reduce aún más su disponible (p. ej. de 4 a 2), **Then** el sistema actualiza las instantáneas y la cantidad sugerida del episodio activo existente sin crear una segunda alerta (garantía de unicidad: a lo sumo un episodio activo por artículo).
4. **Given** un artículo con una alerta activa (disponible 4, mínimo 5), **When** una entrada confirmada (recepción o ajuste positivo) o una liberación de reservas eleva el disponible a un valor mayor o igual al mínimo (p. ej. disponible 7), **Then** el episodio activo pasa inmediatamente a estado Resuelto, registrando su fecha y hora de resolución.
5. **Given** un artículo con umbral mínimo fijado en 0, **When** se confirman movimientos de stock, **Then** no se genera ninguna alerta mientras el disponible sea mayor o igual a 0.

---

### User Story 3 - Consulta de alertas y cálculo de cantidad sugerida de compra (Priority: P2)

Como comprador o responsable de aprovisionamiento, quiero consultar la lista de alertas activas con sus métricas y la cantidad sugerida de compra, para tomar decisiones de reposición e iniciar pedidos a proveedores sin compras automáticas descontroladas.

**Why this priority**: Conecta el control de inventario con el aprovisionamiento (HU-07), proporcionando visibilidad clara y la sugerencia de reposición exacta sin violar el principio de intervención humana en compras.

**Independent Test**: Consultar `/alertas` filtrando por estado activo. Constatar que cada entrada muestra SKU, descripción, unidad base, existencias físicas, reservas, disponible, mínimo configurado, fecha desde la que está activa y cantidad sugerida `max(0, Mínimo − Disponible)`. Verificar el enlace directo para redactar un pedido de compra en HU-07.

**Acceptance Scenarios**:

1. **Given** múltiples alertas en el sistema (activas y resueltas), **When** el usuario consulta el panel de alertas filtrando por `state=Active`, **Then** obtiene la lista paginada de alertas activas ordenadas por fecha de apertura descendente.
2. **Given** una alerta activa con mínimo 10 y disponible 3, **When** se calcula la cantidad sugerida, **Then** la sugerencia es exactamente 7 unidades (`10 − 3`).
3. **Given** el panel de alertas, **When** el comprador hace clic en la acción de reposición de una alerta, **Then** la interfaz abre `/compras/nuevo` con el artículo y la cantidad sugerida editables, sin crear, emitir ni confirmar compras automáticamente.
4. **Given** una cuenta con permiso de consulta de alertas (`Alerts.Read`) pero sin gestión de umbrales, **When** accede a la vista de alertas, **Then** puede revisarlas y no se le muestran acciones de edición de mínimos. `Inventory.Read` por sí solo no concede acceso al panel.

---

### User Story 4 - Suspensión y reactivación de alertas ante el ciclo de vida del artículo (Priority: P2)

Como gestor de maestros, quiero que al desactivar un artículo se cierren sus alertas activas y no se vuelvan a evaluar alertas mientras esté inactivo, y que al reactivarlo se reevalúe su política, para mantener el panel limpio de referencias descatalogadas.

**Why this priority**: Resuelve la coordinación de ciclo de vida con datos maestros (`INT-HU06-01` y `HU-08`). Un artículo inactivo no debe generar ruido de compra ni alarmas de reposición.

**Independent Test**: Con un artículo que tiene una alerta activa, ejecutar la desactivación del artículo desde maestros. Verificar que en la misma transacción el episodio de alerta pasa a Resuelto. Registrar intentos de alteración de stock sobre el artículo inactivo y verificar que no genera nuevas alertas. Reactivar el artículo y verificar que si su disponible sigue bajo el mínimo, se genera un nuevo episodio de alerta.

**Acceptance Scenarios**:

1. **Given** un artículo activo con un episodio de alerta abierto, **When** el gestor de maestros desactiva el artículo (`IsActive = false`), **Then** el evaluador de ciclo de vida resuelve el episodio activo en la misma transacción y registra su cierre.
2. **Given** un artículo inactivo, **When** se consultan las alertas activas, **Then** no figura ninguna alerta activa para dicho artículo.
3. **Given** un artículo inactivo cuyo stock disponible es inferior a su stock mínimo configurado, **When** el gestor reactiva el artículo (`IsActive = true`), **Then** la reactivación reevalúa de inmediato la política de stock y abre un nuevo episodio de alerta activo con las instantáneas vigentes.

---

## Edge Cases

- **Disponibilidad inconsistente**: Un ajuste no puede confirmar físico negativo ni reservas superiores al físico. Una diferencia física se documenta y resuelve conforme al protocolo común; no se convierte en un saldo negativo de alerta.
- **Mínimo igual a cero**: Un artículo con mínimo 0 no emite alerta mientras su disponible es no negativo.
- **Unidades enteras**: Para artículos con `AllowsFractional = false`, cualquier valor configurado para `MinimumAvailable` que contenga decimales distintos de cero debe ser rechazado con error de validación (422), impidiendo que existan umbrales fraccionarios inalcanzables.
- **Concurrencia de movimientos**: Si dos movimientos de stock (p. ej. dos reservas concurrentes en HU-05 o dos salidas en HU-03) ocurren en paralelo y ambos dejan el disponible por debajo del umbral, la evaluación se ejecuta bajo el bloqueo del artículo dentro de la transacción de stock; la restricción de base de datos impide crear más de una fila activa simultánea.
- **Ausencia de compras automáticas**: El sistema bajo ninguna circunstancia debe enviar órdenes de compra ni contactar proveedores automáticamente al dispararse una alerta. La creación de compras es prerrogativa exclusiva del usuario a través de HU-07.
- **Rollback conjunto**: Si falla la confirmación del movimiento de stock (por falta de saldo, error en asignación o violación de invariante), cualquier actualización sobre la alerta debe revertirse limpiamente dentro de la misma transacción.
- **Lecturas compuestas en aislamiento**: La consulta de alertas debe leer instantáneas consistentes y no mezclar saldos de transacciones en vuelo.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: El sistema DEBE permitir configurar un umbral de stock mínimo (`MinimumAvailable`) para cada artículo, almacenado con una precisión de hasta tres decimales (`decimal(18,3)`) y siendo siempre mayor o igual a cero.
- **FR-002**: El sistema DEBE validar que los artículos con `AllowsFractional = false` solo admitan umbrales mínimos en unidades enteras.
- **FR-003**: La actualización del umbral mínimo DEBE requerir control de concurrencia optimista (`If-Match` con ETag o rowversion de `StockPolicy`) y el permiso específico `StockPolicy.Manage`.
- **FR-004**: Toda modificación del umbral mínimo DEBE desencadenar una reevaluación inmediata de las existencias disponibles del artículo.
- **FR-005**: El sistema DEBE evaluar automáticamente la condición de alerta (`Available < MinimumAvailable`) en la misma transacción atómica en que se confirme cualquier movimiento de stock (recepción, salida, reserva, liberación, ajuste físico o modificación del mínimo).
- **FR-006**: El cálculo de disponibilidad utilizado para comparar contra el umbral DEBE ser el común `Disponible = suma de Físico − suma de Reservado`, consolidando todos los buckets del artículo sin filtrar sus ubicaciones; una ubicación con existencias o reservas no puede desactivarse.
- **FR-007**: Si `Disponible < MinimumAvailable` y no existe una alerta activa para el artículo, el sistema DEBE crear un nuevo registro de alerta (`StockAlert`) en estado `Active`, capturando las instantáneas del momento: stock físico, stock reservado, stock disponible, umbral mínimo, cantidad sugerida e instante de apertura (`OpenedAt`).
- **FR-008**: Si `Disponible < MinimumAvailable` y ya existe un episodio de alerta en estado `Active` para ese artículo, el sistema DEBE actualizar las instantáneas y la cantidad sugerida de dicho episodio, sin crear una nueva alerta.
- **FR-009**: La cantidad sugerida de reposición DEBE calcularse exactamente como `max(0, MinimumAvailable − Disponible)`.
- **FR-010**: Cuando una transacción confirmada determine que `Disponible >= MinimumAvailable`, cualquier alerta en estado `Active` para ese artículo DEBE pasar a estado `Resolved`, registrando el instante de resolución (`ResolvedAt`).
- **FR-011**: El sistema DEBE garantizar que exista como máximo un episodio de alerta en estado `Active` por cada artículo mediante restricciones de base de datos.
- **FR-012**: El sistema DEBE proporcionar una consulta paginada de alertas con filtros por estado (`Active`, `Resolved`), artículo y rango de fechas, requiriendo el permiso `Alerts.Read`.
- **FR-013**: La vista de alertas en la interfaz DEBE ofrecer un acceso directo al flujo de redacción de compras de HU-07, precargando el artículo y la cantidad sugerida en el borrador de compra si el usuario así lo decide.
- **FR-014**: La desactivación de un artículo (`IsActive = false`) DEBE resolver inmediatamente cualquier episodio de alerta activo en la misma transacción y suspender la generación de nuevas alertas para dicho artículo (`INT-HU06-01`).
- **FR-015**: La reactivación de un artículo (`IsActive = true`) DEBE reevaluar inmediatamente el stock disponible frente a la política de stock y abrir un episodio de alerta si procede (`INT-HU06-01`).
- **FR-016**: La generación o resolución de alertas NO DEBE bajo ningún concepto crear, enviar ni modificar pedidos a proveedores de forma autónoma.

---

### Key Entities

- **StockPolicy**: Define la política de inventario para un artículo. Relación 1:1 obligatoria con `Article`.
  - `ArticleId`: Identificador único del artículo (clave primaria y foránea restrictiva).
  - `MinimumAvailable`: Umbral de disponibilidad mínima (`decimal(18,3)` no negativo).
  - `Version`: Marca de versión para concurrencia optimista (`rowversion`).
- **StockAlert**: Representa un episodio de alerta de stock bajo.
  - `Id`: Identificador único (UUID).
  - `ArticleId`: Identificador del artículo afectado (clave foránea restrictiva).
  - `State`: Estado del episodio (`Active` o `Resolved`).
  - `PhysicalSnapshot`: Existencias físicas en el momento de la última evaluación.
  - `ReservedSnapshot`: Existencias reservadas en el momento de la última evaluación.
  - `AvailableSnapshot`: Existencias disponibles en el momento de la última evaluación.
  - `MinimumSnapshot`: Umbral mínimo configurado al momento de la evaluación.
  - `SuggestedQuantity`: Cantidad sugerida calculada (`max(0, Mínimo − Disponible)`).
  - `OpenedAt`: Instante UTC en que se abrió la alerta.
  - `UpdatedAt`: Instante UTC de la última actualización de instantáneas mientras sigue activa.
  - `ResolvedAt`: Instante UTC en que el stock recuperó el umbral o el artículo fue desactivado (nulo mientras esté activa).

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Coherencia transaccional del 100%: Ningún movimiento de stock que vulnere o recupere el umbral mínimo deja la alerta en un estado discordante con la disponibilidad resultante.
- **SC-002**: Unicidad garantizada: En ningún escenario de concurrencia existen dos o más alertas en estado `Active` para el mismo artículo.
- **SC-003**: Criterio de disponibilidad verificado (escenario V-11): Con existencias físicas superiores al mínimo pero reservas que sitúan el disponible por debajo del mínimo, el sistema activa la alerta en el 100% de los casos.
- **SC-004**: Rendimiento de evaluación: La evaluación de la política de stock dentro de la transacción de movimientos añade una sobrecarga inferior a 25 ms en percentil 95 (p95).
- **SC-005**: En el 100 % de los recorridos definidos, las cuentas autorizadas ven en configuración el mínimo y la unidad, y en cada alerta el mínimo, disponible, sugerencia y unidad; pueden distinguir por texto el resultado confirmado, el error y la ausencia de alertas.
- **SC-006**: Aislamiento de compra: El 100% de las sugerencias de reposición requieren confirmación explícita del comprador mediante el flujo de HU-07, sin compras fantasma ni efectos automáticos en proveedores.

---

## Assumptions

- Se reutiliza la entidad `StockPolicy` ya existente en el modelo de datos (inicializada con mínimo cero por HU-08).
- La disponibilidad compartida sigue estrictamente el principio contractual `Disponible = Físico − Reservado`.
- Las notificaciones externas (correo, webhook o push) están fuera del alcance de este módulo; la alerta es una entidad y vista interna del sistema.
- El usuario comprador interactúa voluntariamente con la sugerencia navegando a la pantalla de compra de HU-07.
