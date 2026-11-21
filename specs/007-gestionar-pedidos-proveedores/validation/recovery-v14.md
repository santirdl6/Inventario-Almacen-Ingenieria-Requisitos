# V-14 — Restauración técnica de compras y stock

**2026-11-21 · Resultado: aprobado en el entorno de prueba.** SQL Server 2025 de origen
y destino en instancias distintas del proyecto Compose `inventario-hu07-v14-validation`.
La suite backend completa, con el perfil de restauración habilitado, pasó **200/200, sin omisiones**.

```bash
docker compose -p inventario-hu07-v14-validation -f docker-compose.yml -f scripts/v14.compose.yml up -d --wait --wait-timeout 240 sqlserver sqlrestore
docker compose -p inventario-hu07-v14-validation -f docker-compose.yml -f scripts/v14.compose.yml --profile test run --no-deps --rm --volume "$PWD/backend:/source/backend" --env Logging__LogLevel__Default=Warning --entrypoint sh tests -c 'dotnet test Inventario.slnx --configuration Release --no-restore --nologo'
docker compose -p inventario-hu07-v14-validation -f docker-compose.yml -f scripts/v14.compose.yml stop --timeout 45
```

`Hu08RecoveryTests` restaura el backup completo y la cadena de logs hasta `STOPAT` y
ejecuta `DBCC CHECKDB` sin errores. Conserva auditoría, saldo y reserva, y reconcilia
un comprobante de StockCore dos veces con un solo efecto (saldo físico 12 → 16, reserva 3).
En compras, el origen acepta un mensaje mediante transporte controlado después del punto
recuperado; la copia vuelve a `Pending` con pedido congelado, MIME y adjunto idénticos,
mismo MessageId, autorización 202 original, cero intentos y sin el evento posterior de
aceptación. La aplicación restaurada arranca con correo pausado y el transporte no recibe
un segundo envío.

En el pase dirigido de la prueba ampliada: **RPO técnico simulado 2,178 s; RTO técnico,
hasta conciliación, 1,244 s**. Son tiempos de un fixture pequeño. No acreditan H5-02:
siguen pendientes el ejercicio operativo, la conciliación externa antes de reactivar el
correo, la validación con recepciones de HU-01 y la aprobación de RPO/RTO. Se detuvieron
los dos SQL de prueba después de validar.
