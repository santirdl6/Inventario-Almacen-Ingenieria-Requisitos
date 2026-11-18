# Contrato de interacción: HU-02 — /ubicaciones

**Base**: [contrato UI común](../../../.specify/memory/project/contracts/ui.md)
(estados de operación 1–7, accesibilidad, validación con operarios). Aquí solo se concretan
pantallas y textos de HU-02. Código en `frontend/src/app/features/locations/`.

## Rutas (lazy, dentro de `LOCATIONS_ROUTES`)

| Ruta | Pantalla | Historia |
|------|----------|----------|
| /ubicaciones | Lista con búsqueda por código o pasillo, filtros de tipo y estado. | US1.5 |
| /ubicaciones/nueva | Alta. | US1.1–US1.4 |
| /ubicaciones/:id | Detalle: datos físicos, tipo, estado y contenido. | US3 |
| /ubicaciones/:id/editar | Corrección de datos y tipo. | US4.1–US4.2 |
| /ubicaciones/articulo/:articleId | Ubicaciones de un artículo, con total. | US2 |

Los botones de alta, edición y cambio de estado se muestran según `permissions` de
`GET /auth/me`; es solo orientación, el servidor decide (US5.1).

## Comportamiento

- **Lista**: activas por defecto; «Incluir inactivas» visible. Sin resultados: «No hay
  ubicaciones que coincidan con la búsqueda» y botón para limpiar filtros. Tipo y estado
  con texto («Almacenamiento», «Zona de recepción», «Activa», «Inactiva») e icono.
- **Alta/edición**: campos Código, Pasillo, Estantería, Nivel (obligatorios con «*» y texto
  «obligatorio»), Tipo. Botón «Proponer código» que rellena `P01-E01-N01` a partir de los
  campos físicos, editable. Longitudes máximas visibles (32 y 16).
  Con `hasMovements`, los cuatro campos de identificación se muestran de solo lectura con
  la explicación de FR-011 y un enlace «Crear otra ubicación».
- **Errores**: 409 de duplicado marca el campo y enlaza a la ubicación existente; 422
  marca cada campo; los valores válidos se conservan y el foco va al primer error.
- **Conflicto 412**: «Otra persona ha cambiado esta ubicación». Botones «Ver datos vigentes»
  (recarga sin perder lo escrito en un panel aparte) y «Descartar mis cambios».
- **Desactivar/Reactivar** (en el detalle): diálogo de revisión con código, descripción,
  contenido («vacía» o lista) y consecuencia («dejará de ofrecerse para nuevas recepciones,
  salidas y reservas; su historial se conserva»). Si el servidor responde 409
  `NoActiveReceivingLeft`, o `isLastActiveReceiving` es verdadero, el diálogo añade:
  «No quedará ninguna zona de recepción activa» y requiere confirmar de nuevo.
  `LocationNotEmpty` muestra físico y reservado y enlaza al contenido.
- **Contenido de ubicación**: tabla artículo, lote («Sin lote» para SIN-LOTE), cantidad
  con unidad, reservado con texto; artículos inactivos con «Inactivo». Ubicación vacía:
  «Esta ubicación está vacía».
- **Ubicaciones de un artículo**: total del artículo encima de la tabla, separado de la
  página; filas por ubicación y lote; ubicaciones inactivas con «Inactiva». Sin
  existencias: «Este artículo no está almacenado en ninguna ubicación» (no es un error).
  Botón «Actualizar» y `asOf` visible.
- **Envío**: botón deshabilitado mientras confirma; la clave de idempotencia de la intención
  se guarda en sessionStorage. Timeout o error de red: «Resultado pendiente de comprobar»
  con botón «Comprobar» que repite la misma petición y clave; nunca se muestra éxito sin 2xx.
- **Éxito**: mensaje con el código de la ubicación y recarga del detalle desde el servidor.

## Accesibilidad

Etiquetas `<label for>`, mensajes de error asociados con `aria-describedby`, foco tras
error y al abrir/cerrar diálogos, tabla PrimeNG con encabezados y nombre accesible,
operación completa por teclado. Ningún estado se comunica solo con color.
