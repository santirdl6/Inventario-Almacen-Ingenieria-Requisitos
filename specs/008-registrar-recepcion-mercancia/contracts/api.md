# Contrato HTTP — HU-01

Prefijo `/api/v1`; [contrato común](../../../.specify/memory/project/contracts/api.md), [modelo](../data-model.md), [integración](integration.md). Identidad y antiforgery comunes, no-store; las mutaciones requieren `Idempotency-Key` UUID. Cantidades en cadenas decimales exactas, fechas del hecho ISO 8601 con zona. Actor, estado, recibido y efectos se calculan en servidor.

| Método y recurso | Permiso | Éxito |
|---|---|---|
| GET `/receipts?purchaseOrderId=&articleId=&actorId=&from=&to=&state=&page=&pageSize=` | `Inventory.Read` | 200, página filtrada. Parámetros opcionales; período `[from,to)`, por fecha del hecho descendente e ID como desempate; 25 por defecto, máximo 100. |
| GET `/receipts/{id}` | `Inventory.Read` | 200, documento, compensaciones persistidas y ETag fuerte. |
| GET `/receipts/{id}/context` | `Inventory.Read` | 200, `receiptVersion`, `purchaseOrderVersion`, pendiente vigente, destinos/maestros aplicables y condiciones de revisión; sin ETag propio. |
| POST `/receipts` | `Receiving.Record` | 201, borrador, Location y ETag. |
| PUT `/receipts/{id}` | `Receiving.Record` | 200, reemplazo completo del borrador, `If-Match` requerido, nuevo ETag. |
| POST `/receipts/{id}/confirm` | `Receiving.Record` | 200, recepción/entrada confirmada, `If-Match` requerido, ETag resultante. Cuerpo `{}`. |
| POST `/movements/{id}/corrections` | `Inventory.Adjust` | 201, compensación y nuevo ETag de la recepción origen, requerido en `If-Match`. Solo originales de recepción en el corte HU-01. |
| POST `/contingencies/reconcile` | `Inventory.Adjust` y `Receiving.Record` | 201, recepción confirmada y comprobante conciliado. |
| POST `/contingencies/receiving-scopes` / GET, PUT `/contingencies/receiving-scopes/{id}` | `Inventory.Adjust` | 201/200, declaración, lectura o revisión del alcance suspendido. ETag fuerte; `If-Match` para PUT. |
| POST `/contingencies/receiving-scopes/{id}/reopen` | `Inventory.Adjust` | 200, autorización registrada con evidencia y ETag resultante. Requiere `If-Match` del alcance. |
| GET `/operations/{operationId}` | Política original | 200, resultado histórico; 404 no afirma fallo definitivo. |

La selección usa GET `/purchase-orders` con filtros `state=Sent` o `PartiallyReceived`; el detalle distingue líneas aún pendientes y exige aceptación del envío. El diario común de artículos conserva sus filtros. El detalle de recepción no mezcla etiquetas vivas ni pendientes de otras operaciones bajo el mismo ETag. El contexto declara ambas versiones; exigir coherencia antes de revisar y revalidar toda condición decisoria al confirmar.

### Peticiones y resultados

- Borrador: `{purchaseOrderId, occurredAt, supplierDocumentReference, supportNotApplicableReason, reason, lines, incidents}`. Línea `{id,purchaseOrderLineId,reportedQuantity,acceptedQuantity,discrepancyReason,allocations}`; asignación `{id,locationId,lotCode,quantity}`; incidencia `{id,receiptLineId?,kind,observedDescription,quantity?,observedUnit?,reason}`. UUID de hijos estables, no reutilizables en otro documento, compra inmutable. Permitir líneas a cero y borrador solo de incidencias. El borrador admite destinos aún sin completar; no admite cantidad/precisión inválida ni referencias ajenas. Confirmar exige soporte o motivo de ausencia, motivo de entrada, destino y suma exacta; nunca rectifica cifras automáticamente.
- Mutaciones devuelven `{operationId, resource}`; confirmación añade `movementId`, `purchaseOrderId`, `purchaseOrderVersion`, `purchaseState` y acumulados confirmados/pendientes por línea. No devolver éxito antes del commit. Devolver diferencias de la revisión sin confundirlas con las cifras congeladas del resultado.
- Compensación: `{correctionId,reason,reference?,lines:[{receiptAllocationId,quantity}]}`. Derivar compra, línea, unidad y bucket desde el original, no aceptar efectos o saldos arbitrarios. Un ID de compensación aplicado con nueva clave HTTP produce 409 con referencia consultable; repetición de la misma intención devuelve el resultado histórico.
- Contingencia: `{type:"Receive",voucherId,responsibleReference,receipt}`. `voucherId` se normaliza por trim y mayúsculas invariantes; el alcance es global, sin prefijos implícitos por usuario. El recibo de la operación queda ligado al comprobante confirmado. Otro `Idempotency-Key` con comprobante existente devuelve 409: `ContingencyAlreadyReconciled` con referencia si coinciden sus datos; `ContingencyConflict` si difieren. El ejecutor y GET de operaciones verifican la **política compuesta** en ambos casos. Conciliación y reapertura operativa son decisiones distintas.
- Declaración/revisión del alcance: compras y ubicaciones afectadas, comprobantes esperados y evidencia de las comprobaciones de diario, existencias y pedido; cada diferencia revisada declara su estado y referencia de resolución cuando esté cerrada. `/reopen` recibe `{}` y decide con el alcance persistido bajo lock: 409 `ContingencyScopeNotReconciled` si hay comprobante pendiente o ajeno, evidencia incompleta o diferencia abierta. No aceptar por el cliente el nuevo estado, el actor ni el instante de autorización. El resultado conserva la evidencia y solo acredita la autorización de ese alcance.

### Rechazos y reintentos

- 401/403 identidad o permiso; 400 antiforgery/clave/ETag inválidos; 404 recurso inexistente; 428 falta precondición, 412 revisión obsoleta; 422 forma, cantidades, soporte o asignación inválidos, con `errors` por campo/línea.
- 409 por envío no aceptado, `OverReceipt`, `ArticleInactive`, `CountScopeFrozen`, `DocumentAlreadyConfirmed`, dependencias de corrección, `InsufficientStock` o conflicto de intención. Devolver pendiente actual y línea afectada cuando sea seguro para el actor autorizado; no confirmar las otras líneas por separado.
- Se aplica huella normalizada del método/ruta/cuerpo con las reglas comunes; no derivarla de maestros, proyección viva ni de los buckets agrupados. Resolver replay antes de `If-Match` o del estado actual; conservar `ETag/Location` históricos. Ante red/503, comprobar la misma operación y respetar `Retry-After`.
