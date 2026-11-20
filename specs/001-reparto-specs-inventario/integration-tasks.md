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

- [ ] INT-HU07-01 Integrar PurchaseOrder/Line con el contrato de aceptación de escritores: primera línea de borrador, retirada de la última línea, definición vigente e imposibilidad de borrado con documentos. Conservar instantáneas históricas de artículo/proveedor y cobertura SQL real. Origen HU-08 T035/T076/T100/T101; US2.8, FR-009/010/019.
- [ ] INT-HU07-02 Verificar editar, desactivar y reactivar el proveedor con pedidos existentes: identidad/documentos/instantáneas intactos, bloqueo de nuevas compras de proveedor o artículo inactivo bajo los recursos documentales compartidos y reanudación al reactivar. Origen HU-08 T076/T078/T102/T103; US3.3/3.4/3.6, FR-012/013, SC-007.

## HU-01 — Recepciones — Persona A

- [ ] INT-HU01-01 Integrar Receipt/Line/Allocation con el contrato de aceptación de escritores desde el borrador; probar las referencias directas o indirectas reales, la retirada y las carreras con edición/borrado. Generar cantidades exclusivamente por el núcleo común con soporte y contraparte o sus motivos explícitos. Origen HU-08 T035/T064/T066/T075/T076/T100/T101; FR-009/010/019.
- [ ] INT-HU01-02 Rechazar recepción nueva de artículo inactivo y probar confirmación/desactivación en ambos órdenes; conservar compra, recepción, instantáneas y trazabilidad previas. No acreditar este flujo solo por llamar Receive sin su documento. Origen HU-08 T078/T103; US4.6, FR-013/019, Q-09/V-12.

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

- [ ] INT-HU06-01 Implementar el evaluador real de IArticleStateEffects: desactivar resuelve el episodio y suspende nuevas alertas; reactivar reevalúa la StockPolicy común. Probar misma transacción/bloqueo de artículo, rollback conjunto y reacción de alertas en los consumidores de stock, sin otro cálculo de disponibilidad. Origen HU-08 T068/T078/T106; FR-012/013 y plan: integración HU-06.

## Coordinación e integración compartida — A+B

- [x] COORD-01 B aprobó de forma independiente el lector, el campo de contraparte, la migración `20261119171332_HU08MovementCounterparty` y su snapshot, el replay y los contratos en su [revisión del PR #7](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/7#pullrequestreview-5990430106). El PR se fusionó en `a/h0-identity-article` con el commit `c904824`. HU-05 puede regenerar su migración local sobre esa base. Origen T095 y parte de T097/T108.
- [ ] Coordinar la migración posterior de exclusividad con HU-05 antes de fusionarla. Revisar con B que reservar y liberar envíen un único valor y aplicar la misma regla a recepción, salida y ajustes en sus módulos. Conservar migraciones aplicadas y regenerar únicamente las locales aún sin compartir si cambia el orden de integración.

## H5 — Aceptación operativa y puesta en servicio — Responsables del proyecto/almacén

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
