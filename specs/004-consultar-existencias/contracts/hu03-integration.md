# Contrato de integración: HU-05 → HU-03 (salidas)

**Productor**: HU-05 (Persona B). **Consumidor**: HU-03 (Persona B), al planificar su spec.
**Base**: [protocolo de stock](../../../.specify/memory/project/contracts/stock-operations.md#cálculo-de-salida),
[modelo común](../../../.specify/memory/project/data-model.md#shipment-y-shipmentline).
HU-05 entrega el pedido de cliente mínimo y sus reservas. HU-03 los consume al servir sin crear
otra lectura del pendiente ni otro cálculo de reserva.

## 1. Lectura para la pantalla (HTTP)

`GET /api/v1/customer-orders/{id}` (Inventory.Read) devuelve, con `ETag`, solo datos propios:
por línea, `articleId`, `ordered`, `shipped`, `pending` y `reservedRemaining`; por asignación, el
bucket por ids (`articleId`, `locationId`, `lotId`) y su `remaining`
([contrato API](stock-api.md#get-customer-ordersid)). Los SKU, nombres, códigos y estados actuales
salen de `GET /api/v1/customer-orders/{id}/context`, sin `ETag` y con `orderVersion`
([contrato API](stock-api.md#get-customer-ordersidcontext)). La pantalla de salidas combina ambas
lecturas, como la de HU-05, y comprueba que `orderVersion` coincide con el `ETag`.

Con ellas, la pantalla de salidas muestra «reservado propio» por bucket. El «reservado ajeno» es
`GET /stock` del bucket menos el propio. Es información de ayuda: el servidor revalida al
confirmar. Como la salida cambia `ShippedQuantity` y renueva la versión de la cabecera (regla 4),
el `ETag` del detalle cambia con cada salida.

## 2. Lectura dentro del comando de salida (en proceso)

HU-05 expone en `Features/Stock/CustomerOrders` un lector sin estado que se usa con el
`OperationContext` del ejecutor, es decir, el mismo `DbContext` y la misma transacción:

```csharp
public interface ICustomerOrderReader
{
    // Después de bloquear document:customer-order:{orderId}. null si no existe.
    Task<CustomerOrderSnapshot?> ReadAsync(OperationContext context, Guid customerOrderId, CancellationToken ct);
}

public sealed record CustomerOrderSnapshot(Guid Id, string ExternalReference, string State, byte[] Version,
    IReadOnlyList<CustomerOrderLineSnapshot> Lines);

public sealed record CustomerOrderLineSnapshot(Guid Id, Guid ArticleId, decimal Ordered, decimal Shipped,
    IReadOnlyList<ReservationAllocationSnapshot> ActiveAllocations)
{
    public decimal Pending => Ordered - Shipped;
}

// Remaining > 0 solamente; LotId y LotCode del bucket.
public sealed record ReservationAllocationSnapshot(Guid AllocationId, Guid ReservationId,
    Guid LocationId, Guid LotId, string LotCode, decimal Remaining);
```

La forma exacta de los records se fija al implementar HU-05. Este contrato fija su contenido y
cuándo se puede llamar.

**Construido (T008, `7378daf`)**:

- Los records son exactamente los de arriba, en
  `backend/src/Inventario.Api/Features/Stock/CustomerOrders/ICustomerOrderReader.cs`, junto a la
  implementación `CustomerOrderReader`, registrada en `AddStock()`.
- El lector lee el estado guardado sin seguimiento, en la transacción del ejecutor, y no toma
  bloqueos.
- Las líneas y las asignaciones van ordenadas por Id. `ActiveAllocations` solo incluye las
  asignaciones de reservas `Active` con remanente mayor que cero.
- Para modificar, HU-03 carga sus propias entidades después de los bloqueos.

## 3. Reglas que HU-03 debe respetar

1. **Bloqueos**: en la fase de documentos, una sola llamada con
   `customer-order:{orderId}` y `shipment:{shipmentId}`, más `reservation:{id}` de cada reserva
   cuyo consumo vaya a cambiar. Orden ordinal: `customer-order:` < `reservation:` < `shipment:`.
   Después, ubicaciones y artículos, y por último `StockCore.ApplyAsync` (fuente
   `shipment`/`shipmentId`). Leer con `ICustomerOrderReader` **después** de estos bloqueos.
2. **Reserva propia por bucket**: `OwnReservedToConsume` = min(Q, Σ `Remaining` de las
   `ActiveAllocations` de la línea en ese bucket). Es el cálculo del contrato común; HU-05 no
   ofrece otro.
3. **Consumo**: HU-03 aumenta `ConsumedQuantity` de esas asignaciones (en cualquier orden estable,
   por ejemplo por Id) hasta sumar lo consumido. Recalcula el `State` derivado de cada reserva y
   aumenta `ShippedQuantity` de la línea. Si todas las líneas quedan con pendiente 0, pasa el pedido
   a `Fulfilled`.
4. **Tocar versiones**: marca como modificadas la cabecera del pedido y la de cada reserva cuyo
   consumo cambie (research D-07), para que los `If-Match` de HU-05 detecten la salida.
   HU-03 también debe actualizar la cabecera del pedido en **toda** salida, aunque no consuma
   reservas, porque cambia `ShippedQuantity`. El ETag fuerte de `GET /customer-orders/{id}` solo
   es válido si todo escritor del pedido lo renueva.
5. **Remanente sobrante**: si tras servir el remanente activo de la línea supera su nuevo pendiente,
   rechaza con `ReservedStockConflict`, como dice el contrato común. HU-05 aporta la liberación
   explícita para resolverlo. HU-03 no libera en silencio.
6. **Pedido**: solo se sirve contra pedidos `Open`. HU-03 no crea pedidos: usa el alta de HU-05.

## 4. Lo que HU-05 garantiza

- Σ `Remaining` de las asignaciones de un bucket = `StockBalance.Reserved` del bucket (prueba S-07).
- Σ remanente activo de una línea ≤ pendiente de la línea, tras cada reserva o liberación.
- `CustomerOrderLine` es único por artículo dentro del pedido (P-01).
- Las cabeceras del pedido y de la reserva cambian de versión en cada reserva o liberación.

## 5. Pruebas de contrato

HU-05 incluye S-18, que llama a `ICustomerOrderReader` dentro del ejecutor tras bloquear el pedido y
comprueba pendiente y remanente por bucket. **Construido**:

- `CustomerOrderTests`, sin reservas y con un pedido inexistente (null);
- `ReservationTests.S18_…`, con asignaciones en dos buckets, comparado con `GET /customer-orders/{id}`. HU-03 añadirá sus pruebas de salida (V-04) sobre este
contrato.
