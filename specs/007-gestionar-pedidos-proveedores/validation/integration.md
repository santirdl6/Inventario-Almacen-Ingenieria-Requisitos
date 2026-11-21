# Regresión integrada HU-07

**Corte local: 2026-11-21.** Verificación funcional del código con los dos índices
aditivos de rendimiento. T056 acredita la regresión del corte entregado. El último ensayo
solo cambió el runner, sus datos sintéticos y el override de Compose; no modificó código
productivo. Se verificaron la compilación del fixture, la configuración y la ejecución
completa. [T055/SC-007](performance.md) sigue abierto; la regresión debe repetirse sobre
los cambios productivos que se hagan para resolverlo. La [revisión A+B](shared-review.md)
cuenta con confirmación del usuario; H5 conserva su aceptación operativa.

## Backend, publicación y esquema

```bash
docker compose config --quiet
docker compose build initialize
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet tool restore && dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api && dotnet test Inventario.slnx --configuration Release --no-restore --nologo'
```

Publicación Docker y validación Compose correctas. El pase ordinario obtuvo **199 aprobadas,
0 fallidas y 1 omitida** en SQL Server real (V-14 requiere otra instancia). El nuevo pase
en dos instancias aisladas obtuvo **200 aprobadas, 0 fallidas y 0 omitidas**, incluido el
[ensayo técnico V-14](recovery-v14.md). EF no detecta cambios de modelo pendientes.
Incluye H0/Identity, ejecutor/bloqueos, StockCore, HU-08, HU-02 y compras. La rama HU-05
de B no está integrada en este corte. La suite aplica la cadena desde cero y comprueba
bajar al esquema anterior a compras y reaplicar conservando datos, además de CHECK/DENY
de ejecución. Se verifica pausa/reanudación del worker y recuperación tras recrear el host.

## Cliente y navegador

Desde `frontend/`:

```bash
npm test && npm run build && npm run test:e2e:types
INVENTORY_E2E_PROFILE=hu07 HU08_E2E_PROJECT=inventario-hu07-e2e-implementation HU08_E2E_BASE_URL=http://localhost:8085 HU08_E2E_BROWSER_CHANNEL=chrome HU08_E2E_SKIP_BUILD=1 npm run test:e2e -- 'purchasing-.*\.spec\.ts' --output=test-results/hu07-regression
```

**138/138 Vitest**, 39 archivos (91,09 % de líneas de la suite global); build y tipos
correctos. **6/6 E2E de compras** juntos en Chrome con API/SQL/Mailpit reales y la
imagen actual. Incluyen borrador, paginación e histórico, aislamiento entre cuentas,
respuesta perdida, fallo SMTP y retry, y comparación del adjunto original byte a byte.
Las cuentas se retiraron y el proyecto E2E se detuvo. La cobertura no equivale a
aceptación con compradores reales.

Tras T061 se repitieron frontend y publicación: **139/139 Vitest** (91,16 % de líneas),
build/tipos y publicación Docker correctos, y **6/6 E2E de compras** con la imagen actual.
La prueba de regresión verifica que la vista oculta no consulta, la vuelta a primer plano
solo inicia una consulta aún pendiente y destruir la vista elimina los disparadores.
También pasaron **12/12 E2E funcionales HU-02/HU-08** (ubicaciones y cinco suites de
maestros; perfil `inventario-hu08-e2e-hu07-regression`, puerto 8082). Se retiraron las
cuentas y se detuvieron sus contenedores.

Evidencia: [US1](us1.md), [US2](us2.md), [US3](us3.md), [US4](us4.md), [US5](us5.md),
[rendimiento y estado de T055](performance.md).
