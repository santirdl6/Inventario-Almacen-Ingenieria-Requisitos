# Feature Specification: HU-03 — Registrar la salida de mercancía

**Feature Branch**: `b/hu-03-registrar-salida-mercancia` (el directorio de la feature es independiente de la rama Git).

**Created**: 2026-11-20

**Status**: Implementada (T003–T018 y Convergence C1; 2026-11-21). Integrable, no aceptada: faltan SC-006 y SC-007 (ver «Estado de la entrega»).

**Input**: User description: "HU-03 «Registrar salida de mercancía» (Persona B). Directorio de feature: specs/005-registrar-salida-mercancia, rama ya creada: b/hu-03-registrar-salida-mercancia. HU-03 consume los pedidos de cliente y reservas de HU-05 para registrar la salida (operación Ship), consume reservas al servir, renueva la versión de la cabecera del pedido y respeta specs/004-consultar-existencias/contracts/hu03-integration.md. Modelo común: idempotencia, If-Match/ETag, InventoryLocks, diario de solo adición, contraparte con exactamente uno de CounterpartyReference o CounterpartyNotApplicableReason, permiso Shipping.Record. Máximo 3 [NEEDS CLARIFICATION]. Solo specify."

**Historia de origen**: HU-03 del [catálogo y reparto](../001-reparto-specs-inventario/spec.md#catálogo-y-asignación-de-las-ocho-futuras-specs):
vinculación a pedido de cliente; artículos, cantidades y ubicaciones de origen; bloqueo de salidas
superiores a lo disponible considerando reservas ajenas; descuento al confirmar; movimiento trazable.
**Responsable principal**: Persona B. **Hito**: H3 (movimientos), según el
[plan de coordinación](../001-reparto-specs-inventario/plan.md#hitos-y-coordinación).
**Numeración**: 005, reservada por B en [docs/agrupamiento.md](../../docs/agrupamiento.md).

**Contexto normativo y acuerdos consumidos**:

- [Constitución vigente](../../.specify/memory/constitution.md): principios I–V, en especial I, III, IV y V, y criterios de aceptación.
- [Plan global](../../.specify/memory/project-plan.md): arquitectura compartida y frontera HU-03/HU-05.
- [Decisiones comunes](../../.specify/memory/project/research.md#r-05-reglas-comunes-de-negocio-para-no-bloquear-las-specs): salidas, reservas, lotes, desactivación y conteos.
- [Modelo común](../../.specify/memory/project/data-model.md#compromisos-y-salidas--persona-b--hu-03-hu-05): pedido de cliente, reserva, salida y diario.
- [Contrato HTTP común](../../.specify/memory/project/contracts/api.md#hu-03-y-hu-05--b-pedidos-de-cliente-reservas-y-salidas): salidas, permisos y errores.
- [Contrato de stock](../../.specify/memory/project/contracts/stock-operations.md#cálculo-de-salida): cálculo de salida, bloqueos e idempotencia.
- [Contrato de interacción](../../.specify/memory/project/contracts/ui.md): pantalla /salidas, estados de operación, información que no debe ocultarse y validación con operarios.
- [Guía de validación](../../.specify/memory/project/quickstart.md#6-escenarios-de-aceptación-integrados): V-04 y su extensión, V-05 y su extensión, V-07, V-08, V-09, V-12 y V-13.
- Entregado por HU-05: [spec](../004-consultar-existencias/spec.md), [modelo](../004-consultar-existencias/data-model.md),
  [contrato de integración HU-05 → HU-03](../004-consultar-existencias/contracts/hu03-integration.md) y
  [convergencia](../004-consultar-existencias/validation/converge-1.md).
- Backlog receptor: [INT-HU03-01 e INT-HU03-02](../001-reparto-specs-inventario/integration-tasks.md#hu-03--salidas--persona-b).

Los enlaces anteriores son referencias normativas; esta especificación describe necesidades
y resultados de negocio. Las decisiones de implementación corresponden al futuro plan de HU-03.

## Clarifications

### Session 2026-11-20

- Q: ¿Se puede descartar (eliminar) un borrador de salida completo, o solo editarlo y quitarle líneas? → A: Se puede eliminar un borrador completo; no afecta al stock y retira sus referencias al artículo, con la prueba de retirada de referencias de INT-HU03-01 (FR-004, US1.9).
- Q: ¿Cómo se eligen los orígenes al preparar una salida? → A: El sistema propone los orígenes a partir de las reservas propias remanentes de la línea y el operario puede cambiarlos, quitarlos o añadir otros antes de guardar; sin reserva propia no hay propuesta. No es una asignación automática: nada se guarda sin que el operario lo revise (FR-006, US1.10, R-05).
- Q: ¿Incluye HU-03 corregir una salida confirmada mediante una reversión vinculada? → A: No. Una salida confirmada no se revierte en HU-03; las diferencias se corrigen con el ajuste de inventario de HU-04, cuya spec deberá cubrir explícitamente la corrección de una salida errónea (FR-013, Alcance y dependencias).
- Q: ¿Cómo elige el operario, en la pantalla de salidas, el pedido de cliente que va a servir? → A: Escribiendo la referencia externa del pedido: búsqueda exacta, sin distinguir mayúsculas ni espacios extremos, que abre ese pedido; HU-03 añade solo esa búsqueda, sin lista paginada de pedidos (FR-001a, US1.11).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Registrar y confirmar una salida contra un pedido de cliente (Priority: P1)

Como operario que prepara salidas, quiero elegir un pedido de cliente abierto, indicar qué
cantidad de cada línea sale de qué ubicación y lote, revisarlo y confirmarlo, para que las
existencias del sistema bajen exactamente cuando la mercancía abandona el almacén y lo servido
del pedido quede actualizado.

**Why this priority**: Es el objetivo original de HU-03 y el único flujo que descuenta existencias
por expedición. Aporta valor aunque el pedido no tenga reservas.

**Independent Test**: Con físico 10 sin reservas en un bucket y un pedido abierto de 6 unidades de
ese artículo, preparar una salida de 6 desde ese bucket, revisarla y confirmarla; la consulta de
existencias muestra físico 4, reservado 0 y disponible 4, el pedido muestra servido 6, pendiente 0
y estado servido, y el historial muestra un movimiento de salida con responsable, instante, pedido
y cliente.

**Acceptance Scenarios**:

1. **Given** un operario autorizado y un pedido abierto con pendiente, **When** prepara una salida
   con una o varias líneas (línea del pedido, ubicación, lote y cantidad positiva) y la guarda,
   **Then** la salida queda en borrador, sin cambiar existencias, reservas ni lo servido del pedido.
2. **Given** una salida en borrador, **When** la revisa antes de confirmar, **Then** ve el pedido y
   su cliente, y por línea el artículo, la unidad, la ubicación, el lote, la cantidad, el pendiente
   de la línea, el físico, el reservado propio, el reservado ajeno y la cantidad posible del origen.
3. **Given** una salida en borrador válida, **When** el operario la confirma, **Then** el físico de
   cada origen baja en la cantidad servida, lo servido de cada línea del pedido aumenta en la misma
   cantidad, la salida queda confirmada y se muestra su identificador como resultado registrado.
4. **Given** un pedido de 10 unidades del que se sirven 6, **When** se confirma la salida, **Then**
   el pedido sigue abierto con pendiente 4; **When** después se confirma otra salida de 4, **Then**
   el pedido pasa a servido.
5. **Given** una línea del pedido que se sirve desde dos ubicaciones, **When** se confirma una
   salida con una línea por cada origen, **Then** cada origen baja en su cantidad y lo servido de la
   línea aumenta en la suma.
6. **Given** una salida con una cantidad mayor que el pendiente de su línea, una cantidad cero o
   negativa, fracciones en un artículo que no las admite, un lote ajeno al artículo, un origen sin
   existencias del artículo o dos líneas con la misma línea de pedido, ubicación y lote, **When**
   se intenta guardar o confirmar, **Then** se rechaza señalando cada línea afectada, no cambia nada
   y se conservan los datos válidos escritos.
7. **Given** un pedido servido, inexistente o no abierto, **When** se intenta preparar o confirmar
   una salida contra él, **Then** se rechaza explicando el motivo.
8. **Given** una salida en borrador, **When** el operario cambia sus líneas o su fecha y hora del
   hecho y guarda, **Then** el borrador se actualiza; si otro usuario lo cambió antes, se rechaza y
   se ofrece el estado actual.
9. **Given** una salida en borrador, **When** el operario la elimina tras confirmar que quiere
   descartarla, **Then** desaparece sin cambiar existencias, reservas ni lo servido del pedido; si
   otro usuario la cambió o la confirmó antes, se rechaza y se ofrece el estado actual.
10. **Given** una línea de pedido con reservas propias remanentes en dos orígenes, **When** el
    operario la añade a la salida, **Then** se proponen esos orígenes con su remanente reservado,
    sin superar el pendiente de la línea, y el operario puede cambiar la cantidad, quitarlos o
    añadir otro origen antes de guardar; una línea sin reserva propia no recibe propuesta.
11. **Given** un pedido con referencia externa «PED-0042», **When** el operario escribe « ped-0042 »
    en la pantalla de salidas, **Then** se abre ese pedido con sus líneas y pendientes; **When**
    escribe una referencia que no existe, **Then** se indica que no hay ningún pedido con esa
    referencia, sin error técnico; **When** el pedido existe pero ya está servido, **Then** se
    muestra indicando que no admite nuevas salidas.

---

### User Story 2 - Respetar las reservas: consumir la propia y no tocar las ajenas (Priority: P1)

Como responsable del almacén, quiero que una salida consuma primero la reserva del propio pedido
en el origen elegido y nunca consuma unidades reservadas para otros pedidos, para que servir un
pedido no rompa los compromisos de los demás ni descuente dos veces la misma reserva.

**Why this priority**: Es el criterio de la HU que protege los compromisos (bloqueo de salidas
superiores a lo disponible considerando reservas ajenas) y el punto de integración con HU-05.
Sin él, la salida podría dejar reservas sin respaldo físico.

**Independent Test**: V-04 del quickstart común: físico 10; reservar 4 para el pedido A y 3 para el
pedido B en el mismo bucket; servir 6 del pedido A deja físico 4, reservado 3 y disponible 1, la
reserva de A consumida y la de B intacta; un intento de servir 4 del pedido A, o de cualquier otro
pedido, desde ese bucket se rechaza.

**Acceptance Scenarios**:

1. **Given** un bucket con físico 10, reserva propia 4 y reserva ajena 3, **When** se confirma una
   salida de 6 del propio pedido desde ese bucket, **Then** se consumen las 4 unidades reservadas
   propias y 2 disponibles; el bucket queda con físico 4, reservado 3 y disponible 1.
2. **Given** el mismo bucket, **When** se intenta una salida de 8, **Then** se rechaza por
   existencias insuficientes explicando la cantidad posible (7), y no cambia nada.
3. **Given** una salida que solo consume parte de la reserva propia, **When** se confirma, **Then**
   la reserva conserva su remanente y sigue activa; **When** una salida posterior consume el resto,
   **Then** la reserva pasa a consumida.
4. **Given** una línea de pedido con reserva propia en el bucket X, **When** se confirma una salida
   de esa línea desde otro bucket Y y el remanente reservado de la línea superaría su nuevo
   pendiente, **Then** se rechaza explicando que antes debe liberarse o reasignarse la reserva
   sobrante; el sistema no libera reservas en silencio.
5. **Given** una salida confirmada que consumió reserva, **When** se consultan el pedido, sus
   reservas y el historial, **Then** se ven el servido de la línea, el consumido de cada asignación
   y, en el movimiento, la parte servida de reserva y la parte servida de disponible.
6. **Given** una salida confirmada, **When** un responsable intenta reservar o liberar sobre el
   pedido o la reserva con datos leídos antes de esa salida, **Then** la acción se rechaza por
   versión desactualizada y se le ofrece el estado actual.
7. **Given** una salida del pedido sin ninguna reserva, **When** se confirma, **Then** la versión del
   pedido también se renueva, porque cambia lo servido.

---

### User Story 3 - Servir compromisos existentes de artículos desactivados (Priority: P2)

Como operario, quiero poder servir los pedidos ya registrados aunque su artículo se haya
desactivado después, para cumplir los compromisos existentes sin reactivar el artículo.

**Why this priority**: Regla común de desactivación (R-05) y tarea receptora INT-HU03-02. Es un caso
menos frecuente que las salidas ordinarias.

**Independent Test**: Registrar un pedido y una reserva de un artículo, desactivarlo y confirmar
una salida de ese pedido; se permite, el historial y los saldos son coherentes y el artículo
aparece como «inactivo» en la revisión. Intentar después registrar un pedido nuevo del artículo
(HU-05) sigue rechazándose.

**Acceptance Scenarios**:

1. **Given** un pedido abierto cuyo artículo se desactivó después del alta, **When** se prepara y
   confirma una salida, **Then** se permite con los mismos límites de pendiente, reserva propia,
   reserva ajena y permisos que para un artículo activo.
2. **Given** ese artículo, **When** se revisa la salida, **Then** se indica textualmente que está
   inactivo, sin impedir la confirmación.
3. **Given** una desactivación y una confirmación de salida del mismo artículo simultáneas, **When**
   ambas terminan, **Then** en cualquier orden se mantienen el historial, los saldos y lo servido
   coherentes, y ninguna queda a medias.

---

### User Story 4 - Consultar las salidas de un pedido (Priority: P2)

Como trabajador del almacén con permiso de consulta, quiero ver las salidas de un pedido, en
borrador y confirmadas, con sus líneas, para saber qué se ha expedido, cuándo, desde dónde y por
quién.

**Why this priority**: Hace visible la trazabilidad de las salidas y permite retomar un borrador,
pero depende de que existan salidas (US1).

**Independent Test**: Con un pedido con un borrador y dos salidas confirmadas, abrir las salidas
del pedido y comprobar su estado, fecha y hora del hecho, responsable y líneas; abrir una salida
confirmada y comprobar que no ofrece edición.

**Acceptance Scenarios**:

1. **Given** un pedido con salidas, **When** un usuario con permiso de consulta las revisa, **Then**
   ve cada salida con su estado (borrador o confirmada), fecha y hora del hecho y del registro,
   responsable y, por línea, artículo, unidad, ubicación, lote, cantidad y reserva propia consumida.
2. **Given** una salida confirmada, **When** se abre, **Then** no puede editarse ni confirmarse de
   nuevo, y se muestra el resultado registrado.
3. **Given** un usuario con permiso de consulta y sin permiso de registro de salidas, **When**
   abre las salidas, **Then** puede verlas pero no se le ofrecen acciones de preparar, editar ni
   confirmar.

---

### User Story 5 - Operar con autorización y resultado fiable (Priority: P3)

Como responsable del proyecto, quiero que preparar y confirmar salidas respete los permisos y dé
un resultado fiable ante reintentos, cambios simultáneos y fallos, para no perder ni duplicar
unidades expedidas.

**Why this priority**: Transversal a las demás historias; protege los principios IV y V una vez que
existe la confirmación.

**Independent Test**: Intentar cada acción sin sesión y con cada rol; repetir una confirmación con
la misma intención y con otra intención; confirmar desde una versión desactualizada del borrador;
simular la pérdida de la respuesta tras confirmar; confirmar a la vez dos salidas que compiten por
el mismo disponible.

**Acceptance Scenarios**:

1. **Given** un usuario sin sesión o sin el permiso de la acción, **When** intenta consultar,
   preparar, editar o confirmar una salida, **Then** se rechaza sin efectos; un operario puede hacer
   todas ellas, y un comprador o gestor de maestros solo consultar.
2. **Given** una confirmación ya registrada, **When** se repite la misma intención, **Then** se
   devuelve el resultado original sin duplicar el efecto; si la intención repetida tiene un
   contenido distinto, se rechaza; **When** se intenta confirmar de nuevo con otra intención,
   **Then** se rechaza porque la salida ya está confirmada.
3. **Given** una respuesta perdida tras confirmar, **When** el usuario vuelve a la pantalla,
   **Then** ve «resultado pendiente de comprobar» y puede consultar la operación antes de repetirla.
4. **Given** un bucket con físico 10 sin reservas, **When** dos operarios confirman a la vez salidas
   de 8 para pedidos distintos, **Then** solo una se confirma, la otra se rechaza por existencias
   insuficientes y el físico final es 2, nunca negativo.
5. **Given** dos salidas del mismo pedido que juntas superan el pendiente de una línea, **When** se
   confirman a la vez, **Then** solo se confirman las que caben y lo servido nunca supera lo pedido.
6. **Given** una reserva o una liberación y una salida simultáneas sobre el mismo bucket o la misma
   reserva, **When** ambas terminan, **Then** el reservado nunca supera el físico ni queda negativo,
   y lo consumido más lo liberado nunca supera lo asignado.
7. **Given** un fallo durante la confirmación, **When** se consultan después existencias, reservas,
   pedido, salida e historial, **Then** no aparece ningún efecto parcial.

---

### Edge Cases

- Artículo sin seguimiento por lote: el origen se presenta como «sin lote» y no se pide lote.
  Artículo con seguimiento: el lote es obligatorio y debe ser del artículo.
- Origen con reserva propia mayor que la cantidad a servir: se consume solo la cantidad servida;
  el resto de la reserva sigue activo si cabe en el nuevo pendiente.
- Origen sin reserva propia y con todo el físico reservado para otros pedidos: la cantidad posible
  es cero y la confirmación se rechaza.
- Varias reservas propias de la misma línea en el mismo origen: se consumen en un orden estable
  hasta cubrir la parte reservada de la salida, sin consumir más que la cantidad servida.
- Varios borradores del mismo pedido: se permiten, pero el pendiente se revalida al confirmar cada
  uno; el segundo que intente superarlo se rechaza aunque se guardara cuando cabía.
- Borrador preparado cuando había existencias que después otro movimiento consumió: el borrador no
  retiene stock; la confirmación revalida y se rechaza explicando la cantidad posible actual.
- Pedido servido por otra salida mientras el borrador esperaba: la confirmación se rechaza porque
  el pedido ya no está abierto; el borrador se conserva para su consulta.
- Ubicación en conteo abierto: se rechaza confirmar salidas desde ella hasta cerrar el conteo
  (cuando HU-04 esté integrada).
- Fecha y hora del hecho posterior al momento del registro: se rechaza. Anterior: se admite y se
  conservan ambas, para registrar una salida tardía tras una contingencia.
- Línea de pedido sin pendiente en el momento de preparar: no se ofrece para la salida.
- Referencia de pedido escrita de forma parcial o con errores: no hay coincidencia aproximada; se
  indica que no existe ningún pedido con esa referencia y se conserva el texto escrito para
  corregirlo.
- Salida confirmada con un error físico (cantidad u origen equivocados): no se edita ni se borra;
  tampoco se revierte en HU-03; la diferencia se corrige con un ajuste de inventario de HU-04.
- Propuesta de orígenes cuya reserva propia supera lo que puede servirse (por ejemplo, porque el
  físico ha bajado): la propuesta es orientativa y la confirmación revalida y explica la cantidad
  posible.

## Requirements *(mandatory)*

### Functional Requirements

**Preparar la salida**

- **FR-001**: El sistema DEBE permitir a los usuarios con permiso de registro de salidas preparar
  una salida vinculada a un único pedido de cliente abierto existente, con fecha y hora del hecho y
  una o varias líneas. Esta feature NO DEBE crear pedidos de cliente ni reservas: usa los que
  registra HU-05.
- **FR-001a**: Los usuarios con permiso de consulta DEBEN poder localizar un pedido de cliente
  escribiendo su referencia externa completa, comparada sin distinguir mayúsculas ni espacios
  extremos, con la misma regla de unicidad del alta de HU-05. La búsqueda DEBE devolver como mucho
  un pedido, indicar expresamente cuándo no existe ninguno y mostrar el estado del pedido
  encontrado. Esta feature NO DEBE ofrecer una lista ni una búsqueda parcial de pedidos.
- **FR-002**: Cada línea DEBE indicar una línea del pedido, una ubicación de origen, el lote (o «sin
  lote» si el artículo no se gestiona por lote) y una cantidad positiva en la unidad del artículo.
  Una misma salida NO DEBE contener dos líneas con la misma línea de pedido, ubicación y lote; sí
  puede servir una línea de pedido desde varios orígenes.
- **FR-003**: Al guardar o confirmar DEBE rechazarse, señalando cada línea afectada y sin cambios
  parciales, toda línea con cantidad no positiva, fracciones en un artículo que no las admite, un
  lote ajeno al artículo o ausente cuando el artículo lo exige, una ubicación inexistente, una línea
  que no pertenezca al pedido o una suma por línea de pedido superior a su pendiente.
- **FR-004**: Una salida guardada sin confirmar DEBE quedar en borrador, editable por usuarios con
  permiso de registro de salidas, y NO DEBE cambiar existencias, reservas ni lo servido del pedido.
  Un borrador DEBE poder eliminarse completo, sin efecto sobre existencias, reservas ni lo servido,
  retirando sus referencias al artículo según el contrato común de escritores de referencias.
  La edición o eliminación de un borrador cambiado o confirmado por otro usuario desde que se leyó
  DEBE rechazarse sin cambios. Una salida confirmada NO DEBE poder eliminarse.
- **FR-005**: La fecha y hora del hecho DEBE ser obligatoria y no posterior al momento del registro;
  el sistema DEBE conservar además la fecha y hora del registro.
- **FR-006**: Antes de confirmar, el usuario DEBE poder revisar el pedido y su cliente y, por línea,
  artículo (con su estado si está inactivo), unidad, ubicación, lote, cantidad, pendiente de la
  línea, físico, reservado propio, reservado ajeno y cantidad posible del origen. Estas cifras son
  orientativas: el sistema las revalida al confirmar.
- **FR-006a**: Al añadir una línea de pedido a la salida, el sistema DEBE proponer como orígenes las
  combinaciones de ubicación y lote con reserva propia remanente de esa línea, con su remanente como
  cantidad y sin superar en total el pendiente de la línea. El operario DEBE poder cambiar, quitar o
  añadir orígenes antes de guardar. Sin reserva propia NO DEBE proponerse ningún origen, y el sistema
  NO DEBE elegir orígenes ni lotes por su cuenta (R-05).

**Confirmar la salida**

- **FR-007**: Confirmar DEBE aplicar, para cada línea, el cálculo común de salida: consumir de la
  reserva propia de la línea en ese origen el mínimo entre la cantidad servida y su remanente; la
  parte restante DEBE caber en el disponible del origen, y la cantidad completa en su físico.
  En ningún caso DEBE consumirse reserva de otro pedido.
- **FR-008**: Confirmar DEBE disminuir el físico de cada origen en la cantidad servida y el
  reservado solo en la parte consumida de la reserva propia; DEBE aumentar el consumido de las
  asignaciones de reserva afectadas, recalcular el estado de cada reserva (activa o consumida) y
  aumentar lo servido de cada línea del pedido en la suma de sus líneas de salida.
- **FR-009**: Si tras la salida todas las líneas del pedido quedan con pendiente cero, el pedido
  DEBE pasar a servido; en otro caso DEBE seguir abierto.
- **FR-010**: La confirmación DEBE rechazarse sin cambios si el pedido no está abierto, si alguna
  línea supera el pendiente de su línea de pedido, si alguna parte sin reserva propia supera el
  disponible de su origen o si la ubicación de origen está en un conteo abierto, explicando el motivo
  y, en el caso de existencias, la cantidad posible actual.
- **FR-011**: Si tras servir el remanente de reservas propias de una línea de pedido superara su
  nuevo pendiente, la confirmación DEBE rechazarse indicando que antes debe liberarse o reasignarse
  la reserva sobrante; el sistema NO DEBE liberar reservas de forma implícita.
- **FR-012**: Toda salida confirmada DEBE renovar la versión del pedido, aunque no consuma reservas,
  y la de cada reserva cuyo consumido cambie, de modo que cualquier reserva, liberación o salida
  preparada sobre datos anteriores se rechace por versión desactualizada.
- **FR-013**: Una salida confirmada NO DEBE editarse, eliminarse, revertirse ni confirmarse de
  nuevo en esta feature. Una diferencia descubierta después se corrige con el ajuste de inventario
  de HU-04, trazable y con su motivo, responsable y autorización.
- **FR-014**: Se DEBE permitir preparar y confirmar salidas de pedidos existentes aunque su
  artículo se haya desactivado después del alta, con los mismos límites y permisos.

**Trazabilidad**

- **FR-015**: Cada salida confirmada DEBE quedar en el historial como movimiento de salida con
  identificador único, responsable, fecha y hora del hecho y del registro, artículo, cantidad
  positiva, unidad, ubicación y lote de origen, motivo, documento de soporte (la salida y la
  referencia externa del pedido) y contraparte; y DEBE distinguir la parte servida de reserva
  propia de la parte servida de disponible.
- **FR-016**: Cada movimiento de salida DEBE registrar exactamente una de estas dos cosas: la
  referencia de la contraparte (el cliente del pedido) o el motivo por el que no aplica.
- **FR-017**: El historial de salidas NO DEBE poder modificarse ni eliminarse; los saldos y lo
  servido DEBEN poder reconstruirse a partir de los movimientos confirmados.
- **FR-018**: Los usuarios con permiso de consulta DEBEN poder ver las salidas de un pedido y el
  detalle de cada una con su estado, responsable, fechas y líneas, incluida la reserva propia
  consumida por línea en las confirmadas.

**Autorización y fiabilidad**

- **FR-019**: Toda consulta y operación de esta feature DEBE exigir una sesión identificada y el
  permiso correspondiente (consulta de inventario para ver; registro de salidas para preparar,
  editar y confirmar), comprobado por el sistema y no solo ocultando opciones en pantalla.
  Confirmar una salida NO DEBE exigir el permiso de gestión de reservas.
- **FR-020**: La confirmación DEBE aplicarse como una sola operación atómica: salida, movimientos,
  saldos, reservas, lo servido y el estado del pedido se confirman todos o ninguno.
- **FR-021**: Repetir la misma intención de guardar o confirmar DEBE devolver el resultado original
  sin repetir el efecto; reutilizar una intención con contenido distinto DEBE rechazarse; una
  intención nueva de confirmar una salida ya confirmada DEBE rechazarse sin efecto.
- **FR-022**: Las operaciones simultáneas (salidas, reservas, liberaciones, desactivación del
  artículo y conteos) NO DEBEN producir físico negativo, reservado superior al físico o negativo,
  servido superior a lo pedido, consumido más liberado superior a lo asignado ni salidas duplicadas.
- **FR-023**: La interfaz DEBE comunicar éxito solo tras la confirmación conservada, mostrar el
  identificador del resultado y, ante una respuesta perdida, indicar «resultado pendiente de
  comprobar» y permitir consultar la operación antes de repetirla.
- **FR-024**: Los errores DEBEN explicar qué ocurrió y cómo corregirlo (por ejemplo, la cantidad
  posible actual o la reserva sobrante que debe liberarse), conservando los datos válidos y llevando
  el foco al primer error.
- **FR-025**: Pantallas y mensajes DEBEN usar español y vocabulario del almacén, mostrar la unidad
  junto a cada cantidad, ser operables por teclado y no comunicar estados solo mediante color.

### Alcance y dependencias

- Esta feature desarrolla verticalmente la preparación, revisión, confirmación y consulta de salidas
  de mercancía contra pedidos de cliente. Quedan fuera el alta, edición y cancelación de pedidos y
  la creación y liberación de reservas (HU-05), la lista paginada y la búsqueda parcial de
  pedidos (pendientes de HU-05, S1), los conteos y ajustes (HU-04), las alertas de mínimo
  (HU-06), los traslados entre ubicaciones, las devoluciones de cliente, el transporte, los
  albaranes imprimibles y cualquier dato comercial.
- Corrección de salidas confirmadas (clarificación 2026-11-20): HU-03 no las revierte. La spec de
  **HU-04** DEBERÁ cubrir explícitamente la corrección de una salida errónea (cantidad u origen
  equivocados) mediante su ajuste trazable, incluidos su efecto sobre el físico del origen y su
  relación con la salida original. Esto no corrige lo servido del pedido ni el consumo de
  reservas, que la salida confirmada conserva; si HU-04 necesitara cambiarlos, deberá acordarse
  como cambio compartido (AGENTS.md).
- **Limitación de la corrección por conteo (P-07)**: con una salida confirmada de 6 cuando salieron
  4, el ajuste del conteo devuelve 2 al saldo de la ubicación, pero el pedido sigue mostrando 6
  servidas y su estado no cambia: **no se corrige el pedido**. Corregirlo exigiría corregir su
  acumulado servido y su estado en el mismo commit que el movimiento, como exige el
  [contrato común § Correcciones](../../.specify/memory/project/contracts/stock-operations.md#correcciones-y-contingencia).
  La pantalla de detalle del conteo de HU-04 debe avisarlo de forma visible.
- Cobertura de los criterios originales de HU-03: vinculación a pedido de cliente (US1, FR-001);
  artículos, cantidades y ubicaciones de origen (US1, FR-002/003); bloqueo de salidas superiores a lo
  disponible considerando reservas ajenas (US2, FR-007/010); descuento al confirmar (US1.1, US1.3,
  FR-004/008); movimiento trazable (US4, FR-015–FR-018).
- **HU-05 (B)** entrega el pedido de cliente mínimo, sus líneas con pedido, servido y pendiente, las
  reservas con sus asignaciones, la consulta de existencias y la liberación con la que se resuelve
  un rechazo por reserva sobrante; HU-03 los consume según el
  [contrato de integración](../004-consultar-existencias/contracts/hu03-integration.md) y no crea
  otra lectura del pendiente ni otro cálculo de reserva. **HU-08 (A)** aporta artículo, unidad,
  admisión de fracciones, seguimiento por lote y estado activo; la definición del artículo ya está
  fijada por la línea de pedido de HU-05. **HU-02 (B)** aporta las ubicaciones. **HU-01 (A)** genera
  las existencias que se expiden. **HU-04 (B)** aporta la congelación por conteo abierto.
  **HU-06 (A)** evalúa el mínimo con el mismo disponible y debe reevaluarse tras cada salida.
- La confirmación reutiliza los componentes comunes de H0 —resultado de operación, ejecutor,
  bloqueos y núcleo de stock— descritos en el
  [contrato de stock](../../.specify/memory/project/contracts/stock-operations.md); esta feature no
  crea otra vía de escritura, otro registro de resultados ni otro cálculo de disponible.

### Integraciones recibidas

HU-03 incorpora estas tareas receptoras con sus IDs de trazabilidad; el plan y las tareas las
concretarán como pruebas de este módulo, no de un doble de otro módulo.

| ID | Origen | Qué exige a HU-03 | Requisitos |
|----|--------|-------------------|------------|
| INT-HU03-01 | [Backlog de integración](../001-reparto-specs-inventario/integration-tasks.md#hu-03--salidas--persona-b) (HU-08 T035/T064/T066/T075/T100/T101) | Integrar la salida y sus líneas con el contrato de aceptación de escritores de referencias (borradores, retirada de líneas, lectores, carreras con la edición y el borrado del artículo); consumir el pedido y las reservas de HU-05 sin duplicar sus altas; confirmar salida, servido y consumo de reserva con el protocolo común. | FR-001–FR-004 (incluida la eliminación de borradores), FR-007–FR-012, FR-020, FR-022 |
| INT-HU03-02 | Ídem (HU-08 T078/T103) | Servir compromisos anteriores de artículos inactivos con sus límites, reservas propias y ajenas y permisos originales; conservar historial y saldos; coordinación con la desactivación. | FR-014, US3 |
| HU-05 regla 3 y 4 del [contrato HU-03](../004-consultar-existencias/contracts/hu03-integration.md#3-reglas-que-hu-03-debe-respetar) | Pendiente declarado por HU-05 | Probar el consumo de reservas al servir (consumido por asignación, estado derivado de la reserva, Σ remanente = reservado del bucket) y la renovación de la versión de la cabecera del pedido en toda salida, con y sin reservas, y la de cada reserva consumida. | FR-008, FR-012, US2.5–US2.7 |
| HU-05 S-18 completo | [tasks.md de HU-05](../004-consultar-existencias/tasks.md), «Si hay tiempo» S4 («HU-03 lo amplía») | Ampliar la prueba del lector del pedido dentro de la confirmación: pendiente y remanente por bucket antes y después de servir. | FR-007, FR-008 |
| V-04 y V-05 con sus extensiones | [Guía de validación](../../.specify/memory/project/quickstart.md#6-escenarios-de-aceptación-integrados) | Reserva propia frente a ajena; servir desde otro bucket sin dejar reservas sobrantes; salidas simultáneas y solapamiento de reserva y salida. | US2, US5.4–US5.6 |

La aceptación operativa H5 y la revisión compartida siguen en el
[backlog de integración](../001-reparto-specs-inventario/integration-tasks.md#h5--aceptación-operativa-y-puesta-en-servicio--responsables-del-proyectoalmacén).

### Principios constitucionales y evidencia de aceptación

| Principio | Aplicación a HU-03 | Evidencia exigida |
|-----------|-------------------|-------------------|
| I. Correspondencia almacén/sistema | El físico baja exactamente al confirmar la salida; el borrador no altera nada; las salidas tardías conservan la fecha y hora del hecho. | US1.1, US1.3, FR-004, FR-005, FR-008; comparación antes/después. |
| II. Ubicación física precisa | Cada unidad expedida sale de una ubicación y lote concretos. | US1.5, FR-002, FR-015; V-03. |
| III. Uso intuitivo | Revisión previa con reserva propia/ajena y cantidad posible, errores corregibles, estados con texto. | US1.2, US1.6, FR-006, FR-023–FR-025; validación con operarios representativos documentada (V-13). |
| IV. Trazabilidad | Movimiento de salida completo, con soporte, contraparte y parte servida de reserva; historial inalterable. | US2.5, US4, FR-015–FR-018; V-04. |
| V. Integridad y recuperación | Atomicidad, sin salidas por encima de lo disponible, idempotencia, versiones, permisos y concurrencia. | US2.1–US2.4, US5, FR-007, FR-010–FR-012, FR-019–FR-022; V-05 con su extensión, V-07, V-08 y V-12. |

La evidencia de implementación conservará fecha, entorno, escenario, resultado e incidencias.
Esta especificación define la aceptación; no declara realizadas pruebas ni revisiones conjuntas.

### Key Entities *(include if feature involves data)*

- **Salida**: documento vinculado a un pedido de cliente, con estado borrador o confirmada, fecha y
  hora del hecho y del registro, responsable y resultado de la operación que la confirmó.
- **Línea de salida**: línea del pedido servida, artículo, ubicación y lote de origen, cantidad y,
  al confirmar, la parte consumida de la reserva propia (entre cero y la cantidad).
- **Movimiento de salida — referencia compartida**: hecho del historial con la parte servida de
  reserva y la parte servida de disponible, soporte y contraparte.
- **Pedido de cliente y línea — de HU-05**: lo pedido, lo servido (que esta feature aumenta), el
  pendiente y el estado abierto o servido.
- **Reserva y asignación — de HU-05**: el consumido de cada asignación (que esta feature aumenta),
  el remanente y el estado derivado activa o consumida.
- **Existencia por ubicación y lote — referencia compartida**: físico y reservado; el disponible se
  deriva.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: En el 100 % de los casos de la batería de aceptación, tras confirmar una salida el
  físico de cada origen baja exactamente en la cantidad servida, el reservado solo en la reserva
  propia consumida y lo servido del pedido en la suma de sus líneas.
- **SC-002**: El 100 % de los intentos de servir por encima del pendiente, de lo disponible sin
  reserva propia o del físico, o de consumir reservas de otro pedido, se rechaza sin ningún cambio.
- **SC-003**: En la prueba de salidas simultáneas sobre el mismo disponible, exactamente una se
  confirma y el físico nunca queda negativo, en todas las repeticiones; lo mismo con salidas del
  mismo pedido que compiten por su pendiente y con una reserva solapada con una salida.
- **SC-004**: En el 100 % de los casos definidos de acceso no autorizado, intención repetida,
  versión desactualizada y fallo inducido, el resultado es el esperado sin efectos duplicados ni
  parciales.
- **SC-005**: Tras el 100 % de las salidas confirmadas de la batería, la versión del pedido ha
  cambiado y, en las que consumen reserva, la de cada reserva consumida; la suma de remanentes de
  reserva de cada bucket coincide con su reservado.
- **SC-006**: Con 25 sesiones simultáneas, el 95 % de las confirmaciones de una salida de hasta
  10 líneas muestra su resultado en 3 segundos o menos.
- **SC-007**: Los operarios representativos que participan en la validación preparan, revisan y
  confirman una salida con reserva propia y otra sin reserva sin asistencia técnica y sin errores de
  registro no detectados.

## Estado de la entrega

Reconciliado con `/speckit-converge` el 2026-11-21 ([informe](validation/converge-1.md)). Los
requisitos no cambian.

- **Construido**: T003–T018 y Convergence C1–C4. La evidencia por requisito está en el informe.
- **Fuera de esta entrega, con justificación**:
  - SC-006 (rendimiento) y SC-007 (validación con operarios, principio III): HU-03 se integra, pero
    no se da por aceptada (plan, Complexity Tracking).
  - El rechazo por conteo abierto de FR-010: `IsInOpenCountAsync` devuelve `false` hasta
    INT-HU04-03.
  - La corrección de salidas confirmadas erróneas pasa a HU-04 (clarificación Q3; P-07). Corrige
    solo el físico, no lo servido ni el estado del pedido (ver «Alcance y dependencias»).
- **Incumplimientos de la convergencia** (informe G1–G4):
  - G1, resuelto en C2: el rechazo por existencias explica la cantidad posible (US2.2, FR-010,
    FR-024).
  - G2, resuelto en C3: cada línea identifica el artículo y la salida muestra la fecha de registro
    (US4.1, FR-018).
  - G4, resuelto en C4: los 409 de estado recargan el estado actual (shipping-ui).
  - G3, límite aceptado: el aviso de existencias va arriba de la página, con foco, y no junto a la
    línea.

## Assumptions

- Permisos según el catálogo común: ver salidas con el permiso de lectura de inventario
  (Inventory.Read); preparar, editar y confirmar con el de registro de salidas (Shipping.Record:
  operario). Consumir reservas al servir forma parte de la salida y no exige el permiso de gestión
  de reservas; resolver una reserva sobrante sí, mediante la liberación de HU-05 (responsable de
  almacén). El administrador no tiene permisos implícitos de inventario.
- La contraparte de una salida es siempre el cliente del pedido: HU-05 exige su referencia en el
  alta, de modo que el movimiento registra la referencia de la contraparte y nunca el motivo de no
  aplicación. El documento de soporte es la propia salida junto con la referencia externa del pedido.
- El motivo del movimiento es la expedición del pedido; no se pide otro motivo al operario.
- La salida sigue el ciclo borrador → confirmada del modelo y del contrato HTTP común; un borrador
  no reserva ni retiene existencias. Eliminar un borrador no añade un estado nuevo al modelo: el
  borrador deja de existir y no deja movimientos, porque nunca los tuvo.
- Puede haber varios borradores y varias salidas parciales del mismo pedido; el pendiente se
  revalida al confirmar.
- El responsable del borrador y el de la confirmación pueden ser distintos; ambos quedan
  registrados.
- La fecha y hora del hecho admite el pasado para registrar salidas tardías tras una contingencia;
  la incorporación de comprobantes sigue el procedimiento común.
- Mientras HU-04 no esté integrada no existen conteos abiertos; la regla de congelación se aplica
  desde su integración (INT-HU04-03).
- No se introduce gestión por serie ni caducidad, selección automática de lotes por antigüedad ni
  cálculo de rutas de preparación.
- Los objetivos de rendimiento y la validación con operarios son criterios a medir, no resultados
  obtenidos; respaldo y restauración siguen la guía global.
