# Preparación HU-07 — T002/T003

**Fecha**: 2026-11-20. Base previa en [baseline.md](baseline.md).

## Soporte SQL/HTTP y transporte controlado

PurchasingTestSupport prepara proveedor/artículo mediante handlers HTTP reales de HU-08,
sesiones Identity independientes y configuración scoped de interceptores; el worker se
deshabilita para las pruebas no específicas. ControlledPurchaseMailTransport conserva
copias de bytes y coordina barreras de envío sin sleeps ni endpoints productivos.

Comando desde raíz (código actual montado en la imagen SDK de pruebas):

```bash
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --no-restore --filter "FullyQualifiedName~PurchasingSupportTests|FullyQualifiedName~PurchasingE2EProfileTests|FullyQualifiedName~MasterDataFixtureTests" --nologo'
```

**Resultado: 7/7 aprobadas**, tres nuevas de soporte/perfil y cuatro previas del fixture.
Incluye maestros reales, separación de permisos, transporte pausado y validación del
perfil/base/dominio de cuentas antes de abrir conexiones; perfil HU-08 conserva tres roles.

## Perfil E2E aislado

El perfil explícito hu07 requiere nombre `inventario-hu07-e2e-*`, base InventoryHu07E2E y
cuatro cuentas de una sola ejecución en hu07.e2e.test, incluida Comprador. Se comprueba
que no usa InventoryDev ni el puerto 8081; roles/dominio/base deben corresponder al perfil.
Las cuentas se retiran mediante bloqueo/security stamp, no se borran actores históricos.

Desde frontend:

```bash
npm run test:e2e:types
INVENTORY_E2E_PROFILE=hu07 HU08_E2E_PROJECT=inventario-hu07-e2e-implementation HU08_E2E_BASE_URL=http://localhost:8085 npm run test:e2e -- purchasing-fixture.spec.ts
```

**Resultado: tipos correctos y 1/1 E2E aprobado** (2,1 s de recorrido, 2,1 min con entorno).
El entorno se compiló con Node 24 en Docker, inicializó SQL aislado, autenticó gestor/
comprador, detuvo y restauró solo su SMTP y comprobó Mailpit `/readyz` y API de mensajes.
Terminó retirando cuentas, comprobando rechazo de login y deteniendo sus contenedores.
InventoryDev y sus servicios no se recrearon.

Las opciones reducidas del worker están preparadas en el perfil; su mapping/registro y
ejecución real de entregas corresponden a T027/T046. Esto no acredita aún envío de compras.
