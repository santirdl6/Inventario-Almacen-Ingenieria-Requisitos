# US3 — Consulta y seguimiento de compras

**Fecha**: 2026-11-20 · T033–T039 verificadas localmente.

GET /purchase-orders implementa número por prefijo, proveedor histórico, estado logístico,
período UTC [from,to), paginación 1/25/máximo100 y count/filas en SNAPSHOT. Una página
fuera de rango conserva el total real. No consulta datos actuales para reescribir snapshots.
El primer test de listado falló por 404 antes de implementar su ruta.

Pruebas SQL selectivas:

```bash
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --no-restore --filter FullyQualifiedName~PurchaseOrderQueryTests --nologo'
```

**3/3 aprobadas**: filtros/páginas/lectura, count pausado mediante interceptor mientras
otra conexión inserta (mismo snapshot devuelve count/filas anteriores coherentes), y
proyección técnica de recibido 6/pendiente4 y recibido10/pendiente0 con ETag renovado/CHECK.
Esta preparación no genera Receipt/stock ni completa SC-006/V-02/V-06: siguen en HU-01.

Frontend: listado definitivo sustituye la raíz provisional, mantiene operaciones pendientes,
filtra proveedores incluso inactivos y conserva filtros/página al ir a detalle/edición/envío.
Errores conservan filas previas; página vacía con total positivo permite volver a una válida.
Detalle muestra cantidades de solo lectura y progreso/documento congelados; polling Pending
se limpia al destruir/cambiar actor, y una respuesta antigua no sustituye una consulta reciente.

```bash
npm test -- --include='src/app/features/purchasing/**/*.spec.ts' --include=src/app/core/operations/purchasing-pending-operation.spec.ts
npm run test:e2e:types
INVENTORY_E2E_PROFILE=hu07 HU08_E2E_PROJECT=inventario-hu07-e2e-implementation HU08_E2E_BASE_URL=http://localhost:8085 HU08_E2E_BROWSER_CHANNEL=chrome npm run test:e2e -- purchasing-query.spec.ts
```

**24/24 frontend de compras/transporte**, tipos correctos y **1/1 E2E** (5,8 s de recorrido,
2,3 min con entorno). Chrome/API/SQL reales: 26 compras de un proveedor, renombrado y
desactivación posteriores conservan historia, lector sin gestión navega página 2→detalle→
página2 con filtros intactos, página99 vacía recupera última válida y un 503 inyectado solo
en la consulta del navegador no borra filas ni simula ausencia. Búsqueda por número vuelve
a una coincidencia. Cuentas retiradas y contenedores propios detenidos al terminar.

Cobertura: FR-018–022/029/030 y Q07-11/12. No acredita recepción real, rendimiento SC-007,
revisión A+B ni aceptación/restauración operativas H5.
