# Feature Specification: HU-08 — Mantener artículos, familias y proveedores

**Feature Branch**: Sin rama nueva asociada; directorio de feature independiente de la rama Git.

**Created**: 2026-11-12

**Status**: Draft

**Input**: User description: "Crea la especificación para La historia de usuario 8 : artículos, familias, proveedores"

**Historia de origen**: HU-08 del [catálogo y reparto](../001-reparto-specs-inventario/spec.md#catálogo-y-asignación-de-las-ocho-futuras-specs).
**Responsable principal**: Persona A. **Hito de inicio**: H1, según el
[plan de coordinación](../001-reparto-specs-inventario/plan.md#hitos-y-coordinación).

**Contexto normativo y acuerdos consumidos**:

- [Constitución vigente](../../.specify/memory/constitution.md): principios I–V y criterios de aceptación.
- [Plan global](../../.specify/memory/project-plan.md): arquitectura compartida, responsabilidades y coordinación.
- [Decisiones comunes](../../.specify/memory/project/research.md#r-05-reglas-comunes-de-negocio-para-no-bloquear-las-specs): unidades, lotes y efecto de la desactivación.
- [Modelo común](../../.specify/memory/project/data-model.md#datos-maestros--persona-a--hu-08): datos maestros, referencias y auditoría.
- [Contrato HTTP común](../../.specify/memory/project/contracts/api.md): permisos y acuerdos de los recursos HU-08.
- [Contrato de stock](../../.specify/memory/project/contracts/stock-operations.md#alcance-y-disciplina-de-escritura): coordinación de cambios de estado con operaciones de inventario.
- [Contrato de interacción](../../.specify/memory/project/contracts/ui.md): maestros, mensajes y visibilidad de artículos inactivos.
- [Guía de validación](../../.specify/memory/project/quickstart.md): datos compartidos, V-01, V-08, V-12 y criterios transversales de recuperación.

Los enlaces anteriores son referencias normativas; esta especificación describe necesidades
y resultados de negocio. Las decisiones de implementación corresponden al futuro plan de HU-08.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Organizar los artículos por familias (Priority: P1)

Como gestor de datos maestros, quiero crear, consultar y modificar familias para clasificar
los artículos con una organización común y reconocible para el almacén.

**Why this priority**: Cada artículo pertenece a una familia; disponer de familias válidas
permite comenzar el catálogo sin depender de compras o movimientos.

**Independent Test**: Con una cuenta autorizada y sin artículos ni movimientos previos,
crear una familia GENERAL, localizarla y modificar su nombre, comprobando que el cambio
se conserva al volver a consultarla.

**Acceptance Scenarios**:

1. **Given** un gestor autorizado y un código de familia libre, **When** registra código y
   nombre válidos, **Then** la familia queda disponible para consulta y asignación a artículos.
2. **Given** una familia existente, **When** el gestor modifica su código o nombre con valores
   válidos, **Then** se conserva su identidad y los artículos ya asociados siguen vinculados.
3. **Given** un código ya utilizado, **When** se intenta crear o modificar otra familia con
   ese mismo código, incluso variando mayúsculas o espacios extremos, **Then** se rechaza
   el duplicado, se explica el campo afectado y se conservan los demás datos introducidos.
4. **Given** una familia asociada a un artículo, **When** el gestor la desactiva,
   **Then** se conserva la asociación y su consulta, se indica el estado inactivo y no se
   ofrece la familia para nuevas asignaciones; el artículo no se desactiva automáticamente.
5. **Given** una familia inactiva, **When** el gestor revisa y confirma su reactivación,
   **Then** vuelve a ofrecerse para asignaciones sin cambiar las asociaciones existentes.

---

### User Story 2 - Mantener un catálogo de artículos identificables (Priority: P1)

Como gestor de datos maestros, quiero crear, consultar y modificar artículos, asignándoles
una familia y una unidad de medida, para que las demás tareas utilicen la misma definición
del producto y de sus cantidades.

**Why this priority**: Artículos e identidades consistentes son la base de ubicaciones,
compras, recepciones, consultas, reservas, salidas y conteos.

**Independent Test**: Preparar una familia activa y registrar ART-01 con nombre, unidad UD,
cantidades enteras y seguimiento por lote. Buscarlo, abrir su detalle y cambiar su nombre,
sin preparar existencias ni compras.

**Acceptance Scenarios**:

1. **Given** una familia activa y un código de artículo libre, **When** el gestor registra
   código, nombre, familia, unidad base, admisión de fracciones y seguimiento por lote,
   **Then** el artículo se crea activo, muestra esos datos y parte sin existencias y con
   mínimo de reposición cero, sin pedir una ubicación ni una cantidad inicial.
2. **Given** un artículo creado, **When** un usuario con permiso de consulta lo busca por
   código o nombre, **Then** puede identificarlo y consultar su familia, unidad, reglas
   de cantidades y lotes, y estado, sin permisos de modificación.
3. **Given** un artículo existente, **When** el gestor cambia su nombre o familia por otra
   activa, **Then** el cambio se conserva sin crear otro artículo ni alterar sus referencias.
4. **Given** un artículo con movimientos, **When** se intenta cambiar su unidad base o su
   modalidad de seguimiento por lote, **Then** el cambio se rechaza con una explicación y
   los movimientos mantienen su interpretación original.
5. **Given** un alta con código duplicado, nombre vacío, unidad ausente o familia inexistente
   o inactiva, **When** se intenta guardar, **Then** se señalan los campos inválidos y no se
   crea un artículo parcial ni se pierde el resto del formulario.
6. **Given** artículos de distintas familias y estados, **When** el usuario selecciona una
   familia y un estado para la búsqueda, **Then** solo aparecen las coincidencias, con código,
   nombre y estado textual; si no hay coincidencias, puede ajustar los filtros.

---

### User Story 3 - Mantener proveedores utilizables en compras (Priority: P1)

Como gestor de datos maestros, quiero crear, consultar y modificar proveedores para que el
comprador pueda identificar la contraparte y disponer de los datos de contacto necesarios.

**Why this priority**: Permite preparar HU-07 manteniendo independientes la gestión del
proveedor y la creación y envío de pedidos.

**Independent Test**: Crear PROV-01 con nombre y correo de prueba, localizarlo y modificar
su contacto; comprobar la persistencia de sus datos sin crear ni enviar un pedido.

**Acceptance Scenarios**:

1. **Given** un gestor autorizado y un código libre, **When** registra código, nombre y,
   cuando estén disponibles, correo e identificador fiscal, **Then** el proveedor queda
   activo y consultable con los datos introducidos.
2. **Given** un proveedor, **When** se guarda un correo no vacío con formato inválido,
   **Then** se rechaza ese campo; si no hay correo, se permite guardar y se informa de
   que deberá completarse antes de enviar pedidos.
3. **Given** un proveedor utilizado en documentos, **When** se modifica su nombre, correo
   o identificador fiscal, **Then** se conservan su identidad y referencias y no se
   reescriben los datos históricos ya conservados por los documentos.
4. **Given** un proveedor activo, **When** el gestor lo desactiva, **Then** sigue siendo
   consultable con indicación textual de su estado, deja de ofrecerse para nuevas compras
   y no se borran ni cierran sus documentos existentes.
5. **Given** un código de proveedor ocupado, **When** se intenta reutilizarlo en otro
   proveedor, **Then** se rechaza el duplicado y se mantiene intacto el proveedor original.
6. **Given** un proveedor inactivo, **When** el gestor revisa y confirma su reactivación,
   **Then** vuelve a ofrecerse para nuevas compras sin modificar sus documentos anteriores.

---

### User Story 4 - Retirar artículos sin perder su trazabilidad (Priority: P1)

Como gestor de datos maestros, quiero desactivar los artículos que dejan de utilizarse y
eliminar únicamente los que no tienen referencias, para depurar el catálogo sin perder
movimientos ni la explicación de las operaciones del almacén.

**Why this priority**: La conservación del historial es un criterio original de HU-08 y
una obligación constitucional, incluso cuando el artículo ya no está disponible para altas.

**Independent Test**: Usar un artículo sin referencias y otro con un movimiento trazable.
Comprobar que el primero puede eliminarse y que el segundo solo puede desactivarse,
manteniendo accesible su historia. La preparación del movimiento respeta los acuerdos
comunes de inventario; no se introducen existencias editando saldos.

**Acceptance Scenarios**:

1. **Given** un artículo con movimientos, **When** el gestor intenta eliminarlo,
   **Then** se impide la eliminación, se explica que existe historial y se ofrece la
   desactivación conservando todos los movimientos y referencias.
2. **Given** un artículo sin movimientos pero referenciado por documentos o reservas,
   **When** se intenta eliminar, **Then** se rechaza la acción y se explica la existencia
   de referencias; la ausencia de movimientos no basta para autorizar el borrado.
3. **Given** un artículo sin movimientos, documentos ni reservas, **When** el gestor revisa
   y confirma su eliminación, **Then** deja de aparecer en el catálogo y queda constancia
   de quién realizó la acción y cuándo.
4. **Given** un artículo con historial, **When** se confirma su desactivación,
   **Then** conserva su identidad, movimientos, cantidades y ubicaciones; las consultas
   permiten encontrarlo como inactivo y acceder a su historial sin reactivarlo.
5. **Given** un artículo inactivo y un usuario autorizado para consultar inventario,
   **When** consulta sus movimientos por período, responsable, ubicación o lote,
   **Then** obtiene los movimientos correspondientes con su tipo, cantidad, unidad,
   fechas, responsable, motivo y origen o destino aplicables.
6. **Given** un artículo inactivo con compromisos existentes, **When** se opera con él,
   **Then** se rechazan nuevos pedidos, reservas y recepciones, pero se conservan la consulta
   histórica, la liberación de reservas, el servicio de compromisos existentes y los ajustes
   autorizados, sujetos a sus reglas y permisos originales.
7. **Given** un artículo inactivo, **When** el gestor lo reactiva,
   **Then** vuelve a estar disponible para operaciones nuevas, sin perder historial ni
   cambiar cantidades por el mero cambio de estado.

---

### User Story 5 - Guardar cambios con autorización y resultado fiable (Priority: P2)

Como trabajador autorizado, quiero saber si un cambio de datos maestros se ha guardado y
resolver errores sin perder mi trabajo, para evitar duplicados y sobrescrituras involuntarias.

**Why this priority**: Un catálogo compartido requiere distinguir consulta y modificación,
conservar responsabilidades y hacer visibles los conflictos de edición.

**Independent Test**: Preparar cuentas de consulta y gestión, abrir el mismo maestro en dos
sesiones e intentar guardar cambios incompatibles; repetir además una misma alta tras
simular la pérdida de su respuesta.

**Acceptance Scenarios**:

1. **Given** un usuario con permiso de consulta pero sin gestión de maestros,
   **When** intenta crear, modificar, desactivar o eliminar un maestro,
   **Then** la operación se rechaza sin cambios y puede seguir consultando.
2. **Given** dos gestores editando la misma versión de un maestro,
   **When** uno guarda y el otro intenta guardar después,
   **Then** el segundo recibe un conflicto, puede consultar los datos vigentes y no
   sobrescribe silenciosamente el primer cambio.
3. **Given** una operación guardada cuya respuesta se perdió,
   **When** se comprueba su resultado o se repite la misma intención,
   **Then** se recupera el resultado sin duplicar el maestro ni su registro de cambio.
4. **Given** un fallo antes de completar el guardado,
   **When** se vuelve a consultar el maestro y su registro de cambios,
   **Then** no aparece un maestro modificado sin su correspondiente evidencia de autoría
   ni un mensaje de éxito sin cambios conservados.

### Edge Cases

- Los códigos son únicos dentro de cada catálogo, también para registros inactivos;
  un nombre repetido no implica por sí solo duplicidad de identidad.
- Los campos obligatorios que contienen solo espacios se rechazan. Los códigos se comparan
  sin distinguir mayúsculas y minúsculas y sin espacios extremos.
- Una familia inactiva ya asignada sigue visible al editar el artículo. Conservar esa
  asignación permite corregir otros campos; asignar una familia distinta exige que esté activa.
- Desactivar una familia o un proveedor no desactiva artículos ni elimina documentos en cascada.
- Un artículo sin existencias actuales puede tener movimientos o reservas/documentos referenciados;
  tener saldo cero no autoriza su eliminación.
- Ante eliminación y creación simultánea de una referencia al artículo, no pueden quedar
  documentos, reservas o movimientos sin su artículo: se conserva la referencia válida o
  se rechaza la operación incompatible.
- Ante desactivación y nueva operación simultáneas, el resultado respeta el estado vigente
  al confirmar cada acción; ninguna operación nueva se admite después de la desactivación.
- Cambiar nombres, códigos o familia no altera las instantáneas históricas de documentos
  ni la unidad registrada en movimientos anteriores.
- Repetir una intención con contenido distinto se rechaza como conflicto; un tiempo de
  espera agotado se muestra como resultado pendiente de comprobar, no como éxito ni fallo definitivo.
- Un listado sin resultados explica que no hay coincidencias y permite cambiar la búsqueda;
  los artículos inactivos son localizables mediante selección explícita de estado.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: El sistema DEBE permitir a usuarios autorizados crear, consultar y modificar
  familias con código, nombre y estado activo/inactivo, conservando una identidad estable.
- **FR-002**: El sistema DEBE permitir crear, consultar y modificar artículos con código,
  nombre, una familia, unidad base, admisión de fracciones, seguimiento por lote y estado.
  Todos esos datos DEBEN quedar definidos al crear el artículo.
- **FR-003**: Cada artículo DEBE pertenecer a una familia existente. Las nuevas asignaciones
  DEBEN utilizar familias activas; una asignación previa a una familia inactiva DEBE conservarse
  hasta que el gestor decida cambiarla, sin impedir la consulta o edición de otros campos.
- **FR-004**: El sistema DEBE permitir crear, consultar y modificar proveedores con código,
  nombre, estado y correo e identificador fiscal opcionales. Un correo informado DEBE ser
  válido; la ausencia de correo DEBE advertirse como impedimento para el futuro envío de pedidos.
- **FR-005**: Los códigos de artículos, familias y proveedores DEBEN ser obligatorios y
  únicos dentro de su catálogo, incluyendo inactivos, sin distinguir mayúsculas ni espacios
  extremos. Los nombres obligatorios NO DEBEN admitir valores vacíos o compuestos solo por espacios.
- **FR-006**: Los usuarios con permiso de consulta de inventario DEBEN poder listar y
  consultar los tres catálogos, buscar por código o nombre y seleccionar activos, inactivos
  o ambos. En artículos también DEBEN poder filtrar por familia. Los resultados DEBEN mostrar
  código, nombre y estado textual; el detalle del artículo DEBE mostrar su familia y unidad.
- **FR-007**: Solo los usuarios con permiso de gestión de datos maestros DEBEN poder crear,
  modificar, desactivar, reactivar o eliminar registros según las acciones admitidas en esta
  historia. Disponer únicamente de administración de cuentas NO DEBE conceder ese permiso.
- **FR-008**: Crear un artículo NO DEBE registrar cantidades ni ubicaciones, generar
  movimientos o modificar existencias. El artículo DEBE comenzar sin existencias y con
  mínimo de reposición cero; la configuración del mínimo pertenece a HU-06.
- **FR-009**: El sistema DEBE impedir el cambio de unidad base o seguimiento por lote de un
  artículo con historial de movimientos, explicando el motivo y conservando los datos vigentes.
- **FR-010**: El sistema DEBE impedir eliminar artículos con movimientos, documentos o
  reservas referenciados, independientemente de su estado o existencias actuales, y ofrecer
  la desactivación como forma de retirarlos del uso ordinario.
- **FR-011**: El sistema DEBE permitir eliminar un artículo únicamente si no tiene ninguna
  de las referencias de FR-010, después de que el gestor revise su identificación y confirme
  la acción. La eliminación DEBE conservar evidencia de la identidad eliminada, autor y fecha.
- **FR-012**: El sistema DEBE permitir desactivar y reactivar artículos, familias y proveedores
  sin borrar referencias ni documentos, alterar cantidades o desactivar otros maestros en cascada.
  Las familias inactivas NO DEBEN ofrecerse para nuevas asignaciones y los proveedores inactivos
  NO DEBEN ofrecerse para nuevas compras.
- **FR-013**: Un artículo inactivo DEBE conservar consulta e historial. Su estado DEBE impedir
  nuevos pedidos, reservas y recepciones sin cerrar documentos existentes, manteniendo las
  operaciones permitidas por la regla común de desactivación descrita en US4.6.
- **FR-014**: La consulta de movimientos de un artículo DEBE estar disponible también cuando
  esté inactivo y permitir filtros por período, responsable, ubicación y lote. DEBE identificar tipo,
  cantidad, unidad, fechas del hecho y registro, responsable, motivo, documento relacionado y
  origen/destino aplicables, conservando soporte, contraparte y lote cuando correspondan.
- **FR-015**: Cada alta, modificación y cambio de estado DEBE conservar evidencia no alterable
  de entidad afectada, valores anteriores y posteriores aplicables, acción, usuario responsable
  y fecha y hora de registro. La identidad del responsable DEBE proceder del usuario autenticado.
- **FR-016**: Un cambio de maestro y su evidencia de auditoría DEBEN completarse conjuntamente
  o no realizarse. Un rechazo o fallo NO DEBE dejar datos parciales ni modificar movimientos.
- **FR-017**: Una edición basada en datos ya sustituidos por otro usuario DEBE rechazarse sin
  sobrescritura silenciosa, mostrando el conflicto y permitiendo consultar el estado vigente.
- **FR-018**: Repetir una misma intención de cambio DEBE devolver su resultado sin duplicar
  registros ni auditoría. Ante resultado incierto, el usuario DEBE poder comprobarlo antes
  de emitir una intención diferente; reutilizar la intención con otros datos DEBE rechazarse.
- **FR-019**: La eliminación y los cambios de estado DEBEN coordinarse con las operaciones
  que crean referencias o afectan al artículo, sin referencias huérfanas ni aceptación de
  operaciones nuevas incompatibles con un estado inactivo ya confirmado.
- **FR-020**: Los formularios y resultados DEBEN usar español, etiquetas explícitas,
  obligatoriedad visible y estados expresados con texto. Los errores DEBEN explicar cómo
  corregirlos, conservar los datos válidos y permitir completar la tarea mediante teclado.
- **FR-021**: La interfaz DEBE comunicar éxito únicamente cuando el cambio esté conservado
  y sea consultable. Para desactivación, reactivación y eliminación DEBE permitir revisar
  el maestro afectado y la consecuencia antes de confirmar.

### Alcance y dependencias

- Esta feature desarrolla verticalmente los tres catálogos y sus reglas de conservación,
  autorización, auditoría y consulta de historial de artículos. No ofrece borrado de familias
  o proveedores, cargas masivas, gestión de cuentas, pedidos ni edición de existencias.
- Los criterios originales quedan cubiertos por US1–US4: altas, consulta y modificación de
  los tres maestros; asignación de familia; borrado impedido con movimientos; desactivación
  con conservación de movimientos y consulta de trazabilidad.
- H1 puede desarrollar los catálogos sobre la base de sesión y permisos H0 sin esperar a
  que B termine ubicaciones o el núcleo de stock. A propone identidad, unidades y estado
  del artículo; A y B revisan los acuerdos aplicables antes del desarrollo dependiente.
- El historial consume los movimientos compartidos, no crea un historial de stock alternativo.
  La aceptación integrada de FR-009, FR-010, FR-013, FR-014 y FR-019 requiere los documentos,
  reservas y movimientos pertinentes de HU-01/HU-03/HU-04/HU-05/HU-07. En H1 se comprueban
  los casos disponibles y se registra evidencia parcial; no se declara aprobada la integración
  completa por haber utilizado únicamente datos de prueba.
- La conservación de documentos de proveedor de US3.3, US3.4 y US3.6 se contrasta con HU-07
  cuando existan pedidos, además de comprobar los datos propios del proveedor en H1.
- HU-06 conserva la gestión y evaluación de mínimos y alertas, incluida su reacción al
  cambio de estado. HU-08 conserva el mínimo inicial cero y el cambio de estado del maestro.
- Las decisiones compartidas mantienen su fuente en los anexos globales; cualquier cambio
  sigue la revisión conjunta establecida por el plan y la constitución.

### Principios constitucionales y evidencia de aceptación

| Principio | Aplicación a HU-08 | Evidencia exigida |
|-----------|-------------------|-------------------|
| I. Correspondencia almacén/sistema | Cambiar el catálogo no inventa cantidades ni corrige saldos. | US2.1, US4.4 y US4.7; comparación de existencias antes/después. |
| II. Ubicación física precisa | Identidad y unidad comunes; editar/desactivar no elimina ubicaciones históricas. | US2.1–US2.3 y US4.4; V-01 integrado con HU-02. |
| III. Uso intuitivo | Etiquetas, estados explícitos, corrección de errores y revisión de acciones de retirada. | US1.3, US2.5 y FR-020–FR-021; recorrido con gestores representativos y evidencia de resultado. |
| IV. Trazabilidad | Historial consultable de inactivos y auditoría de cambios, sin reescribir hechos previos. | US3.3, US4.1–US4.5 y FR-015; V-12 con movimientos reales. |
| V. Integridad y recuperación | Permisos, unicidad, cambios completos, conflictos y reintentos sin duplicados. | US5.1–US5.4, casos simultáneos de Edge Cases, V-08 y V-12; recuperación compartida V-14 antes de producción. |

La evidencia de implementación conservará fecha, entorno, escenario, resultado e incidencias.
Los responsables del proyecto y almacén revisarán los resultados conforme a la constitución.
Esta especificación define la aceptación; no declara realizadas pruebas ni revisiones conjuntas.

### Key Entities *(include if feature involves data)*

- **Familia**: agrupación con identidad estable, código único, nombre y estado. Una familia
  puede clasificar varios artículos; cada artículo pertenece a una familia.
- **Artículo**: producto identificable por código único, nombre, familia, unidad base,
  admisión de fracciones, seguimiento por lote y estado. Conserva sus referencias al cambiar
  datos descriptivos o desactivarse.
- **Proveedor**: contraparte con identidad estable, código único, nombre, datos de contacto,
  identificador fiscal opcional y estado. Sus cambios no reescriben documentos históricos.
- **Registro de cambio de maestro**: evidencia de una acción sobre un maestro, valores
  anteriores/posteriores aplicables, responsable, instante e identificación de la operación.
- **Movimiento de artículo — referencia compartida**: hecho confirmado del inventario
  consultable desde el artículo, con cantidades, unidad, ubicaciones, fechas y responsabilidad.
  Su generación y corrección pertenecen a las historias de movimientos.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un gestor autorizado completa el 100 % de los recorridos de alta, consulta y
  modificación de familia, artículo y proveedor con datos válidos, y los resultados siguen
  visibles al volver a abrir cada registro.
- **SC-002**: En el 100 % de los casos de códigos duplicados, campos obligatorios vacíos y
  referencias inválidas definidos en los escenarios se impide el guardado sin crear registros
  parciales, con indicación del dato que debe corregirse.
- **SC-003**: El 100 % de los intentos de borrar artículos referenciados se rechaza; tras
  desactivar un artículo se conserva el 100 % de sus movimientos y referencias y pueden
  consultarse sin reactivarlo. Los artículos sin referencias sí pueden eliminarse con confirmación.
- **SC-004**: En el 100 % de los casos de acceso no autorizado, edición simultánea incompatible,
  reintento y fallo de guardado no hay cambios indebidos, pérdida silenciosa de cambios ajenos,
  duplicados ni modificaciones sin evidencia de responsabilidad.
- **SC-005**: Con un catálogo de 10.000 artículos y 25 sesiones concurrentes, al menos el
  95 % de las búsquedas paginadas de artículos muestra resultados en 2 segundos o menos,
  conforme a las condiciones de medición de la guía global.
- **SC-006**: Al menos tres gestores representativos completan, cada uno, el alta de una
  familia, un artículo y un proveedor, la corrección de un campo inválido y la desactivación
  de un artículo sin asistencia técnica y sin errores de registro no detectados. Se documentan
  los resultados y se corrigen las incidencias antes de aceptar la interfaz.
- **SC-007**: En el 100 % de los casos de modificación o desactivación revisados, el responsable
  y el momento del cambio son identificables y no varían las cantidades ni las evidencias
  históricas de los documentos y movimientos relacionados.

## Assumptions

- Se reutiliza la identidad y separación de permisos acordada en H0. Las cuentas de gestión
  y consulta necesarias para validar HU-08 se preparan de forma controlada; esta historia
  no incorpora administración de usuarios ni exige que el administrador tenga permisos de maestros.
- Como valores iniciales de interacción, los maestros nuevos son activos; las búsquedas
  muestran activos por defecto y permiten incluir inactivos. Los códigos ignoran mayúsculas
  y espacios extremos, sin introducir reglas de formato particulares del sector.
- Una familia inactiva no recibe nuevas asignaciones, pero mantiene las existentes; un
  proveedor inactivo no se ofrece en nuevas compras. Son valores iniciales de esta feature
  que se revisan con HU-07 sin alterar documentos existentes.
- No se exige un identificador fiscal ni correo para dar de alta un proveedor. El correo
  válido y obligatorio para enviar pedidos se comprueba en HU-07 según el acuerdo global.
- Las unidades de medida se identifican de forma explícita según los ejemplos comunes;
  no se añade un catálogo administrable de unidades ni conversiones implícitas. El seguimiento
  inicial contempla lotes, sin gestión por serie o caducidad.
- Las consultas de historial pueden no tener resultados en H1. Las comprobaciones de
  conservación y operaciones pendientes se completan con movimientos y documentos válidos
  cuando las historias consumidoras estén disponibles, sin darlas por demostradas anticipadamente.
- Los objetivos de rendimiento y la muestra de gestores son criterios de aceptación a medir,
  no resultados obtenidos. Respaldo, restauración y aprobación operativa siguen la guía global.
