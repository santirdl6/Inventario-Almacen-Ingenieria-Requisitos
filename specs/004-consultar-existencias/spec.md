# Feature Specification: HU-05 — Consultar existencias y reservas

**Feature Branch**: `b/hu-05-consultar-existencias` (el directorio de la feature es independiente de la rama Git).

**Created**: 2026-11-19

**Status**: Draft

**Input**: User description: "HU-05: consultar existencias y reservas. Alcance: consulta de existencias de un artículo (exige artículo; filtros opcionales por ubicación y lote; físico, reservado y disponible = físico − reservado; totales del filtro completo; solo movimientos confirmados; artículos inactivos visibles y marcados); pedido de cliente mínimo (alta y consulta, sin precios ni módulo comercial); reservar y liberar existencias (permiso Reservations.Manage; consulta con Inventory.Read; alta de pedido con Shipping.Record). Fuera de alcance: salidas (HU-03), conteos (HU-04), alertas (HU-06). Reutiliza OperationResult, InventoryOperationExecutor, InventoryLocks y StockCore; no redefine el cálculo de disponible."

**Historia de origen**: HU-05 del [catálogo y reparto](../001-reparto-specs-inventario/spec.md#catálogo-y-asignación-de-las-ocho-futuras-specs).
**Responsable principal**: Persona B. **Hito de inicio**: H2, según el
[plan de coordinación](../001-reparto-specs-inventario/plan.md#hitos-y-coordinación).

**Contexto normativo y acuerdos consumidos**:

- [Constitución vigente](../../.specify/memory/constitution.md): principios I–V, en especial I, II, IV y V, y criterios de aceptación.
- [Plan global](../../.specify/memory/project-plan.md): arquitectura compartida, alcance mínimo de pedidos de cliente y reservas, y coordinación.
- [Decisiones comunes](../../.specify/memory/project/research.md#r-05-reglas-comunes-de-negocio-para-no-bloquear-las-specs): reservas explícitas, lotes, desactivación de artículos y conteos.
- [Modelo común](../../.specify/memory/project/data-model.md#compromisos-y-salidas--persona-b--hu-03-hu-05): saldo por bucket, pedido de cliente mínimo, reserva y asignación.
- [Contrato HTTP común](../../.specify/memory/project/contracts/api.md#hu-05--b-consultas): consultas, pedidos de cliente, reservas, permisos y errores.
- [Contrato de stock](../../.specify/memory/project/contracts/stock-operations.md): protocolo de reservar y liberar, implementación común de H0 y consulta consistente.
- [Contrato de interacción](../../.specify/memory/project/contracts/ui.md): pantalla /existencias, estados de operación y accesibilidad.
- [Guía de validación](../../.specify/memory/project/quickstart.md#6-escenarios-de-aceptación-integrados): datos comunes, V-03, V-04, V-05, V-07, V-08 y V-11.

Los enlaces anteriores son referencias normativas; esta especificación describe necesidades
y resultados de negocio. Las decisiones de implementación corresponden al futuro plan de HU-05.

## Clarifications

### Session 2026-11-19

- Q: ¿Puede un mismo artículo aparecer en dos líneas distintas de un pedido de cliente? → A: No: una línea por artículo; el alta rechaza el artículo repetido señalando la línea (FR-011).
- Q: ¿Qué hace la consulta con las combinaciones de ubicación y lote cuyo físico ha quedado a cero? → A: Se ocultan por defecto, con una opción explícita para mostrarlas; los totales no cambian (FR-002).
- Q: ¿Incluye esta entrega editar o cancelar un pedido de cliente, dado que el modelo común define el estado cancelado y un comando de modificación? → A: Ni editar ni cancelar: solo alta y consulta; un compromiso innecesario se resuelve liberando reservas. Se documenta la desviación respecto al modelo común (Alcance y dependencias).
- Q: ¿Cómo se indica el motivo al liberar una reserva: texto libre obligatorio o lista cerrada? → A: Texto libre obligatorio y no vacío, sin catálogo de motivos (FR-018).
- Q: ¿Se confirman la asignación manual de ubicación y lote al reservar y las liberaciones parciales múltiples? → A: Sí, ambas: el responsable elige ubicación y lote sin propuesta automática (FR-015), y una reserva admite varias liberaciones parciales distintas, cada una aplicada una sola vez y sin superar el remanente (FR-018).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Consultar las existencias de un artículo (Priority: P1)

Como trabajador del almacén con permiso de consulta, quiero elegir un artículo y ver cuánto hay
de él en cada ubicación y lote, separando lo físico, lo reservado y lo disponible, para saber
dónde está y cuánto puedo comprometer o servir sin recorrer el almacén ni preguntar a nadie.

**Why this priority**: Es el objetivo original de HU-05 y el dato que comparten salidas (HU-03),
conteos (HU-04) y alertas (HU-06). Aporta valor por sí sola aunque aún no existan reservas.

**Independent Test**: Preparar un artículo con 2 unidades en una ubicación y 4 en otra, ambas del
lote L-001; consultarlo y comprobar dos filas, totales físico 6, reservado 0 y disponible 6;
filtrar por una ubicación y comprobar que filas y totales corresponden solo a ella.

**Acceptance Scenarios**:

1. **Given** un artículo con existencias en varias ubicaciones y lotes, **When** un usuario con
   permiso de consulta lo selecciona, **Then** ve una fila por cada combinación de ubicación y lote
   con físico, reservado y disponible, la unidad de medida y unos totales que agregan todo el
   resultado, no solo la página visible.
2. **Given** el mismo artículo, **When** filtra por una ubicación, por un lote o por ambos,
   **Then** las filas y los totales corresponden exclusivamente al filtro aplicado.
3. **Given** una fila con físico 12 y reservado 8, **When** se muestra, **Then** su disponible es 4;
   y el disponible total es el físico total menos el reservado total del mismo filtro.
4. **Given** una búsqueda sin artículo seleccionado, **When** se intenta consultar, **Then** se
   indica que debe elegirse un artículo y no se muestra un listado general de existencias.
5. **Given** un movimiento confirmado y otro documento todavía en borrador sobre el artículo,
   **When** se consulta, **Then** las cifras incluyen el confirmado y no el borrador.
6. **Given** un artículo desactivado con existencias o historial, **When** se busca y consulta,
   **Then** aparece con la indicación textual «inactivo» y sus cantidades siguen visibles.
7. **Given** un artículo sin existencias, o un filtro sin coincidencias, **When** se consulta,
   **Then** se indica expresamente que no hay existencias para esa selección.
8. **Given** una consulta mostrada, **When** otro usuario confirma un movimiento, **Then** la
   pantalla indica el instante al que corresponden las cifras y el usuario puede actualizarlas
   manualmente para ver el cambio.
9. **Given** un artículo con una combinación de ubicación y lote que quedó a físico cero,
   **When** se consulta, **Then** esa fila no aparece por defecto; al activar la opción de mostrar
   las de físico cero aparece con físico, reservado y disponible 0, y los totales no cambian.

---

### User Story 2 - Registrar y consultar un pedido de cliente mínimo (Priority: P2)

Como operario que prepara salidas, quiero registrar un pedido de cliente con sus líneas de
artículo y cantidad y consultar después su situación, para disponer del compromiso al que se
asocian las reservas y, más adelante, las salidas, sin necesidad de un módulo comercial.

**Why this priority**: Sin pedido no hay a qué vincular una reserva. Es soporte mínimo compartido
con HU-03; la consulta de existencias (US1) aporta valor sin él.

**Independent Test**: Registrar un pedido con dos líneas de artículos activos, abrirlo y comprobar
pedido, servido cero, pendiente igual a lo pedido y reservado cero por línea; intentar registrar
otro con la misma referencia externa.

**Acceptance Scenarios**:

1. **Given** un operario autorizado y una referencia externa no utilizada, **When** registra el
   pedido con la referencia del cliente y al menos una línea de artículo activo y cantidad
   positiva, **Then** el pedido queda abierto, con servido cero y sin reservas, y se muestra su
   identificador como resultado registrado.
2. **Given** un pedido existente, **When** se intenta registrar otro con la misma referencia
   externa, aunque difiera en mayúsculas o espacios extremos, **Then** se rechaza indicando el campo
   y no se crea ningún pedido.
3. **Given** un alta con una línea de cantidad cero o negativa, de un artículo inexistente o
   inactivo, con fracciones en un artículo que no las admite o con un artículo repetido,
   **When** se intenta guardar, **Then** se rechaza señalando cada línea afectada, no se crea nada
   y se conservan los datos válidos escritos.
4. **Given** un pedido con reservas, **When** un usuario con permiso de consulta lo abre,
   **Then** ve por línea lo pedido, servido, pendiente y reservado remanente, y las reservas
   asociadas con sus ubicaciones y lotes.

---

### User Story 3 - Reservar existencias para una línea de pedido (Priority: P2)

Como responsable del almacén, quiero reservar cantidades concretas de una línea de pedido en
ubicaciones y lotes que elijo, para que ese stock quede comprometido y no se prometa ni se
sirva a otro pedido.

**Why this priority**: Hace visible la diferencia entre físico y disponible y protege los
compromisos, pero depende de existencias (US1) y pedidos (US2).

**Independent Test**: Con físico 10 sin reservas en un bucket y un pedido abierto de 7 unidades,
reservar 7 en ese bucket; la consulta muestra físico 10, reservado 7, disponible 3, y los
compromisos del artículo muestran el pedido y el bucket. Intentar reservar 4 para otro pedido.

**Acceptance Scenarios**:

1. **Given** una línea de pedido abierta con pendiente sin reservar y un bucket con disponible
   suficiente, **When** un responsable reserva una cantidad en ese bucket, **Then** el reservado del
   bucket aumenta en esa cantidad, el físico no cambia y el resultado indica que quedó registrado.
2. **Given** un bucket con disponible inferior a la cantidad solicitada, **When** se intenta
   reservar, **Then** se rechaza explicando el disponible actual y no cambia nada.
3. **Given** una ubicación o lote del artículo sin existencias, **When** se intenta reservar en
   ellos, **Then** se rechaza aunque la ubicación y el lote existan.
4. **Given** una línea cuyo pendiente ya está cubierto por sus reservas remanentes, **When** se
   intenta reservar más, **Then** se rechaza por superar el pendiente de la línea.
5. **Given** una reserva con varias asignaciones en distintos buckets, **When** alguna no es
   válida, **Then** no se registra ninguna asignación.
6. **Given** un bucket con físico 10 sin reservas, **When** dos responsables reservan 7 a la vez
   para pedidos distintos, **Then** solo una reserva se confirma, la otra se rechaza por existencias
   insuficientes y el reservado nunca supera el físico.
7. **Given** un artículo desactivado, un pedido no abierto o una ubicación en conteo abierto,
   **When** se intenta reservar, **Then** se rechaza explicando el motivo.
8. **Given** un artículo con reservas para varios pedidos, **When** un usuario con permiso de
   consulta revisa sus compromisos, **Then** ve cada reserva activa con su pedido, línea, ubicación,
   lote y cantidades asignada, consumida, liberada y remanente.

---

### User Story 4 - Liberar reservas (Priority: P3)

Como responsable del almacén, quiero liberar total o parcialmente una reserva indicando el motivo,
para devolver a disponible el stock que ya no debe quedar comprometido.

**Why this priority**: Evita compromisos indefinidos y permite resolver discrepancias de conteo o
cambios de origen en las salidas, pero solo tiene sentido cuando existen reservas (US3).

**Independent Test**: Con una reserva remanente de 6, liberar 2 con motivo; la consulta muestra
reservado 2 menos y disponible 2 más, sin cambio del físico. Liberar otras 2 con otro motivo,
repetir la primera liberación sin efecto adicional e intentar liberar 5 de las 2 restantes.

**Acceptance Scenarios**:

1. **Given** una reserva con remanente, **When** un responsable libera una cantidad indicando el
   motivo, **Then** el reservado disminuye en esa cantidad, el físico no cambia y quedan registrados
   motivo, responsable e instante.
2. **Given** una reserva ya liberada parcialmente, **When** se registra otra liberación distinta,
   **Then** se acepta mientras no supere el remanente; repetir una misma liberación ya registrada
   no vuelve a aplicarla.
3. **Given** una liberación mayor que el remanente de alguna asignación, **When** se intenta,
   **Then** se rechaza sin cambios.
4. **Given** una liberación sin motivo, **When** se intenta, **Then** se rechaza señalando el campo.
5. **Given** una reserva sin remanente, **When** se intenta liberar, **Then** se rechaza explicando
   que no queda cantidad comprometida.
6. **Given** un artículo desactivado o una ubicación en conteo abierto, **When** se libera una
   reserva existente, **Then** se permite, porque liberar no añade compromisos nuevos.

---

### User Story 5 - Operar con autorización y resultado fiable (Priority: P3)

Como responsable del proyecto, quiero que consultas, pedidos, reservas y liberaciones respeten los
permisos de cada puesto y den un resultado fiable ante reintentos, cambios simultáneos y fallos,
para no perder ni duplicar compromisos de stock.

**Why this priority**: Transversal a las demás historias; protege los principios IV y V una vez
que existen las operaciones que lo necesitan.

**Independent Test**: Intentar cada acción sin sesión y con cada rol; repetir una reserva y una
alta de pedido con la misma intención; reservar desde una versión desactualizada del pedido;
simular la pérdida de la respuesta tras confirmar.

**Acceptance Scenarios**:

1. **Given** un usuario sin sesión o sin el permiso de la acción, **When** intenta consultar,
   registrar un pedido, reservar o liberar, **Then** se rechaza sin efectos; un operario puede
   consultar y registrar pedidos, pero no reservar ni liberar.
2. **Given** un alta de pedido, reserva o liberación ya confirmada, **When** se repite la misma
   intención, **Then** se devuelve el resultado original sin duplicar el efecto; si la intención
   repetida tiene un contenido distinto, se rechaza.
3. **Given** una respuesta perdida tras confirmar, **When** el usuario vuelve a la pantalla,
   **Then** ve «resultado pendiente de comprobar» y puede consultar la operación antes de repetirla.
4. **Given** una reserva o liberación preparada sobre datos del pedido o de la reserva que otro
   usuario ha cambiado, **When** se confirma, **Then** se rechaza sin cambios parciales y se ofrece
   el estado actual.
5. **Given** un fallo durante la confirmación, **When** se consultan después existencias,
   reservas e historial, **Then** no aparece ningún efecto parcial.

---

### Edge Cases

- Artículo con seguimiento por lote y varios lotes en la misma ubicación: cada lote es una fila
  distinta y el filtro por lote solo muestra el suyo.
- Artículo sin seguimiento por lote: sus filas se presentan como «sin lote» y el filtro por lote
  no se ofrece para él.
- Combinaciones de ubicación y lote que tuvieron existencias y ahora tienen físico cero: se ocultan
  por defecto y aparecen al activar la opción de mostrarlas; nunca impiden consultar el historial
  del artículo. Si solo hay combinaciones de físico cero, se indica «sin existencias».
- Filtro por una ubicación o un lote inexistentes o ajenos al artículo: se explica que no hay
  coincidencias, sin error técnico.
- Consulta durante la confirmación de otro movimiento: el resultado corresponde a un único
  instante y nunca muestra cifras a medio aplicar; filas y totales son coherentes entre sí.
- Reservado superior al físico en una fila: no puede producirse; si una conciliación lo
  detectara, se trata como incidencia y no se oculta.
- Reserva con dos asignaciones en la misma ubicación y lote: se rechaza indicando que deben
  unirse en una sola asignación; nunca se duplica el compromiso.
- Pedido servido por completo (HU-03): deja de admitir reservas; sus reservas consumidas siguen
  consultables.
- Liberación sobre una reserva consumida parcialmente por una salida: solo puede liberar el
  remanente, nunca lo ya consumido.
- Ubicación en conteo abierto: se rechazan nuevas reservas en ella, pero se permite liberar las
  existentes para resolver la discrepancia.

## Requirements *(mandatory)*

### Functional Requirements

**Consulta de existencias**

- **FR-001**: El sistema DEBE permitir a los usuarios con permiso de consulta de inventario buscar
  un artículo por SKU o nombre, incluidos los inactivos, y consultar sus existencias. La consulta
  DEBE exigir un artículo; NO DEBE ofrecer un listado general de existencias sin artículo.
- **FR-002**: Cada fila DEBE corresponder a una combinación de ubicación y lote del artículo, e
  identificar ubicación (código y descripción), lote o «sin lote», físico, reservado, disponible y
  unidad de medida. Por defecto DEBEN mostrarse solo las combinaciones con físico mayor que cero;
  una opción explícita DEBE permitir mostrar también las de físico cero, sin alterar los totales.
- **FR-003**: El disponible DEBE ser físico menos reservado, en cada fila y en los totales, según el
  cálculo común del proyecto; esta feature NO DEBE redefinir ese cálculo ni mantener otro contador.
- **FR-004**: La consulta DEBE admitir como filtros opcionales una ubicación y un lote, y los
  totales DEBEN agregar todo el resultado filtrado con independencia de la paginación.
- **FR-005**: Los resultados DEBEN incluir solo movimientos, reservas y ajustes confirmados, nunca
  documentos en borrador, y reflejar cada confirmación en la primera consulta posterior.
- **FR-006**: Filas y totales DEBEN corresponder a un mismo instante, que DEBE mostrarse; el usuario
  DEBE poder actualizar la consulta manualmente.
- **FR-007**: Un artículo inactivo DEBE poder consultarse y DEBE identificarse como inactivo
  mediante texto, no solo mediante color.
- **FR-008**: Cuando no haya existencias para la selección, el sistema DEBE indicarlo expresamente.

**Pedido de cliente mínimo**

- **FR-009**: El sistema DEBE permitir a los usuarios con permiso de registro de salidas dar de
  alta un pedido de cliente con referencia externa, referencia del cliente y al menos una línea de
  artículo y cantidad; el pedido NO DEBE incluir precios, facturación ni datos comerciales.
- **FR-010**: La referencia externa DEBE ser única entre todos los pedidos, comparada sin
  distinguir mayúsculas ni espacios extremos.
- **FR-011**: El alta DEBE rechazarse si alguna línea tiene cantidad no positiva, un artículo
  inexistente o inactivo, fracciones en un artículo que no las admite o un artículo ya presente en
  otra línea del pedido, señalando cada línea afectada; el rechazo NO DEBE crear un pedido parcial.
- **FR-012**: Un pedido nuevo DEBE quedar abierto, con servido cero y sin reservas.
- **FR-013**: La consulta de un pedido DEBE mostrar su estado y, por línea, artículo, unidad,
  cantidad pedida, servida, pendiente (pedida menos servida) y reservada remanente, además de las
  reservas asociadas con sus asignaciones.

**Reservas**

- **FR-014**: Solo los usuarios con permiso de gestión de reservas DEBEN poder reservar y liberar.
- **FR-015**: Una reserva DEBE pertenecer a una línea de pedido y asignar cantidades positivas a
  combinaciones explícitas de ubicación y lote del artículo de esa línea, elegidas por el usuario;
  el sistema NO DEBE asignar reservas automáticamente.
- **FR-016**: Reservar DEBE rechazarse si alguna asignación supera el disponible actual de su
  combinación, si esta no tiene existencias, si la suma reservada supera el pendiente de la línea
  descontando sus reservas remanentes, si el pedido no está abierto, si el artículo está inactivo
  o si la ubicación está en un conteo abierto.
- **FR-017**: Reservar DEBE aumentar el reservado y liberar DEBE disminuirlo, sin cambiar en
  ningún caso el físico.
- **FR-018**: Liberar DEBE indicar la cantidad por asignación y un motivo en texto libre no vacío,
  sin catálogo cerrado de motivos; NO DEBE superar el remanente de cada asignación y DEBE admitir
  varias liberaciones parciales distintas sobre la misma reserva, cada una aplicada una sola vez. Se DEBE permitir liberar aunque el artículo esté inactivo o la ubicación esté en
  un conteo abierto.
- **FR-019**: Cada reserva y liberación DEBE quedar en el historial con responsable, instante,
  pedido y línea, asignaciones y, en las liberaciones, motivo; el historial NO DEBE poder
  modificarse ni eliminarse.
- **FR-020**: El sistema DEBE mostrar, para un artículo, sus reservas activas por pedido, línea,
  ubicación y lote, y para cada reserva sus cantidades asignada, consumida, liberada y remanente,
  con su estado activa, consumida o liberada.

**Autorización y fiabilidad**

- **FR-021**: Toda consulta y operación de esta feature DEBE exigir una sesión identificada y el
  permiso correspondiente, comprobado por el sistema y no solo ocultando opciones en pantalla.
- **FR-022**: Alta de pedido, reserva y liberación DEBEN confirmarse como una sola operación
  atómica junto con su historial: se aplican todos sus efectos o ninguno.
- **FR-023**: Repetir la misma intención de alta, reserva o liberación DEBE devolver el resultado
  original sin repetir el efecto; reutilizar una intención con contenido distinto DEBE rechazarse.
- **FR-024**: Las operaciones simultáneas NO DEBEN producir reservado superior al físico,
  reservado negativo ni reservas que superen el pendiente de una línea.
- **FR-025**: Una reserva o liberación preparada sobre una versión ya sustituida del pedido o de la
  reserva DEBE rechazarse sin cambios, mostrando el estado actual.
- **FR-026**: La interfaz DEBE comunicar éxito solo tras la confirmación conservada, mostrar el
  identificador del resultado y, ante una respuesta perdida, indicar «resultado pendiente de
  comprobar» y permitir consultar la operación antes de repetirla.
- **FR-027**: Antes de confirmar una reserva o liberación, el usuario DEBE poder revisar pedido,
  artículo, cantidades, unidad, ubicaciones y lotes; los errores DEBEN explicar qué ocurrió y cómo
  corregirlo, conservando los datos válidos.
- **FR-028**: Pantallas y mensajes DEBEN usar español y vocabulario del almacén, mostrar la unidad
  junto a cada cantidad, ser operables por teclado y no comunicar estados solo mediante color.

### Alcance y dependencias

- Esta feature desarrolla verticalmente la consulta de existencias de un artículo, la consulta de
  sus compromisos de reserva, el alta y consulta del pedido de cliente mínimo, y los comandos de
  reservar y liberar. Quedan fuera las salidas y su consumo de reservas (HU-03), los conteos y
  ajustes (HU-04) y las alertas de mínimo (HU-06), así como editar o cancelar pedidos de cliente.
- Cobertura de los criterios originales de HU-05: búsqueda por artículo y filtros por ubicación o
  lote (US1, FR-001, FR-004); distinción entre físico, reservado y disponible (US1, FR-002); disponible
  igual a físico menos reservado (US1.3, FR-003); resultados que reflejan movimientos y ajustes
  confirmados (US1.5, FR-005).
- **HU-08 (A)** aporta artículo, unidad, admisión de fracciones, seguimiento por lote y estado
  activo; la primera reserva o línea de pedido de un artículo fija su definición según la regla
  común. **HU-02 (B)** aporta las ubicaciones. **HU-01 (A)** y **HU-04 (B)** generan los movimientos
  y ajustes que la consulta refleja; **HU-04** aporta además la congelación por conteo abierto.
  **HU-03 (B)** consume reservas y actualiza lo servido. **HU-06 (A)** evalúa el mínimo con el mismo
  disponible y debe reevaluarse al reservar o liberar.
- Reservar y liberar reutilizan los componentes comunes ya implementados en H0 —resultado de
  operación, ejecutor, bloqueos y núcleo de stock— descritos en el
  [contrato de stock](../../.specify/memory/project/contracts/stock-operations.md#implementación-h0-b-2026-11-18);
  esta feature no crea otra vía de escritura, otro registro de resultados ni otro cálculo.
- Dos fuentes sitúan el pedido de cliente mínimo en HU-03: la
  [decisión R-10](../../.specify/memory/project/research.md#r-10-desbloqueo-de-referencias-e-identidad-del-núcleo-h0)
  (research.md, línea 261) y el
  [plan de coordinación](../001-reparto-specs-inventario/plan.md#integración-de-las-futuras-specs)
  (plan.md, línea 114). HU-05 lo incorpora por decisión de B sobre la frontera HU-03/HU-05, porque
  la reserva lo necesita antes; HU-03 lo consumirá. A aprobó esta frontera el 2026-11-19 en su
  PR #7, que actualiza ambos documentos y la reserva de numeración (`docs/agrupamiento.md`). Las
  referencias de línea anteriores corresponden a la versión previa a la fusión de #7.
- Desviación respecto al modelo común: el
  [modelo](../../.specify/memory/project/data-model.md#customerorder-y-customerorderline) define el
  estado cancelado del pedido y el
  [contrato de stock](../../.specify/memory/project/contracts/stock-operations.md#alcance-y-disciplina-de-escritura)
  menciona un comando de modificación del pedido de cliente. HU-05 no implementa ninguno de los dos:
  en esta entrega el estado cancelado no es alcanzable y el pedido no se modifica tras el alta. Se
  conservan en los acuerdos comunes para una historia posterior, sin redefinirlos aquí.
- Mientras no existan recepciones reales (HU-01), las existencias de prueba se preparan mediante
  el núcleo común; la aceptación integrada con recepciones y salidas se completa en H3.

### Principios constitucionales y evidencia de aceptación

| Principio | Aplicación a HU-05 | Evidencia exigida |
|-----------|-------------------|-------------------|
| I. Correspondencia almacén/sistema | La consulta refleja solo hechos confirmados; reservar y liberar no alteran el físico. | US1.5, US3.1, US4.1, FR-005, FR-017; comparación antes/después. |
| II. Ubicación física precisa | Cifras por ubicación y lote; cada reserva apunta a una ubicación y lote concretos. | US1.1–US1.2, US3.3, US3.8, FR-002, FR-015; V-03. |
| III. Uso intuitivo | Vocabulario del almacén, unidades visibles, revisión previa, estados con texto, errores corregibles. | US1.4, US1.7, US2.3, US5.3, FR-026–FR-028; recorrido con trabajadores representativos documentado. |
| IV. Trazabilidad | Reservas y liberaciones con responsable, instante, pedido, asignaciones y motivo; historial inalterable. | US4.1–US4.2, FR-019, FR-020; V-04 (extensión de liberaciones). |
| V. Integridad y recuperación | Atomicidad, sin sobrerreserva, idempotencia, versiones, permisos y concurrencia. | US3.5–US3.6, US5.1–US5.5, FR-021–FR-025; V-05, V-07 y V-08. |

La evidencia de implementación conservará fecha, entorno, escenario, resultado e incidencias.
Esta especificación define la aceptación; no declara realizadas pruebas ni revisiones conjuntas.

### Key Entities *(include if feature involves data)*

- **Existencia por ubicación y lote — referencia compartida**: cantidad física y reservada de un
  artículo en una ubicación y lote; el disponible se deriva. Solo cambia por operaciones confirmadas.
- **Pedido de cliente**: compromiso mínimo con referencia externa única, referencia del cliente y
  estado abierto o servido; el estado cancelado del modelo común no es alcanzable en HU-05. Tiene
  una o varias líneas, con un artículo distinto en cada una.
- **Línea de pedido de cliente**: artículo, cantidad pedida y cantidad servida; el pendiente es la
  diferencia. Puede tener varias reservas.
- **Reserva**: compromiso de una línea de pedido, con estado derivado activa, consumida o liberada.
- **Asignación de reserva**: cantidad asignada en una ubicación y lote, con consumido, liberado y
  remanente (asignado − consumido − liberado).
- **Liberación**: hecho que devuelve parte del remanente a disponible, con motivo, responsable e
  instante; cada liberación es distinta y se aplica una sola vez.
- **Artículo, ubicación y lote — referencias compartidas**: definidos por HU-08, HU-02 y las
  operaciones de inventario.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: En el 100 % de los casos de la batería de aceptación, con y sin filtros, el disponible
  mostrado es físico menos reservado y los totales coinciden con la suma de todas las filas del
  filtro, incluidas las de otras páginas.
- **SC-002**: En el 100 % de las confirmaciones de prueba (recepción, reserva, liberación o ajuste),
  la primera consulta posterior refleja el cambio, y ningún borrador altera las cifras.
- **SC-003**: El 100 % de los intentos de reservar por encima del disponible, del pendiente o en
  una combinación sin existencias se rechaza sin ningún cambio.
- **SC-004**: En la prueba de reservas simultáneas sobre el mismo disponible, exactamente una se
  confirma y el reservado nunca supera el físico, en todas las repeticiones.
- **SC-005**: En el 100 % de los casos definidos de acceso no autorizado, intención repetida,
  versión desactualizada y fallo inducido, el resultado es el esperado sin efectos duplicados ni
  parciales.
- **SC-006**: Con 10.000 artículos, 1.000.000 de entradas de historial y 25 sesiones simultáneas,
  el 95 % de las consultas de existencias muestra su resultado en 2 segundos o menos.
- **SC-007**: Los trabajadores representativos que participan en la validación localizan el
  disponible de un artículo en una ubicación concreta, y registran una reserva y una liberación,
  sin asistencia técnica y sin errores de registro no detectados.

## Assumptions

- Permisos según el catálogo común: consulta de existencias, pedidos y reservas con el permiso de
  lectura de inventario (Inventory.Read: operario, responsable de almacén, comprador y gestor de
  maestros); alta de pedido con el de registro de salidas (Shipping.Record: operario); reservar y
  liberar con el de gestión de reservas (Reservations.Manage: responsable de almacén). El
  administrador no tiene permisos implícitos de inventario.
- Como el reservado nunca supera el físico, ocultar por defecto las combinaciones con físico cero
  no oculta ninguna reserva.
- El pedido de cliente es una referencia local mínima, sin edición ni cancelación tras el alta.
  Un compromiso que deja de ser necesario se resuelve liberando sus reservas.
- La referencia del cliente es obligatoria y de texto libre.
- La reserva se justifica por su pedido y línea, sin motivo adicional.
- Las reservas no caducan; no hay reservas automáticas ni selección por antigüedad de lote.
- Mientras HU-04 no esté integrada no existen conteos abiertos; la regla de congelación se aplica
  desde su integración.
- Los objetivos de rendimiento y la validación con trabajadores son criterios a medir, no
  resultados obtenidos; respaldo y restauración siguen la guía global.
