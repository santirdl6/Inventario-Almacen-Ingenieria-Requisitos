# Interacción de HU-08

**Fuentes**: [spec](../spec.md), [contrato UI común](../../../.specify/memory/project/contracts/ui.md)
y [HTTP particular](api.md). Sesión, los tres catálogos, retirada H1 y recuperación
implementados; [aceptación técnica](../validation/acceptance.md) registra la validación
automatizada. El diario real y la validación humana con gestores siguen pendientes.

## Navegación y permisos

| Ruta | Pantalla |
|------|----------|
| /login | Inicio de sesión común, sin registro de cuentas. |
| /maestros | Entrada al catálogo, redirige a artículos. |
| /maestros/familias | Listado paginado y acceso a alta/detalle. |
| /maestros/familias/nuevo; /maestros/familias/:id | Formulario de alta o detalle/edición. |
| /maestros/proveedores | Listado paginado y acceso a alta/detalle. |
| /maestros/proveedores/nuevo; /maestros/proveedores/:id | Formulario de alta o detalle/edición. |
| /maestros/articulos | Listado con filtro adicional de familia. |
| /maestros/articulos/nuevo; /maestros/articulos/:id | Formulario de alta o detalle/edición. |
| /maestros/articulos/:id/movimientos | Historial, también de artículos inactivos. |

Inventory.Read permite consulta; MasterData.Manage permite acciones de escritura. Mostrar
menús y controles según sesión sin sustituir la autorización del servidor. El administrador
inicial no es gestor de maestros salvo asignación controlada del rol correspondiente.

## Formularios y consultas

- Crear con estado activo explícito. Etiquetas, obligatoriedad, unidad y mensajes en español.
- Listados con código, nombre y estado textual; artículos muestran familia y unidad.
  Búsqueda por código/nombre, activos por defecto, selector activos/inactivos/todos.
- Carga visible, actualización manual y error con opción de volver a consultar. Un error
  de consulta no vacía silenciosamente resultados anteriores ni muestra «sin coincidencias».
- Selección de familia activa para nuevas asignaciones; incluir la familia inactiva actual
  como opción conservable para poder editar los demás campos.
- Alta de Article no pide ubicación, stock inicial ni mínimo; el mínimo se configura en HU-06.
- Proveedor sin correo puede guardarse con aviso textual de que el envío de pedidos requerirá
  completarlo. Identificador fiscal opcional, sin validación nacional inventada.
- Detalle obtiene ETag y capacidades. Unidad/lotes/fracciones bloqueados se muestran con
  explicación de primer uso; no se deduce bloqueo a partir del saldo ni de campos ocultos.
  PUT conserva esos valores usando el valor completo del formulario, no omitiendo controles
  deshabilitados por accidente. El servidor puede rechazar una capacidad que quedó obsoleta.

## Confirmación y conflictos

- Desactivar, reactivar o eliminar abre una revisión con código/nombre, acción y consecuencia.
  Borrar solo se ofrece si capacidad y permiso lo permiten; un rechazo del servidor explica
  las referencias y ofrece desactivar. Nunca se propone borrar movimientos.
- Guardando: impedir doble envío y mantener una única intención hasta conocer su resultado.
  Confirmado: mostrar identificación del maestro y refrescar el estado conservado.
- 422 o 409 validable: mantener campos válidos y errores junto al campo, con resumen y foco.
  412: conservar la edición local, ofrecer cargar la versión vigente y revisar diferencias;
  no fusionar automáticamente ni sobrescribir los cambios del otro gestor.
- Un fallo antes del envío permite corregir datos. Una vez enviada una intención, cualquier
  timeout/resultado desconocido exige comprobar esa intención antes de crear otra diferente.

## Operación pendiente y sesión

Conservar la intención inmutable `{ operationId, method, resource, actorId, payload, etag }`
en memoria y temporalmente en sessionStorage para las mutaciones de maestros. Nunca guardar
allí el payload de login, credenciales, cookies o tokens. Al recargar, comprobar identidad y
consultar primero la operación. Si no hay resultado observado, explicar que puede seguir en
curso y permitir repetir la petición original con la misma clave, sin fabricar un payload
vacío o conservar una edición distinta bajo esa intención. Borrar el estado al resolverla.

Si vence la sesión, volver a autenticar y comprobar permisos; no ejecutar automáticamente
escrituras pendientes ni reutilizarlas bajo otra identidad. El servidor aplica el conflicto
de actor de la clave. Tras logout, eliminar payload/ETag/formularios y conservar solo la
referencia pendiente necesaria para comprobar resultado posteriormente. Si ya no se dispone
del payload original, solo se puede consultar, no reenviar una petición reconstruida sin
verificación ni generar otra intención para resolver un resultado aún incierto.

## Historial, accesibilidad y evidencia

Historial con filtros por período/responsable/ubicación/lote y unidad de cada movimiento.
Artículo inactivo se indica con texto y mantiene navegación. Si el proveedor de diario aún
no está integrado, mostrar «Historial pendiente de integración» o el error correspondiente;
no una tabla vacía presentada como historial completo.

Operación por teclado, nombres accesibles, orden de foco y retorno al control disparador
al cerrar diálogos. Mensajes de estado con texto además de color; no depender solo de toast.
Tres gestores completan los recorridos SC-006 sin asistencia técnica ni errores no detectados;
registrar participantes por rol, tareas, resultados, incidencias y correcciones. Las suites
automatizadas aportan evidencia complementaria, no sustituyen esa aceptación.
