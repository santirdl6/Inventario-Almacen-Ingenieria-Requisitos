# Feature Specification: HU-04 — Realizar inventarios cíclicos y ajustar diferencias

**Feature Branch**: `b/hu-04-realizar-inventarios-ciclicos` (creada desde `b/hu-03-registrar-salida-mercancia`; el directorio de la feature es independiente de la rama Git).

**Created**: 2026-11-21

**Status**: Draft

**Input**: User description: "HU-04 «Realizar inventarios cíclicos» (Persona B). Directorio specs/006-realizar-inventarios-ciclicos. Debe cubrir lo que otras historias dejaron a HU-04: (a) CountScopeFrozen: bloquear reservar, liberar y confirmar salidas sobre un ámbito con un conteo abierto (hoy IsInOpenCountAsync devuelve false; INT-HU04-03); (b) la corrección de una salida confirmada errónea mediante el ajuste (Q3 y P-07 de HU-03), dejando claro que el ajuste corrige el físico pero no lo servido ni el consumo de reservas. Reutiliza el modelo común: idempotencia, If-Match/ETag, bloqueos de InventoryLocks, diario de solo adición, contraparte con exactamente uno de CounterpartyReference o CounterpartyNotApplicableReason, permisos Counts.Record (Operario y ResponsableAlmacen) e Inventory.Adjust (ResponsableAlmacen). Alcance acotado: ciclo conteo → diferencias → ajuste y congelado del ámbito; lo accesorio fuera y marcado. Máximo 3 [NEEDS CLARIFICATION]."

**Historia de origen**: HU-04 del [catálogo y reparto](../001-reparto-specs-inventario/spec.md#catálogo-y-asignación-de-las-ocho-futuras-specs):
selección de una parte del almacén; cantidades contadas comparadas con las teóricas; diferencias
visibles; motivo e identidad de quien ajusta; actualización al confirmar conservando el registro.
**Responsable principal**: Persona B. **Hito**: H4 (control), según el
[plan de coordinación](../001-reparto-specs-inventario/plan.md#hitos-y-coordinación).
**Numeración**: 006, reservada por B en [docs/agrupamiento.md](../../docs/agrupamiento.md).

**Contexto normativo y acuerdos consumidos**:

- [Constitución vigente](../../.specify/memory/constitution.md): principios I–V, en especial I, IV y V, y los controles operativos de conteos y ajustes.
- [Plan global](../../.specify/memory/project-plan.md): arquitectura compartida y módulo de conteos de B.
- [Decisiones comunes](../../.specify/memory/project/research.md#r-05-reglas-comunes-de-negocio-para-no-bloquear-las-specs): conteo con congelación persistente, desactivación y bloqueos.
- [Modelo común](../../.specify/memory/project/data-model.md#conteos--persona-b--hu-04): conteo, ubicaciones del conteo, líneas, estados y reglas del ajuste.
- [Contrato HTTP común](../../.specify/memory/project/contracts/api.md#hu-04--b-conteos-y-ajustes): rutas de conteo, permisos y errores, incluido `CountScopeFrozen`.
- [Contrato de stock](../../.specify/memory/project/contracts/stock-operations.md#conteo-sin-transacción-humana-abierta): conteo sin transacción humana abierta, bloqueos e idempotencia.
- [Contrato de interacción](../../.specify/memory/project/contracts/ui.md): pantalla /inventarios e información que no debe ocultarse.
- [Guía de validación](../../.specify/memory/project/quickstart.md#6-escenarios-de-aceptación-integrados): V-09 y V-10, más V-07, V-08 y V-12 por los comandos comunes.
- Entregado por HU-05 y HU-03: [contrato HU-05 → HU-03](../004-consultar-existencias/contracts/hu03-integration.md),
  [spec de HU-03](../005-registrar-salida-mercancia/spec.md#alcance-y-dependencias) y las convergencias de
  [HU-05](../004-consultar-existencias/validation/converge-1.md) y
  [HU-03](../005-registrar-salida-mercancia/validation/converge-1.md).
- Backlog receptor: [INT-HU04-01, INT-HU04-02 e INT-HU04-03](../001-reparto-specs-inventario/integration-tasks.md#hu-04--conteos-y-ajustes--persona-b).

Los enlaces anteriores son referencias normativas; esta especificación describe necesidades
y resultados de negocio. Las decisiones de implementación corresponden al futuro plan de HU-04.

## Clarifications

### Session 2026-11-21

- Q: ¿Se bloquea liberar reservas sobre una ubicación en conteo, o sigue permitido? → A: Sigue permitido, con permiso de gestión de reservas y motivo, como fija el modelo común: es la vía para resolver un contado inferior al reservado (US2.4, V-10). Se bloquean reservar, confirmar salidas, recibir, desactivar y cambiar el tipo. Bloquear también la liberación sería un cambio compartido (AGENTS.md) y dejaría la cancelación como única salida de ese caso (US3.3, FR-015). Decisión por recomendación, confirmada por B el 2026-11-21 (`/speckit-analyze`, H5).
- Q: ¿La corrección de una salida confirmada errónea se hace contando las ubicaciones afectadas o con un ajuste directo sin conteo? → A: Contando: se crea un conteo sobre las ubicaciones de origen afectadas y, antes de confirmarlo, cada línea con diferencia puede vincularse a la salida confirmada que corrige. El ajuste es el del conteo; no hay un segundo escritor de ajustes ni una corrección directa (US4.1, FR-018). Decisión por recomendación, confirmada por B el 2026-11-21 (`/speckit-analyze`, H5).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Contar una parte del almacén y ver las diferencias (Priority: P1)

Como responsable o operario del almacén, quiero elegir unas ubicaciones, iniciar su conteo,
registrar lo que hay físicamente en cada una y ver la diferencia con lo que dice el sistema, para
detectar descuadres sin parar todo el almacén.

**Why this priority**: Es el objetivo de HU-04 y el control que exige la constitución: cada conteo
compara cantidades físicas y registradas por artículo y ubicación. Sin él no hay diferencias que
ajustar.

**Independent Test**: Con físico 10 del artículo A en la ubicación U1 y 5 del artículo B en U1,
crear un conteo sobre U1, iniciarlo, registrar 8 para A y 5 para B y enviarlo a aprobación; el
conteo muestra para A teórico 10, contado 8 y diferencia −2, y para B diferencia 0, sin que las
existencias hayan cambiado.

**Acceptance Scenarios**:

1. **Given** un usuario con permiso de registro de conteos, **When** crea un conteo eligiendo una o
   varias ubicaciones activas, **Then** el conteo queda en borrador, con su alcance visible, sin
   congelar nada ni cambiar existencias.
2. **Given** un conteo en borrador, **When** lo inicia, **Then** todas sus ubicaciones quedan
   congeladas, se captura como teórico el físico de cada artículo y lote presente en ellas, y el
   conteo pasa a «en conteo».
3. **Given** un conteo en curso, **When** el usuario registra la cantidad contada (cero o más, en la
   unidad del artículo) de cada artículo y lote esperado, **Then** cada línea muestra teórico,
   contado y diferencia, y queda registrado quién contó y cuándo.
4. **Given** un conteo en curso con alguna línea esperada sin contar, **When** se intenta enviar a
   aprobación, **Then** se rechaza señalando las líneas pendientes; una línea vacía no equivale a
   cero: el cero se indica expresamente.
5. **Given** todas las líneas contadas, **When** se envía a aprobación, **Then** el conteo pasa a
   «pendiente de aprobación», las ubicaciones siguen congeladas y las existencias no cambian.
6. **Given** una ubicación que ya está en otro conteo no terminado, **When** se intenta iniciar un
   conteo que la incluye, **Then** se rechaza nombrando la ubicación y el conteo que la ocupa.

---

### User Story 2 - Autorizar el ajuste y dejar las existencias como el físico (Priority: P1)

Como responsable del almacén, quiero revisar las diferencias de un conteo, indicar el motivo de
cada una y autorizar el ajuste, para que el sistema refleje lo que hay realmente y quede constancia
de quién lo decidió y por qué.

**Why this priority**: Cierra el ciclo conteo → diferencias → ajuste; sin él las diferencias
quedan documentadas pero no resueltas (principio I).

**Independent Test**: Con el conteo de US1 pendiente de aprobación, el responsable indica el motivo
de la diferencia de A y confirma; la consulta de existencias muestra físico 8 para A en U1, el
historial muestra un ajuste de −2 con motivo, quien contó y quien autorizó, y U1 deja de estar
congelada.

**Acceptance Scenarios**:

1. **Given** un conteo pendiente de aprobación, **When** el responsable lo revisa, **Then** ve por
   línea ubicación, artículo, unidad, lote, teórico, contado, diferencia, reservado, quién contó y
   cuándo, y el total de líneas con diferencia.
2. **Given** líneas con diferencia distinta de cero, **When** confirma sin motivo en alguna,
   **Then** se rechaza señalando esas líneas y no cambia nada.
3. **Given** todas las diferencias con motivo, **When** el responsable confirma, **Then** en una sola
   operación se añade un ajuste por cada línea con diferencia (al alza o a la baja, por la cantidad
   de la diferencia), el físico de cada bucket queda igual al contado, se registra la aprobación y
   se liberan todas las ubicaciones del conteo.
4. **Given** un conteo en el que el contado de un bucket queda por debajo de su reservado, **When**
   se intenta confirmar, **Then** se rechaza explicando el bucket, el contado y el reservado; el
   conteo y la congelación se conservan hasta resolver la reserva.
5. **Given** un conteo sin ninguna diferencia, **When** se confirma, **Then** no se crea ningún
   ajuste, se registra la aprobación y se libera el alcance.
6. **Given** un conteo en borrador, en curso o pendiente de aprobación, **When** el responsable lo
   cancela indicando el motivo, **Then** se conservan el conteo y lo contado como evidencia, no
   cambia ninguna existencia y se libera el alcance.
7. **Given** un operario sin permiso de ajuste, **When** intenta confirmar o cancelar, **Then** se
   rechaza sin cambios.

---

### User Story 3 - Congelar el ámbito mientras se cuenta (Priority: P1)

Como responsable del almacén, quiero que, mientras unas ubicaciones se cuentan, nadie registre en
ellas movimientos que cambien lo contado, para que el ajuste no sobrescriba hechos ocurridos durante
el conteo.

**Why this priority**: Es la condición para que el ajuste sea correcto (caso «conteo con movimientos
simultáneos» del reparto) y la deuda que dejaron HU-02, HU-05 y HU-03 (INT-HU04-03): hasta ahora el
conteo abierto se considera siempre inexistente.

**Independent Test**: Con U1 en un conteo en curso, intentar confirmar una salida desde U1, reservar
existencias de U1, desactivar U1 y cambiar su tipo; todo se rechaza con el motivo «ubicación en
conteo» y la consulta de ubicaciones muestra U1 como en conteo. Tras confirmar o cancelar el
conteo, las mismas operaciones vuelven a admitirse.

**Acceptance Scenarios**:

1. **Given** una ubicación en un conteo en curso o pendiente de aprobación, **When** se intenta
   confirmar una salida con algún origen en ella, **Then** se rechaza indicando la ubicación y el
   conteo, sin cambios en la salida, el pedido, las reservas ni las existencias.
2. **Given** la misma situación, **When** se intenta reservar existencias en esa ubicación,
   **Then** se rechaza del mismo modo.
3. **Given** la misma situación, **When** se intenta liberar una reserva con asignaciones en esa
   ubicación, **Then** se admite con el permiso de gestión de reservas y motivo, como en el modelo
   común: el reservado baja y el físico no cambia, de modo que el conteo pueda confirmarse si el
   contado quedaba por debajo del reservado (US2.4).
4. **Given** la misma situación, **When** se intenta desactivar la ubicación o cambiar su tipo,
   **Then** se rechaza con el motivo «ubicación en conteo», y las consultas de ubicaciones la
   muestran como en conteo.
5. **Given** un conteo en borrador, **When** se opera sobre sus ubicaciones, **Then** no hay
   ninguna restricción: el borrador no congela.
6. **Given** un conteo que se inicia a la vez que una salida, reserva o cambio de ubicación sobre la
   misma ubicación, **When** ambos compiten, **Then** uno de los dos ocurre primero por completo: o
   el teórico ya incluye el movimiento, o el movimiento se rechaza por la congelación; nunca se
   pierde ni se duplica.
7. **Given** un conteo confirmado o cancelado, **When** se repiten las operaciones anteriores,
   **Then** se admiten con sus reglas habituales.

---

### User Story 4 - Corregir una salida confirmada errónea mediante el ajuste (Priority: P2)

Como responsable del almacén, cuando una salida se confirmó con una cantidad o un origen
equivocados, quiero corregir el físico de las ubicaciones afectadas con un ajuste vinculado a esa
salida, para que el inventario vuelva a coincidir con la realidad sin alterar el historial.

**Why this priority**: HU-03 no revierte salidas confirmadas y deja esta corrección a HU-04
(clarificación Q3 y P-07). Es necesaria, pero menos frecuente que el ciclo de conteo.

**Independent Test**: Una salida confirmada registró 6 unidades desde U1, pero solo salieron 4. Se
cuenta U1, se registra el contado real, el responsable vincula la diferencia de +2 a esa salida y
confirma con motivo; el físico de U1 sube en 2, el historial muestra el ajuste con la referencia a la
salida, y la salida, lo servido del pedido (6) y el consumo de reservas no cambian.

**Acceptance Scenarios**:

1. **Given** una salida confirmada errónea y un conteo pendiente de aprobación sobre sus ubicaciones
   de origen, **When** el responsable vincula a esa salida las líneas con diferencia que la corrigen
   y confirma el conteo, **Then** el ajuste de esas líneas queda vinculado a la salida; no existe otra
   vía de corrección sin conteo.
2. **Given** una corrección registrada, **When** se consulta el historial, **Then** el ajuste
   muestra su motivo, quién lo autorizó, cuándo y el código del conteo como soporte; el detalle del
   conteo cerrado muestra, en cada línea vinculada, la referencia a la salida original; la salida
   original y su movimiento siguen intactos.
3. **Given** una corrección registrada, **When** se consulta el pedido de cliente, **Then** lo
   servido, el pendiente, el estado del pedido y el consumo de las reservas son los mismos que antes
   de la corrección; la pantalla lo indica expresamente.
4. **Given** una salida en borrador o inexistente, o una línea de conteo cuyo artículo, ubicación y
   lote no coinciden con ningún origen de la salida, **When** se intenta vincularlas, **Then** se
   rechaza: solo se corrigen salidas confirmadas y en los buckets que tocaron.

---

### User Story 5 - Registrar hallazgos no previstos (Priority: P2)

Como operario que cuenta, quiero anotar artículos o lotes que encuentro en una ubicación aunque el
sistema no los esperase allí, para que el ajuste los incorpore con ubicación y lote conocidos.

**Why this priority**: El modelo común lo exige (V-10) y evita existencias sin ubicación, pero el
ciclo básico funciona sin hallazgos.

**Independent Test**: En un conteo en curso de U1, sin existencias teóricas del artículo C, el
operario añade C con 3 unidades; al confirmar con motivo, U1 muestra físico 3 de C y el historial un
ajuste al alza de 3.

**Acceptance Scenarios**:

1. **Given** un conteo en curso, **When** el operario añade un hallazgo indicando ubicación del
   alcance, artículo existente, lote (si el artículo se gestiona por lote) y cantidad positiva,
   **Then** se añade una línea con teórico 0 y diferencia igual a lo contado.
2. **Given** un hallazgo con una ubicación fuera del alcance, sin lote en un artículo con lotes o
   con un artículo inexistente, **When** se guarda, **Then** se rechaza indicando el dato a corregir.
3. **Given** un hallazgo añadido por error, **When** se retira antes de enviar a aprobación,
   **Then** desaparece del conteo; las líneas esperadas no se pueden retirar.
4. **Given** un artículo desactivado hallado o esperado en el alcance, **When** se ajusta,
   **Then** se admite con la autorización y el motivo del ajuste, y la consulta lo sigue mostrando
   como inactivo.

---

### User Story 6 - Operar con autorización y resultado fiable (Priority: P3)

Como responsable del almacén, quiero que solo las personas autorizadas registren conteos y ajustes,
que un reintento no duplique un ajuste y que, ante un fallo, pueda comprobar si quedó registrado,
para confiar en el resultado.

**Why this priority**: Son controles comunes que el resto de historias ya cumplen; HU-04 los
reutiliza sobre sus comandos.

**Independent Test**: Repetir la confirmación de un conteo con la misma intención devuelve el mismo
resultado sin un segundo ajuste; confirmar con una versión antigua del conteo se rechaza pidiendo
recargar; un usuario sin permiso de registro de conteos no puede crear ni iniciar conteos.

**Acceptance Scenarios**:

1. **Given** una sesión sin permiso de registro de conteos, **When** intenta crear, iniciar,
   registrar o enviar un conteo, **Then** se rechaza sin cambios; con permiso de consulta puede
   verlo.
2. **Given** una confirmación ya registrada, **When** se repite con la misma intención, **Then** se
   devuelve el resultado original sin nuevos ajustes; con la misma intención y otros datos se
   rechaza.
3. **Given** que otra persona cambió el conteo después de leerlo, **When** se envía una acción con
   la versión anterior, **Then** se rechaza pidiendo recargar sin perder lo escrito.
4. **Given** un fallo inducido durante la confirmación, **When** se comprueba el resultado, **Then**
   o bien todos los ajustes, la aprobación y la liberación del alcance se han registrado, o
   ninguno; la pantalla permite comprobarlo antes de repetir.

---

### Edge Cases

- **Teórico cambiado entre inicio y confirmación**: no debería ocurrir porque el alcance está
  congelado; si al confirmar el físico de un bucket no coincide con el teórico capturado, se rechaza
  la confirmación sin cambios y se informa del bucket (defensa frente a un escritor que no comprobó
  la congelación).
- **Contado por debajo del reservado**: no se confirma (US2.4); se resuelve liberando reservas con
  motivo, permitido durante el conteo (US3.3), o cancelando el conteo.
- **Ubicación vacía**: un conteo puede incluir una ubicación sin existencias; solo tendrá hallazgos o
  ninguna línea.
- **Ubicación desactivada antes de iniciar**: no se puede iniciar un conteo con ubicaciones
  inactivas; el borrador avisa al iniciar.
- **Conteo abandonado**: la congelación no expira sola; solo se libera confirmando o cancelando.
- **Artículo sin fracciones**: una cantidad contada fraccionaria se rechaza.
- **Lote con existencias cero**: los lotes del bucket con físico 0 no se esperan; si aparecen
  físicamente, se registran como hallazgo.
- **Recepciones durante el conteo**: el escritor de recepciones (HU-01, A) también debe rechazar
  entradas en ubicaciones congeladas; HU-04 aporta la comprobación común y la prueba de que el
  lector deja de ser constante.
- **Salida corregida ya contada**: si la ubicación de la salida errónea ya se ajustó por otro conteo,
  el nuevo conteo parte de ese físico ya ajustado y no duplica el efecto; al vincular, la revisión
  muestra los ajustes ya vinculados a la salida.
- **Corrección por debajo del reservado**: el contado nunca es negativo y, si queda por debajo del
  reservado, se aplica US2.4.

## Requirements *(mandatory)*

### Functional Requirements

**Definir e iniciar el conteo**

- **FR-001**: El sistema DEBE permitir a los usuarios con permiso de registro de conteos crear un
  conteo en borrador eligiendo una o varias ubicaciones activas. El borrador NO DEBE congelar
  ubicaciones ni cambiar existencias.
- **FR-002**: Al iniciar el conteo, el sistema DEBE, en una sola operación, congelar todas sus
  ubicaciones y capturar como teórico el físico de cada artículo y lote con físico mayor que cero en
  ellas. DEBE rechazar el inicio si alguna ubicación está inactiva o en otro conteo no terminado,
  nombrándola.
- **FR-003**: Una ubicación NO DEBE pertenecer a más de un conteo en curso o pendiente de aprobación
  a la vez.

**Contar y revisar diferencias**

- **FR-004**: En un conteo en curso, los usuarios con permiso de registro de conteos DEBEN poder
  registrar y corregir la cantidad contada de cada línea (cero o mayor, en la unidad del artículo,
  sin fracciones si el artículo no las admite), quedando registrados quién contó y cuándo.
- **FR-005**: El sistema DEBE mostrar por línea el teórico, el contado y la diferencia (contado −
  teórico), y por conteo el número de líneas con diferencia.
- **FR-006**: Los usuarios con permiso de registro de conteos DEBEN poder añadir hallazgos en
  ubicaciones del alcance (artículo existente, lote cuando aplique, cantidad positiva) y retirarlos
  antes de enviar a aprobación. Las líneas esperadas NO DEBEN poder retirarse.
- **FR-007**: El envío a aprobación DEBE exigir el contado explícito de todas las líneas, incluido el
  cero, y rechazar señalando las pendientes. Tras el envío, las cantidades NO DEBEN modificarse.

**Autorizar el ajuste**

- **FR-008**: Solo los usuarios con permiso de ajuste de inventario DEBEN poder confirmar o cancelar
  un conteo. La aprobación DEBE registrarse con su autor y su instante aunque la misma persona haya
  contado.
- **FR-009**: La confirmación DEBE exigir un motivo para cada línea con diferencia distinta de cero.
- **FR-010**: La confirmación DEBE, en una sola operación atómica: añadir el movimiento de ajuste
  del conteo, con una entrada por cada línea con diferencia (al alza o a la baja, por la cantidad
  absoluta de la diferencia; sin diferencias no hay movimiento),
  dejar el físico de cada bucket igual al contado, registrar la aprobación y liberar todas las
  ubicaciones del conteo.
- **FR-011**: La confirmación DEBE rechazarse, sin cambios y conservando la congelación, si el
  contado de algún bucket queda por debajo de su reservado, o si su físico actual no coincide con el
  teórico capturado; DEBE indicar el bucket y las cantidades.
- **FR-012**: La cancelación DEBE exigir motivo, conservar el conteo y lo contado como evidencia, no
  cambiar existencias y liberar el alcance.
- **FR-013**: Un conteo confirmado o cancelado NO DEBE modificarse. Sus ajustes, como todo
  movimiento, NO DEBEN eliminarse ni editarse.

**Congelación del ámbito (INT-HU04-03)**

- **FR-014**: Mientras una ubicación esté en un conteo en curso o pendiente de aprobación, el sistema
  DEBE rechazar con el motivo «ubicación en conteo» (`CountScopeFrozen` del contrato común), sin
  cambios parciales: confirmar salidas con algún origen en ella, reservar existencias en ella,
  registrar recepciones con destino en ella, desactivarla y cambiar su tipo.
- **FR-015**: La liberación de reservas con asignaciones en una ubicación congelada DEBE seguir
  permitida, con el permiso de gestión de reservas y motivo, sin cambiar el físico (US3.3).
- **FR-016**: Las consultas de ubicaciones DEBEN mostrar si cada ubicación está en un conteo abierto,
  con el valor real; la comprobación de conteo abierto NO DEBE seguir siendo constante una vez
  habilitados los conteos.
- **FR-017**: El inicio de un conteo y cualquier operación que cambie existencias, reservas o el
  estado de sus ubicaciones DEBEN serializarse: el teórico capturado incluye la operación o la
  operación se rechaza por la congelación.

**Corrección de salidas confirmadas (Q3 y P-07 de HU-03)**

- **FR-018**: El sistema DEBE permitir a los usuarios con permiso de ajuste corregir el físico de las
  ubicaciones de origen de una salida confirmada errónea mediante el conteo de esas ubicaciones:
  antes de confirmar, cada línea con diferencia de un bucket que la salida tocó puede vincularse a
  esa salida, y su ajuste queda vinculado a ella, con motivo (US4.1). NO DEBE existir otra vía de
  ajuste sin conteo.
- **FR-019**: La corrección NO DEBE modificar la salida original, su movimiento, lo servido ni el
  pendiente del pedido, su estado ni el consumo de las reservas; la pantalla DEBE indicarlo. Cambiar
  lo servido o el consumo exigiría un cambio compartido (AGENTS.md) fuera de esta feature.
- **FR-020**: El vínculo DEBE rechazarse si la salida no está confirmada o si la línea no
  corresponde a un bucket de origen de esa salida; la confirmación sigue las reglas de FR-011.

**Trazabilidad**

- **FR-021**: Cada ajuste DEBE registrar identificador, tipo, artículo, ubicación, lote, cantidad
  positiva, unidad, sentido, fecha y hora del hecho y del registro, quien contó, quien autorizó,
  motivo (el de cada diferencia se conserva en su línea del conteo, inmutable tras cerrar), el
  conteo como documento de soporte y, si la hay, la salida corregida vinculada, y como contraparte el
  motivo expreso de no aplicación, ya que un ajuste no tiene contraparte externa.
- **FR-022**: El historial de existencias DEBE mostrar los ajustes con sus datos y el código del
  conteo como documento de soporte; el detalle del conteo cerrado DEBE mostrar, por línea, la salida
  corregida vinculada, si la hay.
- **FR-023**: Los usuarios con permiso de consulta DEBEN poder ver la lista de conteos con su estado,
  alcance y responsables, y el detalle de cada uno.

**Artículos y referencias (INT-HU04-01 e INT-HU04-02)**

- **FR-024**: Las líneas de conteo y sus ajustes DEBEN integrarse con el contrato común de
  referencias al artículo desde su primera referencia (incluida la retirada de hallazgos antes del
  envío y las carreras con la edición y el borrado del artículo), sin impedir eliminar artículos
  nunca usados.
- **FR-025**: Los artículos desactivados DEBEN poder contarse y ajustarse con autorización y motivo,
  respetando reservas y congelación y conservando historial, ubicaciones y unidades.

**Fiabilidad y uso**

- **FR-026**: Cada acción que cambie un conteo DEBE exigir la versión vigente del conteo y rechazar
  una versión desactualizada pidiendo recargar.
- **FR-027**: Las acciones de iniciar, enviar, confirmar, cancelar y corregir DEBEN ser idempotentes:
  repetir la misma intención devuelve el resultado original; la misma intención con otros datos se
  rechaza.
- **FR-028**: Ante un fallo, la pantalla DEBE informar del estado de la operación y permitir
  comprobar su resultado antes de repetirla; NO DEBE mostrar éxito sin confirmación persistida.
- **FR-029**: Las pantallas DEBEN usar vocabulario del almacén, mostrar la unidad junto a cada
  cantidad, indicar estados y diferencias con texto (no solo color), señalar el campo a corregir y
  conservar lo escrito ante un rechazo.

### Alcance y dependencias

- Esta feature desarrolla verticalmente el ciclo conteo → diferencias → ajuste sobre ubicaciones
  elegidas, la congelación del ámbito en todos los escritores de B y la corrección de salidas
  confirmadas erróneas.
- **Fuera de alcance (accesorio, marcado como tal)**: calendario y planificación automática de
  conteos (el responsable lo documenta fuera del sistema, como exige la constitución antes de
  operar); conteos por artículo o por familia en lugar de por ubicación; recuentos por segunda
  persona y tolerancias automáticas; clasificación ABC; hojas de conteo imprimibles o lectura por
  escáner; correcciones de recepciones u otros movimientos (`/movements/{id}/corrections` general);
  conciliación de contingencias; traslados; cualquier cambio de lo servido o del consumo de reservas.
- Cobertura de los criterios originales de HU-04: selección de una parte del almacén (US1.1, FR-001);
  cantidades contadas comparadas con las teóricas (US1.2–US1.3, FR-002/004); diferencias visibles
  (US1.3, US2.1, FR-005); motivo e identidad de quien ajusta (US2.2–US2.3, FR-008/009/021);
  actualización al confirmar conservando el registro (US2.3, FR-010/013).
- **HU-02 (B)** aporta las ubicaciones y su comprobación de conteo abierto, que HU-04 hace real.
  **HU-05 (B)** aporta existencias, reservas y su liberación; su reserva debe rechazar sobre un
  ámbito congelado. **HU-03 (B)** aporta las salidas confirmadas, cuya confirmación debe rechazar
  sobre un ámbito congelado y cuyas correcciones se resuelven aquí. **HU-08 (A)** aporta artículo,
  unidad, fracciones, lote y estado activo. **HU-01 (A)** debe comprobar la congelación en su
  escritor de recepciones con la comprobación común. **HU-06 (A)** debe reevaluar el mínimo tras
  cada ajuste.
- La confirmación reutiliza los componentes comunes de H0 —resultado de operación, ejecutor,
  bloqueos y núcleo de stock— descritos en el
  [contrato de stock](../../.specify/memory/project/contracts/stock-operations.md); esta feature no
  crea otra vía de escritura, otro registro de resultados ni otro cálculo de disponible.

### Integraciones recibidas

HU-04 incorpora estas tareas receptoras con sus IDs de trazabilidad; el plan y las tareas las
concretarán como pruebas de este módulo, no de un doble de otro módulo.

| ID | Origen | Qué exige a HU-04 | Requisitos |
|----|--------|-------------------|------------|
| INT-HU04-01 | [Backlog de integración](../001-reparto-specs-inventario/integration-tasks.md#hu-04--conteos-y-ajustes--persona-b) (HU-08 T064/T066/T070/T075/T100/T101) | Integrar líneas de conteo y hallazgos con el contrato de aceptación de escritores desde su primera referencia, antes del ajuste; retirada de borradores y carreras de definición/borrado. | FR-006, FR-024 |
| INT-HU04-02 | Ídem (HU-08 T078/T103) | Ajustes de artículos inactivos solo con autorización y motivo, respetando reservas y congelación, sin reescribir el diario. | FR-011, FR-025, US5.4 |
| INT-HU04-03 | Ídem (HU-02 T034, revisión del PR #6) | Conectar la comprobación de conteo abierto de HU-02 y los controles de congelación de los escritores de stock y reservas; 409 `CountScopeFrozen` al desactivar o cambiar el tipo; `inOpenCount` real. | FR-014–FR-017, US3 |
| HU-05 FR-016 y HU-03 FR-010 | Convergencias de [HU-05](../004-consultar-existencias/validation/converge-1.md) y [HU-03](../005-registrar-salida-mercancia/validation/converge-1.md) | Rechazo por conteo abierto al reservar y al confirmar salidas, hoy pendiente. | FR-014, US3.1–US3.2 |
| HU-03 Q3 / P-07 | [Spec de HU-03](../005-registrar-salida-mercancia/spec.md#alcance-y-dependencias) | Corrección de una salida confirmada errónea mediante ajuste trazable, sin cambiar lo servido ni el consumo de reservas. | FR-018–FR-020, US4 |
| V-09 y V-10 | [Guía de validación](../../.specify/memory/project/quickstart.md#6-escenarios-de-aceptación-integrados) | Operaciones sobre alcance congelado rechazadas; ajuste −2 trazable; no confirmar por debajo del reservado; hallazgos; cancelar conserva evidencia. | US1–US3, US5 |

La aceptación operativa H5 y la revisión compartida siguen en el
[backlog de integración](../001-reparto-specs-inventario/integration-tasks.md#h5--aceptación-operativa-y-puesta-en-servicio--responsables-del-proyectoalmacén).

### Principios constitucionales y evidencia de aceptación

| Principio | Aplicación a HU-04 | Evidencia exigida |
|-----------|-------------------|-------------------|
| I. Correspondencia almacén/sistema | Las diferencias se documentan y se resuelven con ajustes, nunca sobrescribiendo saldos; el físico queda igual al contado. | US2.3, FR-010, FR-011; V-09. |
| II. Ubicación física precisa | Se cuenta por ubicación; los hallazgos exigen ubicación y lote. | US1, US5, FR-002, FR-006; V-10. |
| III. Uso intuitivo | Diferencias en texto, errores corregibles y revisión antes de confirmar. | US2.1, FR-005, FR-028, FR-029; validación con trabajadores documentada (SC-007). |
| IV. Trazabilidad | Ajuste con motivo, quien contó, quien autorizó y vínculo al conteo o a la salida; historial inalterable. | US2.3, US4.2, FR-013, FR-021, FR-022. |
| V. Integridad y recuperación | Congelación, atomicidad, idempotencia, versiones, permisos y concurrencia. | US3, US6, FR-014–FR-017, FR-026–FR-028; V-07, V-08 y V-12. |

La evidencia de implementación conservará fecha, entorno, escenario, resultado e incidencias.
Esta especificación define la aceptación; no declara realizadas pruebas ni revisiones conjuntas.

### Key Entities *(include if feature involves data)*

- **Conteo**: documento con alcance de ubicaciones, estado (borrador, en conteo, pendiente de
  aprobación, confirmado o cancelado), creador, inicio, aprobador y motivo de cancelación.
- **Ubicación del conteo**: cada ubicación del alcance y si está congelada.
- **Línea de conteo**: artículo, ubicación, lote, teórico capturado, contado, diferencia, motivo,
  quien contó y cuándo, si es esperada o hallazgo y, opcionalmente, la salida confirmada que corrige.
- **Movimiento de ajuste — referencia compartida**: hecho del historial al alza o a la baja, con
  el conteo como soporte, la salida corregida si la hay, motivo, autorización y contraparte no
  aplicable.
- **Salida confirmada — de HU-03**: documento al que se vincula una corrección; no cambia.
- **Existencia por ubicación y lote — referencia compartida**: físico y reservado; el ajuste cambia
  solo el físico.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: En el 100 % de los conteos confirmados de la batería de aceptación, el físico final de
  cada bucket es igual al contado y la suma de ajustes de cada bucket es igual a su diferencia.
- **SC-002**: El 100 % de los intentos de confirmar salidas, reservar, recibir, desactivar o cambiar
  el tipo sobre una ubicación en conteo se rechaza sin ningún cambio; tras confirmar o cancelar el
  conteo, el 100 % se admite con sus reglas habituales.
- **SC-003**: En la prueba de inicio de conteo simultáneo con salidas y reservas sobre la misma
  ubicación, en todas las repeticiones el teórico coincide con el físico tras la operación ganadora
  y ningún movimiento se pierde ni se duplica.
- **SC-004**: El 100 % de las confirmaciones sin motivo, sin permiso, con contado por debajo del
  reservado o con versión desactualizada se rechaza sin cambios, y ninguna intención repetida crea un
  segundo ajuste.
- **SC-005**: En el 100 % de las correcciones de salidas de la batería, el físico del origen queda
  corregido y lo servido del pedido, su pendiente y el consumo de reservas no cambian.
- **SC-006**: Con 25 sesiones simultáneas, el 95 % de las confirmaciones de un conteo de hasta 50
  líneas muestra su resultado en 3 segundos o menos.
- **SC-007**: Los trabajadores representativos que participan en la validación cuentan una ubicación
  y el responsable autoriza su ajuste sin asistencia técnica y sin errores de registro no detectados.

## Assumptions

- Permisos según el catálogo común: ver conteos con Inventory.Read; crear, iniciar, registrar,
  añadir hallazgos y enviar con Counts.Record (operario y responsable de almacén); confirmar,
  cancelar y corregir salidas con Inventory.Adjust (responsable de almacén). El administrador no
  tiene permisos implícitos de inventario.
- Se cuenta por ubicaciones completas: todos los artículos y lotes de cada ubicación elegida.
- El contado no oculta el teórico al operario: la pantalla muestra teórico y diferencia mientras se
  cuenta (conteo no ciego), conforme al contrato de interacción común.
- Un ajuste no tiene contraparte externa: registra el motivo expreso de no aplicación, nunca una
  referencia de contraparte, también cuando corrige una salida (el cliente del pedido no participa
  en el ajuste). El documento de soporte es siempre el conteo; la salida corregida es una referencia
  vinculada.
- El motivo es texto libre obligatorio por diferencia; no se introduce un catálogo de motivos.
- La fecha y hora del hecho de un ajuste es la de su confirmación; el inicio y el contado conservan
  sus propios instantes.
- La congelación no caduca; un conteo abandonado se cancela explícitamente.
- Un lote hallado que no existe se identifica por su código y se incorpora con el ajuste conforme a
  la regla común de lotes.
- No se introduce gestión por serie ni caducidad.
- Los objetivos de rendimiento y la validación con trabajadores son criterios a medir, no resultados
  obtenidos; respaldo y restauración siguen la guía global.
