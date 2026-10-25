<!--
Sync Impact Report
- Cambio de versión: plantilla sin versión → 1.0.0.
- Motivo: adopción inicial de cinco principios y sus reglas de gobernanza.
- Principios sustituidos:
  - PRINCIPLE_1_NAME → I. Correspondencia entre almacén y sistema.
  - PRINCIPLE_2_NAME → II. Ubicación física precisa y registrada.
  - PRINCIPLE_3_NAME → III. Uso intuitivo para todo trabajador.
  - PRINCIPLE_4_NAME → IV. Trazabilidad detallada de entradas y salidas.
  - PRINCIPLE_5_NAME → V. Integridad y recuperación de datos críticos.
- Secciones concretadas: Core Principles; Controles operativos del almacén;
  Revisión y criterios de aceptación; Governance.
- Secciones eliminadas: ninguna.
- Pendientes y marcadores diferidos: ninguno.
-->
# Constitución del Control de Inventario y Almacén

## Core Principles

### I. Correspondencia entre almacén y sistema

Todo hecho que modifique las existencias, la ubicación o el estado de un artículo DEBE
reflejarse en el sistema de información. Esto incluye recepciones, despachos, traslados,
devoluciones, daños, pérdidas y ajustes de inventario.

- Cada operación física DEBE registrarse al realizarse y antes de declararse completada.
- Cuando una interrupción impida el registro inmediato, DEBE conservarse un comprobante
  de contingencia con identificador único, fecha, responsable y detalle del hecho.
  El comprobante DEBE incorporarse y conciliarse al restablecerse el servicio, antes
  de reanudar las operaciones ordinarias sobre las existencias afectadas.
- Toda diferencia detectada entre el almacén y el sistema DEBE documentarse, investigarse
  y resolverse mediante un ajuste trazable; NO DEBE ocultarse sobrescribiendo saldos.

La información del sistema DEBE representar la realidad física para sustentar decisiones
de almacenamiento y disponibilidad.

### II. Ubicación física precisa y registrada

Cada artículo almacenado DEBE tener una ubicación física identificable y registrada.
El registro DEBE permitir a un trabajador localizarlo sin depender de conocimiento informal.

- Cada ubicación DEBE tener un identificador único y una descripción con el detalle
  necesario para distinguirla: almacén, zona, pasillo, estantería, nivel o posición,
  según la organización física existente.
- Si un artículo ocupa varias ubicaciones, el sistema DEBE registrar la cantidad en cada una.
- Todo traslado DEBE registrar origen, destino, cantidad, responsable y fecha y hora,
  manteniendo coherentes las cantidades por ubicación y el total del artículo.
- Las áreas temporales de recepción, devolución o cuarentena DEBEN identificarse y
  registrarse como ubicaciones; NO DEBEN existir existencias con ubicación desconocida.

La localización precisa evita pérdidas, búsquedas innecesarias y errores de preparación.

### III. Uso intuitivo para todo trabajador

La aplicación web DEBE permitir que los trabajadores autorizados completen las tareas
de su puesto sin conocimientos técnicos ni asistencia del equipo de desarrollo.

- Las pantallas DEBEN utilizar vocabulario del almacén, etiquetas explícitas y navegación
  consistente; los campos obligatorios y las unidades de medida DEBEN ser visibles.
- Antes de confirmar un movimiento, el trabajador DEBE poder revisar artículo, cantidad
  y ubicaciones aplicables. El resultado DEBE indicar claramente si quedó registrado.
- Los errores DEBEN explicar qué ocurrió y cómo corregirlo, conservando los datos válidos
  ya ingresados. La interfaz NO DEBE comunicar estados únicamente mediante colores.
- Cada flujo de recepción, salida y traslado DEBE validarse con trabajadores
  representativos de sus usuarios: DEBEN completarlo sin asistencia técnica y sin
  errores de registro no detectados. El resultado DEBE quedar documentado.

La facilidad de uso es un control de calidad del inventario, porque reduce omisiones
y equivocaciones durante el trabajo diario.

### IV. Trazabilidad detallada de entradas y salidas

Cada entrada o salida DEBE conservar un registro completo que permita reconstruir
qué ocurrió, cuándo, por qué y bajo la responsabilidad de quién.

- Cada movimiento DEBE incluir identificador único, tipo de operación, artículo,
  cantidad positiva, unidad de medida, fecha y hora del hecho, fecha y hora de registro,
  usuario responsable, motivo y origen o destino aplicable.
- El registro DEBE incluir la referencia al documento de soporte y la contraparte
  cuando existan; si no aplican, esa circunstancia DEBE quedar explícita.
- Cuando el artículo se gestione por lote, serie o caducidad, esos datos DEBEN
  acompañar al movimiento para conservar su trazabilidad.
- Los movimientos confirmados NO DEBEN eliminarse ni modificarse silenciosamente.
  Toda corrección DEBE realizarse mediante una reversión o ajuste vinculado al original,
  con su motivo, responsable y fecha y hora.
- El historial DEBE permitir consultar los movimientos por artículo, período y responsable,
  y reconstruir el saldo y las ubicaciones a partir de los registros confirmados.

La trazabilidad permite auditar las existencias y explicar cada variación del inventario.

### V. Integridad y recuperación de datos críticos

Los datos del almacén DEBEN tratarse como críticos. Ninguna operación DEBE confirmar
información incompleta, contradictoria o parcialmente persistida.

- El sistema DEBE validar artículos, ubicaciones, cantidades, unidades y permisos antes
  de confirmar un movimiento; DEBE impedir salidas superiores a las existencias disponibles.
- El movimiento, su historial y sus efectos sobre saldos y ubicaciones DEBEN guardarse
  como una sola operación atómica: se confirman todos o ninguno.
- Las operaciones simultáneas y los reintentos NO DEBEN producir saldos negativos,
  pérdida de actualizaciones ni movimientos duplicados.
- El acceso de escritura DEBE limitarse a usuarios autorizados e identificados.
  Los cambios de datos maestros que afecten al inventario DEBEN ser auditables.
- DEBEN existir copias de seguridad y procedimientos de restauración comprobados.
  Los límites aceptables de pérdida de datos y tiempo de recuperación DEBEN definirse
  y aprobarse antes de operar en producción, y verificarse mediante una restauración.
- Ante un fallo, el sistema DEBE informar el estado de la operación y permitir comprobar
  su resultado antes de repetirla. NO DEBE mostrar éxito sin confirmación persistida.

La corrección se protege mediante controles verificables y recuperación comprobada;
los fallos NO DEBEN convertirse en pérdida silenciosa o corrupción del inventario.

## Controles operativos del almacén

- Cada artículo DEBE contar con un identificador único y una unidad de medida definida.
  Las conversiones utilizadas en movimientos DEBEN ser explícitas y verificables.
- Las responsabilidades de registrar, autorizar ajustes y revisar diferencias DEBEN
  estar definidas. Todo ajuste DEBE contar con autorización registrada.
- El responsable del almacén DEBE establecer y documentar la frecuencia y el alcance
  de los conteos físicos antes de iniciar la operación, y cumplir ese calendario.
- Cada conteo DEBE comparar cantidades físicas y registradas por artículo y ubicación.
  Las diferencias DEBEN conservar evidencia, resolución y responsable.

## Revisión y criterios de aceptación

Cada especificación, plan y revisión de cambios DEBE identificar los principios afectados
y aportar criterios de aceptación verificables. Cada entrega DEBE incluir evidencia de
cumplimiento antes de aprobarse.

- Los cambios en movimientos DEBEN comprobar entradas, salidas, traslados, devoluciones
  y ajustes afectados, incluidos datos inválidos y existencias insuficientes.
- Los cambios en persistencia DEBEN comprobar atomicidad ante fallos, concurrencia,
  reintentos y preservación del historial.
- Los cambios en flujos de trabajo DEBEN aportar la validación con trabajadores exigida
  por el principio III.
- Los cambios en respaldo o recuperación DEBEN aportar una restauración comprobada
  que respete los límites operativos aprobados.
- Una entrega con incumplimientos conocidos que comprometan saldos, ubicación,
  trazabilidad o recuperación NO DEBE aprobarse hasta su resolución.

## Governance

Esta constitución prevalece sobre las decisiones de diseño y las prácticas del proyecto.
El responsable del proyecto y el responsable del almacén DEBEN verificar su cumplimiento
en cada revisión de especificaciones y antes de cada entrega.

Toda enmienda DEBE documentar el motivo, los principios afectados, el impacto operativo
y las acciones de transición necesarias. DEBE contar con aprobación registrada del
responsable del proyecto y del responsable del almacén antes de entrar en vigor.
La enmienda DEBE actualizar este documento, su informe de impacto, la versión y la fecha
de última modificación. Las contradicciones detectadas en documentos dependientes DEBEN
registrarse como seguimiento y resolverse antes de aprobar el trabajo afectado.

La versión DEBE seguir el formato MAJOR.MINOR.PATCH:

- MAJOR: eliminación o redefinición incompatible de principios o reglas de gobernanza.
- MINOR: incorporación de principios o secciones, o ampliación material de obligaciones.
- PATCH: aclaraciones, correcciones de redacción o cambios sin efecto normativo.

La fecha de ratificación corresponde a esta adopción inicial y DEBE conservarse en
enmiendas futuras. Toda revisión de cumplimiento DEBE registrar evidencia, desviaciones
y responsables de su resolución; una excepción informal NO sustituye una enmienda.

**Version**: 1.0.0 | **Ratified**: 2026-10-25 | **Last Amended**: 2026-10-25
