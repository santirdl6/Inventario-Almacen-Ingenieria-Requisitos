# Ensayo técnico de restauración V-14

**Fecha**: 2026-11-19. **Alcance**: HU-08 y el núcleo de stock H0 de la PR #3
(`25d6df6`). [Guía común V-14](../../../.specify/memory/project/quickstart.md#8-restauración-y-apertura-operativa).

## Resultado

La prueba `Hu08RecoveryTests` pasó con SQL Server 2025 en dos instancias Docker
independientes. Restauró un backup completo y dos backups de log con checksum
hasta el punto elegido en el segundo log. `DBCC CHECKDB` terminó sin errores.

| Verificación | Evidencia |
|-------------|-----------|
| Corte y tiempos | Punto recuperado 11:50:00.5548666 UTC e incidente simulado 11:50:02.5911602 UTC. RPO técnico de 2,036 s y RTO técnico de 2,809 s, con el backup de log final, restauración, controles y conciliación incluidos en el RTO. |
| Maestros y referencias | Familia, vínculo del artículo, política inicial, marca de primera referencia y actualización del proveedor restaurados. Conservadas la auditoría del proveedor y las operaciones confirmadas antes del corte. |
| Diario y permisos | Se recuperaron tres entradas de stock, con físico 12 y reservado 3 conciliados con sus deltas. Se conservó el actor original y el rol de ejecución siguió sin permiso para borrar el diario. |
| Hecho posterior | El albarán sintético de 4 unidades quedó fuera del punto recuperado. Su registro y repetición con la misma clave en la instancia restaurada dejaron una entrada y un resultado nuevos, con físico 16 y reservado 3. |

El ejercicio usa el núcleo de stock para conciliar un comprobante sintético.
El ensayo técnico queda registrado para el cierre académico de T092. La restauración
conjunta con documentos de negocio y reservas documentales, la conciliación con el
circuito de contingencia, la revisión de reapertura y la aprobación de los objetivos
por los responsables quedan para una puesta en servicio. Los tiempos miden una base
pequeña de pruebas y se comparan con los umbrales propuestos en la guía común.

## Reproducción

Desde la raíz de la rama de trabajo. El proyecto Compose crea dos instancias sin
puertos publicados. Los backups pertenecen únicamente al volumen del ensayo.

```bash
docker compose -p inventario-hu08-v14 -f docker-compose.yml -f scripts/v14.compose.yml \
  up -d --wait --wait-timeout 180 sqlserver sqlrestore
docker compose -p inventario-hu08-v14 -f docker-compose.yml -f scripts/v14.compose.yml \
  --profile test run --no-deps --rm --volume "$PWD/backend:/source/backend" \
  --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --filter FullyQualifiedName~Hu08RecoveryTests --logger "console;verbosity=detailed"'
docker compose -p inventario-hu08-v14 -f docker-compose.yml -f scripts/v14.compose.yml down --volumes
```
