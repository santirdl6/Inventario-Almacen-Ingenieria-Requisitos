# Implementation Plan: Coordinación de las ocho specs

**Branch**: `001-reparto-specs-inventario` (identificador lógico, sin rama Git creada)
| **Date**: 2026-10-25 | **Spec**: [spec.md](spec.md)

**Input**: reparto 4+4 de las ocho historias y preparación del trabajo conjunto.
**Base técnica**: [plan global](../../.specify/memory/project-plan.md).

## Summary

Coordinar el trabajo de dos personas conservando el reparto A: HU-08, HU-07, HU-01,
HU-06; B: HU-02, HU-05, HU-03, HU-04. Cada persona desarrollará verticalmente
sus pantallas, endpoints, reglas y pruebas.

El stack, la arquitectura y los acuerdos compartidos se definen una sola vez en
`.specify/memory/project-plan.md` y sus anexos. Este plan mantiene los hitos, las
dependencias entre personas y las condiciones de integración de la propuesta de reparto.

## Technical Context

La base técnica aplicable es el [Technical Context global](../../.specify/memory/project-plan.md#technical-context).
Las futuras specs detallarán únicamente las decisiones particulares de su funcionalidad.

Documentos comunes a consultar según las historias afectadas:

- [Investigación y decisiones](../../.specify/memory/project/research.md).
- [Modelo de datos](../../.specify/memory/project/data-model.md).
- [Contrato HTTP](../../.specify/memory/project/contracts/api.md).
- [Núcleo de stock](../../.specify/memory/project/contracts/stock-operations.md).
- [Interacción de usuario](../../.specify/memory/project/contracts/ui.md).
- [Guía conjunta de validación](../../.specify/memory/project/quickstart.md).

## Constitution Check

Se aplica la [constitución](../../.specify/memory/constitution.md) y su
[matriz de controles global](../../.specify/memory/project-plan.md#constitution-check).
Resultado de revisión de diseño: PASS, sin excepciones.

La aceptación de cada historia conserva las obligaciones de registro, ubicación precisa,
uso intuitivo, trazabilidad e integridad. Los responsables de proyecto y almacén revisan
las evidencias; el reparto no permite omitir controles por depender de la otra persona.

## Project Structure

### Documentation (this feature)

```text
specs/001-reparto-specs-inventario/
├── spec.md
├── plan.md
└── checklists/requirements.md
```

La documentación técnica común reside en `.specify/memory/`; los planes de las futuras
specs la referencian. Las tareas específicas se crearán en sus correspondientes features.

### Source Code (repository root)

La estructura de implementación se mantiene en el
[plan global](../../.specify/memory/project-plan.md#source-code-repository-root).
La ampliación transversal H0 aporta la base ejecutable y la contenerización descritas
en el plan global; los módulos de negocio se desarrollan en sus specs.

### Contenerización de la base H0

Entregables compartidos: `Dockerfile` multi-stage, `.dockerignore`, `docker-compose.yml`,
configuración de ejemplo opcional y README de arranque. Angular compilado se sirve desde
ASP.NET Core; SQL Server y SMTP de desarrollo se levantan como dependencias.

Compose espera el healthcheck de SQL y la finalización correcta del inicializador antes
de arrancar la app. Los datos SQL, claves Data Protection y correo tienen volúmenes.
La prueba `sh scripts/verify-containers.sh` verifica arranque y conservación de un dato
después de recrear contenedores. El detalle técnico y los defaults residen en la base global.

### Hitos y coordinación

| Hito | Persona A | Persona B | Condición conjunta de salida |
|------|-----------|-----------|------------------------------|
| H0. Base común | Configuración API, Identity, inicialización SQL y borrador de contratos HTTP. | Configuración Angular/PrimeNG, contenedores y borrador del servicio de stock. | Ambas revisan arranque Compose, volúmenes, estructura, login, migración base, errores, bloqueos y pruebas de transacción/idempotencia. |
| H1. Fundamentos | HU-08: artículos, familias, proveedores. | HU-02: ubicaciones. | Identificadores/unidades acordados; datos de ejemplo comunes; ubicación temporal disponible. |
| H2. Flujos base | HU-07: compra y envío. | HU-05: consultas; comandos mínimos de reserva/liberación compartidos con HU-03. | Pedido enviado de prueba y físico/reservado/disponible coherentes; contrato de recepción listo. |
| H3. Movimientos | HU-01: recepciones parciales. | HU-03: salidas. | Recibir → ubicar → reservar → servir → consultar, incluyendo concurrencia y reintentos. |
| H4. Control | HU-06: mínimo, alerta interna y sugerencia. | HU-04: conteo y ajuste autorizado. | Ajuste confirmado actualiza consulta y alerta; no sobrescribe movimientos de un conteo. |
| H5. Aceptación | Flujo compra/recepción y evidencia de maestros/alertas. | Flujo salida/conteo y evidencia de stock/ubicaciones. | Validación con operarios, rendimiento, restauración y conciliación documentados. |

H0 no convierte a A en responsable exclusivo del backend ni a B del frontend. B propone
las reglas del núcleo de stock y A revisa su uso desde recepciones. A propone los contratos
de maestros y compras, revisados por B. Cada persona conserva sus cuatro historias.
En H2 las consultas y reservas pueden comprobarse con fixtures que invocan el núcleo H0
en transacción; la aceptación con recepciones reales se completa en H3. Ningún fixture
debe saltarse el diario ni insertar saldos independientes.

Las etapas expresan precedencias, no plazos ni estimaciones equivalentes. Estimar cada
historia antes de comprometer fechas y revisar conjuntamente la carga de trabajo.

### Integración de las futuras specs

Preparación técnica Persona A / H0: implementados la API de sesión, Identity Guid,
migración `H0Identity`, seed de roles/cuenta y base de policies. El borrador HTTP global
distingue endpoints implementados y recursos futuros. Esta preparación se mantiene
en el plan conjunto, sin crear otra spec de negocio. Las pruebas de integración SQL
verifican sesión, antiforgery, permisos y reinicialización; el cierre conjunto de H0
incluye el trabajo de B y la revisión de los acuerdos transversales.

Preparación A → B (2026-11-12): A aporta Article mínima en
`20261112105657_H0ArticleReference`, ICurrentActor con implementación Identity y carga de
configuraciones por entidad. B puede referenciar Article desde su primera migración de
stock; genera `H0_StockCore` sobre esta cadena sin renombrar `H0Identity`. La familia y
el mantenimiento completo del artículo permanecen en HU-08. Detalles y evidencia en el
[handoff de HU-08](../002-gestionar-datos-maestros/h0-handoff.md).

Para H0, B implementa el esqueleto y Receive/Reserve/Ship mínimos que permitan demostrar
idempotencia, rollback y concurrencia en SQL real; las reglas completas quedan en sus
historias. B concreta CustomerOrder mínimo en HU-03 como soporte de HU-03/HU-05.
La revisión cruzada A+B de esta ampliación sigue pendiente de registrarse.

Entrega B / H0 (2026-11-18): A adelantó la base común (OperationResult, InventoryOperationExecutor,
InventoryLocks, MasterDataAuditWriter, rol `inventory_runtime`) y B la reutiliza; A confirmó
también el reuso de su layout con login. El núcleo de stock se integra en `b/h0-stock-service-v2`,
creada desde `a/h0-identity-article`: migración `H0_StockCore` regenerada después de las de A,
`StockCore` dentro del ejecutor común, contribución de stock a los controles de Article y
12 pruebas SQL, además de las de A (evidencia en la [guía común](../../.specify/memory/project/quickstart.md#núcleo-de-stock-h0-b-sobre-la-base-común-de-a)).
Sustituye a `b/h0-stock-service` (PR #2), que creaba su propio resultado y chocaba con esta base.
B registra su revisión de la base de A en el
[handoff de HU-08](../002-gestionar-datos-maestros/h0-handoff.md#revisión-de-b-de-la-base-común-2026-11-18);
queda pendiente que A revise la integración para cerrar H0.

Flujo propuesto por el equipo: ramas cortas desde main, PR revisado por la otra persona
y rebase antes de fusionar. B utiliza `b/h0-stock-service` y `b/hu-0X-...`. Si dos migraciones
se generaron en paralelo, se regenera la local no compartida sobre el snapshot integrado;
un rebase por sí solo no garantiza la consistencia del modelo. Esta documentación no crea
ramas, commits ni PRs.

Cada spec identifica HU original, responsable, dependencias y documentos globales que
consume. Cada plan referencia la base global y añade sus decisiones locales, tareas y
criterios de aceptación. Los cambios compartidos siguen el procedimiento de sincronización
del plan global y actualizan los documentos afectados.

La validación integrada utiliza los escenarios V-01 a V-14 de la guía global, además de
los específicos de cada historia. Las evidencias se incorporan a las specs responsables.

## Complexity Tracking

Sin excepciones constitucionales. La base de ejecución y la contenerización se incorporan
en H0; la aceptación de los movimientos, autenticación y funcionalidades del almacén
se completa en las futuras specs. El reparto continúa siendo de cuatro historias por persona.
