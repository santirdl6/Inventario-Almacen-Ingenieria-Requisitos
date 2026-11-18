# Evidencia del incremento US1 — Familias

**Fecha**: 2026-11-17. **Tareas**: T024–T034.
**Estado**: MVP funcional verificado; no aceptación completa de HU-08 ni revisión conjunta realizada.
**Fuentes**: [spec](../spec.md), [HTTP](../contracts/api.md),
[fundamentos](foundation.md) y [quickstart](../quickstart.md).

## Incremento disponible

- GET/POST/PUT /api/v1/families y GET de detalle, con permisos/CSRF, DTO y ETag.
- Alta activa, código único normalizado incluidos inactivos, edición y estados conservando UUID.
- Maestro, auditoría y resultado de intención atómicos; replay conserva cuerpo/ETag/Location.
- Listados consistentes por q/state/página, orden estable y detalle no cacheado.
- Login visual, menú por permisos, rutas lazy, tabla/formulario PrimeNG, revisión de estados,
  errores por campo, readonly del lector y comprobación/reintento de intención pendiente.
- /maestros apunta a familias en este MVP; el destino de artículos pertenece al siguiente corte.

## Ejecuciones y resultados

- MasterDataFamilyTests: 2 pruebas HTTP/SQL reales dentro de la suite final de 47 casos.
  Incluyen normalización, un alta por intención, Location/ETag, modificación/desactivación,
  versión obsoleta, replay con versión original, código ocupado por inactivo, filtros,
  auditoría/resultados sin duplicados, campos internos rechazados y lector sin escritura.
- FamilyFormComponent: 3 unitarios de incompletos, código duplicado sin pérdida de datos,
  readonly y cancelación de revisión antes de enviar PUT. FamilyListComponent: 1 regresión
  de input/select/búsqueda que conserva filtros; forman parte de los 15 unitarios finales.
- Build de producción de la SPA y publicación backend correctos mediante Dockerfile.
- Suite E2E completa: **5 aprobadas, 0 fallidas**; 3 smoke API previas más 2 casos reales
  de Chromium en master-data-families.spec.ts. Se reutilizó la imagen construida con el
  mismo código al ejecutar la comprobación final con HU08_E2E_SKIP_BUILD=1.

Comando final desde frontend, con Chromium instalado y Node 24.15.0:

```bash
HU08_E2E_SKIP_BUILD=1 npm run test:e2e
```

El recorrido de gestor hace login, crea, consulta, modifica nombre, revisa/desactiva,
busca por código/estado inactivo, reactiva y verifica que un duplicado conserva el nombre
introducido. El de lector consulta campos deshabilitados y verifica además un 403 real
al intentar escribir por API: ocultar botones no es la barrera de seguridad.

El fixture provisiona Identity en InventoryHu08E2E, retira sus cuentas sin borrar actores,
confirma el rechazo de login posterior y conserva volúmenes. El proyecto dedicado terminó
detenido después de la suite final. No se ha reiniciado ni sembrado InventoryDev.

## Fallos encontrados y corregidos

Se corrigió el selector de ConfirmDialog para PrimeNG 22 y la sincronización del test
con la carga real del detalle. También se eliminó una navegación redundante al guardar
el mismo registro.

La prueba Chromium detectó una ventana de activación: los campos de la lista podían verse
y rellenarse antes del primer enlazado de controles, que luego restablecía valores iniciales.
La traza mostró los valores DOM correctos antes de ngOnInit y el modelo vacío al buscar.
La lista/formulario usan fieldset estáticamente deshabilitado hasta su carga/enlazado;
login espera al primer render. No se añadieron sleeps a E2E. Se retiraron los diagnósticos
temporales y el mismo recorrido pasó, seguido de la suite completa.

## Cobertura disponible

Q-01/Q-02/Q-10 del corte de familias y las garantías comunes de Q-03/Q-04/Q-06/Q-08.
No se acredita la asociación de familias con artículos ni las carreras de asignación:
su validación corresponde a T052 después de US2. SC-006 exige trabajadores reales con
los tres catálogos y no se considera aprobada mediante estas suites automatizadas.
