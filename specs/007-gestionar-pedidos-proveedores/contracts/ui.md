# Interacción HU-07 — Compras

**Fecha**: 2026-11-20. Base: [spec](../spec.md), [API](api.md),
[constitución](../../../.specify/memory/constitution.md),
[plan global](../../../.specify/memory/project-plan.md) y
[interacción común](../../../.specify/memory/project/contracts/ui.md).

## Pantallas y navegación

| Ruta | Permiso | Contenido |
|------|---------|-----------|
| /compras | Inventory.Read | Número, proveedor, estado logístico y de correo, período y páginas. |
| /compras/nuevo | Purchasing.Manage | Formulario de proveedor/fecha/líneas. |
| /compras/:id/editar | Purchasing.Manage | Borrador editable solo Draft/None. |
| /compras/:id/enviar | Purchasing.Send | Revisión de documento/destinatario, confirmación o retry conocido. |
| /compras/:id | Inventory.Read | Detalle histórico, cantidades, seguimiento y documento congelado. |

Registrar rutas standalone lazy en features/purchasing y Compras en operation-areas.ts;
estáticas antes de :id. Guards y botones reflejan permisos sin reemplazar policies HTTP.
Reutilizar layout, sesión, selectores de maestros y componente de estado de operaciones.

## Formulario

Proveedor/fecha general obligatorios; FormArray por línea con UUID estable, artículo,
cantidad, unidad visible, precio/moneda y fecha prevista. Agregar/retirar sin fusionar
artículos repetidos; los errores por índice se asocian al ID local mientras llega respuesta.
Decimales se editan/persisten como strings; si se admite coma local, normalizar a punto
sin Number/parseFloat ni cálculos binarios autoritativos. Mostrar datos válidos tras 422.

Selectores paginados de activos para asignaciones nuevas; conservar selección actual
aunque quede fuera de página/inactiva. Filtros históricos admiten proveedores inactivos.
No presentar solo UUID ni desactivar el artículo por familia inactiva. Avisar si falta
correo, permitiendo guardar. Cambiar proveedor indica la reasignación explícita; cambios
de maestros no refrescan silenciosamente los datos históricos del documento.

El selector de artículo conserva por defecto «Código o nombre (contiene)» y permite elegir
explícitamente «Prefijo del código SKU» para localizar códigos mediante el índice existente.
Consume el filtro opcional skuPrefix del mismo catálogo HU-08; mantiene paginación,
referencias históricas y los filtros de activos, sin otro catálogo o permiso.

## Revisión y estados

Separar visualmente «Estado del pedido» y «Envío del correo» con texto/icono, no solo color.
Revisión muestra número, proveedor histórico, destinatario actual, documento completo,
unidades/precios/moneda/fechas y bloqueos actuales. Conservar ETag y reviewToken recibidos
de la misma revisión; POST inicial confirma ambos. No permitir destino libre.

- Guardado: éxito solo tras 201/200 o resultado confirmado recuperado; enlazar al detalle.
- Solicitud conservada: «Envío pendiente», compra Borrador y edición bloqueada.
- Accepted: «Aceptado por el servicio de correo»; aclarar que no acredita entrega/lectura.
- Failed: incidencia legible, intentos y «Reintentar envío» si hay permiso; revisar el
  **documento/destinatario congelados**, no los contactos actuales. Nueva clave, misma entrega.
- acceptanceUncertain: explicar posible copia del mismo pedido, también en Failed.
- Recibido/pendiente se presentan como consulta, nunca campos editables.

Refrescar progreso cada 5 s mientras visible y Pending; detener polling al destruir vista,
cambiar identidad o llegar a terminal. Ofrecer «Actualizar» siempre. La lectura de progreso
incluye purchaseOrderVersion; si difiere del detalle, recargarlo antes de mostrar estados
combinados o habilitar acciones. Un fallo de polling conserva último dato indicando que no
se pudo actualizar, sin inventar aceptación ni mostrar un listado vacío.

## Intenciones y recuperación compartida

Ampliar mutationPermission para método/ruta exactos de compras. Grupo de exclusión local
`purchase-order:{id}` compartido por PUT y POST /send; POST alta usa grupo de creación.
El grupo se deriva de ruta, no sustituye método/URL/cuerpo/ETag de la intención. Mantener
compatibilidad con entradas previas de maestros/ubicaciones en sessionStorage.

Un PUT incierto bloquea send y otra edición; un send incierto bloquea ambas. Tras timeout/
red/503: conservar snapshot/clave/actor/ETag, respetar Retry-After y comprobar resultado.
404 de /operations no prueba fallo. No iniciar retry conocido hasta constatar Failed y
resolver la intención HTTP pendiente. IdempotencyConflict conserva referencia para comprobar
la original; ajustar el descarte genérico de 4xx del transporte en ese caso.

El componente OperationStatus filtra por grupo, enlaza a Compras y reautoriza por acción.
Eventos recovered distinguen alta/PUT/send: recuperar alta navega usando Location/id;
PUT/send refrescan detalle y delivery, no usan el cuerpo histórico como estado actual.
Comprobar actor y generación de vista antes de aplicar respuestas tardías.

Logout retira payload/ETag del store y limpia formulario/revisión de memoria; solo conserva
referencias. Volver con otra cuenta no reutiliza ni revela esa intención. Volver con el
mismo actor permite comprobar, pero si falta payload no reconstruye una petición para repetir.

## Conflictos, paginación y accesibilidad

412 conserva edición local y muestra revisión actual comparada por UUID de línea, con
adiciones/retiradas/cambios; no sobreescritura automática. SendReviewChanged obliga a
recargar y revisar destinatario/documento. Tras corregir un rechazo definitivo puede
emitirse otra intención; una incertidumbre requiere comprobación previa.

Listado sin coincidencias distinto de error; página vacía con totalCount>0 ofrece ir a
una válida conservando filtros. Usar etiquetas españolas, obligatoriedad/unidades explícitas,
resumen de errores y foco al campo; diálogos/revisión operables por teclado y estados
anunciados sin repetir cada polling. No depender de color para estados o inactividad.

Playwright acredita recorridos técnicos; la validación con ≥3 compradores representativos
SC-008 se documenta en H5-03 con incidencias/correcciones, sin declararla realizada por tests.
