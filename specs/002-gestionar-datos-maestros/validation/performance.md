# SC-005 — Búsqueda de artículos

**Fecha**: 2026-11-18. Fuentes: [spec](../spec.md), [plan](../plan.md),
[tarea T090](../tasks.md) y [aceptación técnica](acceptance.md).

Entorno: macOS ARM64, Docker Desktop, SQL Server 2025 amd64 emulado, API Release y
frontend de producción construidos en Docker con Node 24; runner Node 25.9.0 y
Playwright 1.65.0, Chromium headless 158.0.8127.18. Proyecto inventario-hu08-e2e,
InventoryHu08E2E, sin otros tests concurrentes. Los datos de ejecuciones anteriores
se conservan; cada muestra usa una familia y códigos únicos de la ejecución.

Preparación: 10.000 artículos creados mediante HTTP autenticado con auditoría, política
inicial y resultado del ejecutor; 50 se desactivan por el mismo protocolo. Se verifica
el total de 10.000 por familia. No se insertan saldos ni movimientos por DML.
Veinticinco sesiones independientes de consulta realizan ocho búsquedas cada una,
por código, nombre, familia y estado. Se mide desde el clic de búsqueda hasta respuesta
200 y filas visibles, incluidas las esperas del navegador. Umbral: al menos 190 de
200 búsquedas en ≤2 s, cero errores.

## Primera medición: instrumentación de trazas activada

| Métrica | Resultado |
|---------|-----------|
| Artículos de la ejecución | 10.000 |
| Sesiones / muestras | 25 / 200 |
| Errores | 0 |
| p95 | 2.365,918 ms |
| Muestras en ≤2 s | 171/200 (85,5 %) |
| Preparación | 53.277,925 ms |
| SC-005 | **No superado**. |

No se descartan muestras ni se atribuye el resultado a un componente sin medición.
Esta ejecución grabó trazas completas de preparación y navegadores. El archivo de prueba
se ajustó para desactivar esa instrumentación durante la medición y escribir las 200
muestras/errores en JSON tanto si supera el umbral como si falla. La siguiente ejecución
se registra por separado; este primer resultado se conserva.

Comando (desde frontend, sobre la misma imagen productiva ya construida):

```bash
HU08_E2E_SKIP_BUILD=1 npm run test:e2e -- e2e/master-data-performance.spec.ts
```

No mide un millón de entradas del diario ni confirmaciones de stock: esos componentes
no están integrados en H1. Tampoco acredita rendimiento de producción en SQL x64 nativo.


## Medición final: trazas desactivadas

Mismo código productivo e imagen; nuevas 10.000 altas auditadas, 25 sesiones y 200 búsquedas.
La base conservaba las 10.000 altas anteriores; no se vació para mejorar la medición.

| Métrica | Resultado |
|---------|-----------|
| Errores | 0/200 |
| p95 | 1.676,571 ms |
| Muestras en ≤2 s | 193/200 (96,5 %) |
| Preparación | 48.765,578 ms |
| SC-005 | **Superado en este entorno**. |

Las siete muestras superiores a 2 s se conservan en el cálculo; ninguna se descarta.
Datos completos: [200 muestras y metadatos](performance-2026-11-18.json).
La diferencia entre ambas ejecuciones no demuestra por sí sola dónde está cada coste.
Este resultado satisface el criterio local sin cambiar el umbral ni acreditar producción.
