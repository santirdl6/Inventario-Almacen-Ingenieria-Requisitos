# Contrato HTTP — HU-06

**Prefijo**: `/api/v1` · Base: [contrato común](../../../.specify/memory/project/contracts/api.md#hu-06--a-mínimos-y-alertas), [modelo](../data-model.md) y [especificación](../spec.md).

Formato de respuestas en JSON, cabeceras `Cache-Control: no-store`. Fechas en ISO 8601 con zona UTC (`O`). Cantidades en cadenas decimales exactas de hasta 3 decimales (`"15.000"`).

---

## 1. Endpoints

| Método y ruta | Permiso | Descripción y código de éxito |
|---|---|---|
| `GET /api/v1/articles/{id}/stock-policy` | `Inventory.Read` | 200 OK. Consulta el umbral de stock mínimo del artículo y su versión. Cabecera `ETag` fuerte con la rowversion. |
| `PUT /api/v1/articles/{id}/stock-policy` | `StockPolicy.Manage` | 200 OK. Actualiza el umbral de stock mínimo. Requiere cabecera `If-Match`. Reevalúa inmediatamente el artículo y devuelve la nueva política y `ETag`. |
| `GET /api/v1/stock-alerts` | `Alerts.Read` | 200 OK. Consulta paginada de episodios de alerta. Parámetros opcionales: `state` (`Active` por defecto, `Resolved`, `All`), `q` literal por SKU/nombre, `articleId`, `from`, `to`, `page` (1 por defecto), `pageSize` (25 por defecto, máx. 100). Orden `OpenedAt` descendente e ID como desempate; count/filas en una lectura consistente, intervalo de apertura `[from,to)`. |
| `GET /api/v1/stock-alerts/{id}` | `Alerts.Read` | 200 OK. Detalle completo de un episodio de alerta histórico o activo. |

---

## 2. Peticiones y respuestas

### GET `/api/v1/articles/{id}/stock-policy`

**Respuesta 200 OK**:
```json
{
  "articleId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "minimumAvailable": "10.000",
  "version": "AAAAAAAAB9M="
}
```
Cabecera: `ETag: "AAAAAAAAB9M="`

### PUT `/api/v1/articles/{id}/stock-policy`

**Cabeceras requeridas**:
- `If-Match: "AAAAAAAAB9M="`
- `Idempotency-Key`: UUID de la intención; repetir la misma petición conserva clave y cuerpo.
- `X-XSRF-TOKEN`: token antiforgery asociado a la sesión.
- `Content-Type: application/json`

**Cuerpo de petición**:
```json
{
  "minimumAvailable": "15.000"
}
```

**Respuesta 200 OK**:
```json
{
  "articleId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "minimumAvailable": "15.000",
  "version": "AAAAAAAAB9Q="
}
```
Cabecera: `ETag: "AAAAAAAAB9Q="`. La respuesta definitiva sigue el sobre común
`{ operationId, resource }`; el objeto mostrado es `resource`. El replay reautorizado
devuelve cuerpo y ETag históricos antes de comprobar `If-Match` del recurso actual.

### GET `/api/v1/stock-alerts?state=Active&page=1&pageSize=25`

**Respuesta 200 OK**:
```json
{
  "items": [
    {
      "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "articleId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "articleSku": "ART-001",
      "articleName": "Tornillo Acero 50mm",
      "baseUnit": "UD",
      "state": "Active",
      "physicalSnapshot": "12.000",
      "reservedSnapshot": "8.000",
      "availableSnapshot": "4.000",
      "minimumSnapshot": "5.000",
      "suggestedQuantity": "1.000",
      "openedAt": "2026-11-21T14:30:00Z",
      "updatedAt": "2026-11-21T14:30:00Z",
      "resolvedAt": null
    }
  ],
  "page": 1,
  "pageSize": 25,
  "totalCount": 1
}
```

---

## 3. Códigos de error y rechazos

- **400 Bad Request**: JSON o identificador mal formado, antiforgery, clave de intención o ETag inválidos.
- **401 Unauthorized**: Falta de autenticación de usuario.
- **403 Forbidden**: Permiso insuficiente (`StockPolicy.Manage` para PUT, `Alerts.Read` para consulta de alertas).
- **404 Not Found**: Artículo o alerta inexistente.
- **412 Precondition Failed**: La cabecera `If-Match` no coincide con la versión actual de `StockPolicy`.
- **422 Unprocessable Entity**:
  - `minimumAvailable < 0`: `"El stock mínimo no puede ser negativo."`
  - Formato decimal no válido, valor fuera de rango o más de tres decimales significativos; no redondear silenciosamente.
  - Fraccionario en artículo entero: `"El artículo ART-001 no permite decimales; el mínimo debe ser un número entero."`
  - Artículo desactivado: `"No se puede configurar el stock mínimo de un artículo inactivo."`
- **428 Precondition Required**: Falta la cabecera `If-Match` en el método `PUT`.
- **409 IdempotencyConflict**: la clave identifica otra intención; no descartar la referencia original. Ante 404 de resultado o 503, conservar la intención y comprobar de nuevo según el contrato común.
