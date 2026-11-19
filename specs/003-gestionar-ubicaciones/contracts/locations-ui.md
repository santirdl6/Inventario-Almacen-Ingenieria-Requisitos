# Contrato de interacción: HU-02 — /ubicaciones

**Base**: [contrato UI común](../../../.specify/memory/project/contracts/ui.md)
(estados de operación 1–7, accesibilidad). Aquí solo se concretan pantallas y textos de
HU-02. Código en `frontend/src/app/features/locations/`, sobre el layout, el login y la
sesión de A.

**Revisión 2026-11-19**: rutas protegidas con `permissionGuard`; búsqueda de artículos con
`GET /articles` de A; mensajes en la propia página; la última zona de recepción ya no se
confirma de nuevo, se explica y se impide.

## Rutas (lazy, dentro de `LOCATIONS_ROUTES`)

El área `/ubicaciones` ya exige `Inventory.Read` (`operation-areas.ts`).

| Ruta | Pantalla | Permiso adicional | Historia |
|------|----------|-------------------|----------|
| /ubicaciones | Lista con búsqueda por código o pasillo, filtros de tipo y estado. | — | US1.5 |
| /ubicaciones/nueva | Alta. | Locations.Manage | US1.1–US1.4 |
| /ubicaciones/articulo | Búsqueda de artículo por código o nombre. | — | US2.1 |
| /ubicaciones/articulo/:articleId | Ubicaciones de un artículo, con total. | — | US2 |
| /ubicaciones/:id | Detalle: datos físicos, tipo, estado y contenido. | — | US3 |
| /ubicaciones/:id/editar | Corrección de datos y tipo. | Locations.Manage | US4.1–US4.2 |

Los botones de alta, edición y cambio de estado se muestran según
`SessionService.has('Locations.Manage')`; es solo orientación, el servidor decide (US5.1).
Los parámetros se leen con `ActivatedRoute`.

## Comportamiento

- **Lista**: activas por defecto; «Incluir inactivas» visible. Sin resultados: «No hay
  ubicaciones que coincidan con la búsqueda» y botón para limpiar filtros. Tipo y estado
  con texto («Almacenamiento», «Zona de recepción», «Activa», «Inactiva»).
- **Caracteres**: Código, Pasillo, Estantería y Nivel solo admiten letras A–Z sin acentos ni Ñ,
  dígitos y guion (la ayuda de cada campo lo dice); otro carácter, también un espacio interior,
  se marca en el campo con «Solo letras A–Z sin acentos ni Ñ, dígitos y guion, sin espacios
  interiores.» antes de enviar, conservando lo escrito.
- **Alta/edición**: campos Código, Pasillo, Estantería, Nivel (obligatorios con «*» y texto
  «obligatorio»), Tipo. Botón «Proponer código» que rellena `P01-E01-N01` a partir de los
  campos físicos, editable. Longitudes máximas visibles (32 y 16).
  Con `hasMovements`, los cuatro campos de identificación se muestran de solo lectura con
  la explicación de FR-011 y un enlace «Crear otra ubicación».
- **Errores** (con `apiProblem()`): 409 `DuplicateCode`/`DuplicateLocationPlace` marca los
  campos, muestra el `detail` (que en el lugar repetido nombra la ubicación existente) y
  enlaza a la lista filtrada (`/ubicaciones?search=` con el código o el pasillo; la lista
  lee `search` de la URL); 422 marca cada campo; los
  valores válidos se conservan y el foco va al primer error.
- **Conflicto 412**: «Otra persona ha cambiado esta ubicación». Botones «Ver datos vigentes»
  (recarga sin perder lo escrito en un panel aparte) y «Descartar mis cambios».
- **Desactivar/Reactivar** (en el detalle): diálogo de revisión con código, descripción,
  contenido («vacía» o lista) y consecuencia («dejará de ofrecerse para nuevas recepciones,
  salidas y reservas; su historial se conserva»). Si `isLastActiveReceiving` es verdadero, el
  diálogo explica que no puede retirarse la única zona de recepción activa y que antes debe
  crearse o activarse otra, sin botón de confirmar. Si el servidor responde 409
  `NoActiveReceivingLeft`, muestra su `detail` y no reintenta. `LocationNotEmpty` muestra el
  `detail` y enlaza al contenido.
- **Contenido de ubicación**: tabla artículo, lote («Sin lote» para SIN-LOTE), cantidad
  con unidad, reservado con texto; artículos inactivos con «Inactivo». Ubicación vacía:
  «Esta ubicación está vacía». Cada fila enlaza a «Ver todas sus ubicaciones».
- **Ubicaciones de un artículo**: búsqueda por código o nombre (`GET /articles?q=`); total
  del artículo encima de la tabla, separado de la página; filas por ubicación y lote;
  ubicaciones inactivas con «Inactiva». Sin existencias: «Este artículo no está almacenado
  en ninguna ubicación» (no es un error). Botón «Actualizar» y `asOf` visible.
- **Envío**: transporte común y sesión/permiso original; intención inmutable con clave,
  cuerpo y ETag en sessionStorage, restaurada al recargar. Timeout o error de red:
  «Resultado pendiente de comprobar». «Comprobar» consulta la operación; si aún no hay
  resultado, se ofrece «Repetir petición original» explícitamente. No cambia el cuerpo,
  ETag ni clave para resolver incertidumbre. Logout elimina el payload y conserva solo
  la referencia para consulta por su identidad original. Nunca se muestra éxito sin 2xx.
- **Paginación ante movimientos**: una página sin filas con totalCount positivo no significa
  almacén/ubicación vacíos. Se recarga una página válida conservando la consulta y los totales.
- **Éxito y avisos**: en la propia página (`role="status"`, o `role="alert"` para errores),
  con el código de la ubicación; después se recarga el detalle desde el servidor.

## Accesibilidad

Etiquetas `<label for>`, mensajes de error asociados con `aria-describedby`, foco tras
error y al abrir/cerrar diálogos, tabla PrimeNG con encabezados y nombre accesible,
operación completa por teclado. Ningún estado se comunica solo con color. Se comprueba en
las specs de Vitest y en el E2E (D-13).
