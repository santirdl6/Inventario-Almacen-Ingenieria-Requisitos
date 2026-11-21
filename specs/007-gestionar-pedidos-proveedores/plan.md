# Implementation Plan: HU-07 — Crear, enviar y seguir pedidos a proveedores

**Branch**: `a/hu08-cierre-coordinacion` (rama Git actual; sin nueva rama HU-07)
| **Date**: 2026-11-20 | **Spec**: [spec.md](spec.md)

**Input**: `specs/007-gestionar-pedidos-proveedores/spec.md`.
**Feature ID**: `007-gestionar-pedidos-proveedores`, independiente de la rama Git.
**Responsable**: Persona A · **Hito**: H2.
**Estado**: entrega funcional local cerrada, 2026-11-21. T001–T054 y T056–T061
verificadas o documentadas según su alcance. T055 queda abierta: el último ensayo
alcanza la muestra mínima y cumple en consultas, pero no en guardados.
Véanse [carga](validation/performance.md), [regresión](validation/integration.md)
y [matriz de aceptación](validation/acceptance.md).
Evidencias [US1](validation/us1.md), [US2](validation/us2.md), [US3](validation/us3.md), [US4](validation/us4.md) y [US5](validation/us5.md);
[revisión A+B](validation/shared-review.md) confirmada por el usuario; aceptación operativa pendiente.

## Summary

Entregar preparación/corrección de borradores, revisión y solicitud de correo a proveedor,
recuperación de fallos y seguimiento por línea. Implementar Purchasing vertical en API/UI,
con maestros HU-08 y el ejecutor/resultados/bloqueos comunes, sin modificar stock al comprar.
Congelar documento/destinatario en una Delivery inmutable y outbox persistente; worker
MailKit en la misma API, claim/SMTP/finalización separados y fencing multiproceso.
Solo aceptación SMTP persistida cambia Draft/Pending a Sent/Accepted.

La revisión detecta cambios de destinatario mediante token ligado al contenido y maestros;
ETag protege la revisión del agregado. Recuperación incierta repite la misma intención;
retry de Failed es una nueva acción autorizada sobre la misma entrega/MessageId. No se
promete entrega única externa ni lectura final del correo.

### Contexto obligatorio y acuerdos consumidos

- [Constitución v1.0.0](../../.specify/memory/constitution.md), principios I–V y gobernanza.
- [Plan global](../../.specify/memory/project-plan.md), stack/arquitectura/migraciones.
- [Decisiones comunes](../../.specify/memory/project/research.md), R-02–R-08/R-10/R-11.
- [Modelo común](../../.specify/memory/project/data-model.md), compras, outbox, identidad.
- [HTTP común](../../.specify/memory/project/contracts/api.md#hu-07--a-pedidos-a-proveedores).
- [Protocolo transaccional](../../.specify/memory/project/contracts/stock-operations.md).
- [Interacción común](../../.specify/memory/project/contracts/ui.md).
- [Validación común](../../.specify/memory/project/quickstart.md), V-02/06/08/12/13/14.
- [Tareas receptoras](../001-reparto-specs-inventario/integration-tasks.md#hu-07--compras--persona-a),
  INT-HU07-01/02; [reparto/hitos](../001-reparto-specs-inventario/plan.md) y
  [reserva de numeración](../../docs/agrupamiento.md).

[tasks.md](tasks.md) descompone este diseño en 60 tareas e incorpora los IDs receptores
con evidencia propia prevista; su generación no declara realizadas pruebas ni revisiones compartidas.

## Technical Context

**Language/Version**: C# 14, .NET/ASP.NET Core 10 LTS; TypeScript 6.0.3, Node 24 LTS
≥24.15.0; UI en español.

**Primary Dependencies**: EF Core/SqlServer/Identity/OpenAPI 10.0.13 existentes;
MailKit 5.0.0 propuesta y fijada al implementar; Angular/CLI/CDK 22.2.3, PrimeNG 22.1.4,
temas 3.1.0, RxJS 7.8.2 existentes. Handlers DI y BackgroundService, sin broker.

**Storage**: SQL Server 2025, un InventoryDbContext y migraciones aditivas compartidas;
UUID, rowversion, decimal(18,3)/(18,2), instantes UTC y fechas previstas SQL date.
Snapshots/HTML/MIME persistentes, auditoría/eventos/resultados append-only protegidos
por inventory_runtime. Detalles compuestos y lista/count en SNAPSHOT.

**Testing**: xUnit 2.9.3, WebApplicationFactory + SQL real y transporte SMTP controlado;
Mailpit para SMTP real; Angular TestBed/Vitest 5.1.0, coverage 5.1.0, jsdom 31.0.0;
Playwright 1.65.0. Suites existentes conservadas; evidencia local US1–US5 en `validation/`.

**Target Platform**: navegador Chrome/Edge vigente, API de ejecución continua en Linux/
Windows x64; desarrollo Mac con SQL amd64 por emulación/remoto. Mismo origen y HTTPS
fuera del entorno local. Worker excluido del modo de inicialización/mantenimiento.

**Project Type**: aplicación web interna, monolito modular, REST `/api/v1`.

**Performance Goals**: SC-007, p95 ≤2 s para consultas paginadas/guardado de hasta 20 líneas,
25 sesiones y 10.000 artículos; condiciones de volumen compartidas. SMTP y conflictos
medidos aparte, sin ocultar errores. Son objetivos, no resultados medidos.

**Constraints**: compras no generan stock; solo Draft/None editable; una Delivery por pedido;
ningún envío sin intención conservada. HTTP reautoriza siempre; worker solo autoridad
persistida comprobada. Reintentos SQL hasta tres/lock 5 s. SMTP al menos una vez, contenido
estable; lease 120 s/timeout 30 s, cinco claims por ciclo y backoff acotado. Retry conocido
no cambia documento/destinatario. Permisos de servidor, antiforgery y recuperación por actor.

**Scale/Scope**: una pyme/almacén/moneda; cinco historias de usuario, 30 FR, INT-HU07-01/02.
Recepciones, stock, cancelación/borrado, cambios posteriores del envío, facturación y
compras automáticas siguen las fronteras de la spec.

## Constitution Check

*GATE antes de Phase 0 y repetido después de Phase 1. PASS expresa conformidad del diseño,
no aprobación de implementación ni aceptación de responsables/operarios.*

| Principio / regla | Control verificable y evidencia prevista | Antes | Después |
|------------------|------------------------------------------|-------|---------|
| I. Correspondencia almacén/sistema | Comprar/enviar no crea stock; lectura de recibido solo de confirmaciones HU-01. Q07-01/10, V-02/06 en HU-01. | PASS | PASS |
| II. Ubicación precisa | Compras no inventa ubicaciones/mercancía; HU-01 exige destinos junto a stock, según contrato común. | PASS | PASS |
| III. Uso intuitivo | Revisión exacta del destinatario/documento, teclado, errores por línea y estados textuales separados; Q07-02/03/05/11. SC-008 operativo en H5-03. | PASS | PASS |
| IV. Trazabilidad | Snapshots, entrega inmutable, auditoría/eventos append-only, actor autenticado o autorización persistida, MessageId estable; Q07-04/07/08/09/10. | PASS | PASS |
| V. Integridad/recuperación | Commit único, FK/locks/ETag, replay, lease/fencing, SMTP fuera de transacción, incertidumbre explícita, pruebas SQL/caídas; Q07-05/06/09/10/12. V-14 operativo en H5-02. | PASS | PASS |
| Gobernanza compartida | Base global enlazada/actualizada, revisión A+B antes de integrar; IDs INT conservados; HU-01/H5 aceptan sus propios escritores/ensayos. | PASS | PASS |

Resultado posterior: sin excepciones constitucionales ni aclaraciones técnicas bloqueantes.
Guía verificable en [quickstart](quickstart.md). El gate documental no sustituye la
[revisión A+B](validation/shared-review.md) ni la aceptación operativa H5.

## Project Structure

### Documentation (this feature)

```text
specs/007-gestionar-pedidos-proveedores/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── tasks.md
├── checklists/requirements.md
└── contracts/
    ├── api.md
    ├── integration.md
    └── ui.md
```

tasks.md se generó mediante /speckit.tasks; validation/ conserva la evidencia por fase.
El estado ejecutado de cada tarea se mantiene en tasks.md; el cierre transversal sigue en curso.

### Source Code (repository root)

Rutas de destino previstas, salvo infraestructura compartida ya existente:

```text
backend/src/Inventario.Api/
├── Features/Purchasing/
│   ├── Commands/                 # alta, edición y initial/retry de send
│   ├── Queries/                  # listado, detalle, contexto, preview, delivery/document
│   └── PurchasingEndpoints.cs
├── Domain/Purchasing/            # pedido/línea, entrega, auditoría/eventos
├── Infrastructure/Messaging/     # worker, opciones, render y adaptador MailKit
└── Infrastructure/Persistence/
    ├── Configurations/          # mappings por entidad
    ├── Operations/              # factoring mínimo del ejecutor único
    ├── PurchasingArticleUsageContributor.cs
    └── Migrations/              # HU07PurchasingOutbox aditiva
backend/tests/Inventario.IntegrationTests/Purchasing/
backend/tests/Inventario.UnitTests/             # reglas/renderizado si se precisan
frontend/src/app/
├── features/purchasing/
│   ├── data/purchase-orders-api.service.ts
│   ├── pages/                   # lista, formulario, revisión y detalle
│   └── purchasing.routes.ts
└── core/
    ├── http/master-data-mutations.service.ts   # transporte compartido ampliado
    └── operations/              # grupo por pedido y recuperación/navegación
frontend/e2e/                    # recorridos compras y regresiones
scripts/provision-e2e-users.cs    # perfil HU-07 aislado, compatible con perfiles previos
```

**Structure Decision**: un corte vertical Purchasing y Messaging en API existente;
comandos internos especializados sobre el mismo núcleo transaccional. No segundo
DbContext, fuente de disponibilidad, migrador, transporte frontend ni cuenta sistema.
Program.cs registra feature/worker/opciones en ejecución ordinaria y csproj fija MailKit.
app.routes.ts/operation-areas.ts integran navegación; fixtures existentes se amplían sin
debilitar aislamiento de bases/cuentas o compatibilidad con HU-08/HU-02.

## Phase 0 — Investigación resuelta

[research.md](research.md) documenta decisiones, fundamentos, alternativas y fuentes:
número/moneda/fechas, snapshots, revisión/ETag, MailKit/documento, retry, autoridad interna,
claim/fencing y UI compartida. La inspección del código confirma que el ejecutor HTTP
actual no admite worker sin factoring; sustituir solo ICurrentActor no basta.

## Phase 1 — Diseño y secuencia de implementación posterior

1. **Base de persistencia e integración receptora**: entidades/mappings/migración aditiva,
   contributor y Tracker con PurchaseOrderLine real; transacciones/ETag y permisos;
   pruebas de creación/edición/concurrencia/referencias con INT-HU07-01/02.
2. **Corte vertical borrador/consulta**: endpoints y páginas de formulario/lista/detalle;
   ampliar transporte/store por permiso/acción/grupo; recuperación de alta/PUT y snapshots.
3. **Corte vertical revisión/solicitud**: preview, token, `/send` inicial, Delivery/outbox
   junto a auditoría/resultado; congelación y carreras con maestros, sin SMTP en endpoint.
4. **Entrega y recuperación**: factoring común revisable, worker/SMTP/MIME, claims/leases/
   finalizaciones idempotentes, retry autorizado y seguimiento en UI; tests con dos workers,
   aceptación incierta y rollback. No aplazar estas garantías más allá del primer envío.
5. **Verificación técnica**: matriz Q07/FR/SC/INT, regresiones comunes, migración/SQL runtime,
   E2E con Mailpit y rendimiento. Registrar evidencia/revisión A+B al existir resultados;
   HU-01 acepta recepción real y H5 sus ejercicios operativos según responsabilidades.

Artefactos normativos locales: [modelo](data-model.md), [HTTP](contracts/api.md),
[protocolo de integración](contracts/integration.md), [UI](contracts/ui.md) y
[validación](quickstart.md). La secuencia no sustituye la lista ejecutable de tareas.

### Trazabilidad y puntos de coordinación

| Cobertura | Diseño / verificación |
|-----------|-----------------------|
| US1; FR-001–008; SC-001/002/005 | Pedido/líneas/snapshots, contributor y locks. Q07-01/02/07/08. |
| US2; FR-009–013; SC-003 | Preview/token, Delivery atómica, congelación. Q07-03/06. |
| US3; FR-018–022; SC-006/007 | SNAPSHOT, paginación y lector de acumulados. Q07-11/12; HU-01 V-02/06; ensayo de rendimiento. |
| US4; FR-014–017/025/027; SC-003/004 | Outbox/fencing, replay frente a retry, store por grupo. Q07-04/05/06/09. |
| US5; FR-023–026/028; SC-002/004/005 | Policy/autoría/atomicidad/auditoría/DENY. Q07-05–10. |
| FR-029/030; SC-008 | UI/recuperación/accesibilidad Q07-02/03/05/11; H5-03 validación humana. |
| INT-HU07-01/02 | Obligaciones receptoras propias, no deferidas: Q07-07/08 y contrato de integración §6. |

Cambios compartidos concretados y enlazados desde la base global: autoridad interna del
worker, distinción entrega/operación de retry, ampliaciones de modelo y rutas auxiliares.
Revisar con A+B antes de integrar, especialmente regresión de ejecutor H0/HU-08/HU-02/
StockCore y contrato de acumulados HU-01. No modifica estados/efectos/cálculo de stock.

## Complexity Tracking

Sin violaciones constitucionales. Outbox/lease responden al correo recuperable solicitado;
fencing y autoridad persistida son necesarios por concurrencia y ejecución fuera de HTTP.
Documento HTML evita una dependencia PDF; no se introduce infraestructura distribuida.
