# Paquete de revisión compartida A+B — HU-07

**Estado de la revisión A+B de HU-07: realizada, según confirmación del usuario en la
sesión del 2026-11-21.** No se indicaron revisores nominales, referencia de versión ni
acta separada. El alcance de esta confirmación es HU-07; COORD-01 conserva su alcance propio de HU-08.

| Área | Revisar | Evidencia |
|---|---|---|
| Ejecutor y worker | Autorización persistida para comandos tipados internos; el camino HTTP mantiene actor, permisos, huella y replay. Lease/generación y resultado técnico no equivalen a aceptación SMTP. | [Contrato](../contracts/integration.md), [US2](us2.md), [US4](us4.md) |
| Locks y stock | Lote SQL parametrizado por fase, adquisición en orden ordinal con propietario de transacción, cinco segundos por recurso y rollback al primer rechazo. Compras no invoca StockCore para registrar mercancía. | [Protocolo común](../../../.specify/memory/project/contracts/stock-operations.md), [regresión](integration.md) |
| Migraciones | Cadena única: `HU07PurchasingOutbox`, `HU07PurchasingQueryIndexes`, `HU07ArticleLookupIndex`. Conservar restricciones y combinar snapshot al integrar HU-05; no sustituir migraciones ya aplicadas. | [Modelo](../data-model.md), [regresión](integration.md) |
| Lectura y consumidor | ETag solo del detalle propio; contexto y entrega identificados por versión, `skuPrefix` compatible con el catálogo existente. HU-01 toca cabecera al actualizar acumulados. | [HTTP](../contracts/api.md), [HU-01](hu01-handoff.md) |
| Transporte UI | Resolver permiso por método/ruta, grupo de intención de compra, aislamiento por actor/generación, recuperación tras logout. Contrastar coexistencia de rutas HU-05 al combinar ambos cortes. | [UI](../contracts/ui.md), [US4](us4.md), [US5](us5.md) |
| Correo | MailKit 5.0.0, SMTP fuera del commit, mismo MIME/MessageId en retry conocido; SMTP puede producir una copia tras acuse perdido. | [US2](us2.md), [US4](us4.md), [H5](h5-handoff.md) |

La [regresión local](integration.md) registra las comprobaciones disponibles del corte.
[Rendimiento](performance.md) conserva la evidencia y el estado de SC-007.
