# Evidencia del primer corte de HU-01

2026-11-21. `dotnet test Inventario.slnx --configuration Release --no-restore --filter FullyQualifiedName~Receiving --nologo` obtuvo 11/11 en la selección. La migración `20261121152902_HU01Receiving` se aplicó sobre base vacía del fixture y se reaplicó desde `20261121025020_HU07ArticleLookupIndex`, conservando una compra enviada; SQL rechazó las infracciones de CHECK/FK ensayadas. EF no detectó cambios de modelo pendientes.

El contrato HTTP ensayó antiforgery, permisos, decimales, ETag del borrador y revisión obsoleta. Las pruebas de confirmación ejercitaron 6+4 y 6+6, recuperación histórica, cantidades y ETag de compra. Se comprobó protección heredada al retirar referencias y carrera de desactivación/confirmación en ambos órdenes con barrera SQL. Queda ampliar la matriz de V-03, primer bucket concurrente, rollback y carreras de edición/borrado para completar el checkpoint íntegro.

La selección Vitest de recepción y navegación pasó 5/5; build, tipos y publicación Docker correctos. `receiving-partial.spec.ts` pasó en Chrome, con Mailpit y SQL aislados (`inventario-hu07-e2e-hu01`, puerto 8084): 6 en dos destinos (2+4), pendiente 4, entrega de las últimas 4, compra `Received`. Se retiraron cuentas y se detuvo el proyecto E2E.

Ampliación posterior: **3/3 pruebas SQL** de lote obligatorio/conservación del lote
normalizado y de confirmaciones 6+6/6+4 sobre el primer bucket, coordinadas con barrera
después de escribir el pedido y espera observada del applock. El segundo comando vuelve
a leer bajo el lock: rechaza el exceso o confirma la suma exacta de 10. Aún falta completar
la matriz de referencias T012 y los demás casos descritos en T010–T013 antes de marcar el
checkpoint íntegro.

Otra prueba SQL confirmó dos líneas del mismo artículo, con precios y cantidades
independientes, en una ubicación/lote común: el pedido conserva 6 y 4 por línea mientras
el diario contiene una sola entrada agrupada de 10. Resultado **1/1**.
