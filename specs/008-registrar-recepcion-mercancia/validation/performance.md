# Perfil de rendimiento SC-007 — HU-01 Recepciones

**Estado**: Documentación del perfil Q01-08 / SC-007 implementado en `scripts/measure-hu01-receiving.mjs`.

## Protocolo y parámetros de medición

- **Semilla**: `7001`.
- **Sesiones concurrentes**: 25 sesiones de navegador independientes con cuentas de operario autenticadas.
- **Volumen de datos base**: 10.000 artículos, 10.000 pedidos y 1.000.000 de entradas de stock históricas.
- **Ventana temporal**: 2 minutos (120 s) de calentamiento y 10 minutos (600 s) de medición activa continua.
- **Muestra requerida**: Mínimo 1.000 observaciones válidas por cada tipo de operación (`query` y `confirm`). En caso de no alcanzar 1.000 observaciones por tipo, la prueba se declara **INCONCLUSIVE**.
- **Criterio de aceptación (p95)**:
  - Tiempo visible de extremo a extremo (desde la acción del usuario hasta el renderizado de la tabla o estado confirmado en DOM): **p95 ≤ 2.000 ms** (2,0 s).
  - Tasa de errores inesperados: 0.

## Operaciones evaluadas

1. **Consulta paginada (`query`)**: Navegación y filtrado en `/recepciones` con paginación de 25 elementos por página, alternando páginas inicial, intermedia y final, con filtros de estado y fechas.
2. **Confirmación y consulta de recepción (`confirm`)**: Confirmación de borradores de 1, 10 y 20 líneas con asignaciones por ubicación/lote, registrando el tiempo hasta que el estado `Recepción confirmada` es visible en la interfaz.

## Script de ejecución

El arnés automatizado reside en [scripts/measure-hu01-receiving.mjs](../../../scripts/measure-hu01-receiving.mjs).
Ejecución:

```bash
node scripts/measure-hu01-receiving.mjs
```

El script genera artefactos inmutables en `frontend/test-results/hu01-performance/<run-id>/`:
- `report.json`: resumen de hardware, versión de Chromium/Node, recuentos iniciales/finales, percentil p95 por operación y veredicto (`PASS`, `FAIL` o `INCONCLUSIVE`).
- `samples.json`: traza completa de muestras individuales con tiempos de acción, respuesta HTTP y renderizado en DOM.
