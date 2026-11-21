# Validación y ejecución — HU-06

**Estado**: guía para la futura implementación, no resultados ejecutados. Base: [spec](spec.md),
[contratos](contracts/api.md), [integración](contracts/integration.md) y
[guía común](../../.specify/memory/project/quickstart.md).

## Entorno

Usar SQL Server 2025 de pruebas y bases aisladas, Docker activo, Node 24 y un navegador
compatible. Crear cuentas con permisos independientes de gestión de mínimos, lectura de
alertas, consulta, maestros y compras; no añadir roles productivos. Crear artículo activo,
artículo entero, destinos y lote por flujos reales. Preparar físico/reservas mediante
operaciones de stock, sin DML de saldos. El enlace de compra usa correo de captura y no
genera pedido al abrirse.

```bash
docker compose config --quiet
docker compose build initialize
docker compose --profile test run --build --rm tests
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet tool restore && dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api'
```

Desde `frontend/`: `npm test`, `npm run build`, `npm run test:e2e:types` y las pruebas
Playwright de alertas con fixture y proyecto aislados. Los fixtures de HU-06 se implementan
en la fase de tareas; conservar compatibilidad de los perfiles HU-07/HU-08. No ejecutar
pruebas de carga simultáneamente con otras suites.

## Escenarios

| ID | Preparación y resultado esperado |
|---|---|
| Q06-01 · FR-001–004 | Artículo con mínimo 0: cambiar a 10, consultar ETag y reproducir la misma intención. Rechazar negativo, fracción no admitida, precisión excesiva, versión obsoleta y falta de permiso sin cambio parcial. Cambio de mínimo y alerta se confirman juntos. |
| Q06-02 · V-11 | Físico 12, reservado 8, mínimo 5 → disponible 4 y sugerido 1. Recibir o liberar hasta disponible ≥5 resuelve en la primera consulta. Mínimo 0/disponible 0 no alerta. |
| Q06-03 · SC-002 | Dos movimientos concurrentes reales, con barreras, bajo el mismo artículo: a lo sumo un episodio activo, snapshots del estado confirmado final. Probar buckets nuevos, varios lotes/ubicaciones y cambios EF sin guardar. |
| Q06-04 · INT-HU06-01 | Desactivar con alerta activa resuelve en el mismo commit; reactivar bajo mínimo abre otro episodio. Forzar rollback y ambos órdenes frente a movimiento y cambio de política; conservar documentos y alertas históricas. |
| Q06-05 · FR-012/013/016 | `Alerts.Read` consulta y `Inventory.Read` sin ese permiso no accede. Comprador autorizado abre `/compras/nuevo` con artículo/cantidad editables, sin crear pedido ni SMTP; revalidar artículo inactivo o parámetro inválido. |
| Q06-06 · integridad | Inyectar fallo antes del commit y respuesta perdida después; comprobar stock, política, alerta, auditoría y OperationResult. Misma clave devuelve resultado histórico y otra intención no duplica efectos. Verificar historial de alerta ante intento de borrar artículo. |

## Medición SC-004

`node scripts/measure-hu06-alerts.mjs smoke` comprueba el instrumento y la ruta de carga en una base aislada. Para el ensayo completo, configurar `Testing__SqlConnection` con una conexión administrativa a un SQL Server x64 **dedicado a pruebas**, accesible desde Docker, y ejecutar `node scripts/measure-hu06-alerts.mjs full`. El fixture inicializa y retira su propia base de pruebas; el modo `smoke` no acredita SC-004.

En SQL Server x64 dedicado, preparar por comandos auditados 10.000 artículos y un millón
de entradas; fijar semilla 6006, 25 sesiones y conjuntos equivalentes de 1 y 20 líneas con
estado inicial comparable. Medir por separado la duración del evaluador dentro de la
transacción mediante instrumentación de pruebas, desde entrada al evaluador hasta salida,
sin incluir espera por locks, preparación de datos ni trabajo ajeno. Calentar 2 minutos y
medir 10 minutos; conservar al menos 1.000 observaciones exitosas, tamaños, errores,
conflictos y esperas por locks por separado. Calcular p95 por rango más próximo; SC-004
exige **p95 <25 ms** y cero errores inesperados. Si falta muestra, declarar inconcluso.

La medición del componente no sustituye el objetivo global de acción→resultado visible;
medir también consulta y confirmación con su perfil común. Registrar versión, CPU/RAM/OS,
arquitectura, límites de Docker, volumen, comandos y resultados antes/después de cualquier
optimización. Validar carga y concurrencia sin otras suites en paralelo. La aceptación H5 y
las revisiones externas no se deducen de estos resultados.
