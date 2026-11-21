# Guía de validación — HU-01

**Estado**: escenarios previstos para la implementación. [Spec](spec.md) · [HTTP](contracts/api.md) · [UI](contracts/ui.md) · [integración](contracts/integration.md) · [guía común](../../.specify/memory/project/quickstart.md).

## Preparación y comandos

Con Docker activo, dependencias frontend instaladas y un proyecto Compose de prueba,
usar bases/actores aislados: comprobante identificado, comprador, operario, lector y
responsable de ajustes (para contingencia, una cuenta con ambos permisos). Crear artículos
activo entero y activo con lote, proveedor, destinos Storage y Receiving, y un pedido de 10
con envío aceptado por Mailpit bajo el flujo de HU-07. Preparar stock/reservas solo por
comandos reales. Para carreras usar conexiones independientes y barreras, no sleeps.

Desde raíz, para la suite SQL y montaje de código en el SDK de pruebas:

```bash
docker compose config --quiet
docker compose build initialize
docker compose --profile test run --build --rm tests
docker compose --profile test run --rm --volume "$PWD/backend:/source/backend" --entrypoint sh tests -c 'dotnet tool restore && dotnet ef migrations has-pending-model-changes --project src/Inventario.Api --startup-project src/Inventario.Api'
```

Desde `frontend/`:

```bash
npm test
npm run build
npm run test:e2e:types
npm run test:e2e -- 'receiving-.*\.spec\.ts'
```

La última selección es para el fixture HU-01 previsto: aprovisionar usuarios/puerto/base
aislados, capturar correo en Mailpit y retirar cuentas/contenedores del proyecto al terminar.
Repetir las suites afectadas de ubicación, maestros y compras. No usar el fixture de
rendimiento de HU-07 como evidencia de recepción.

## Escenarios verificables

| ID | Ejercicio y resultado esperado |
|---|---|
| Q01-01 · SC-001/V-02 | Partir de 10, guardar borrador de 6 sin efectos; confirmar 6 y después 4. Compras pasa de 0/10 Enviado a 6/4 Parcial y 10/0 Recibido, con ETag actualizado. Saldo +10, reservas sin cambio, ambas recepciones consultables. Acredita SC-006 de HU-07 con escritor real. |
| Q01-02 · SC-002/V-03 | Distribuir 2+4 en destino permanente y temporal con lote. Probar lote omitido o incompatible, destino inactivo, asignación que no cuadra, unidades enteras con fracción y destino ausente. Artículos repetidos mantienen su línea y suman en un solo bucket cuando corresponda. |
| Q01-03 · SC-002 | Reportar 7 contra 4 pendientes; intentar aceptar 7 sin efectos. Aceptar 4 con motivo conserva el exceso. Probar sustitución en texto, parcial normal y borrador compuesto solo por incidencias sin confirmación. |
| Q01-04 · SC-003/V-06 | Confirmar dos borradores de 6 contra 10 en paralelo: una aceptación y un rechazo íntegro. Probar dos entregas válidas creando a la vez el primer bucket y constatar suma exacta. |
| Q01-05 · SC-003/004/V-07/V-08 | Perder respuesta, consultar y recuperar mismo resultado, rechazar clave reutilizada para otra petición, repetir con clave nueva sobre documento confirmado. Probar revisión obsoleta, permiso insuficiente, fallo inducido antes del commit y conteo iniciado primero al integrar HU-04. |
| Q01-06 · SC-005 | Compensar 2 de 6 respetando bucket y pendiente; original inmutable. Rechazar excesos, saldo reservado o disminución física posterior no conciliada. Incorporar y repetir el comprobante con la misma y con distinta clave, incluso concurrentemente. Declarar un alcance con dos comprobantes, conciliar uno e intentar autorizar reapertura: sigue suspendido. Conciliar el segundo y repetir con una diferencia abierta o evidencia incompleta: sigue suspendido. Completar la evidencia, resolver la diferencia y comprobar autorización con actor, fecha y referencias. |
| Q01-07 · SC-006/INT-HU01-01/02 | Probar referencias reales de borrador y retirada, definición/borrado con compras vinculadas, y confirmación frente a desactivación en ambos órdenes. Conservar historial por artículo, período y responsable, soporte y contraparte. |
| Q01-08 · SC-007 | Ejecutar el perfil de carga indicado abajo. Exigir p95 visible ≤2 s en cada operación. |
| Q01-09 · SC-008 | Tres operarios representativos registran parcial, corrigen un dato inválido y localizan cantidades sin asistencia ni errores de registro no detectados. Registrar incidencias y repetir los casos corregidos. |

Reportar comandos, entorno, fecha, resultados y omisiones reales. El guion H5-02 requiere
la restauración operativa, aprobación de RPO/RTO y conciliación previa a la reapertura;
la evidencia automatizada de HU-01 y la restauración técnica precedente no la certifican.

## Perfil SC-007

En SQL Server x64 dedicado, preparar mediante comandos auditados 10.000 artículos y un
millón de entradas. Usar semilla 7001, con compras enviadas, borradores válidos de 1, 10 y
20 líneas y destinos repartidos entre sesiones; mantener un manifiesto sin credenciales.
Alternar consulta paginada y confirmación por sesión, sin inducir conflictos: 25 sesiones
de navegador independientes, 2 minutos de calentamiento y 10 de medición. Medir desde
Buscar o Confirmar hasta filas o resultado confirmado visibles; preparar formularios fuera
del intervalo y conservar errores y conflictos por separado. Exigir 1.000 observaciones
por tipo; si faltan, declarar inconcluso. Calcular p95 por tipo sobre éxitos con rango
más próximo, sin contar errores como éxitos ni aprobar con errores inesperados. Registrar
hardware, versiones, distribución, recuentos, errores y latencia de API como diagnóstico.
