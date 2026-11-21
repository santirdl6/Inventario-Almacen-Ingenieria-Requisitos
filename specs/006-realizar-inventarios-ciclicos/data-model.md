# Data Model: HU-04 — Conteos cíclicos y ajustes

Concreta para esta feature el [modelo común § Conteos](../../.specify/memory/project/data-model.md#conteos--persona-b--hu-04)
sin redefinirlo. Lo que lo amplía está en la tabla P-xx del [plan](plan.md#cambios-al-modelo-común-pendientes-de-a).
Decisiones en [research.md](research.md).

Migración: `HU04_CycleCounts`, sobre `20261121135335_HU03_Shipments` (D-14).

## CycleCount (`CycleCounts`)

| Campo | Tipo | Regla |
|-------|------|-------|
| Id | uniqueidentifier PK | Generado al crear. |
| CountCode | nvarchar(16), único | `CC-` + 12 primeros hexadecimales del Id (D-10). |
| State | nvarchar(24) | `Draft`, `Counting`, `AwaitingApproval`, `Confirmed`, `Cancelled` (D-02). |
| CreatedBy / CreatedAt | FK usuario / datetimeoffset | Al crear. |
| StartedBy / StartedAt | nullable | Obligatorios desde `Counting`. |
| SubmittedBy / SubmittedAt | nullable | Obligatorios desde `AwaitingApproval` (salvo cancelado antes). |
| ApprovedBy / ApprovedAt | nullable | Obligatorios solo en `Confirmed`: la aprobación registrada aunque cuente la misma persona. |
| CancelledBy / CancelledAt / CancelReason | nullable; motivo ≤ 500 | Obligatorios solo en `Cancelled`. |
| OperationId | uniqueidentifier nullable, único filtrado | Operación que confirma o cancela; obligatoria solo en estados cerrados. |
| AdjustmentMovementId | FK `StockMovements` nullable | Movimiento del ajuste; null si no hubo diferencias o se canceló. |
| Version | rowversion | `ETag` del detalle (D-11, D-12). |

CHECK `CK_CycleCounts_State`: los campos de cada estado según la tabla. `DENY DELETE`.
Trigger `TR_CycleCounts_ClosedImmutable` (D-07).

## CountLocation (`CountLocations`)

| Campo | Tipo | Regla |
|-------|------|-------|
| CountId | FK `CycleCounts` | PK compuesta con LocationId. |
| LocationId | FK `Locations` (`Restrict`) | Activa al crear y al iniciar. |
| IsFrozen | bit | 1 de `Counting` a cerrar; 0 en `Draft` y tras cerrar (D-04). |

Índice único filtrado `UX_CountLocations_Frozen` sobre `LocationId` `WHERE IsFrozen = 1`: una
ubicación en un solo conteo abierto. Índice `IX_CountLocations_Location` para el lector
`IsInOpenCountAsync`. Trigger `TR_CountLocations_Scope` (D-07).

## CountLine (`CountLines`)

| Campo | Tipo | Regla |
|-------|------|-------|
| Id | uniqueidentifier PK | Orden de las líneas en el detalle. |
| CountId, LocationId | FK compuesta a `CountLocations` | La ubicación pertenece al alcance. |
| ArticleId | FK `Articles` (`Restrict`) | Contributor de uso (D-08). |
| LotCode | nvarchar(64) | Normalizado como en `StockCore`; `SIN-LOTE` si el artículo no gestiona lote. |
| LotId | nullable, FK compuesta `(LotId, ArticleId)` a `Lots` | Null solo en un hallazgo de lote nuevo; el ajuste crea el lote (D-13). |
| IsFound | bit | 0 = esperada (creada al iniciar); 1 = hallazgo. |
| TheoreticalPhysical | decimal(18,3) ≥ 0 | Físico al iniciar; 0 en hallazgos. |
| CountedPhysical | decimal(18,3) nullable, ≥ 0 | Obligatorio para enviar; > 0 en un hallazgo. |
| Difference | columna calculada persistida | `CountedPhysical − TheoreticalPhysical`. |
| CountedBy / CountedAt | nullable | Último registro del contado. |
| Reason | nvarchar(500) nullable | Obligatorio para confirmar si `Difference ≠ 0`. |
| ReasonBy | nullable | Quien fijó el motivo. |
| CorrectsShipmentId | FK `Shipments` nullable (`Restrict`) | Salida confirmada que corrige (D-09). |

Único `UX_CountLines_Bucket` `(CountId, ArticleId, LocationId, LotCode)`. CHECK de cantidades, de
fracciones (en el comando, según el artículo) y de `CorrectsShipmentId` solo con `Difference ≠ 0`.
Trigger `TR_CountLines_OpenCount` (D-07).

## Movimiento de ajuste (diario común)

Un `StockMovement` por conteo confirmado con diferencias (D-06):

| Campo | Valor |
|-------|-------|
| Kind | `Adjustment` |
| SourceDocumentType / Id | `cycle-count` / `CycleCount.Id` |
| Reason | «Ajuste por conteo cíclico» (el motivo de cada diferencia está en `CountLine.Reason`) |
| SupportReference | `CountCode` |
| CounterpartyNotApplicableReason | «Ajuste de inventario: sin contraparte externa» |
| Actor / OccurredAt / RecordedAt | quien aprueba / instante de la confirmación / registro |
| Entradas | una por línea con diferencia: `AdjustUp` (+) o `AdjustDown` (−) por `|Difference|` |

## Transiciones

```text
Draft ──start──▶ Counting ──submit──▶ AwaitingApproval ──confirm──▶ Confirmed
  │                 │                        │
  └──────cancel─────┴──────────cancel────────┴──▶ Cancelled
```

| Transición | Efectos en una transacción |
|------------|----------------------------|
| start | `IsFrozen = 1` en todo el alcance; líneas esperadas con su teórico; primera referencia de sus artículos. |
| submit | Exige `CountedPhysical` en todas las líneas. Sin efectos en stock. |
| review (sin transición) | Motivos y `CorrectsShipmentId`; no cambia el contado. |
| confirm | Revalida teórico y reservado; movimiento de ajuste; `IsFrozen = 0`; aprobación y `OperationId`. No modifica líneas. |
| cancel | `IsFrozen = 0`; motivo y `OperationId`. Conserva líneas. Sin efectos en stock. |

## Invariantes comprobables (quickstart C-14)

1. Tras confirmar, el físico de cada bucket del conteo es igual a su `CountedPhysical`.
2. Σ de las entradas del ajuste por bucket = `Difference` de su línea; sin diferencias, sin movimiento.
3. Ninguna ubicación tiene `IsFrozen = 1` en un conteo `Confirmed` o `Cancelled`; como mucho una fila
   con `IsFrozen = 1` por ubicación.
4. Un conteo cerrado no cambia: ni cabecera, ni alcance, ni líneas.
5. Reservado de cada bucket ≤ físico tras el ajuste; el reservado no lo cambia el ajuste.
6. Una corrección no cambia la salida vinculada, `ShippedQuantity` de sus líneas de pedido ni los
   consumos o remanentes de sus reservas.
7. Diario = saldo para todos los buckets del conteo (`AssertCommitmentsMatchBalancesAsync` común).
