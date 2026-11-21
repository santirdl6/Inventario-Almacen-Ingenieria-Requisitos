# Contrato de interfaz de usuario — HU-06

**Base**: [spec](../spec.md), [UI común](../../../.specify/memory/project/contracts/ui.md) y [contrato HTTP](api.md).

---

## 1. Navegación y permisos en la aplicación

- **Ruta principal**: `/alertas`
  - Vinculada en la barra de navegación superior o menú lateral dentro de [`operation-areas.ts`](../../../frontend/src/app/operation-areas.ts).
  - Visible para usuarios con permiso `Alerts.Read`.
- **Ruta de configuración**: Ficha de detalle de artículo (`/maestros/articulos/:id`)
  - Bloque "Política de existencias" que muestra el stock mínimo actual y su unidad; las métricas de disponibilidad y sugerencia se consultan en el panel de alertas con `Alerts.Read`.
  - Formulario de edición del umbral visible solo si el usuario dispone de `StockPolicy.Manage`.

---

## 2. Panel de Alertas (`/alertas`)

### Estructura de la página

1. **Encabezado y filtros**:
   - Selector de estado: `Activas` (por defecto), `Resueltas`, `Todas`.
   - Búsqueda por SKU o nombre de artículo.
   - Paginación con selector de páginas y total de alertas coincidentes.
2. **Tabla de alertas**:
   - Columnas:
     - **Artículo**: SKU y nombre con enlace a la ficha del artículo.
     - **Estado**: Badge visual accesible (`Activa` en advertencia, `Resuelta` en éxito).
     - **Físico**: Unidades totales físicas en el almacén.
     - **Reservado**: Unidades comprometidas para clientes/salidas.
     - **Disponible**: `Físico − Reservado`.
     - **Mínimo**: Umbral configurado.
     - **Sugerido**: Cantidad recomendada a reponer (`max(0, Mínimo − Disponible)`).
     - **Fecha**: Instante de apertura (y de resolución si aplica).
     - **Acciones**:
        - Botón **"Crear pedido"** para usuario con `Purchasing.Manage`: navega a `/compras/nuevo?articleId={articleId}&suggestedQuantity={suggestedQuantity}`. El formulario valida el artículo activo y presenta cantidad editable; no preselecciona proveedor ni guarda automáticamente.
       - Tratar los parámetros como propuesta no confiable, con cantidades decimales exactas. Si hay una intención de alta pendiente, no sobrescribirla con los parámetros. Si falta el artículo o está inactivo, o la cantidad no es válida para su unidad, comunicar el problema y conservar una entrada de compra editable sin asignación automática. El guardado normal vuelve a validar y requiere proveedor y autorización.
3. **Comportamiento sin compra automática**:
   - La interfaz comunica con claridad que la alerta es informativa y que pulsar "Crear pedido" abre el borrador de compra para revisión humana, sin tramitar pedidos automáticos.

---

## 3. Configuración del Stock Mínimo

1. **Edición con control de concurrencia**:
   - El campo de entrada valida en cliente que el valor sea un decimal válido no negativo.
   - Si el artículo no permite fraccionario (`allowsFractional: false`), se fuerza la validación de número entero.
   - Al guardar, se envía la cabecera `If-Match` con el ETag obtenido en la carga inicial.
    - Si se produce un conflicto (412 Precondition Failed), se conserva el valor local y se ofrece consultar la política vigente antes de emitir otra intención. Ante respuesta incierta se conserva clave/cuerpo/ETag y se comprueba el resultado antes de repetir.
2. **Feedback y accesibilidad**:
   - Estados de carga (`busy`) inhabilitan los botones para evitar dobles envíos.
   - Notificaciones accesibles mediante roles ARIA (`role="alert"`, `role="status"`).
   - No se emplean exclusivamente colores para distinguir el estado de una alerta.
