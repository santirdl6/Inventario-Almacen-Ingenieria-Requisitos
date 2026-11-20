# Feature Specification: Reparto de las ocho specs de inventario

**Feature Branch**: Sin rama asociada; el proyecto no dispone de repositorio Git.

**Created**: 2026-10-25

**Status**: Draft

**Contexto compartido**: [constitución](../../.specify/memory/constitution.md) y
[plan global](../../.specify/memory/project-plan.md).

**Input**: User description: "Cada historia será una spec a desarrollar; somos dos personas
y queremos agrupar cuatro specs para cada una, reuniendo las que tengan más acoplamiento."
La narrativa y los criterios de las ocho historias proporcionados por el usuario son
la fuente del catálogo funcional de esta propuesta.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Conocer el reparto de responsabilidades (Priority: P1)

Como integrante del equipo, quiero conocer qué cuatro historias me corresponden y por qué
se agrupan, para trabajar con continuidad sobre un mismo ámbito del negocio.

**Why this priority**: El reparto completo y sin duplicidades es el resultado principal solicitado.

**Independent Test**: Revisar la tabla de asignación sin desarrollar el sistema y comprobar
que las ocho historias tienen exactamente un responsable, con cuatro por persona.

**Acceptance Scenarios**:

1. **Given** las ocho historias originales, **When** se consulta el reparto,
   **Then** la persona A tiene HU-01, HU-06, HU-07 y HU-08, y la persona B tiene
   HU-02, HU-03, HU-04 y HU-05, sin omisiones ni duplicidades.
2. **Given** cualquiera de los dos grupos, **When** se revisa su justificación,
   **Then** se identifican las reglas y datos de negocio que comparten sus historias.
3. **Given** una historia del catálogo, **When** se prepara su futura spec,
   **Then** conserva su identificador, objetivo y todos los criterios de aceptación originales.
4. **Given** una futura spec preparada para revisión, **When** se contrasta con la constitución,
   **Then** identifica los cinco principios, explica su aplicación y aporta criterios verificables
   para las obligaciones que afectan a esa historia.

---

### User Story 2 - Identificar los acuerdos entre ambos ámbitos (Priority: P2)

Como integrante del equipo, quiero conocer las dependencias entre mis historias y las de mi
compañero, para acordar las reglas comunes antes de desarrollar comportamientos incompatibles.

**Why this priority**: Las recepciones y las alertas dependen de las existencias, aunque estén
en otro grupo; ignorarlo trasladaría el conflicto a la integración.

**Independent Test**: Recorrer la tabla de dependencias y comprobar que cada intercambio
identifica ambas historias, la información compartida y quién propone la regla común.

**Acceptance Scenarios**:

1. **Given** una recepción parcial, **When** ambos integrantes revisan el acuerdo de integración,
   **Then** pueden identificar quién mantiene el pendiente del pedido y quién define las reglas
   comunes para incorporar las unidades a sus ubicaciones y existencias.
2. **Given** un artículo con reservas, **When** se revisan consultas, salidas y alertas,
   **Then** las tres utilizan las mismas definiciones de stock físico, reservado y disponible.
3. **Given** un artículo desactivado con movimientos, **When** se revisa la dependencia de datos
   maestros, **Then** se conserva su consulta histórica en ambos ámbitos sin borrar movimientos.

---

### User Story 3 - Preparar trabajo paralelo y una revisión conjunta (Priority: P3)

Como equipo de dos personas, queremos una secuencia de trabajo con puntos de encuentro,
para avanzar en paralelo y comprobar los procesos que atraviesan ambos grupos.

**Why this priority**: La secuencia permite convertir el reparto en trabajo coordinado
sin confundir cuatro historias por persona con una estimación de esfuerzo equivalente.

**Independent Test**: Seguir las etapas propuestas y verificar que ambas personas tienen
una primera historia abordable tras los acuerdos iniciales, y que cada dependencia cruzada
dispone de un escenario de revisión.

**Acceptance Scenarios**:

1. **Given** los acuerdos iniciales, **When** comienza la primera etapa,
   **Then** A puede trabajar en HU-08 y B en HU-02 usando los ejemplos de artículos acordados.
2. **Given** HU-07 y las reglas comunes de existencias, **When** se aborda HU-01,
   **Then** la recepción actualiza el pedido y las existencias sin contabilizaciones duplicadas.
3. **Given** las ocho historias detalladas, **When** se revisa la carga de trabajo,
   **Then** se contrastan sus estimaciones y se ajusta la secuencia si es necesario,
   manteniendo cuatro historias por persona salvo acuerdo expreso del equipo.

---

### User Story 4 - Arrancar un entorno compartido reproducible (Priority: P1)

Como integrante del equipo, quiero levantar la aplicación y sus dependencias con un único
comando documentado, para trabajar sobre la misma base sin instalar SDKs ni preparar
manualmente la base de datos en cada equipo.

**Why this priority**: El entorno reproducible es un requisito transversal del hito H0,
necesario para integrar el trabajo de ambas personas. No añade una novena historia de negocio.

**Independent Test**: Desde una copia limpia, con el motor de contenedores iniciado,
ejecutar el comando del README y comprobar la página de estado y la conexión con la base.

**Acceptance Scenarios**:

1. **Given** un equipo con el motor de contenedores disponible y sin SDKs del proyecto,
   **When** ejecuta el único comando de arranque, **Then** se construyen e inician
   la aplicación y sus dependencias sin copiar configuración ni crear la base a mano.
2. **Given** el entorno iniciado con un dato de prueba persistido, **When** se detienen
   y recrean sus contenedores, **Then** el dato sigue disponible y la inicialización
   se puede repetir sin borrar ni duplicar información.
3. **Given** una configuración de puerto distinta mediante variables de entorno,
   **When** se inicia el entorno, **Then** la aplicación usa el puerto indicado
   sin editar el código ni recompilar el frontend por ese cambio.
4. **Given** una dependencia o inicialización fallida, **When** se consulta el estado,
   **Then** se identifica el error y la aplicación no comunica disponibilidad falsa.

### Edge Cases

- **Dependencia transversal de datos maestros**: ambas personas utilizan artículos, pero A
  mantiene su definición; B conserva las referencias y el historial en sus movimientos.
- **Recepción sin ubicación definitiva**: debe utilizar una ubicación temporal identificada;
  no se admite stock sin localizar. El acuerdo entre HU-01 y HU-02 debe recogerlo.
- **Reserva del propio pedido frente a reservas ajenas**: HU-03 y HU-05 deben distinguirlas.
  Una salida no puede consumir unidades comprometidas para otros pedidos ni descontar dos
  veces la reserva del pedido atendido.
- **Conteo con movimientos simultáneos**: HU-04 debe definir cómo evitar que un ajuste
  sobrescriba movimientos posteriores al conteo antes de iniciar su desarrollo.
- **Stock físico suficiente pero disponible insuficiente**: HU-06 debe usar el disponible
  para evaluar el mínimo, de forma coherente con HU-03 y HU-05.
- **Recepción con exceso o sustitución de artículos**: HU-01 deberá concretar su aceptación
  o rechazo y sus efectos en HU-07; esta propuesta no inventa esa política comercial.
- **Diferencia de esfuerzo entre grupos**: el reparto equilibra el número de historias y
  favorece la cohesión; las estimaciones posteriores pueden mostrar cargas distintas.
- **Dependencias aún no disponibles**: el entorno espera a la base y a su inicialización;
  un fallo no debe presentarse como un arranque correcto de la aplicación.
- **Recreación del entorno**: reconstruir imágenes o recrear contenedores conserva los
  datos persistidos; el borrado requiere una acción explícita y documentada.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: La propuesta DEBE asignar las ocho historias originales a dos personas,
  exactamente cuatro a cada una y con un único responsable principal por historia.
- **FR-002**: La propuesta DEBE mantener las ocho historias como futuras specs independientes;
  este documento coordina su desarrollo y no las sustituye ni fusiona funcionalmente.
- **FR-003**: El catálogo DEBE conservar los objetivos y todos los criterios de aceptación
  aportados para cada historia, sin ampliar silenciosamente su alcance.
- **FR-004**: Cada grupo DEBE tener una justificación basada en dependencias, reglas y datos
  compartidos; la igualdad de esfuerzo NO DEBE presentarse como demostrada sin estimaciones.
- **FR-005**: Cada dependencia cruzada identificada DEBE indicar historias afectadas,
  acuerdo necesario y responsable de proponerlo, con revisión por ambas personas.
- **FR-006**: Las definiciones comunes DEBEN distinguir stock físico, reservado y disponible,
  incluyendo el tratamiento de reservas del pedido atendido y de los demás pedidos.
- **FR-007**: La secuencia DEBE permitir comenzar HU-08 y HU-02 en paralelo después de acordar
  los conceptos compartidos, sin exigir que un grupo termine sus cuatro historias primero.
- **FR-008**: Cada integración cruzada DEBE disponer de un resultado de negocio comprobable.
- **FR-009**: La preparación de cada futura spec DEBE revisar los cinco principios de la
  constitución y concretar sus criterios aplicables de registro, ubicación, usabilidad,
  trazabilidad e integridad. La asignación de responsables no permite eludirlos.
- **FR-010**: Las reglas no detalladas de reservas, pedidos de cliente, lotes, diferencias de
  recepción, concurrencia en conteos y sugerencia de compra DEBEN identificarse como decisiones
  de sus futuras specs y resolverse antes de desarrollar el comportamiento afectado.
- **FR-011**: El proyecto DEBE disponer de un entorno contenedorizado que construya
  y levante aplicación, base de datos y correo de desarrollo con un único comando documentado,
  sin preparación manual de base de datos ni instalación local de SDKs.
- **FR-012**: Los datos de la base, las claves persistentes de la aplicación y el correo
  capturado DEBEN conservarse al detener o recrear contenedores mediante volúmenes persistentes.
- **FR-013**: Puertos y credenciales DEBEN poder configurarse mediante variables de entorno;
  el arranque de desarrollo DEBE funcionar también con valores locales predeterminados.
- **FR-014**: El arranque DEBE comprobar la disponibilidad de la base y aplicar su preparación
  inicial de forma repetible antes de iniciar la app; una inicialización fallida DEBE impedir
  que el entorno se presente como listo.
- **FR-015**: El README DEBE documentar arranque, parada, reconstrucción, consulta de estado,
  configuración, validación de persistencia y borrado explícito de los datos locales.

### Catálogo y asignación de las ocho futuras specs

Los identificadores HU-01 a HU-08 corresponden a la numeración original del usuario;
no son números de directorio ni implican que ya existan ocho archivos de especificación.

| Historia | Futura spec | Responsable | Alcance y criterios que debe preservar |
|----------|-------------|-------------|---------------------------------------|
| HU-01 | Registrar la recepción de mercancía | A | Vinculación a pedido de proveedor; cantidades recibidas por artículo; recepciones parciales y pendientes identificados; diferencias registradas; actualización del stock con lo realmente recibido. |
| HU-02 | Gestionar las ubicaciones de los artículos | B | Código único con pasillo, estantería y nivel; artículo en varias ubicaciones; cantidad por artículo y ubicación; consulta de todas las ubicaciones de un artículo. |
| HU-03 | Registrar la salida de mercancía | B | Vinculación a pedido de cliente; artículos, cantidades y ubicaciones de origen; bloqueo de salidas superiores a lo disponible considerando reservas ajenas; descuento al confirmar; movimiento trazable. |
| HU-04 | Realizar inventarios cíclicos y ajustar diferencias | B | Selección de una parte del almacén; cantidades contadas comparadas con las teóricas; diferencias visibles; motivo e identidad de quien ajusta; actualización al confirmar conservando el registro. |
| HU-05 | Consultar existencias y reservas | B | Búsqueda por artículo y filtros por ubicación o lote; distinción entre físico, reservado y disponible; disponible igual a físico menos reservado; resultados que reflejen movimientos y ajustes confirmados. Soporte mínimo acordado: alta/consulta de pedido de cliente y reservar/liberar; HU-03 consume ese pedido para las salidas. |
| HU-06 | Configurar alertas de stock mínimo | A | Mínimo configurable por artículo; alerta automática al caer el disponible por debajo del mínimo; identificación del artículo, stock y mínimo; cantidad sugerida de compra. |
| HU-07 | Crear, enviar y seguir pedidos a proveedores | A | Borrador asociado a proveedor; líneas con artículos, cantidades, precios y fecha prevista; envío al proveedor; consulta de estado; estados borrador, enviado, recibido parcialmente y recibido; pendientes y estado actualizados por las recepciones. |
| HU-08 | Mantener los datos maestros del almacén | A | Crear, consultar y modificar artículos, familias y proveedores; asignar familia; impedir eliminación de artículos con movimientos; desactivar artículos con historial conservando movimientos y consulta de trazabilidad. |

### Justificación del agrupamiento

La numeración de directorios y los nombres completos confirmados se registran en
[docs/agrupamiento.md](../../docs/agrupamiento.md): B usa 004–006 para HU-05/HU-03/HU-04
y A 007–009 para HU-07/HU-01/HU-06, conservando 002/HU-08 y 003/HU-02. No se cambia
el identificador ni los criterios originales de ninguna historia. El pedido mínimo
se adelanta a HU-05 por su dependencia con reservas; HU-03 mantiene las salidas.

**Persona A — Abastecimiento y datos maestros: HU-08, HU-07, HU-01 y HU-06.**

- HU-08 proporciona los artículos y proveedores utilizados por HU-07 y HU-01.
- HU-07 y HU-01 comparten el pedido, sus líneas, cantidades pendientes y estados.
  Mantener ambas juntas evita repartir entre personas la recepción parcial y su seguimiento.
- HU-06 completa el ciclo de reposición: necesidad detectada, sugerencia de compra,
  pedido y recepción. La alerta orienta al comprador; no crea ni envía pedidos automáticamente.

**Persona B — Operación y disponibilidad: HU-02, HU-05, HU-03 y HU-04.**

- HU-02 y HU-05 comparten cantidades por artículo, ubicación y, cuando corresponda, lote.
- HU-03 y HU-05 comparten las reservas y el cálculo de disponibilidad para evitar sobreventas.
- HU-04 utiliza las mismas existencias teóricas y ubicaciones, y sus ajustes se reflejan
  inmediatamente en las consultas de HU-05.

Este reparto concentra las reglas de existencias, salidas y ajustes en B, y las de compra,
pendientes y recepción en A. Es una propuesta cualitativa de cohesión; no afirma un mínimo
matemático de dependencias cruzadas ni una carga de trabajo idéntica.

### Dependencias y acuerdos entre personas

La persona indicada propone la definición del concepto compartido; ambas la revisan.
Cada responsable conserva el desarrollo y la aceptación de sus propias historias.

| Relación | Acuerdo de negocio necesario | Propone | Comprobación conjunta |
|----------|-----------------------------|---------|-----------------------|
| HU-08 → HU-02, HU-03, HU-04, HU-05 | Identidad de artículo, unidad de medida, estado activo e historial conservado. | A | Un artículo desactivado sigue apareciendo en consultas históricas sin borrar movimientos. |
| HU-01 ↔ HU-02 | Destino de cada cantidad recibida, con ubicación definitiva o temporal identificada. | B para ubicaciones; A para captura de recepción | Una recepción distribuida entre dos ubicaciones conserva la suma recibida y permite localizar todas las unidades. |
| HU-01 → HU-05 | Efecto único de una recepción confirmada sobre físico y disponible, conservando lote cuando aplique. | B para reglas de existencias; A para confirmación de recepción | Recibir 6 unidades de un pedido de 10 incrementa físico en 6 y deja 4 pendientes; repetir la confirmación no duplica las unidades. |
| HU-05 → HU-06 | Disponible utilizado para el umbral, momento de evaluación y actualización tras movimientos confirmados. | B para disponible; A para política de alerta | Con físico 12, reservado 8 y mínimo 5, el disponible 4 genera una alerta aunque el físico supere el mínimo. |
| HU-08 ↔ todas | Datos históricos consultables y tratamiento de un artículo desactivado en operaciones pendientes. | A | Se rechaza eliminar un artículo con movimientos; desactivarlo conserva las referencias y permite consultar su historial. Las operaciones pendientes se contrastan con la política acordada antes de desarrollarlas. |

**Acuerdos internos relevantes**: A resuelve HU-07 ↔ HU-01 sobre estados y pendientes;
B resuelve HU-02 ↔ HU-05 sobre distribución física, HU-05 ↔ HU-03 sobre reservas y
HU-05 ↔ HU-04 sobre diferencias y saldos. No requieren repartir esas reglas entre personas.

### Secuencia propuesta

| Etapa | Persona A | Persona B | Punto de encuentro |
|-------|-----------|-----------|--------------------|
| 0. Acuerdos | Artículos, proveedores, recepción y estados de compra. | Ubicaciones, movimientos, reservas, lotes y disponibilidad. | Glosario y ejemplos compartidos; fronteras y decisiones pendientes registradas. |
| 1. Fundamentos | HU-08: datos maestros. | HU-02: ubicaciones, con ejemplos de artículos acordados. | Verificar identificación y cantidades por ubicación. |
| 2. Flujos base | HU-07: pedidos a proveedores. | HU-05: existencias y reservas, con ejemplos de movimientos y reservas. | Revisar estados de compra y definiciones de stock; concretar el origen y ciclo de las reservas. |
| 3. Movimientos | HU-01: recepciones. | HU-03: salidas. | Recibir, localizar, consultar y despachar sin duplicar unidades ni consumir reservas ajenas. |
| 4. Control | HU-06: alertas y reposición sugerida. | HU-04: conteos y ajustes. | Verificar que los ajustes actualizan consultas y evaluación del mínimo. |

Las etapas expresan precedencias de negocio, no duraciones ni fechas comprometidas.
HU-05 puede comprobarse inicialmente con casos de stock y reservas preparados para revisión;
su aceptación integrada requiere contrastarla después con movimientos y ajustes reales.
Antes de fijar plazos, ambos integrantes estimarán sus historias y revisarán la carga total.

### Key Entities *(include if feature involves data)*

- **Historia de origen**: identificador HU-01 a HU-08, actor, objetivo y criterios originales.
- **Futura spec**: desarrollo detallado de una única historia, con responsable principal,
  dependencias, decisiones y evidencia de aceptación; todavía no creada por esta propuesta.
- **Asignación**: relación entre una persona, sus cuatro historias y su ámbito de negocio.
- **Acuerdo compartido**: regla utilizada por ambos ámbitos, con proponente, participantes,
  historias afectadas y ejemplo de resultado esperado.
- **Etapa de trabajo**: conjunto de historias abordables en paralelo y su revisión conjunta.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: El 100 % de las ocho historias tiene un único responsable principal;
  cada persona tiene exactamente cuatro, sin historias omitidas ni duplicadas.
- **SC-002**: El 100 % de los criterios originales puede localizarse en el catálogo y
  trasladarse a su futura spec sin pérdida de alcance.
- **SC-003**: Las cinco relaciones cruzadas documentadas tienen información compartida,
  proponente y comprobación conjunta explícitos.
- **SC-004**: Tras leer la propuesta, ambos integrantes pueden identificar sin ayuda externa
  sus cuatro historias, su primera historia abordable y los acuerdos que necesitan del otro.
- **SC-005**: El plan ofrece una primera historia por persona abordable en paralelo tras
  los acuerdos iniciales, y cubre las ocho historias en las cuatro etapas posteriores.
- **SC-006**: Antes de comenzar el desarrollo de una historia con dependencia cruzada,
  ambas personas han revisado sus acuerdos aplicables y los ejemplos de aceptación.
- **SC-007**: En un entorno limpio con los requisitos de contenedores disponibles,
  un único comando produce una página accesible y una comprobación de base correcta,
  con cero pasos manuales de inicialización o configuración obligatoria.
- **SC-008**: El 100 % de los datos de prueba de persistencia sobrevive a una recreación
  de los contenedores sin eliminación de volúmenes.
- **SC-009**: Cambiar el puerto publicado mediante una variable permite acceder al mismo
  frontend y API, sin cambiar el código de aplicación.

## Assumptions

- La arquitectura y los acuerdos técnicos compartidos se mantienen en el
  [plan global](../../.specify/memory/project-plan.md); el [plan local](plan.md) recoge
  los hitos y la coordinación. Las futuras specs referencian esa base común.
- La contenerización forma parte de la base transversal H0. Su entrega incluye una
  aplicación técnica mínima de estado; las ocho funcionalidades se desarrollan por separado.
- «Persona A» y «Persona B» son nombres provisionales, sin suponer experiencia ni preferencias.
- El entregable de esta invocación es la propuesta de coordinación. Las ocho specs funcionales
  se redactarán individualmente en invocaciones posteriores, conservando este catálogo.
- Se prioriza agrupar reglas relacionadas y asignar cuatro historias por persona; no se dispone
  de estimaciones que permitan certificar igualdad de esfuerzo.
- Los pedidos de cliente y las reservas son conceptos necesarios para HU-03 y HU-05,
  pero su creación, modificación, liberación y origen no están descritos. B deberá delimitar
  ese comportamiento compartido al especificarlas, sin asumir un módulo comercial completo.
- El seguimiento por lote aparece en HU-05. A y B deben acordar su captura en recepciones
  y su conservación en movimientos cuando proceda; no se supone que ya esté implementado.
- HU-06 utilizará el stock disponible para evaluar el mínimo. La fórmula de cantidad sugerida,
  el tratamiento de compras pendientes, la repetición de alertas y el canal de aviso se
  concretarán en su spec; no es necesario decidirlos para asignar responsables.
- La decisión sobre excesos de recepción, operaciones pendientes con artículos desactivados
  y movimientos durante conteos pertenece a sus futuras specs y sus acuerdos compartidos.
- Las cinco relaciones cruzadas agrupan familias de dependencias; no representan un inventario
  exhaustivo de todas las interacciones posibles del sistema.
- La constitución vigente es una dependencia obligatoria para las ocho futuras specs.
  No se incorporan historias adicionales de devoluciones, traslados o administración de
  seguridad al reparto, aunque los principios constitucionales sigan siendo exigibles.
