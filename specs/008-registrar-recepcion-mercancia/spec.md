# Feature Specification: HU-01 — Registrar recepciones parciales de mercancía

**Feature Branch**: Sin rama nueva asociada; el directorio es independiente de la rama Git.  
**Created**: 2026-11-21  
**Status**: Draft  
**Input**: «haz la especificacion de HU-01 recepciones parciales».

**Origen**: [catálogo de historias](../001-reparto-specs-inventario/spec.md) · Persona A ·
[Hito H3](../001-reparto-specs-inventario/plan.md) · [Reserva 008](../../docs/agrupamiento.md).

**Base normativa y acuerdos**: [constitución](../../.specify/memory/constitution.md),
[plan global](../../.specify/memory/project-plan.md), [reglas de negocio](../../.specify/memory/project/research.md#r-05-reglas-comunes-de-negocio-para-no-bloquear-las-specs),
[modelo compartido](../../.specify/memory/project/data-model.md), [contratos comunes](../../.specify/memory/project/contracts/api.md),
[stock](../../.specify/memory/project/contracts/stock-operations.md#recepción-y-compra),
[interacción](../../.specify/memory/project/contracts/ui.md), [validación conjunta](../../.specify/memory/project/quickstart.md),
[entrega de HU-07 a HU-01](../007-gestionar-pedidos-proveedores/validation/hu01-handoff.md) y
[tareas receptoras de HU-01](../001-reparto-specs-inventario/integration-tasks.md#hu-01--recepciones--persona-a).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Registrar y confirmar una entrega parcial (Priority: P1)

Como operario autorizado, quiero registrar cuánto acepté de un pedido de proveedor y dónde
quedó cada cantidad, para incorporar la mercancía y dejar pendiente lo que aún debe llegar.

**Why this priority**: Es el flujo principal de entrada. Debe conciliar la mercancía, sus ubicaciones y el pedido desde la primera entrega.

**Independent Test**: Partir de un pedido enviado de 10 unidades, con artículo activo y sin existencias. Guardar un borrador de 6 unidades, distribuirlas en dos ubicaciones, confirmar y registrar las 4 restantes en otra recepción.

**Acceptance Scenarios**:

1. **Given** un pedido enviado y una línea con pendiente, **When** el operario prepara, reabre o corrige un borrador, **Then** ve pedido, recibido previo, cantidad aceptada ahora y pendiente previsto; el stock, el diario y el recibido confirmado no cambian.
2. **Given** 6 unidades aceptadas de las 10 pedidas, **When** confirma 2 en una ubicación y 4 en otra, **Then** se registra una recepción, se localizan las 6 unidades, las reservas no cambian y la primera consulta de Compras muestra recibido 6, pendiente 4 y «Recibido parcialmente».
3. **Given** ese pendiente de 4, **When** confirma otra entrega de 4, **Then** la primera consulta muestra recibido 10, pendiente 0 y «Recibido»; la primera recepción sigue consultable sin cambios.
4. **Given** un artículo con seguimiento por lote y una zona temporal habilitada, **When** se revisa y confirma una distribución válida, **Then** cada cantidad conserva su destino y lote. Si falta un lote exigido, falta destino, el destino no es utilizable o la distribución no suma lo aceptado, se rechaza la confirmación y se indican las líneas afectadas.
5. **Given** un pedido aún en borrador, con correo pendiente o fallido, **When** se intenta recibir contra él, **Then** se explica que solo se puede recibir un pedido cuyo envío haya sido aceptado. Si ya no hay pendiente para una línea, no puede aceptarse otra cantidad positiva en ella.

### User Story 2 - Registrar diferencias sin inflar las existencias (Priority: P1)

Como operario, quiero distinguir lo reportado en la entrega, lo que acepto y las incidencias,
para explicar faltas, excesos o sustituciones sin registrar mercancía que no incorporé.

**Why this priority**: Una discrepancia mal registrada altera las existencias o cierra un pendiente que el proveedor aún debe atender.

**Independent Test**: Con 4 unidades pendientes, registrar 7 reportadas, aceptar expresamente 4 con motivo para el exceso y verificar que solo entran 4. Intentar aceptar las 7 y comprobar su rechazo.

**Acceptance Scenarios**:

1. **Given** 4 unidades pendientes y 7 reportadas del artículo pedido, **When** el operario intenta aceptar 7, **Then** se rechaza la confirmación completa. Si acepta expresamente 4 y explica el exceso de 3, **Then** se conservan ambas cifras y solo se incorporan 4.
2. **Given** una entrega de 6 contra 10 sin unidades rechazadas, **When** se confirma, **Then** las 4 restantes siguen pendientes y se muestran como tal. Si se reportan 6 pero se aceptan 5 por daño, se conserva además la incidencia por la unidad no aceptada, con motivo.
3. **Given** un artículo sustituido o ajeno al pedido, **When** se registra la incidencia, **Then** queda identificable en la recepción, sin asignación ni entrada de stock para ese artículo y sin descontar el pendiente. El sistema no permite aceptarlo como si fuera la línea original.
4. **Given** un borrador sin ninguna cantidad positiva aceptada, **When** se intenta confirmar, **Then** se explica que la incidencia puede quedar documentada en el borrador y no se genera una entrada ni se modifica el pedido.
5. **Given** dos líneas de compra con el mismo artículo, **When** se registra una entrega contra una de ellas, **Then** se conserva esa identidad y no se consume el pendiente de la otra por coincidencia de artículo.

### User Story 3 - Confirmar con seguridad y recuperar el resultado (Priority: P1)

Como operario, quiero saber si mi confirmación quedó registrada aunque falle la conexión o
haya otro operario trabajando, para que ninguna recepción se pierda ni se duplique.

**Why this priority**: La confirmación altera inventario, pedido e historial; requiere garantías desde el primer uso.

**Independent Test**: Preparar dos borradores de 6 contra el mismo pendiente de 10. Confirmar simultáneamente, perder la respuesta del ganador y recuperar el resultado original.

**Acceptance Scenarios**:

1. **Given** dos borradores de 6 contra 10 pendientes, **When** se confirman en paralelo, **Then** solo uno incrementa existencias y recibido; el otro explica el nuevo pendiente de 4 y conserva sus datos para revisión. Dos entregas válidas que coinciden en artículo, ubicación y lote acumulan la suma exacta.
2. **Given** una respuesta perdida, **When** se vuelve a la pantalla o se consulta el resultado, **Then** se conserva la referencia de la intención; comprobar o repetir esa misma confirmación no crea otra entrada. Usar otra intención sobre una recepción ya confirmada tampoco la confirma de nuevo.
3. **Given** que cambió el borrador después de revisarlo, **When** se intenta confirmar la revisión anterior, **Then** se rechaza y exige volver a revisar. Si falla el registro antes de quedar confirmado, no aparecen efectos parciales en pedido, saldo ni historial.
4. **Given** que un artículo o destino dejó de ser utilizable entre revisión y confirmación, **When** la recepción intenta confirmar después del cambio, **Then** se rechaza sin efectos; si la recepción se confirmó primero, se conservan el documento y sus entradas históricas.
5. **Given** una persona sin permiso de registro, **When** intenta guardar o confirmar, **Then** se rechaza sin efectos. Un lector autorizado puede consultar la recepción, sin controles de escritura. Un operario que registra entradas pero no puede autorizar ajustes tampoco puede compensarlas.

### User Story 4 - Consultar la evidencia y corregir un registro confirmado (Priority: P2)

Como responsable del almacén, quiero reconstruir qué se recibió y corregir un error autorizado
sin borrar el registro original, para mantener coherentes el stock y el saldo del pedido.

**Why this priority**: La operación confirmada es evidencia de una entrada física; las incidencias posteriores requieren trazabilidad.

**Independent Test**: Confirmar una recepción de 6 contra 10; consultar su historial, compensar 2 unidades registradas por error y comprobar recibido 4, pendiente 6 y una corrección vinculada.

**Acceptance Scenarios**:

1. **Given** una recepción confirmada, **When** se consulta por pedido o se busca en el historial por artículo, período y responsable, **Then** se puede reconstruir cantidad, unidad, destino, lote aplicable, responsable, instantes, motivo, proveedor y soporte o motivo de ausencia, incluso si cambian los maestros.
2. **Given** una entrada de 6 que debió ser de 4, **When** un responsable autorizado registra una compensación de 2 con motivo y quedan unidades suficientes sin comprometer reservas, **Then** se conserva el original, el stock y recibido disminuyen en 2, se restaura el pendiente correspondiente y se recalcula el estado en la primera consulta.
3. **Given** mercancía ya consumida o reservada que impediría la compensación, **When** se intenta corregir sin resolver esa situación, **Then** se rechaza íntegramente sin tocar el historial ni las reservas. No se permite compensar más de lo que queda por corregir de la entrada original.
4. **Given** una interrupción que impide registrar una recepción al momento, **When** se restablece el servicio, **Then** se incorpora y comprueba el comprobante identificado sin duplicar entradas. El responsable autorizado del almacén solo puede aprobar la reapertura del alcance tras conciliar todos sus comprobantes y resolver las diferencias; se conserva quién aprobó, cuándo y con qué evidencia.

### Edge Cases

- Las cantidades negativas, fracciones en unidades enteras y más de tres decimales efectivos se rechazan sin redondeo silencioso. No se convierte la unidad de medida implícitamente.
- Una línea con aceptado cero puede documentar una incidencia; no lleva asignaciones. Una línea no incluida en la entrega no se considera recibida ni rechazada automáticamente.
- Un destino temporal se identifica igual que uno definitivo. Un destino inactivo, congelado por conteo o no habilitado para recepción no se sustituye automáticamente por otro.
- Sin seguimiento por lote no se pide un lote al operario. Si se exige, debe quedar identificado para cada asignación; se respeta la definición vigente del artículo.
- El proveedor se identifica por el pedido; su inactivación posterior no borra la compra ni impide por sí sola recibir un compromiso ya enviado. La inactivación del **artículo** sí impide nuevas entradas de recepción para ese artículo.
- Una confirmación incierta no equivale a fallo definitivo; conservar la referencia al recargar y recuperar el resultado con la identidad autorizada, sin repetir los datos bajo otra cuenta.
- Una corrección total puede devolver el pedido a «Enviado» si ya no queda recibido neto; no altera la aceptación de su envío. Una devolución comercial se gestiona fuera de este alcance.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: El operario autorizado DEBE poder localizar pedidos de proveedor enviados con pendiente y elegir el pedido y sus líneas. Solo se admite incorporar mercancía contra un pedido con envío aceptado y contra la línea concreta correspondiente (US1.1/5, US2.5).
- **FR-002**: El sistema DEBE permitir guardar, reabrir y corregir borradores con identidad estable, líneas, cantidades reportadas y aceptadas, fecha del hecho y referencias de entrega. Un borrador NO DEBE modificar saldos, reservas, diario ni recibido confirmado (US1.1).
- **FR-003**: Para cada línea DEBE cumplirse `0 ≤ aceptado ≤ reportado`. Un borrador puede guardarse y reabrirse sin ninguna aceptación positiva. Solo al confirmar se exige al menos una línea con aceptación positiva y `aceptado ≤ pendiente vigente` por línea. Las cantidades respetan la unidad base y precisión de hasta tres decimales; los artículos de unidades enteras rechazan fracciones (US1.4, US2.1/4 y casos límite).
- **FR-004**: La revisión DEBE distinguir cantidad pedida, recibido confirmado previo, reportado, aceptado ahora, diferencias y pendiente previsto. La parte de la compra que no se atiende con esta entrega sigue pendiente sin exigir que toda entrega parcial se trate como rechazo (US1.1–3, US2.2).
- **FR-005**: Las unidades reportadas y no aceptadas DEBEN quedar explicadas con motivo. Excesos y sustituciones se identifican como incidencias, sin incorporarlos al stock. El sistema DEBE rechazar una aceptación fuera del pedido o superior al pendiente, sin recortar automáticamente lo ingresado (US2.1–5).
- **FR-006**: Cada cantidad aceptada DEBE asignarse a una o más ubicaciones activas habilitadas y, si aplica, a lotes identificados. Cada cantidad asignada es positiva y su suma por línea es exactamente la aceptada. Una ubicación temporal es válida si está identificada y habilitada (US1.2/4).
- **FR-007**: Solo la cantidad aceptada de una recepción confirmada DEBE aumentar existencias y recibido. Confirmar DEBE guardar como un solo resultado la recepción, sus entradas, saldos, acumulados del pedido, su estado y la evidencia correspondiente. Las reservas existentes NO DEBEN cambiar por recibir (US1.2/3, US3.3).
- **FR-008**: El sistema DEBE revalidar la revisión del borrador, el envío aceptado, el pendiente vigente, las cantidades y los maestros/destinos antes de confirmar, incluyendo restricciones de conteo vigentes. Las confirmaciones simultáneas NO DEBEN exceder el pendiente ni duplicar o perder cantidades en una ubicación/lote (US1.4/5, US3.1/3/4).
- **FR-009**: La primera consulta tras confirmar o compensar DEBE presentar el nuevo recibido y pendiente por línea y el estado derivado de la compra: «Enviado» si no queda recibido, «Recibido parcialmente» si hay recibido y pendiente, «Recibido» si todas las líneas quedan satisfechas (US1.2/3, US4.2 y casos límite).
- **FR-010**: El sistema DEBE exigir identidad y autorización para consultar, registrar y corregir según el permiso de cada acción. El permiso de registro no concede autorización de ajuste. La autoría del registro y de la confirmación NO DEBE poder escogerse como un dato editable (US3.5, US4.1/2).
- **FR-011**: La recepción confirmada y sus entradas DEBEN conservar identidad, tipo, artículo/unidad, cantidades, ubicación/lote, instantes del hecho y registro, motivo, responsable, pedido y proveedor, y referencia del soporte o motivo explícito si no aplica. Se pueden consultar aunque cambien los maestros (US4.1).
- **FR-012**: Un documento confirmado NO DEBE editarse, borrarse ni confirmarse otra vez. La corrección de una entrada errónea DEBE dejar un movimiento compensatorio vinculado y motivado, con autorización, y ajustar en conjunto stock y recibido. Debe respetar cantidades netas, saldo físico y reservas existentes (US3.2, US4.2/3).
- **FR-013**: Ante un resultado incierto, el sistema DEBE mostrar «pendiente de comprobar», permitir recuperar la confirmación original y conservar la referencia al recargar. Repetir la misma intención no repite el efecto; reutilizar su referencia con otros datos se rechaza. No se muestra éxito antes de que el resultado esté guardado (US3.2/3).
- **FR-014**: La interfaz DEBE permitir revisar y corregir por teclado, mostrar etiquetas y unidades explícitas, distinguir estados con texto y explicar rechazos por campo o línea sin perder datos válidos (US1.1/4, US3.1/3/4, SC-008).
- **FR-015**: El sistema DEBE poder incorporar un comprobante de contingencia identificado, con fecha, responsable y detalle del hecho, incluidas ubicaciones y lotes aplicables, sin duplicar su efecto. DEBE rechazar la autorización de reapertura del alcance si falta conciliar un comprobante declarado, falta evidencia de conciliación de pedido, existencias o diario, o queda una diferencia sin resolver. Solo el responsable del almacén autorizado puede registrar esa decisión, con autoría, fecha y evidencia (US4.4).
- **FR-016**: Las referencias de recepciones, también en borrador, DEBEN respetar el bloqueo de definición del artículo y el rechazo de borrado con uso real. Retirar una referencia no desbloquea la definición ni permite borrar si subsisten el pedido, otros usos o historial (INT-HU01-01, SC-006).
- **FR-017**: Un artículo inactivo NO DEBE admitirse en una nueva entrada, incluso si la compra o el borrador son anteriores. Deben conservarse los documentos históricos y sus cantidades; una corrección autorizada no equivale a una entrada nueva (INT-HU01-02, US3.4, US4.1/3).

### Alcance y dependencias

- HU-07 proporciona pedidos enviados y sus líneas estables; HU-01 actualiza sus acumulados y acredita **V-02/V-06 y el SC-006 de HU-07** con recepciones reales de la funcionalidad. El estado del correo se mantiene independiente del estado de recepción.
- HU-08 aporta artículos/proveedores y sus reglas de referencia; HU-02, destinos identificados; HU-05 consume el saldo común, HU-04 aporta la restricción de conteo cuando se integre y HU-06 consume los efectos sobre disponibilidad para sus alertas. Se preservan los acuerdos compartidos de stock; no se redefine disponibilidad.
- Incluye recepción ligada a compra, parcial o completa, diferencias documentadas, consulta, recuperación y compensación de errores de registro de esa entrada. Quedan fuera compras sin pedido, aprobación comercial de excesos o sustituciones, devoluciones al proveedor, traslados, facturación y gestión de maestros.
- La restauración operativa y la aprobación de los límites de recuperación se conservan en H5-02. La prueba con operarios de HU-01 es evidencia propia de la recepción y alimenta H5-03; no se presume ejecutada por pruebas automáticas.

### Obligaciones receptoras de HU-01

| ID | Evidencia exigida en HU-01 |
|---|---|
| INT-HU01-01 | Con escritores reales, probar vinculación y retirada de líneas/asignaciones de borrador sin desbloquear definición ni aceptar borrado con uso. Verificar las referencias directas o indirectas a artículo y las carreras con edición/borrado en ambos órdenes. Si la línea de compra ya protege al artículo, demostrar esa protección heredada sin fabricar una compra inválida para simular un primer uso. Los efectos de recepción conservan soporte y contraparte. FR-006/007/011/016; SC-006. |
| INT-HU01-02 | Probar confirmación frente a desactivación de artículo en ambos órdenes; rechazar si se inactivó primero, conservar documento, pedido, cantidades e historial si se confirmó antes. No basta generar un movimiento aislado sin documento de recepción. FR-008/011/017; SC-006. |

### Key Entities

- **Pedido de compra y línea de compra**: compromiso enviado; identifica proveedor y artículo, cantidad pedida, recibido confirmado y pendiente por línea.
- **Recepción**: documento estable de una entrega, con pedido, soporte, instantes, responsable y estado «Borrador» o «Confirmada».
- **Línea de recepción y discrepancia**: referencia a una línea de compra, cantidades reportada y aceptada, y evidencia de diferencias o mercancía observada que no se acepta.
- **Asignación de recepción**: parte de la cantidad aceptada localizada en un destino con su lote si aplica.
- **Entrada y compensación**: hechos trazables vinculados al documento original; explican cambios en existencias y acumulados sin reescribir la historia.
- **Comprobante de contingencia y resultado de confirmación**: identifican el hecho y su registro comprobable para recuperarlo o conciliarlo sin duplicarlo.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: En V-02, con pendiente inicial de 10, las primeras consultas posteriores a dos confirmaciones válidas de 6 y 4 muestran recibido/pendiente 6/4 y 10/0, los estados correspondientes y entradas totales de 10; el borrador previo produce cero efectos.
- **SC-002**: En V-03, las 6 unidades aceptadas repartidas en 2 y 4 son localizables íntegramente por ubicación/lote. En el 100 % de los casos de exceso, sustitución, destino/lote inválido y discrepancia, ninguna unidad no aceptada incrementa stock ni recibido.
- **SC-003**: En V-06/V-07, dos parciales de 6 contra 10 no aportan 12; las dos parciales válidas en un mismo destino/lote aportan su suma exacta. Una respuesta perdida y su repetición originan un solo efecto; se conserva el resultado confirmado.
- **SC-004**: En el 100 % de los casos definidos de revisión obsoleta, permiso insuficiente y fallo antes de confirmar, no hay cambios parciales en recepción, existencias, diario ni pedido. Cada rechazo indica cómo revisar o corregir el problema.
- **SC-005**: En los escenarios de compensación y contingencia, se conserva el 100 % de la evidencia original. Una compensación válida refleja su efecto una vez en saldo y pedido; una inválida deja ambos intactos; el comprobante repetido no duplica existencias. En todos los intentos de reapertura con comprobantes sin conciliar, evidencia incompleta o diferencias abiertas, el alcance sigue sin autorización de reapertura.
- **SC-006**: Se superan todos los casos aplicables de INT-HU01-01/02 con referencias y recepciones reales de la funcionalidad: protección de definición, ausencia de borrados indebidos y ningún alta confirmada de mercancía de un artículo inactivado antes de confirmar.
- **SC-007**: Con 25 sesiones concurrentes, 10.000 artículos y 1.000.000 de entradas históricas, el 95 % de las consultas paginadas y el 95 % de las confirmaciones de hasta 20 líneas muestran su resultado en 2 s o menos, medidos por separado según la guía común, con esperas por conflictos y errores informados aparte.
- **SC-008**: Al menos tres operarios representativos completan una recepción parcial, corrigen un dato inválido y localizan la mercancía confirmada sin asistencia técnica ni errores de registro no detectados. Se documentan observaciones y se repiten los casos afectados tras corregir incidencias.

### Evidencia constitucional prevista

| Principio | Evidencia |
|---|---|
| I. Correspondencia almacén/sistema | Solo entra lo aceptado; las diferencias y la contingencia quedan conciliables (SC-001/002/005). |
| II. Ubicación precisa | Cantidades rastreables por destino y lote, incluso temporal (SC-002). |
| III. Uso intuitivo | Revisión, errores recuperables y ejercicio con operarios representativos (FR-014, SC-008). |
| IV. Trazabilidad | Entradas y compensaciones vinculadas con soporte, autoría y dos instantes (FR-011/012, SC-005/006). |
| V. Integridad y recuperación | Confirmación íntegra, concurrencia, reintentos, comprobantes y restauración operativa en H5 (SC-003/004/005, H5-02). |

## Assumptions

- Referencia de origen: pedido de HU-07 aceptado para envío; este estado no presupone que el proveedor haya leído el correo. El operario transcribe o verifica la entrega con el soporte disponible.
- Un borrador usa una entrada de captura por línea del pedido; para un mismo artículo pueden existir varias líneas de pedido y varias asignaciones de ubicación/lote. Se usa la unidad base del maestro.
- Se admite una entrega con exceso observado si el operario declara el exceso y acepta solo la cantidad válida. La conservación de la incidencia no concede permiso comercial para aceptar el exceso. Sin ninguna cantidad aceptada se conserva como borrador, conforme al modelo común.
- Si no hay ubicación definitiva se usa un destino temporal habilitado existente. La gestión por número de serie o caducidad no se añade al alcance base acordado.
- Los criterios son objetivos de aceptación; este documento no acredita pruebas efectuadas ni aprueba la puesta en servicio.
