# Feature Specification: HU-02 — Gestionar las ubicaciones de los artículos

**Feature Branch**: `b/hu-02-ubicaciones` (el directorio de la feature es independiente de la rama Git).

**Created**: 2026-11-16

**Status**: Draft

**Input**: User description: "Especificación de la historia de usuario 2: gestionar las ubicaciones de los artículos"

**Historia de origen**: HU-02 del [catálogo y reparto](../001-reparto-specs-inventario/spec.md#catálogo-y-asignación-de-las-ocho-futuras-specs).
**Responsable principal**: Persona B. **Hito de inicio**: H1, según el
[plan de coordinación](../001-reparto-specs-inventario/plan.md#hitos-y-coordinación).

**Contexto normativo y acuerdos consumidos**:

- [Constitución vigente](../../.specify/memory/constitution.md): principios I–V, en especial II, y criterios de aceptación.
- [Plan global](../../.specify/memory/project-plan.md): arquitectura compartida, responsabilidades y coordinación.
- [Decisiones comunes](../../.specify/memory/project/research.md#r-05-reglas-comunes-de-negocio-para-no-bloquear-las-specs): unidades, lotes y conteos.
- [Modelo común](../../.specify/memory/project/data-model.md#dimensiones-y-existencias--persona-b--hu-02-hu-05): ubicación, lote y saldo por bucket.
- [Contrato HTTP común](../../.specify/memory/project/contracts/api.md#hu-02--b-ubicaciones): permisos y recursos de ubicaciones.
- [Contrato de stock](../../.specify/memory/project/contracts/stock-operations.md#alcance-y-disciplina-de-escritura): coordinación del cambio de estado de una ubicación con los movimientos.
- [Contrato de interacción](../../.specify/memory/project/contracts/ui.md): pantalla /ubicaciones, estados de operación y accesibilidad.
- [Guía de validación](../../.specify/memory/project/quickstart.md#5-datos-comunes-de-validación): datos comunes, V-01 y V-03.

Los enlaces anteriores son referencias normativas; esta especificación describe necesidades
y resultados de negocio. Las decisiones de implementación corresponden al futuro plan de HU-02.

## Clarifications

### Session 2026-11-18

- Q: ¿Debe ser única la combinación pasillo+estantería+nivel entre todas las ubicaciones, incluidas las inactivas? → A: Sí, única entre todas, incluidas las inactivas (normalizada sin mayúsculas ni espacios extremos). **Confirmada por B, pendiente de validación de A en el PR**, porque añade una restricción al modelo común.
- Q: ¿Qué datos de una ubicación quedan bloqueados una vez que algún movimiento confirmado la ha usado? → A: Código, pasillo, estantería y nivel no cambian desde el primer movimiento confirmado que la referencie, aunque esté vacía o el movimiento se revirtiera; el tipo puede cambiar si está vacía, sin reservas ni conteo abierto.
- Q: ¿Se puede borrar una ubicación que nunca ha tenido movimientos, o solo se desactivan? → A: Ninguna ubicación se borra, tenga o no historial; las creadas por error se corrigen (si no tienen movimientos) o se desactivan.
- Q: ¿La descripción física se limita a pasillo+estantería+nivel en un único almacén, sin almacén, zona ni posición? → A: Sí: un único almacén, sin zonas ni posiciones; es la organización física existente que exige el principio II. Zonas o varios almacenes requerirían un cambio del modelo común.
- Q: ¿Debe impedirse desactivar (o cambiar a almacenamiento) la última zona temporal de recepción activa? → A: Se permite, pero antes de confirmar se avisa con texto de que no quedará ninguna zona de recepción activa.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Registrar las ubicaciones físicas del almacén (Priority: P1)

Como operario autorizado, quiero dar de alta y consultar las ubicaciones del almacén con un
código único y su pasillo, estantería y nivel, para que cualquier trabajador pueda encontrar
físicamente el lugar donde se guarda la mercancía sin depender de conocimiento informal.

**Why this priority**: Sin ubicaciones registradas no puede recibirse ni localizarse ningún
artículo; es la base de recepciones, salidas, consultas y conteos.

**Independent Test**: Con una cuenta autorizada y sin existencias, crear REC-01 como zona de
recepción y P01-E01-N01 y P01-E01-N02 como ubicaciones de almacenamiento; buscarlas por código
y comprobar que muestran su pasillo, estantería, nivel y tipo. Intentar repetir un código.

**Acceptance Scenarios**:

1. **Given** un operario autorizado y un código libre, **When** registra código, pasillo,
   estantería, nivel y tipo (almacenamiento o zona temporal de recepción), **Then** la ubicación
   se crea activa, vacía y queda disponible para consulta y para recibir mercancía.
2. **Given** una ubicación existente, **When** se intenta crear otra con el mismo código, aunque
   varíen mayúsculas o espacios extremos, **Then** se rechaza el duplicado indicando el campo,
   sin crear una ubicación parcial y conservando el resto de datos introducidos.
3. **Given** una ubicación existente, **When** se intenta crear otra con el mismo pasillo,
   estantería y nivel, **Then** se rechaza por designar el mismo lugar físico y se identifica
   la ubicación ya registrada.
4. **Given** un alta con pasillo, estantería o nivel vacíos o compuestos solo por espacios,
   incluso para una zona de recepción, **When** se intenta guardar, **Then** se señalan los
   campos obligatorios y no se crea la ubicación.
5. **Given** ubicaciones de distintos tipos y estados, **When** un usuario con permiso de
   consulta busca por código o por pasillo y filtra por tipo y estado, **Then** obtiene solo
   las coincidencias, con código, descripción física, tipo y estado expresados con texto;
   si no hay coincidencias se le indica y puede cambiar la búsqueda.

---

### User Story 2 - Conocer todas las ubicaciones de un artículo (Priority: P1)

Como trabajador del almacén, quiero consultar en qué ubicaciones se encuentra un artículo y
qué cantidad hay en cada una, separada por lote cuando corresponda, para ir directamente al
lugar correcto y saber que la suma coincide con el total del artículo.

**Why this priority**: Es el criterio central de HU-02: un artículo puede estar en varias
ubicaciones y el registro debe permitir localizar todas sus unidades.

**Independent Test**: Recibir 6 unidades de ART-01 con lote L-001 distribuyendo 2 en
P01-E01-N01 y 4 en P01-E01-N02 mediante las operaciones acordadas del inventario. Consultar
el artículo y comprobar que aparecen ambas ubicaciones con 2 y 4 unidades y un total de 6.

**Acceptance Scenarios**:

1. **Given** un artículo con existencias en varias ubicaciones, **When** el usuario lo busca
   por código o nombre y consulta sus ubicaciones, **Then** ve cada ubicación con su código,
   descripción física, lote cuando aplique, cantidad y unidad, y el total del artículo.
2. **Given** un artículo con dos lotes en la misma ubicación, **When** se consultan sus
   ubicaciones, **Then** cada lote aparece con su cantidad y la ubicación no se duplica en el total.
3. **Given** un artículo sin existencias, **When** se consultan sus ubicaciones, **Then** se
   indica que no está almacenado en ninguna ubicación, sin mostrarlo como un error.
4. **Given** un artículo inactivo o una ubicación inactiva con existencias, **When** se consulta
   el artículo, **Then** sus cantidades siguen apareciendo con indicación textual de su estado.
5. **Given** un movimiento confirmado que cambia las cantidades, **When** se vuelve a consultar
   el artículo, **Then** la consulta refleja el nuevo reparto por ubicación sin pasos manuales.

---

### User Story 3 - Revisar qué contiene una ubicación (Priority: P2)

Como operario o responsable de almacén, quiero ver qué artículos y cantidades hay registrados
en una ubicación, para comprobar el contenido físico de un hueco y preparar su reorganización
o su recuento.

**Why this priority**: Complementa la consulta por artículo y es necesaria para decidir si una
ubicación puede retirarse y para preparar los conteos de HU-04.

**Independent Test**: Con el reparto de US2, abrir P01-E01-N02 y comprobar que lista ART-01,
lote L-001 y 4 unidades; abrir una ubicación vacía y comprobar que se indica como vacía.

**Acceptance Scenarios**:

1. **Given** una ubicación con existencias de varios artículos, **When** se abre su detalle,
   **Then** se listan artículo, lote, cantidad y unidad de cada existencia, con el estado
   textual de artículos inactivos.
2. **Given** una ubicación sin existencias, **When** se abre su detalle, **Then** se indica que
   está vacía y se muestran sus datos físicos, tipo y estado.
3. **Given** una ubicación con existencias reservadas o incluida en un conteo abierto,
   **When** se abre su detalle, **Then** esas circunstancias se indican con texto.

---

### User Story 4 - Corregir y retirar ubicaciones sin perder la trazabilidad (Priority: P2)

Como operario autorizado, quiero corregir los datos de una ubicación y desactivar las que dejan
de usarse, para mantener el mapa del almacén al día sin dejar mercancía sin localizar ni
alterar la historia de los movimientos.

**Why this priority**: El almacén cambia; retirar una ubicación no debe ocultar existencias ni
romper la reconstrucción de dónde estuvo cada unidad.

**Independent Test**: Usar una ubicación nueva sin movimientos y otra con existencias.
Corregir el nivel de la primera; intentar desactivar la segunda, vaciarla mediante las
operaciones del inventario y desactivarla después. Reactivarla y comprobar su historial.

**Acceptance Scenarios**:

1. **Given** una ubicación sin movimientos, **When** el operario corrige su código, pasillo,
   estantería, nivel o tipo con valores válidos y no repetidos, **Then** se conserva su
   identidad y los cambios quedan visibles al volver a consultarla.
2. **Given** una ubicación con movimientos registrados, **When** se intenta cambiar su código,
   pasillo, estantería o nivel, **Then** se rechaza explicando que la identificación física
   forma parte del historial, y se indica que puede crearse otra ubicación y retirar esta.
3. **Given** una ubicación con existencias, reservas o un conteo abierto, **When** se intenta
   desactivar o cambiar su tipo, **Then** se rechaza explicando la circunstancia y
   enlazando a su contenido; no se mueven ni se pierden existencias.
4. **Given** una ubicación vacía, sin reservas ni conteo abierto, **When** el operario revisa
   y confirma su desactivación, **Then** deja de ofrecerse como destino u origen de nuevas
   operaciones, sigue siendo consultable como inactiva y conserva su historial.
5. **Given** una ubicación inactiva, **When** el operario revisa y confirma su reactivación,
   **Then** vuelve a ofrecerse para nuevas operaciones sin cambiar su historial.
6. **Given** una solicitud de borrado de una ubicación, **When** se busca la acción,
   **Then** el sistema no ofrece eliminar ubicaciones, tampoco las que no tienen movimientos:
   la retirada se realiza mediante desactivación.
7. **Given** la única zona temporal de recepción activa, vacía y sin reservas ni conteo abierto,
   **When** el operario solicita desactivarla o cambiar su tipo a almacenamiento, **Then** se le
   advierte con texto de que no quedará ninguna zona de recepción activa y la operación solo se
   realiza si la confirma.

---

### User Story 5 - Guardar cambios con autorización y resultado fiable (Priority: P3)

Como trabajador autorizado, quiero saber si un cambio de ubicación se ha guardado y resolver
los errores sin perder mi trabajo, para evitar duplicados y sobrescrituras involuntarias.

**Why this priority**: El mapa del almacén es compartido; deben distinguirse consulta y
modificación, y hacerse visibles los conflictos de edición y los resultados inciertos.

**Independent Test**: Preparar cuentas con y sin permiso de gestión de ubicaciones, abrir la
misma ubicación en dos sesiones e intentar guardar cambios incompatibles; repetir un alta tras
simular la pérdida de su respuesta.

**Acceptance Scenarios**:

1. **Given** un usuario con permiso de consulta pero sin gestión de ubicaciones, **When**
   intenta crear, modificar, desactivar o reactivar una ubicación, **Then** se rechaza sin
   cambios y puede seguir consultando.
2. **Given** dos usuarios editando la misma versión de una ubicación, **When** uno guarda y el
   otro intenta guardar después, **Then** el segundo recibe un conflicto, puede ver los datos
   vigentes y no sobrescribe el primer cambio.
3. **Given** un alta guardada cuya respuesta se perdió, **When** se comprueba su resultado o se
   repite la misma intención, **Then** se obtiene el resultado sin duplicar la ubicación.
4. **Given** una desactivación y una recepción simultáneas sobre la misma ubicación vacía,
   **When** ambas se confirman, **Then** o la recepción entra antes y la desactivación se
   rechaza por tener existencias, o la desactivación entra antes y la recepción se rechaza;
   nunca queda mercancía en una ubicación inactiva.

### Edge Cases

- Los códigos se comparan sin distinguir mayúsculas y sin espacios extremos, también frente a
  ubicaciones inactivas; un código inactivo no se reutiliza para otra ubicación.
- Pasillo, estantería y nivel se normalizan del mismo modo; dos ubicaciones no pueden designar
  la misma combinación física, aunque una esté inactiva.
- Una zona temporal de recepción exige también pasillo, estantería y nivel: no existen
  existencias con ubicación desconocida ni ubicaciones genéricas sin descripción física.
- Una ubicación con existencias a cero pero reservas pendientes de liberar no está vacía a
  efectos de desactivación.
- Una ubicación con historial pero vacía puede desactivarse; no puede cambiar su
  identificación física, para que los movimientos antiguos sigan señalando el mismo lugar.
  Ninguna ubicación puede borrarse, tampoco una creada por error sin movimientos.
- Retirar la última zona de recepción activa (desactivándola o cambiando su tipo) está
  permitido, pero se advierte antes de confirmar; las recepciones pueden seguir usando
  ubicaciones de almacenamiento activas.
- Una ubicación inactiva sigue apareciendo en consultas por artículo e historial; las listas
  para elegir destino u origen de nuevas operaciones no la ofrecen.
- Una consulta de artículo con muchas ubicaciones muestra el total de todo el artículo,
  no solo de la página visible.
- Repetir una intención con contenido distinto se rechaza como conflicto; un tiempo de espera
  agotado se muestra como resultado pendiente de comprobar, no como éxito ni fallo definitivo.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: El sistema DEBE permitir a usuarios autorizados crear, consultar y modificar
  ubicaciones con código, pasillo, estantería, nivel, tipo (almacenamiento o zona temporal
  de recepción) y estado activo/inactivo, conservando una identidad estable. Todas las
  ubicaciones pertenecen al único almacén; no se registran almacén, zona ni posición.
- **FR-002**: El código DEBE ser obligatorio y único entre todas las ubicaciones, incluidas
  las inactivas, sin distinguir mayúsculas ni espacios extremos.
- **FR-003**: Pasillo, estantería y nivel DEBEN ser obligatorios para todo tipo de ubicación,
  y su combinación, normalizada sin distinguir mayúsculas ni espacios extremos, DEBE ser única
  entre todas las ubicaciones, incluidas las inactivas (confirmada por B; pendiente de
  validación de A en el PR por afectar al modelo común).
- **FR-004**: Una ubicación nueva DEBE crearse activa y sin existencias; crearla NO DEBE
  generar movimientos ni cantidades.
- **FR-005**: Los usuarios con permiso de consulta de inventario DEBEN poder listar y buscar
  ubicaciones por código o pasillo, filtrar por tipo y estado, y ver sus datos con el estado
  y el tipo expresados con texto.
- **FR-006**: Un artículo DEBE poder estar almacenado en cualquier número de ubicaciones.
  El sistema DEBE mantener la cantidad de cada artículo por ubicación y, cuando el artículo
  se gestione por lote, por lote dentro de la ubicación.
- **FR-007**: El sistema DEBE permitir consultar, para un artículo, todas las ubicaciones en
  las que tiene existencias, con cantidad y unidad por ubicación y lote, y el total del
  artículo, incluso si el artículo o alguna ubicación están inactivos.
- **FR-008**: El sistema DEBE permitir consultar el contenido de una ubicación: artículos,
  lotes, cantidades y unidades, indicando con texto si hay reservas o un conteo abierto.
- **FR-009**: Las cantidades por ubicación DEBEN proceder exclusivamente de movimientos
  confirmados del inventario. Esta historia NO DEBE ofrecer la edición directa de cantidades.
- **FR-010**: Solo los usuarios con permiso de gestión de ubicaciones DEBEN poder crear,
  modificar, desactivar o reactivar ubicaciones.
- **FR-011**: El código, pasillo, estantería y nivel de una ubicación NO DEBEN modificarse
  desde que algún movimiento confirmado la referencie, aunque esté vacía o ese movimiento se
  haya revertido; el sistema DEBE explicar el motivo y la alternativa de crear otra ubicación.
  El tipo sigue la regla de FR-012.
- **FR-012**: El sistema DEBE impedir desactivar o cambiar el tipo de una ubicación con
  existencias, reservas pendientes o un conteo abierto, explicando la circunstancia.
- **FR-013**: Una ubicación inactiva NO DEBE aceptarse como destino u origen de nuevas
  recepciones, salidas o reservas, y DEBE seguir siendo consultable con su historial.
- **FR-014**: El sistema NO DEBE permitir eliminar ubicaciones, tengan o no historial; la
  retirada se realiza mediante desactivación reversible y los errores de alta se corrigen
  según FR-011 o se desactivan.
- **FR-015**: Cada alta, modificación y cambio de estado DEBE conservar evidencia de valores
  anteriores y posteriores, acción, usuario responsable autenticado y fecha y hora de registro,
  completándose junto con el cambio o no realizándose.
- **FR-016**: El cambio de estado de una ubicación DEBE coordinarse con los movimientos que la
  afectan, de modo que una operación simultánea no deje existencias en una ubicación inactiva.
- **FR-017**: Una edición basada en datos ya sustituidos por otro usuario DEBE rechazarse sin
  sobrescritura silenciosa, mostrando el conflicto y permitiendo consultar el estado vigente.
- **FR-018**: Repetir una misma intención de alta o cambio DEBE devolver su resultado sin
  duplicar ubicaciones ni evidencias; reutilizarla con otros datos DEBE rechazarse.
- **FR-019**: Los formularios y resultados DEBEN usar español y vocabulario del almacén,
  etiquetas explícitas, obligatoriedad visible, unidades junto a las cantidades y estados con
  texto. Los errores DEBEN explicar cómo corregirse, conservar los datos válidos y permitir
  completar la tarea mediante teclado.
- **FR-020**: La interfaz DEBE comunicar éxito solo cuando el cambio esté conservado y sea
  consultable, y permitir revisar la ubicación afectada y la consecuencia antes de confirmar
  una desactivación o reactivación.
- **FR-021**: Si desactivar una zona temporal de recepción o cambiar su tipo a almacenamiento
  deja el almacén sin ninguna zona de recepción activa, el sistema DEBE advertirlo con texto
  antes de confirmar; la operación se permite si el usuario la confirma y cumple FR-012.

### Alcance y dependencias

- Esta feature desarrolla verticalmente el mantenimiento de ubicaciones y las consultas de
  existencias por artículo y por ubicación. No incluye traslados entre ubicaciones,
  recepciones, salidas, reservas, ajustes ni la consulta de físico/reservado/disponible de HU-05.
  Los traslados no forman parte del reparto inicial; si se incorporan, seguirán el protocolo común.
- Los criterios originales quedan cubiertos así: código único con pasillo, estantería y nivel
  (US1); artículo en varias ubicaciones y cantidad por artículo y ubicación (US2, US3);
  consulta de todas las ubicaciones de un artículo (US2).
- La ubicación mínima y el núcleo de stock de H0 ya permiten registrar cantidades por
  artículo, ubicación y lote. HU-02 completa su mantenimiento y consultas sin redefinir el
  modelo común; la regla de combinación física única (FR-003), confirmada por B, se incorpora
  al modelo común solo tras su validación por A en el PR.
- **HU-08 (A)** aporta la identidad, unidad y estado del artículo. **HU-01 (A)** utiliza las
  ubicaciones activas como destino, incluidas las zonas temporales de recepción.
  **HU-03/HU-05 (B)** aportan reservas y la consulta de disponibilidad. **HU-04 (B)** aporta
  los conteos abiertos que impiden la desactivación.
- En H1 las existencias de prueba se preparan mediante las operaciones del núcleo de stock;
  la aceptación integrada de US2 con recepciones reales se completa en H3 (V-03). Las
  comprobaciones de reservas y conteos de FR-012 se completan cuando existan HU-03 y HU-04,
  registrando entretanto evidencia parcial sin darlas por demostradas.

### Principios constitucionales y evidencia de aceptación

| Principio | Aplicación a HU-02 | Evidencia exigida |
|-----------|-------------------|-------------------|
| I. Correspondencia almacén/sistema | Las cantidades por ubicación solo cambian por movimientos confirmados; mantener ubicaciones no las altera. | US2.5, US4.3 y FR-009; comparación de cantidades antes/después de editar o desactivar. |
| II. Ubicación física precisa | Código único, descripción física completa y cantidad por ubicación y lote; sin stock en ubicaciones desconocidas o inactivas. | US1.1–US1.4, US2.1–US2.2, US4.3 y US5.4; V-01 y V-03. |
| III. Uso intuitivo | Vocabulario del almacén, estados con texto, errores corregibles y revisión antes de retirar. | US1.2, US1.5, US4.7, FR-019–FR-021; recorrido con operarios representativos. |
| IV. Trazabilidad | La identificación física de ubicaciones con historial no cambia; la historia se conserva al desactivar. | US4.2, US4.4–US4.6, FR-011, FR-014 y FR-015. |
| V. Integridad y recuperación | Permisos, unicidad, cambios completos, conflictos, reintentos y coordinación con movimientos. | US5.1–US5.4 y Edge Cases; V-08 y V-12 según la guía global. |

La evidencia de implementación conservará fecha, entorno, escenario, resultado e incidencias.
Los responsables del proyecto y almacén revisarán los resultados conforme a la constitución.
Esta especificación define la aceptación; no declara realizadas pruebas ni revisiones conjuntas.

### Key Entities *(include if feature involves data)*

- **Ubicación**: lugar físico identificable por un código único y la combinación única de
  pasillo, estantería y nivel; tiene un tipo (almacenamiento o zona temporal de recepción) y
  un estado. Puede contener existencias de varios artículos y lotes.
- **Existencia por ubicación — referencia compartida**: cantidad de un artículo, en un lote
  cuando corresponda, almacenada en una ubicación. Se deriva de movimientos confirmados.
- **Registro de cambio de ubicación**: evidencia de una alta, modificación o cambio de estado,
  con valores anteriores/posteriores, responsable e instante.
- **Artículo y lote — referencias compartidas**: identidad, unidad y estado definidos en HU-08
  y lotes registrados por las operaciones de inventario.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un operario autorizado completa el 100 % de los recorridos de alta, consulta,
  corrección, desactivación y reactivación de ubicaciones con datos válidos, y los resultados
  siguen visibles al volver a abrir cada ubicación.
- **SC-002**: En el 100 % de los casos definidos de código o combinación física repetidos
  (también frente a ubicaciones inactivas), campos obligatorios vacíos y cambios de código,
  pasillo, estantería o nivel en ubicaciones con algún movimiento confirmado (incluidos los
  revertidos) se impide el guardado sin crear ni modificar registros, con indicación del dato
  que debe corregirse.
- **SC-003**: Para el 100 % de los artículos de prueba, la suma de las cantidades mostradas por
  ubicación y lote coincide con el total del artículo y con los movimientos confirmados.
- **SC-004**: En el 100 % de los intentos de desactivar ubicaciones con existencias, reservas o
  conteo abierto, y en los casos simultáneos de desactivación y movimiento, no queda ninguna
  existencia en una ubicación inactiva. Ninguna ubicación, con o sin historial, puede
  eliminarse, y el 100 % de las retiradas de la última zona de recepción activa muestran la
  advertencia antes de confirmarse.
- **SC-005**: Un trabajador localiza todas las ubicaciones de un artículo en menos de
  30 segundos desde la pantalla de inicio, y con 2.000 ubicaciones y 25 sesiones concurrentes
  al menos el 95 % de las consultas muestran resultados en 2 segundos o menos.
- **SC-006**: Al menos tres operarios representativos completan, cada uno, el alta de una
  ubicación, la corrección de un campo inválido, la localización de un artículo repartido y la
  desactivación de una ubicación vacía sin asistencia técnica y sin errores de registro no
  detectados. Se documentan los resultados y se corrigen las incidencias antes de aceptar.
- **SC-007**: En el 100 % de los casos de acceso no autorizado, edición simultánea, reintento y
  fallo de guardado no hay cambios indebidos, duplicados ni cambios sin responsable identificable.

## Assumptions

- Se reutilizan la sesión y los permisos de H0: la consulta usa el permiso de lectura de
  inventario y el mantenimiento, el de gestión de ubicaciones asignado a operarios.
- El almacén es único (confirmado en Clarifications): la descripción física se compone de
  pasillo, estantería y nivel, sin zonas, almacenes múltiples ni posiciones dentro del nivel,
  porque esa es la organización física existente a la que se refiere el principio II.
- El código lo introduce el usuario; la interfaz puede proponerlo a partir de pasillo,
  estantería y nivel (por ejemplo P01-E01-N01), pero no se impone un formato del sector.
- Las zonas temporales de devolución o cuarentena se representarían como ubicaciones con
  descripción física; no se añaden tipos adicionales mientras no exista su historia.
- Las búsquedas muestran por defecto ubicaciones activas y permiten incluir inactivas.
- Los objetivos de rendimiento y la muestra de operarios son criterios a medir, no resultados
  obtenidos. Respaldo, restauración y aprobación operativa siguen la guía global.
