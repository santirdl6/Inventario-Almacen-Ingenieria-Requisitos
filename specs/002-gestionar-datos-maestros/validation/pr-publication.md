# Publicación e integración de HU-08 y HU-02

**Fecha**: 2026-11-19 (UTC). **Base integrada**: `45292a9`, merge del PR #6.

- [PR #6 — HU-02](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/6):
  R1–R4 corregidos en `dfc1fd1`,
  [aprobación de A publicada](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/6#pullrequestreview-5984781753)
  y fusión completada en `a/h0-identity-article`.
- [PR #7 — Cierre de HU-08 y coordinación](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/7):
  publicado desde `a/hu08-cierre-coordinacion`; revisión independiente solicitada a
  `nayara-cortes`. COORD-01 conserva esa revisión antes de fusionar los cambios de A;
  no se atribuye una autoaprobación ni una revisión no recibida.

La rama de A incorpora la base ya fusionada de HU-02. El snapshot conserva tanto
índices/CHECK de ubicaciones y auditoría Location como CounterpartyNotApplicableReason
y su CHECK. No se reescriben las migraciones previas. El fixture de historial usa una
posición física única por ejecución, compatible con la unicidad introducida por HU-02.

## Verificación conjunta

| Comprobación | Resultado |
|--------------|-----------|
| SQL Server real | 127 aprobadas, 0 fallidas; V-14 específica omitida por su entorno separado. |
| Vitest | 108/108, 30 archivos. |
| Playwright funcional | 15/15 en una pasada, incluyendo ubicaciones, recuperación tras respuesta perdida e historial poblado. |
| Build y tipos | Correctos, también publicación API y frontend con Node 24 en Docker. |
| EF 10.0.13 | Sin diferencias de modelo. |

Se usaron los comandos SQL completos de [convergencia](convergence.md). Desde frontend:

```bash
npm test
npm run build
npm run test:e2e:types
HU08_E2E_PROJECT=inventario-pr6-e2e-integracion HU08_E2E_BASE_URL=http://localhost:8084 HU08_E2E_BROWSER_CHANNEL=chrome npm run test:e2e -- --grep-invert SC-005
```

La primera ejecución frontend simultánea con SQL agotó el límite de 5 s en una prueba
de router. El archivo aislado pasó 2/2 y la suite posterior completa pasó 108/108 sin
modificar ni debilitar esas aserciones; el primer intento no se cuenta como aprobado.
El proyecto E2E tiene base/volúmenes dedicados; las cuentas se retiraron y los contenedores
se detuvieron. InventoryDev no se modifica. SC-005 y la aceptación operativa H5 conservan
la evidencia y condiciones registradas previamente.
