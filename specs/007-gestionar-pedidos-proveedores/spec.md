# Feature Specification: HU-07 — Crear, enviar y seguir pedidos a proveedores

**Feature Branch**: Sin rama nueva asociada; el directorio de feature es independiente de la rama Git.

**Created**: 2026-11-20

**Estado**: cerrado el alcance funcional local, 2026-11-21. **SC-007 no superado** en
guardados y pendiente de seguimiento. La [matriz de aceptación](validation/acceptance.md)
distingue la entrega local de la revisión A+B, la recepción integrada HU-01 y la aceptación H5.

**Input**: User description: "Haz la especifiacion del HU-07 compra y envio"

**Historia de origen**: HU-07 del [catálogo y reparto](../001-reparto-specs-inventario/spec.md#catálogo-y-asignación-de-las-ocho-futuras-specs).
**Responsable principal**: Persona A. **Hito de inicio**: H2, según el
[plan de coordinación](../001-reparto-specs-inventario/plan.md#hitos-y-coordinación).
**Directorio reservado**: 007, conforme a [docs/agrupamiento.md](../../docs/agrupamiento.md).

**Contexto normativo y acuerdos consumidos**:

- [Constitución vigente](../../.specify/memory/constitution.md): principios I–V, aceptación y gobernanza.
- [Plan global](../../.specify/memory/project-plan.md): arquitectura y responsabilidades compartidas.
- [Decisiones comunes](../../.specify/memory/project/research.md): reglas de maestros, cantidades y envío de compras.
- [Modelo común](../../.specify/memory/project/data-model.md#compras-y-recepciones--persona-a--hu-07-hu-01): pedido, líneas, envío y acumulados de recepción.
- [Contrato HTTP común](../../.specify/memory/project/contracts/api.md#hu-07--a-pedidos-a-proveedores): recursos, permisos, conflictos y resultados de operaciones.
- [Contrato de stock](../../.specify/memory/project/contracts/stock-operations.md#recepción-y-compra): efectos de la recepción y coordinación con compras y maestros.
- [Contrato de interacción](../../.specify/memory/project/contracts/ui.md): revisión, mensajes, errores y resultados inciertos.
- [Guía de validación](../../.specify/memory/project/quickstart.md): V-02, V-06, V-08, V-12, V-13 y recuperación conjunta V-14.
- [Tareas receptoras](../001-reparto-specs-inventario/integration-tasks.md#hu-07--compras--persona-a): INT-HU07-01 e INT-HU07-02, recibidas del cierre de HU-08.

Los enlaces anteriores son acuerdos normativos. Esta spec define necesidades y resultados
de negocio; los detalles de implementación corresponden al plan y los contratos técnicos.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Preparar y corregir un pedido de compra (Priority: P1)

Como comprador autorizado, quiero guardar un pedido a un proveedor con sus artículos,
cantidades, precios y fechas previstas, y corregirlo mientras sea borrador, para revisar
la compra antes de comprometerla con el proveedor.

**Why this priority**: El borrador es el soporte de envío y recepción. Aporta valor sin
correo disponible ni mercancía recibida y evita preparar compras fuera del catálogo común.

**Independent Test**: Preparar un proveedor y dos artículos activos mediante HU-08.
Crear un pedido de 10 UD de ART-01, con precio unitario y fecha prevista; abrirlo,
cambiar la cantidad y añadir o retirar una línea, sin enviar correo ni preparar stock.

**Acceptance Scenarios**:

1. **Given** un comprador autorizado, proveedor activo y al menos una línea válida,
   **When** guarda el pedido, **Then** obtiene un número único, el estado «Borrador»
   y envío «Sin solicitar»; cada línea muestra pedido, recibido cero y pendiente igual
   a lo pedido. No se crean existencias, reservas ni movimientos.
2. **Given** un borrador sin solicitud de envío, **When** cambia proveedor, fecha prevista
   o líneas con datos válidos, **Then** conserva la identidad del pedido y registra la
   corrección; puede añadir, modificar o retirar líneas, pero no guardar sin ninguna.
3. **Given** cantidades no positivas, fracciones no permitidas, precio negativo, fecha
   inválida o datos obligatorios ausentes, **When** intenta guardar, **Then** se señalan
   los campos o líneas afectados, el foco permite corregirlos, no se conserva un pedido
   parcial y no pierde los datos válidos.
4. **Given** un proveedor o artículo inexistente o inactivo, **When** intenta incorporarlo
   a un pedido nuevo o como nueva asignación, **Then** se rechaza explicando el motivo.
   Un proveedor activo sin correo permite preparar el borrador, con aviso de que no podrá enviarse.
5. **Given** la primera línea de pedido que utiliza un artículo, **When** se guarda,
   **Then** su unidad, seguimiento por lote y admisión de fracciones quedan protegidos
   desde ese borrador. Retirar después la última línea no permite cambiar esa definición;
   el posible borrado del artículo se evalúa por separado según referencias e historial.
6. **Given** una primera referencia simultánea a una edición de definición o eliminación
   del artículo, **When** ambas acciones se confirman, **Then** la referencia utiliza la
   definición vigente o se rechaza la acción incompatible; no queda una línea sin artículo
   ni una cantidad interpretada con una definición anterior a la confirmada.
7. **Given** el mismo artículo en dos líneas con condiciones de precio o fecha distintas,
   **When** guarda el borrador, **Then** conserva ambas líneas identificables y sus cantidades
   independientes; no las fusiona ni duplica al repetir el guardado.

---

### User Story 2 - Revisar y solicitar el envío al proveedor (Priority: P1)

Como comprador con permiso de envío, quiero revisar el destinatario y el documento que
recibirá el proveedor y confirmar su envío, para emitir la compra con el contenido correcto
y distinguir que se ha registrado la solicitud de que el correo ya ha sido aceptado.

**Why this priority**: El envío es el compromiso externo de HU-07 y habilita la recepción.
Una confirmación prematura o con contenido distinto al revisado produciría compras ambiguas.

**Independent Test**: Usar un pedido válido y un buzón de pruebas controlado. Revisar
proveedor, correo y líneas; solicitar el envío, observar su resultado pendiente y después
comprobar que el correo aceptado contiene el mismo pedido, sin realizar una recepción.

**Acceptance Scenarios**:

1. **Given** un borrador listo para enviar, **When** abre la revisión, **Then** ve número
   de pedido, proveedor, correo destinatario y líneas con código/nombre de artículo,
   cantidad, unidad, precio y fecha prevista antes de confirmar; puede completar la revisión por teclado.
2. **Given** esa revisión válida, **When** confirma el envío y la solicitud queda conservada,
   **Then** se informa «Envío pendiente» y el pedido sigue en «Borrador» hasta la aceptación
   del correo; no se comunica que el proveedor lo haya recibido.
3. **Given** una solicitud pendiente, **When** el servicio de correo acepta el mensaje,
   **Then** el pedido pasa a «Enviado», el envío se muestra «Aceptado por el servicio de
   correo» y quedan identificados el momento y la solicitud que produjo ese cambio.
4. **Given** un proveedor sin correo o con correo no válido, **When** intenta solicitar
   el envío, **Then** se rechaza sin crear la solicitud ni cambiar el estado; se explica
   qué contacto debe corregir y puede conservar el borrador para volver a revisarlo.
5. **Given** proveedor o artículos desactivados antes de confirmar una primera solicitud,
   **When** intenta enviarla, **Then** se rechaza con explicación y se conserva el pedido.
   La desactivación no borra ni cierra automáticamente sus documentos anteriores.
6. **Given** un pedido cuyo envío ya se solicitó, **When** intenta cambiar proveedor,
   destinatario, líneas, cantidades, precios o fechas, **Then** se impide, incluso si
   el envío terminó en error; la solicitud conserva el contenido revisado originalmente.
7. **Given** otra edición del pedido o un cambio de destinatario desde que se revisó,
   **When** intenta confirmar esa revisión antigua, **Then** se informa del conflicto,
   no se envía contenido distinto sin revisión y puede consultar los datos vigentes.

---

### User Story 3 - Consultar pedidos, estados y cantidades pendientes (Priority: P1)

Como comprador o trabajador con permiso de consulta, quiero localizar los pedidos y ver
qué se pidió, qué se recibió y qué queda pendiente, para hacer seguimiento de las compras
y facilitar la recepción sin modificar manualmente sus acumulados.

**Why this priority**: Consulta de estado, recepciones parciales y pendientes son criterios
originales de HU-07. Deben distinguirse del estado de envío del correo.

**Independent Test**: Preparar pedidos identificables de varios proveedores y estados;
buscar por número y filtrar por proveedor, estado y período. Abrir un pedido y revisar
sus líneas y estado de envío con una cuenta de solo consulta. Los casos recibidos se
contrastan con las confirmaciones válidas del módulo de recepciones cuando se integre.

**Acceptance Scenarios**:

1. **Given** varios pedidos, **When** busca por número o filtra por proveedor, estado
   logístico y período de creación, **Then** obtiene solo coincidencias, con número,
   proveedor, fechas y estados textuales, y puede recorrer todas las páginas.
2. **Given** un pedido existente, **When** consulta su detalle, **Then** ve por línea
   artículo, unidad, cantidad pedida, recibida y pendiente, precio y fecha prevista,
   junto con el estado logístico y el estado independiente del correo.
3. **Given** un pedido enviado de 10 UD y una recepción confirmada válida de 6 por HU-01,
   **When** vuelve a consultarlo, **Then** muestra recibido 6, pendiente 4 y «Recibido
   parcialmente»; tras confirmar las 4 restantes, muestra pendiente cero y «Recibido».
4. **Given** un borrador de recepción todavía sin confirmar, **When** consulta la compra,
   **Then** sus cantidades y estado siguen basados solo en las recepciones confirmadas;
   ningún usuario puede simular la recepción editando esos campos desde compras.
5. **Given** proveedor o artículos modificados o desactivados después de guardar el pedido,
   **When** consulta sus documentos previos, **Then** conserva sus identidades, datos
   históricos y condiciones de envío, identifica las referencias inactivas y no exige
   reactivarlas para consultar. Reactivar el proveedor permite nuevas compras con sus
   reglas originales, sin modificar por ello sus pedidos previos.
6. **Given** una búsqueda sin coincidencias, una página que deja de tener filas o un fallo
   de consulta, **When** revisa el resultado, **Then** distingue ausencia real de resultados
   de un error, puede ajustar filtros o volver a consultar y no pierde el acceso a otras páginas.

---

### User Story 4 - Resolver fallos y comprobar envíos inciertos (Priority: P2)

Como comprador autorizado, quiero conocer el progreso o error del envío y recuperar su
resultado antes de repetirlo, para no perder solicitudes ni emitir compras duplicadas.

**Why this priority**: Correo y conectividad pueden fallar. Estas garantías se aplican
desde el primer envío; la prioridad identifica el recorrido de recuperación, no permite posponerlas.

**Independent Test**: Con un pedido y servicio de correo controlados, provocar rechazo
o indisponibilidad, revisar el error y reintentar explícitamente. Simular también pérdida
de la respuesta y recarga de pantalla, comprobando el mismo pedido y solicitud conservada.

**Acceptance Scenarios**:

1. **Given** una solicitud conservada y el servicio de correo no disponible, **When**
   consulta su progreso, **Then** ve pendiente o error según el resultado conocido,
   intentos e incidencia explicada, sin presentar el pedido como «Enviado».
2. **Given** un envío en error confirmado, **When** una persona con permiso lo reintenta,
   **Then** se reactiva esa solicitud con el mismo documento, destinatario e identificación
   de envío; la acción tiene un resultado comprobable y no crea otro pedido.
3. **Given** la respuesta perdida de un guardado, solicitud o reintento, **When** recarga
   o vuelve a la pantalla, **Then** conserva la referencia y puede comprobar el resultado
   antes de emitir otra intención; si aún no se observa, no se considera fallo definitivo.
4. **Given** el correo aceptado externamente cuya confirmación local se perdió, **When**
   se recupera la solicitud, **Then** se conserva su misma identidad y contenido, no se
   crea otra compra y se hace explícita la incertidumbre; una posible copia del correo
   no se presenta como un pedido distinto ni como garantía de entrega única al destinatario.
5. **Given** una solicitud pendiente y una interrupción del servicio de la aplicación,
   **When** se restablece, **Then** la solicitud sigue disponible para completar o comprobar
   su entrega; no desaparece ni se desbloquea la edición del documento por el reinicio.
6. **Given** dos confirmaciones de envío simultáneas o una segunda solicitud para un pedido
   ya pendiente o aceptado, **When** se procesan, **Then** solo se conserva una solicitud
   de envío; la incompatible se rechaza y remite a consultar la existente. Los reintentos
   de un error conocido siguen el caso 2, no crean una solicitud alternativa.

---

### User Story 5 - Operar con autorización, autoría y cambios fiables (Priority: P2)

Como responsable del almacén o del proyecto, quiero que las compras respeten permisos,
conserven quién hizo cada cambio y resuelvan conflictos y fallos sin efectos parciales,
para mantener una referencia fiable para proveedor y recepción.

**Why this priority**: Es transversal: autorización, conservación de evidencia y cambios
completos son obligatorios desde el primer incremento, aunque su recorrido se valide por separado.

**Independent Test**: Con cuentas controladas de consulta, gestión y envío, abrir un
borrador en dos sesiones; probar cambios incompatibles, repetir una acción confirmada y
provocar un fallo antes de completar el guardado. Revisar documento, autoría y resultado.

**Acceptance Scenarios**:

1. **Given** una cuenta sin sesión o sin el permiso de la acción, **When** intenta consultar,
   crear, editar, solicitar envío o reintentar, **Then** se rechaza sin efectos. Poder
   gestionar cuentas no concede compras; consultar, gestionar y enviar se autorizan por separado.
2. **Given** dos ediciones de la misma revisión o una edición simultánea al envío,
   **When** se confirman, **Then** se conserva un resultado compatible con el orden confirmado:
   no se sobrescribe silenciosamente un cambio ni se envía un contenido distinto al revisado.
3. **Given** una acción ya confirmada, **When** se repite la misma intención, **Then**
   devuelve su resultado original sin otro pedido, cambio o evidencia. Reutilizarla con
   otros datos o identidad se rechaza, aunque el pedido haya evolucionado posteriormente.
4. **Given** un fallo antes de completar guardado o solicitud de envío, **When** vuelve
   a consultar, **Then** no hay cabeceras sin sus líneas, cambios sin autoría, solicitudes
   desligadas del pedido ni mensajes de éxito sin datos conservados; no se emite correo de una solicitud no registrada.
5. **Given** una nueva referencia o primera solicitud de envío simultánea a la desactivación
   de sus maestros, **When** ambas acciones se confirman, **Then** se respeta el estado
   vigente al confirmar cada acción, sin referencias huérfanas ni compras nuevas incompatibles con una desactivación ya confirmada.
6. **Given** altas, correcciones, solicitudes y reintentos de compra, **When** se revisa su
   registro, **Then** se identifican responsable autenticado, momento y cambios aplicables.
   La evolución automática del correo conserva la relación con quien solicitó el envío;
   el usuario no puede atribuir la acción a otra persona ni alterar su evidencia.

### Edge Cases

- Los campos obligatorios compuestos solo por espacios se rechazan. Las cantidades y
  precios conservan exactamente la precisión admitida; no se aceptan conversiones de unidad implícitas.
- Precio cero es válido; cantidad cero o negativa no. Un artículo de unidades enteras
  rechaza fracciones. Las líneas repetidas de un artículo siguen siendo líneas distintas.
- La fecha prevista puede estar vencida: se conserva como previsión, sin convertir por
  ello el pedido en enviado o recibido ni registrar mercancía automáticamente.
- La familia inactiva de un artículo activo ya clasificado no desactiva el artículo
  ni sustituye la comprobación de su propio estado para comprar.
- Un proveedor sin correo permite preparar, pero no solicitar envío. Cambiar el correo
  antes de confirmar exige revisar el destinatario que realmente se utilizará.
- Cambiar maestros después de solicitar el envío no sustituye sus instantáneas ni el
  destinatario congelado. Corregir un contacto del maestro no reescribe un envío fallido
  anterior; cambiar su destinatario o contenido exige un flujo distinto, fuera de esta entrega.
- La desactivación posterior a una solicitud conservada no la borra ni cancela de forma
  automática. Tampoco cierra pedidos anteriores ni reinterpreta sus cantidades.
- La primera referencia de borrador bloquea la definición del artículo aunque se retire
  la línea, el artículo esté inactivo o el saldo sea cero. Esa marca sola no prohíbe su borrado.
- Dos compradores editando, editando frente a envío o solicitando envío a la vez no pueden
  provocar sobrescrituras ni dos solicitudes distintas para el mismo pedido.
- Un tiempo de espera agotado o un resultado todavía no observado se comunica como pendiente
  de comprobar. Cerrar sesión retira los datos de la petición; otra identidad no reutiliza esa intención.
- La aceptación por el servicio de correo no acredita lectura, recepción final ni aceptación
  comercial del proveedor. Una copia por recuperación incierta conserva la identificación del mismo pedido.
- Un reintento explícito de un error conocido y la repetición de una acción cuya respuesta
  se perdió son casos distintos: ambos conservan sus resultados y ninguno cambia el documento de envío.
- Un borrador de recepción no cambia acumulados. Dos recepciones parciales no pueden superar
  lo pedido; HU-01 coordina esa confirmación y HU-07 muestra el resultado confirmado.
- Una corrección autorizada de recepción deriva de nuevo cantidades y estado según el
  contrato común; no se corrige el pendiente sobrescribiéndolo desde compras.
- Un listado sin filas en una página que quedó fuera de rango no equivale a inexistencia
  de pedidos. Un fallo de consulta no se presenta como listado vacío confirmado.

## Requirements *(mandatory)*

### Functional Requirements

**Preparación del pedido**

- **FR-001**: El sistema DEBE permitir a usuarios con permiso de gestión crear un pedido
  con proveedor activo, fecha prevista general y al menos una línea; DEBE asignar un
  número único legible y conservar una identidad estable. Se crea en «Borrador», con envío «Sin solicitar».
- **FR-002**: Cada línea DEBE identificar artículo activo, cantidad positiva, unidad base,
  precio unitario no negativo y fecha prevista; esta puede heredarse de la general.
  Las cantidades DEBEN respetar la unidad y admisión de fracciones del artículo, con
  hasta tres decimales, y los precios, hasta dos decimales en la moneda del almacén.
- **FR-003**: Una referencia de proveedor/artículo inexistente o una nueva asignación a
  uno inactivo DEBE rechazarse. La falta de correo NO DEBE impedir preparar un borrador,
  pero DEBE advertirse como impedimento para solicitar su envío.
- **FR-004**: Solo un pedido en «Borrador» cuyo envío esté «Sin solicitar» DEBE admitir
  cambios de proveedor, fechas y líneas. DEBE permitir añadir, corregir y retirar líneas,
  manteniendo al menos una al guardar y sin fusionar líneas repetidas de un artículo.
- **FR-005**: El guardado DEBE validar campos obligatorios, fechas, cantidades, precios
  y referencias, señalar los datos o líneas que corregir y conservar los datos válidos;
  un rechazo NO DEBE crear ni modificar parcialmente el pedido.
- **FR-006**: Los pedidos DEBEN conservar los datos históricos de proveedor y artículos
  usados en el documento. Cambios posteriores de códigos, nombres, contacto o definición
  NO DEBEN reescribir las condiciones históricas ni el documento de un envío ya solicitado.
- **FR-007**: La primera referencia de artículo en una línea, incluso de borrador, DEBE
  impedir cambios posteriores de unidad, lotes y fracciones conforme a HU-08. Retirar
  la última línea NO DEBE restablecer esa edición. La existencia de una referencia real
  DEBE impedir borrar el artículo; la marca sola no sustituye la evaluación independiente del borrado.
- **FR-008**: Crear/retirar referencias DEBE coordinarse con eliminación, edición de
  definición y desactivación de maestros, sin líneas huérfanas ni uso de definiciones
  obsoletas; las operaciones incompatibles DEBEN rechazarse sin efectos parciales.

**Solicitud y entrega del correo**

- **FR-009**: Antes de solicitar envío, el usuario DEBE poder revisar número, proveedor,
  correo destinatario y documento completo con artículos, cantidades, unidades, precios
  y fechas. Lo que se envíe DEBE corresponder a esa revisión confirmada.
- **FR-010**: La primera solicitud DEBE exigir pedido válido, proveedor y artículos activos
  y correo de proveedor presente y válido; los cambios desde la revisión DEBEN revalidarse.
  Un rechazo DEBE explicar qué corregir y conservar el pedido sin generar otra solicitud.
- **FR-011**: La solicitud de envío y el documento que la sustenta DEBEN quedar conservados
  conjuntamente antes de emitir correo. La confirmación inicial DEBE comunicar «Envío
  pendiente», sin afirmar aceptación del correo ni cambiar prematuramente a «Enviado».
- **FR-012**: El pedido DEBE permanecer «Borrador» mientras el correo esté pendiente o en
  error y pasar a «Enviado» únicamente cuando el servicio de correo confirme su aceptación.
  Esa aceptación DEBE distinguirse de la entrega o lectura final por el proveedor.
- **FR-013**: Desde que se conserva una solicitud, el pedido y su documento/destinatario
  DEBEN quedar bloqueados para edición, también ante error de envío; todos sus reintentos
  DEBEN conservar la misma identificación de envío y contenido original.
- **FR-014**: La consulta del envío DEBE mostrar estado «Sin solicitar», «Pendiente»,
  «Aceptado por el servicio de correo» o «Error», intentos, instantes aplicables y última
  incidencia comprensible, indicando si cabe reintentar sin exponer detalles internos o secretos.
- **FR-015**: Ante un error conocido, un usuario con permiso de envío DEBE poder reintentar
  explícitamente la solicitud original, sin otro pedido ni otro documento de envío.
  Las solicitudes pendientes DEBEN conservarse y poder continuar o comprobarse tras una interrupción.
- **FR-016**: Ante aceptación externa incierta, el sistema DEBE conservar la identidad y
  contenido del envío e informar con honestidad del estado conocido. NO DEBE garantizar
  entrega única de correo si una recuperación puede producir copias, ni tratar una copia como otra compra.
- **FR-017**: Las peticiones repetidas o simultáneas NO DEBEN crear más de una solicitud
  de envío para un pedido. Una nueva solicitud incompatible con otra ya pendiente o
  aceptada DEBE rechazarse y permitir consultar la existente; el reintento de un error sigue FR-015.

**Consulta y seguimiento de recepción**

- **FR-018**: Los usuarios con permiso de consulta DEBEN poder buscar pedidos por número,
  filtrar por proveedor, estado logístico y período de creación, recorrer resultados
  paginados y ver número, proveedor, fechas y estados expresados con texto.
- **FR-019**: El detalle DEBE mostrar por línea cantidad pedida, recibida y pendiente
  (pedida menos recibida), unidad, precio y fecha prevista, además de los datos del
  pedido y el estado independiente del correo. Filas y acumulados DEBEN ser coherentes entre sí.
- **FR-020**: Con el envío aceptado, cero recibido DEBE corresponder a «Enviado»; alguna
  cantidad recibida con pendiente, a «Recibido parcialmente»; todas las líneas con
  pendiente cero, a «Recibido». Los estados y acumulados NO DEBEN poder editarse manualmente desde compras.
- **FR-021**: Solo las recepciones confirmadas y sus correcciones autorizadas de HU-01
  DEBEN modificar los acumulados de recepción. HU-07 DEBE mostrar sus cambios en la primera
  consulta posterior, sin incluir borradores ni permitir recibido superior a pedido o pendiente negativo.
- **FR-022**: Crear, editar o enviar una compra NO DEBE cambiar existencias, ubicaciones,
  reservas o mínimos ni generar movimientos de stock. Pedidos de maestros inactivos
  DEBEN seguir consultables con referencias e historial conservados, sin cerrar documentos automáticamente.

**Autorización, integridad e interacción**

- **FR-023**: Consulta, gestión de borradores y envío/reintento DEBEN exigir sesión y sus
  permisos correspondientes, comprobados por el sistema. Administración de cuentas NO
  DEBE conceder esos permisos implícitamente; gestionar borradores no sustituye la autorización de envío.
- **FR-024**: Altas, correcciones, solicitudes y reintentos DEBEN conservar responsable
  autenticado, momento, acción y valores anteriores/posteriores aplicables, sin alteración
  de la evidencia. La evolución automática del correo DEBE conservar su relación con la
  solicitud autorizada y su responsable, sin aceptar otra autoría suministrada por el usuario.
- **FR-025**: Repetir una misma intención de cambio DEBE devolver su resultado original
  sin duplicar pedidos, solicitudes ni evidencia. Reutilizarla con otros datos o identidad
  DEBE rechazarse; un resultado histórico NO DEBE reconstruirse modificando el pedido vigente.
- **FR-026**: Una edición o envío basado en datos sustituidos DEBE rechazarse con conflicto,
  conservar la edición local y permitir consultar el estado vigente. Edición/envío
  simultáneos DEBEN conservar el contenido confirmado y el bloqueo de FR-013.
- **FR-027**: Ante resultado incierto, el usuario DEBE poder comprobarlo antes de repetir
  o emitir otra intención. Recargar DEBE conservar la referencia y la petición original
  cuando siga disponible; cerrar sesión DEBE retirar sus datos y conservar solo la
  referencia para comprobar el resultado con la autorización correspondiente. La interfaz
  NO DEBE reutilizar la intención bajo otra cuenta ni fabricar un contenido nuevo.
- **FR-028**: Los cambios aplicables a pedido, líneas, evidencia, solicitud de envío y
  resultado de cada acción DEBEN conservarse conjuntamente o no realizarse. Fallos/rechazos
  NO DEBEN dejar cambios parciales, modificar stock ni emitir correo sin solicitud conservada.
- **FR-029**: Formularios y resultados DEBEN usar español, vocabulario del almacén, etiquetas
  y obligatoriedad explícitas, unidades y moneda visibles, estados con texto y operación
  por teclado; los errores DEBEN explicar la corrección y conservar datos válidos.
- **FR-030**: La interfaz DEBE comunicar éxito solo de acciones conservadas y consultables,
  identificar pedido/solicitud afectados y distinguir guardado, envío pendiente y correo
  aceptado; ausencia de resultados, fallo de consulta e incertidumbre NO DEBEN confundirse.

### Alcance, dependencias y responsabilidad de integración

- HU-07 entrega verticalmente borrador, corrección, revisión, solicitud de correo,
  recuperación y seguimiento del pedido. Consume artículos/proveedores de HU-08 y los
  permisos existentes; no crea catálogos ni gestión de cuentas propios.
- Quedan fuera recepción física y sus escritores (HU-01), salidas/pedidos de cliente,
  reservas, conteos, alertas y compras automáticas, facturación, impuestos, varias monedas,
  cancelación/borrado de pedidos y cambio de destinatario/contenido de un envío ya solicitado.
- HU-07 implementa el pedido y su lectura de recibido/pendiente/estado. HU-01 consume ese
  soporte y debe confirmar recepción, acumulados y stock conjuntamente, impedir excesos
  y probar sus escritores reales, incluidas dos parciales simultáneas de 6 contra 10.
  Es trabajo de HU-01 al integrarse, no una implementación pendiente que mantenga abierta HU-07.
- Los escenarios de seguimiento con recepción real se aceptan en esa integración de HU-01
  (V-02/V-06); HU-07 conserva sus invariantes, lectura y contrato para recibirla. No se
  declara probada una recepción real utilizando únicamente preparaciones de pedidos.
- La revisión cruzada de acuerdos compartidos se realiza conforme al plan global antes
  de integrar cambios. La aceptación operativa y recuperación completa permanecen en
  H5-02/H5-03 del backlog conjunto; no se presuponen realizadas por terminar el módulo.

### Obligaciones receptoras de HU-07

| ID de origen | Obligación que debe satisfacer este módulo | Trazabilidad |
|--------------|-------------------------------------------|--------------|
| INT-HU07-01 | La primera línea de borrador protege la definición; retirada sin desbloqueo, referencias que impiden borrado, ausencia de huérfanos y carreras con ambos órdenes de confirmación. Conservar datos históricos y no duplicar escrituras ni efectos. | US1.5–US1.6; FR-006/007/008/024/028; SC-005. |
| INT-HU07-02 | Editar/desactivar/reactivar proveedor conserva pedidos e instantáneas; nuevas compras de proveedor/artículo inactivos se rechazan, coordinadas con su cambio de estado. Reactivar permite nuevas compras sin alterar documentos previos. | US1.4; US2.5–US2.6; US3.5; US5.5; FR-003/006/008/010/022; SC-005. |

Estos IDs se incorporarán a la descomposición de HU-07 al generar sus tareas. La evidencia
se produce con sus escritores reales; no se reabre HU-08 ni se dan por demostradas
estas conexiones mediante sustitutos que siempre respondan «sin referencias».

### Principios constitucionales y evidencia de aceptación

| Principio | Aplicación a HU-07 | Evidencia requerida |
|-----------|-------------------|---------------------|
| I. Correspondencia almacén/sistema | Pedir o enviar no registra mercancía; recibido deriva de hechos confirmados. | US1.1; US3.3–US3.4; FR-021/022; comparación de cantidades antes/después. |
| II. Ubicación precisa | Compra no inventa destinos ni cantidades por ubicación; la recepción identifica su destino en HU-01. | Alta/envío sin stock; contrato integrado V-02/V-03 en HU-01. |
| III. Uso intuitivo | Documento y destinatario revisables, estados separados, errores conservados y teclado. | US2.1–US2.4; US3.6; FR-029/030; SC-008 operativo en H5. |
| IV. Trazabilidad | Autoría y documentos conservados, identidades/definiciones protegidas, seguimiento sin reescribir hechos. | INT-HU07-01/02; US3.5; US5.6; FR-006/007/024. |
| V. Integridad y recuperación | Permisos, acciones completas, conflictos, intención durable y resultado comprobable. | US4–US5; FR-023–FR-028; fallos/concurrencia y V-08/V-13; V-14 operativo en H5. |

Cada implementación conserva fecha, entorno, escenario, resultado e incidencias. Esta
spec define la aceptación: no declara ejecutadas pruebas, validaciones humanas ni revisiones compartidas.

### Key Entities *(include if feature involves data)*

- **Pedido a proveedor**: identidad/número únicos, proveedor y sus datos conservados,
  fecha prevista, responsable de creación, líneas, estado logístico y situación del envío.
- **Línea de pedido**: identidad dentro del pedido, artículo y definición conservada,
  cantidad pedida, recibida y pendiente, unidad, precio unitario y fecha prevista propia.
- **Solicitud de envío**: compromiso conservado de comunicar un documento y destinatario
  concretos, con identidad estable, responsable, progreso, intentos e incidencia o aceptación.
- **Documento de pedido enviado**: contenido revisado y fijado al solicitar envío,
  reproducible para consulta y reintentos sin sustituirlo por datos actuales de los maestros.
- **Evidencia de cambio y resultado de acción**: qué se hizo, sobre qué pedido, autor,
  instante, valores aplicables y resultado recuperable de la intención original.
- **Proveedor y artículo — referencias compartidas**: identidad, contacto, unidad,
  reglas de cantidades/lotes y estado mantenidos en HU-08.
- **Recepción — referencia consumidora**: hecho de HU-01 que justifica lo recibido;
  su confirmación y efectos sobre mercancía se implementan en ese módulo.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: El 100 % de los recorridos válidos definidos permite crear, consultar y
  corregir borradores y solicitar envío, conservando los datos al volver a abrir el
  pedido y sin cambios de existencias, reservas o ubicaciones.
- **SC-002**: El 100 % de los casos definidos de datos inválidos, referencias inexistentes
  o inactivas y permisos insuficientes se rechaza sin pedido parcial ni solicitud indebida,
  indicando qué corregir y conservando los datos válidos introducidos.
- **SC-003**: En el 100 % de los escenarios de correo controlados, la solicitud pendiente
  o en error mantiene el pedido en «Borrador»; solo la aceptación del servicio lo pasa
  a «Enviado». Tras una interrupción, toda solicitud conservada sigue localizable y comprobable.
- **SC-004**: En todos los casos definidos de repetición, pérdida de respuesta, recarga,
  cambio de identidad, edición/envío simultáneos y fallo de guardado hay cero pedidos
  duplicados, cero solicitudes de envío alternativas y cero cambios parciales o sobrescrituras
  silenciosas. Las posibles copias de correo por aceptación incierta conservan la misma identidad de compra.
- **SC-005**: El 100 % de las comprobaciones INT-HU07-01/02 conserva referencias e
  instantáneas, impide cambios de definición desde la primera línea incluso retirada,
  rechaza borrado con referencias y compras nuevas incompatibles con maestros inactivos;
  responsable y momento de cada cambio son identificables.
- **SC-006**: El 100 % de las consultas del escenario integrado de pedido enviado de 10 UD y
  recepciones válidas de 6 y 4 muestra respectivamente recibido/pendiente 0/10, 6/4 y
  10/0, con estados «Enviado», «Recibido parcialmente» y «Recibido», en la primera consulta
  posterior a cada confirmación. HU-01 aporta y acepta sus escritores en esa integración.
- **SC-007**: Con 25 sesiones concurrentes, 10.000 artículos y pedidos de hasta 20 líneas,
  al menos el 95 % de las consultas paginadas y guardados de compra muestra su resultado
  en 2 segundos o menos bajo las condiciones de la guía compartida, midiendo aparte la
  espera del correo y los conflictos, sin descartar errores como muestras de éxito.
- **SC-008**: En la validación operativa H5-03, al menos tres compradores representativos
  completan preparación, corrección de un campo inválido, revisión de envío y consulta de
  un resultado pendiente/error sin asistencia técnica ni errores de registro no detectados;
  se documentan resultados e incidencias y se corrigen antes de la aceptación operativa.
  Este ejercicio no es una tarea de implementación que bloquee el cierre técnico de HU-07.

## Assumptions

- Se reutilizan cuentas y permisos compartidos: consulta de inventario para leer,
  gestión de compras para preparar/corregir y permiso de envío para solicitar/reintentar.
  El rol Comprador combina los de compra; administración de cuentas no los concede implícitamente.
- El número del pedido lo asigna el sistema al guardar y no lo introduce el comprador.
  No se impone aquí un formato comercial ni una numeración con significado contable.
- La fecha general es obligatoria y propone la de las líneas; una línea puede tener
  otra fecha prevista. Una fecha pasada válida permite representar una previsión vencida.
- Se utiliza la moneda única del almacén, sin impuestos, conversión de moneda o unidades
  ni cálculo comercial adicional. El precio es unitario, no negativo y puede ser cero.
- Pueden repetirse artículos en distintas líneas; sus condiciones y cantidades se
  conservan por línea, sin agruparlas automáticamente ni confundir sus recepciones.
- El destinatario procede del proveedor y se muestra antes de confirmar. Al solicitar
  envío quedan fijados documento y destinatario; actualizaciones posteriores del maestro
  no los sustituyen. Un error no desbloquea su edición ni habilita cambiar el destino en el reintento.
- Se revalidan maestros para nuevas compras y primeras solicitudes de envío. La caducidad
  de la sesión o desactivación posterior no cancela silenciosamente una solicitud ya
  conservada; todo reintento manual vuelve a exigir autorización y conserva su identidad de envío.
- «Enviado» significa aceptación por el servicio de correo, no entrega final, lectura ni
  aprobación comercial del proveedor. No se promete ausencia absoluta de copias ante una confirmación externa perdida.
- La recuperación de una acción incierta repite su intención original; reintentar un
  envío en error conocido es una acción autorizada sobre la misma solicitud de envío,
  con resultado propio comprobable y sin cambiar su documento o crear otra compra.
- HU-01 desarrolla las recepciones y prueba sus efectos reales al integrarse. HU-07
  entrega el pedido, acumulados y contrato de lectura; no incorpora recepciones ficticias
  para declarar completado ese módulo ni mantiene abierto su cierre por su ausencia.
- Rendimiento y recorridos son criterios a verificar, no resultados obtenidos. Recuperación
  y aceptación operativas permanecen en H5 y siguen la constitución y guía globales.
