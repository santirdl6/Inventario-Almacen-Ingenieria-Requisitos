# Persistencia HU-07 — evidencia en curso

**Fecha**: 2026-11-20. Base en [baseline.md](baseline.md).

## T004 — Pedido, líneas y mappings

Implementados tipos propios, instantáneas JSON, precisión exacta y estados. La versión de
cabecera se toca por cambios del agregado, envío y acumulados; líneas repetidas conservan
identidades independientes. No se añaden escritores de stock/recepción HTTP.

```bash
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --no-restore --filter FullyQualifiedName~PurchasingDomainTests --nologo'
```

**2/2 aprobadas**: transiciones/fijación/retry/no degradación de recibido, límites de
recibido y metadatos de precisión, rowversion y FK restrictivas. Se corrigió una llamada
de construcción de secuencia que no compilaba antes de obtener este resultado.
Esta comprobación todavía no acredita la migración SQL ni la integración de escritores,
que corresponden a T007/T008 y US1.

## T005/T006/T007 — Entrega, coordinación, evidencia y registro

Implementadas Delivery inmutable (snapshot/MIME/hashes), OutboxMessage mutable con
concesiones/ciclos/contadores y auditoría/eventos separados append-only; mappings con
unicidad y FK restrictivas al pedido, Identity y resultado común. El escritor de auditoría
solo añade evidencia al contexto del intento, sin guardar antes del resultado principal.

PurchasingArticleUsageContributor declara PurchaseOrderLine para uso y primer enlace,
consulta su FK real y solo admite ausencia de esquema acreditada por migración. Los fallos
SQL no se interpretan como ausencia de referencias. Registro de ambos contratos en DI.

La suite PurchasingDomainTests ampliada pasa **4/4**: añade metadatos de entrega única,
hash/precisión/longitudes/relaciones y el gate combinado de cobertura/primer escritor junto
a StockCore/HU-08. La primera referencia del escritor real y las carreras permanecen en US1;
no se completan aquí los IDs INT-HU07 del backlog global. La defensa SQL append-only y la
aplicación de la migración se verifican a continuación en T008.

## T008/T009 — Migración y registro ordinario

Generada `20261120111422_HU07PurchasingOutbox` sobre HU08MovementCounterparty, conservando
HU02 y todas las migraciones anteriores. Se añaden seis tablas, secuencia de numeración,
constraints/índices y DENY UPDATE/DELETE de entregas/auditoría/eventos al rol runtime.
El usuario de ejecución puede insertar pedidos con número generado por SQL y rowversion,
pero no modificar evidencia ni insertar rangos/JSON/estados contradictorios.

PurchasingMigrationTests verificó base vacía mediante IdentityFixture, reversión al esquema
previo vacío de compras y reaplicación conservando proveedor/artículo existentes. Comprobó
la numeración real, errores SQL 229 de permisos y 547 de CHECK con conexión runtime.

Registrados Purchasing/lector/opciones de moneda en Program y grupo de rutas sin exponer
handlers incompletos. El worker no se ha registrado todavía. Dos pruebas previas de
metadatos se actualizaron para exigir exactamente la nueva unión StockCore+PurchaseOrderLine;
se conserva además la comprobación específica de las tres referencias de StockCore.

```bash
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --no-restore --nologo && dotnet tool restore && dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api'
```

**Resultado final de fase 2: 135 aprobadas, 0 fallidas, 1 omitida V-14; build sin avisos
y EF sin cambios pendientes.** El primer pase integral tuvo dos expectativas antiguas
de metadatos fallidas (solo-stock); el resultado anterior es posterior a su actualización.
La referencia real desde HTTP/Tracker y sus carreras se acreditarán en US1, no mediante
las preparaciones técnicas de la prueba de migración.
