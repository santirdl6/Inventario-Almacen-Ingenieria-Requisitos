# Compensación — avance parcial

2026-11-21. Se incorporaron tablas `ReceiptCorrections` y `ReceiptCorrectionLines` mediante
la migración aditiva `20261121163017_HU01ReceiptCorrections`, con FK restrictivas y DENY
UPDATE/DELETE para `inventory_runtime`. La acción tipada `CorrectReceiptAsync` del núcleo
genera `Correction` vinculada a la entrada original, con `AdjustDown` y reserva intacta;
el comando valida el ETag, remanente por asignación, saldo disponible y disminuciones
posteriores bajo locks de documentos/ubicaciones/artículos.

`ReceivingCorrectionTests` pasó **2/2**: corrección autorizada de 6 a 4, replay y rechazo
de ID repetido con otra intención; rechazo de 7 sobre asignación original de 6; e intento
de borrado por usuario SQL runtime rechazado con 229. La selección completa de pruebas
SQL de Receiving pasó **14/14** antes de añadir la última aserción de DENY; la selección
de corrección pasó de nuevo 2/2 después. EF no informa cambios pendientes.

La regresión backend completa posterior obtuvo **201 aprobadas, 12 fallidas y una omitida**:
los fallos corresponden al arranque de fixtures SQL en `DatabaseBootstrapper`, antes de
ejecutar sus casos. Al repetir las clases/casos afectados de manera aislada, pasaron **12/12**.
No se presenta el pase global como aprobado. La suite frontend completa obtuvo **142/142**,
build y tipos correctos.

Faltan las carreras y rechazos por reservas y consumo posterior, la consulta de evidencia,
la interfaz y la matriz completa de contingencia/reapertura. No se cierran T029 ni
T034–T036 y T038–T040 por estas pruebas parciales; revisión A+B sigue pendiente.

La política compuesta de contingencia `Receiving.Reconcile` exige simultáneamente
`Receiving.Record` e `Inventory.Adjust`; no añade un permiso a los roles. Se integró
con el ejecutor y el lector de `OperationResult`, que conserva y vuelve a autorizar la
misma política. `ReceivingContingencyAuthorizationTests` pasó **1/1** tras reproducir
el fallo anterior: ambos permisos son necesarios en escritura, replay y consulta. La
regresión selectiva junto a `MasterDataOperationAuthorizationTests` y
`OperationQueryTests` pasó **3/3**.

Se añadieron los modelos y la migración `20261121170744_HU01ReceivingEvidence`: comprobante
único con evidencia protegida, y alcance con revisión, evidencia y autorización inmutable.
El usuario SQL runtime no puede alterar comprobantes ni borrar alcances; un trigger impide
modificar una autorización ya registrada. La prueba de migración y la de política compuesta
pasaron 2/2, y EF no detectó diferencias pendientes.

`ReceivingContingencyTests` pasó **3/3**: importar/repetir el mismo comprobante, rechazar
otra clave ante comprobante ya conciliado o contenido distinto, y dos importaciones
concurrentes con espera de lock observada. También se comprobó que el servidor rechaza
la reapertura por comprobante pendiente o diferencia abierta y acepta solo después de
registrar evidencia y resolución; sin permiso o con ETag obsoleto se rechaza, y la
autorización persistida no se puede modificar mediante SQL runtime. El procedimiento
operativo común mantiene el alcance suspendido hasta su conciliación; esta prueba no
acredita todavía el ejercicio operativo H5-02.

Tras incorporar ambas rutas de servidor, se repitió la selección completa
`dotnet test ... --filter FullyQualifiedName~Inventario.IntegrationTests.Receiving`:
**22/22 aprobadas**; `dotnet ef migrations has-pending-model-changes` no encontró
diferencias. La selección no reemplaza la regresión global ni los casos pendientes
de T030 (incluidos comprobante ajeno, evidencia incompleta y fallo inyectado).

Avance posterior: GET del documento incluye compensaciones y asignaciones bajo el ETag
actual, en la misma lectura coherente. La referencia opcional de compensación se conserva
mediante la migración aditiva `20261121172905_HU01CorrectionReference`; la selección de
corrección/migración pasó **3/3** y EF no detectó cambios pendientes. Se prepararon controles
de compensación y contingencia en frontend y se amplió el transporte común para exigir
ambos permisos de contingencia. La selección Vitest de recepción/operaciones pasó **19/19**;
build y tipos E2E también pasaron. La prueba dirigida comprueba rechazo de reapertura con
alcance suspendido, sin presentar autorización ficticia. Los recorridos E2E de evidencia,
las carreras de corrección y las garantías de los demás módulos siguen por completar.
