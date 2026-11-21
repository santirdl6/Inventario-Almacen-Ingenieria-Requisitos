# Tareas de integración por módulo y puesta en servicio

**Fecha**: 2026-11-19. **Origen**: cierre técnico de
[HU-08](../002-gestionar-datos-maestros/validation/acceptance.md).
**Decisión**: el usuario solicita cerrar HU-08 y asignar a cada consumidor el trabajo
que solo puede realizarse cuando exista ese módulo. Los IDs de origen se conservan.
Este es el backlog receptor mientras las futuras specs no dispongan de directorio.
Al crearlas, incorporar sus tareas en `tasks.md` y enlazar el ID receptor; completar
el checkbox aquí solo al tener implementación y evidencia en el módulo correspondiente.

Base: [constitución](../../.specify/memory/constitution.md),
[plan global](../../.specify/memory/project-plan.md), [reparto](spec.md), [hitos](plan.md),
[modelo](../../.specify/memory/project/data-model.md),
[API](../../.specify/memory/project/contracts/api.md),
[protocolo de stock](../../.specify/memory/project/contracts/stock-operations.md) y
[validación conjunta](../../.specify/memory/project/quickstart.md).

## Contrato de aceptación de cada nuevo escritor de referencias

Cada módulo prueba sus escritores reales, no un doble de otro módulo: aporta lector
autoritativo y enlace a ArticleReferenceTracker dentro del bloqueo/transacción comunes;
acredita crear/retirar la referencia de borrador manteniendo la definición bloqueada;
rechaza borrar mientras haya referencias, incluso sin movimientos o saldo; prueba
referencia/edición y referencia/borrado con conexiones/barreras independientes en ambos
órdenes. La marca histórica por sí sola no impide el borrado tras retirar referencias.
Estos son los criterios FR-009/010/011/019 de HU-08 que consume cada nuevo escritor.
Un escritor no se habilita sin esa cobertura; HU-08 no necesita reabrirse para añadirlo.

## HU-07 — Compras — Persona A

- [X] INT-HU07-01 Integrar PurchaseOrder/Line con el contrato de aceptación de escritores: primera línea de borrador, retirada de la última línea, definición vigente e imposibilidad de borrado con documentos. Conservar instantáneas históricas de artículo/proveedor y cobertura SQL real. Origen HU-08 T035/T076/T100/T101; US2.8, FR-009/010/019. [Evidencia HU-07 US1](../007-gestionar-pedidos-proveedores/validation/us1.md).
- [X] INT-HU07-02 Verificar editar, desactivar y reactivar el proveedor con pedidos existentes: identidad/documentos/instantáneas intactos, bloqueo de nuevas compras de proveedor o artículo inactivo bajo los recursos documentales compartidos y reanudación al reactivar. Origen HU-08 T076/T078/T102/T103; US3.3/3.4/3.6, FR-012/013, SC-007. [Evidencia HU-07 US1](../007-gestionar-pedidos-proveedores/validation/us1.md) y [US2](../007-gestionar-pedidos-proveedores/validation/us2.md). Aquí SC-007 es el criterio de origen de HU-08, no la prueba de carga de HU-07.

**Seguimiento de rendimiento HU-07 (T055/SC-007)**: la consulta cumple; alta y corrección
superan el p95 de 2 s. Conservar la tarea abierta para perfilar POST/PUT bajo carga y
repetir el protocolo completo en un entorno SQL Server x64 representativo. La entrega
funcional no acredita ese objetivo. [Evidencia](../007-gestionar-pedidos-proveedores/validation/performance.md).

## HU-01 — Recepciones — Persona A

- [X] INT-HU01-01 Integrar Receipt/Line/Allocation con el contrato de aceptación de escritores desde el borrador; probar las referencias directas o indirectas reales, la retirada y las carreras con edición/borrado. Generar cantidades exclusivamente por el núcleo común con soporte y contraparte o sus motivos explícitos. Origen HU-08 T035/T064/T066/T075/T076/T100/T101; FR-009/010/019. [Evidencia HU-01 US1](../008-registrar-recepcion-mercancia/validation/us1.md) y [aceptación](../008-registrar-recepcion-mercancia/validation/acceptance.md).
- [X] INT-HU01-02 Rechazar recepción nueva de artículo inactivo y probar confirmación/desactivación en ambos órdenes; conservar compra, recepción, instantáneas y trazabilidad previas. No acreditar este flujo solo por llamar Receive sin su documento. Origen HU-08 T078/T103; US4.6, FR-013/019, Q-09/V-12. [Evidencia HU-01 US1](../008-registrar-recepcion-mercancia/validation/us1.md) y [aceptación](../008-registrar-recepcion-mercancia/validation/acceptance.md).

## HU-03 — Salidas — Persona B

- [ ] INT-HU03-01 Integrar Shipment/Line con el contrato de aceptación de escritores, incluidos borradores retirados, lectores, marca y carreras reales con definición/borrado. Consumir el CustomerOrder/Line y las reservas entregadas por HU-05, sin duplicar sus altas; confirmar salida/servido/consumo de reserva con el protocolo común. Origen HU-08 T035/T064/T066/T075/T100/T101; FR-009/010/019.
- [ ] INT-HU03-02 Permitir servir compromisos anteriores de artículos inactivos con sus límites, reservas propias/ajenas y permisos originales; comprobar conservación de historial/saldos y coordinación con la desactivación. El rechazo de pedidos nuevos pertenece al escritor HU-05. Origen HU-08 T078/T103; FR-013/019, Q-09/V-12.

## HU-05 — Consultas, pedido mínimo y reservas — Persona B

- [ ] INT-HU05-01 Integrar CustomerOrder/Line (alta y consulta) y Reservation/Allocation con el contrato de aceptación de referencias, manteniendo el bloqueo de definición desde la primera línea de pedido y tras retirar referencias cuando proceda, y el rechazo de borrado con compromisos reales. Publicar el pedido/servido/pendiente y reservas que consume HU-03; no sustituir Reservation por el efecto Reserve del núcleo. Origen HU-08 T035/T064/T066/T075/T100/T101; FR-009/010/019 y acuerdo de alcance HU-05/HU-03 del 2026-11-19.
- [ ] INT-HU05-02 Rechazar pedidos y reservas nuevos tras desactivar, permitir liberar compromisos existentes con permisos/motivo y comprobar las carreras de alta/reserva/desactivación y la conciliación documental/diario/saldo. Origen HU-08 T078/T103; FR-013/019, Q-09/V-12.

## HU-04 — Conteos y ajustes — Persona B

- [ ] INT-HU04-01 Integrar CountLine y hallazgos con el contrato de aceptación de escritores desde su primera referencia, antes del ajuste; comprobar retirada permitida de borradores y carreras de definición/borrado sin crear dimensiones técnicas que impidan eliminar artículos nunca usados. Origen HU-08 T064/T066/T070/T075/T100/T101; FR-009/010/011/019.
- [ ] INT-HU04-02 Verificar ajustes de artículos inactivos solo con autorización/motivo persistidos, respetando reservas y congelación, sin borrar ni reescribir diario y conservando ubicaciones/unidades históricas. Origen HU-08 T078/T103; FR-013, Q-09/V-12 y Constitution IV/V.
- [ ] INT-HU04-03 Al integrar CountLocation, conectar ILocationUsage.IsInOpenCountAsync de HU-02 y los controles de congelación de los escritores de stock/reservas; probar 409 CountScopeFrozen al desactivar/cambiar tipo de una ubicación en conteo y mostrar inOpenCount real en sus consultas. No habilitar conteos dejando el lector constante false. Origen HU-02 T034 y revisión PR #6; FR-008/012 y US3.3/US4.3 de HU-02.

## HU-06 — Mínimos y alertas — Persona A

- [X] INT-HU06-01 Implementar el evaluador real de IArticleStateEffects: desactivar resuelve el episodio y suspende nuevas alertas; reactivar reevalúa la StockPolicy común. Probar misma transacción/bloqueo de artículo, rollback conjunto y reacción de alertas en los consumidores de stock, sin otro cálculo de disponibilidad. Origen HU-08 T068/T078/T106; FR-012/013 y plan: integración HU-06. [Evidencia técnica local](../009-configurar-alertas-stock-minimo/validation/acceptance.md) y [revisión A+B confirmada por el usuario](../009-configurar-alertas-stock-minimo/validation/shared-review.md).

## Coordinación e integración compartida — A+B

La [revisión A+B de HU-07](../007-gestionar-pedidos-proveedores/validation/shared-review.md)
fue confirmada por el usuario. COORD-01 mantiene su alcance específico de HU-08.

- [ ] COORD-01 Revisar antes de integrar/fusionar los cambios compartidos de HU-08: IArticleMovementReader.FiltersAsync, campo CounterpartyNotApplicableReason, migración 20261119171332_HU08MovementCounterparty y snapshot, compatibilidad de replay y contratos actualizados. Registrar revisión real de la otra persona; conservar migraciones aplicadas y regenerar únicamente migraciones locales no compartidas si otra entrega adelanta la cadena. El cierre técnico local de HU-08 no certifica esta revisión. Origen T095 y parte de T097/T108; plan global y gobernanza.

## H5 — Aceptación operativa y puesta en servicio — Responsables del proyecto/almacén

Evidencia reutilizable para H5-02: [restauración técnica V-14 con compras](../007-gestionar-pedidos-proveedores/validation/recovery-v14.md).

- [ ] H5-01 Realizar y documentar SC-006 con tres gestores representativos: cada uno alta de familia/artículo/proveedor, corrección y desactivación sin asistencia; registrar incidencias, corregir y repetir lo afectado. Reutilizar el guion de `specs/002-gestionar-datos-maestros/validation/usability.md`. Se conserva la exclusión de este ejercicio en la entrega académica. Origen HU-08 T091/T105; SC-006 operativo. Los flujos de recepción/salida/traslado mantienen además la validación humana exigida por Constitution III en sus módulos.
- [ ] H5-02 Completar V-14 operativo con compras, recepciones, reservas y contingencia reales: restauración independiente, conciliación de hechos posteriores, revisión de reapertura y RPO/RTO aprobados. Reutilizar el ensayo técnico y `Hu08RecoveryTests`, sin presentar sus tiempos de una base pequeña como aprobación operativa. Origen HU-08 T092/T107; Constitution V y guía global V-14.
- [ ] H5-03 Revisar y aceptar el conjunto de módulos antes de producción con evidencia FR/SC/V y responsables reales, incluyendo H5-01/H5-02. Mantener separada esta aceptación del cierre técnico/académico de HU-08. Origen parte de T096/T108; Governance.

## Trazabilidad del traslado

| Origen HU-08 | Receptores |
|-------------|------------|
| T100: referencias retiradas | INT-HU01/03/04/05/07-01. |
| T101: borrado y carreras documentales | INT-HU01/03/04/05/07-01. |
| T102: proveedor/documentos | INT-HU07-02, con recepción consumidora INT-HU01-02. |
| T103: operaciones de inactivos | INT-HU01/03/04/05-02 e INT-HU07-02. |
| T105: gestores | H5-01. |
| T106: alertas | INT-HU06-01. |
| T107: recuperación operativa | H5-02. |
| Revisión de T097/T108 y aprobación operativa | COORD-01/H5-03. |
