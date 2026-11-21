# Línea base de implementación HU-07

**Fecha**: 2026-11-20. **Tarea**: T001 de [tasks.md](../tasks.md).
**Rama**: `a/hu08-cierre-coordinacion`. **HEAD**: `b13bcd9`.
Se conservan las modificaciones documentales locales previas de HU-07/base global.
Esta evidencia corresponde al código anterior a la implementación de compras.

## Entorno y esquema

- Docker Engine 29.1.3, SQL Server 2025 amd64 de Compose saludable.
- Backend net10.0, EF/SqlServer/Identity/OpenAPI 10.0.13; dotnet-ef 10.0.13 en contenedor.
- Frontend Angular/CLI/CDK 22.2.3, PrimeNG 22.1.4, temas 3.1.0, TS 6.0.3,
  RxJS 7.8.2, Vitest 5.1.0 y Playwright 1.65.0.
- Host Node 25.9.0; advierte versión no LTS. La baseline de despliegue sigue siendo
  Node 24 ≥24.15.0 del Dockerfile; no se cambia engines ni el lockfile.
- La cadena incorpora HU02_Locations, HU02_LocationCharacters y termina en
  `20261119171332_HU08MovementCounterparty`. No se modifica ninguna migración compartida.
- `.gitignore` y `.dockerignore` cubren dependencias, bin/obj, resultados/cobertura,
  secretos locales y temporales. El paquete npm es privado, no requiere `.npmignore`.

## Comandos y resultados

Desde raíz:

```bash
docker compose --profile test run --build --rm tests
docker compose --profile test run --rm tests
docker compose --profile test run --rm --entrypoint sh tests -c 'dotnet tool restore && dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api'
```

La primera ejecución reconstruyó la imagen y agotó el límite de terminal de 120 s
cuando la suite empezaba: no se contabiliza como aprobada. La segunda usó la imagen ya
construida, terminó en 1 min 10 s y obtuvo **127 aprobadas, 0 fallidas, 1 omitida**.
La omisión es V-14, que requiere el entorno separado de restauración; no se presenta
como validación operativa realizada. El modelo EF no tiene diferencias pendientes.
Los fixtures usan bases/logins aleatorios y el usuario runtime, sin modificar InventoryDev.

Desde `frontend/`:

```bash
npm test
npm test -- --include=src/app/features/locations/locations.routes.spec.ts
npm test
npm run build
npm run test:e2e:types
```

La primera suite, simultánea con la reconstrucción Docker, obtuvo 107 aprobadas y un
timeout de 5 s en la navegación existente de ubicaciones. La prueba aislada pasó 2/2;
la suite completa posterior, sin reconstrucción Docker simultánea, pasó **108/108**
en 30 archivos. No se alteraron sus aserciones ni tiempos para obtener el resultado.
Build de producción y tipos E2E correctos. No se reejecutaron aquí los navegadores E2E;
el perfil aislado de compras se prepara en T003 y sus recorridos en las historias.

## Resultado

Línea base técnica comprobada; T001 satisfecha. No acredita todavía comportamiento,
rendimiento, integración receptora, aceptación humana ni correo de HU-07.
