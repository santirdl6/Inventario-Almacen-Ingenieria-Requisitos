# Implementation Plan: HU-08 — Artículos, familias y proveedores

**Branch**: `002-gestionar-datos-maestros` — identificador lógico de feature.
**Rama Git al planificar**: `a/h0-identity-article`.
**Date**: 2026-11-12 | **Spec**: [spec.md](spec.md).

**Input**: HU-08 y aclaración de primera referencia, Persona A, inicio en H1.
**Alcance del comando**: investigación y diseño Phase 0–1; las tareas se generan después.

**Estado de ejecución, 2026-11-17**: base común, sesión/layout, familias y catálogo Article H1
verificados localmente: 64 pruebas backend, 22 unitarias frontend y 7 E2E aprobadas.
[Foundation](validation/foundation.md), [US1](validation/us1.md) y [US2](validation/us2.md)
registran el alcance. T035 mantiene pendiente su parte de referencia real retirada;
consumidores futuros y revisión conjunta siguen pendientes.

## Summary

Entregar verticalmente el mantenimiento de familias, proveedores y artículos, con consulta
por permisos, auditoría, conservación de historial y escrituras fiables. Ampliar Article H0
sin sustituir sus UUID ni referencias. Crear StockPolicy a cero con cada alta de artículo;
la configuración de mínimos y sus efectos completos siguen en HU-06.

La definición del artículo queda bloqueada desde la primera referencia documental, reserva
o movimiento, incluidos borradores. Una marca persistente conserva esa condición aun si se
retira una línea. El borrado utiliza referencias reales e historial y conserva auditoría y
resultados de operación. El ejecutor transaccional y OperationResult se comparten con B.

### Base normativa y técnica consumida

- [Constitución v1.0.0](../../.specify/memory/constitution.md) y [plan global](../../.specify/memory/project-plan.md).
- [Decisiones comunes](../../.specify/memory/project/research.md) y [modelo común](../../.specify/memory/project/data-model.md).
- [HTTP](../../.specify/memory/project/contracts/api.md), [stock](../../.specify/memory/project/contracts/stock-operations.md),
  [UI](../../.specify/memory/project/contracts/ui.md) y [validación común](../../.specify/memory/project/quickstart.md).
- [Reparto H0–H5](../001-reparto-specs-inventario/plan.md) y [handoff H0 existente](h0-handoff.md).

Los anexos globales son la fuente compartida. Este plan añade decisiones y evidencia
particulares; no redefine disponibilidad, estados ni efectos de movimientos.

## Technical Context

**Language/Version**: C# 14/net10.0; TypeScript 6.0.3 y Angular/CLI 22.2.3; Node 24 LTS
≥ 24.15.0. Versiones del repositorio y baseline global, sin cambio de stack.

**Primary Dependencies**: EF Core SQL Server/Design, Identity EF y OpenAPI 10.0.13;
PrimeNG 22.1.4, CDK 22.2.3, RxJS 7.8.2 y Aura de @primeuix/themes 3.1.0. Minimal APIs
delgadas y handlers por DI, sin mediador obligatorio ni repositorios genéricos.

**Storage**: SQL Server 2025; InventoryDbContext único por intento de comando; migraciones
aditivas en la cadena común, rowversion, UUID, UTC y precisión global. Nuevas entidades de
maestros y auditoría; StockPolicy/OperationResult son las mismas entidades compartidas.

**Testing**: xUnit 2.9.3 con MVC Testing 10.0.13 y SQL real aislado del proyecto existente.
Configurar Vitest 5.1.0 + coverage-v8 5.1.0 + jsdom 31.0.0 y Playwright Test 1.65.0,
versiones elegibles investigadas y pendientes de instalar/fijar en lockfile. Angular Build
22.2.3 admite Vitest ^4.0.8 o ^5.0.0. No existen aún target test ni suites frontend.

**Target Platform**: navegador en español Chrome/Edge vigentes; API y SPA en mismo origen;
Compose de desarrollo y despliegue con HTTPS según base global. SQL x64 real, incluida
la emulación amd64 de desarrollo en macOS cuando corresponda.

**Project Type**: aplicación web interna, monolito modular existente.

**Performance Goals**: SC-005, p95 de búsqueda paginada ≤ 2 s con 10.000 artículos y
25 sesiones concurrentes; medir errores y esperas aparte sin ocultarlos. Sin nuevos SLO.

**Constraints**: no escrituras parciales, duplicados o sobrescrituras silenciosas; permisos
de servidor; evidencia append-only; bloqueo de definición desde primer uso; no crear stock
al crear maestros. Revisión A+B antes de integrar cambios compartidos. RPO/RTO y recuperación
siguen la base global, con aprobación/demostración antes de producción.

**Scale/Scope**: tres catálogos de una pyme y consulta del diario común. Login/layout mínimos
son dependencia transversal; sin gestión de cuentas, compras, importación masiva, stock
editable, series/caducidad ni nuevo catálogo de unidades.

## Constitution Check

GATE previo a Phase 0 y repetido tras Phase 1. PASS evalúa diseño y cobertura, no pruebas
ejecutadas ni aprobación operativa. Las revisiones de ambas personas siguen pendientes.

| Principio/control | Diseño y evidencia exigida | Antes | Después |
|-------------------|----------------------------|-------|---------|
| I. Correspondencia almacén/sistema | Alta/edición de maestros sin saldos ni movimientos; política inicial cero; Q-02/Q-09. | PASS | PASS |
| II. Ubicación precisa | Identidad/unidad estables; no borrar referencias ni ubicaciones; Q-05/Q-09 y V-01 integrado. | PASS | PASS |
| III. Uso intuitivo | Formularios españoles, errores conservados, teclado, revisión de retiradas; Q-01/Q-10 y SC-006 con tres gestores. | PASS | PASS |
| IV. Trazabilidad | Auditoría append-only que sobrevive al borrado; diario real de inactivos, sin reescritura; Q-04/Q-08/Q-09. | PASS | PASS |
| V. Integridad y recuperación | Mutación+política+auditoría+resultado atómicos; SQL real, ETag, replay, fallos/carreras, respaldo común; Q-03–Q-08 y V-14 antes de producción. | PASS | PASS |
| Controles operativos | Unidad explícita y permisos diferenciados; cambios de definición bloqueados; Q-02/Q-05. | PASS | PASS |
| Gobernanza y revisión | Una cadena, contratos actualizados, responsabilidad A/B y cierre integrado separado del corte H1; Q-11 y revisión cruzada registrada antes de integrar. | PASS | PASS |

Escenarios Q-01–Q-11 en [quickstart](quickstart.md). No se solicitan excepciones ni enmienda
constitucional. Una implementación que omita auditoría, coordinación o pruebas SQL no
supera estos gates por el hecho de que este diseño tenga PASS.

## Project Structure

### Documentation (this feature)

```text
specs/002-gestionar-datos-maestros/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── api.md
│   ├── ui.md
│   └── integration.md
├── h0-handoff.md
└── checklists/requirements.md
```

`tasks.md` será salida de `/speckit.tasks`; no forma parte de esta fase.

### Source Code (repository root)

Estructura de destino; solo la base H0 está implementada hoy:

```text
backend/src/Inventario.Api/
├── Features/MasterData/
│   ├── Commands/              # altas, modificaciones, cambios de estado, borrado
│   ├── Queries/               # catálogos, detalle/capacidades, historial
│   ├── Maintenance/           # preparación H0 autenticada, solo en ventana de transición
│   └── MasterDataEndpoints.cs
├── Domain/
│   ├── Identity/              # ICurrentActor H0 existente
│   ├── MasterData/            # Article ampliada, Family, Supplier, MasterDataAudit
│   └── Stock/                 # StockPolicy común; otras entidades de B
└── Infrastructure/
    ├── Identity/              # reutilizar sesión/actor H0
    ├── Http/                  # ETag, errores y consulta de resultado común
    └── Persistence/
        ├── Configurations/    # un mapping por entidad
        ├── Operations/        # ejecutor y resultados comunes coordinados con B
        └── Migrations/        # única cadena; no migraciones paralelas por feature
backend/tests/Inventario.IntegrationTests/
├── IdentityTests.cs; H0ReferenceTests.cs
├── MasterDataMutationTests.cs
├── MasterDataConcurrencyTests.cs
└── MasterDataMigrationTests.cs
frontend/src/app/
├── core/                      # sesión, guards, layout, ProblemDetails, operaciones pendientes
├── shared/                    # presentación realmente reutilizable
└── features/master-data/      # rutas lazy, servicios, DTO, páginas/formularios/historial
frontend/e2e/                  # login real y recorridos de maestros
```

**Structure Decision**: mantener un proyecto API y el proyecto de integración existentes.
No crear otro contexto ni un segundo protocolo de stock. El mínimo frontend se integra con
la base de B. Ubicación final de componentes compartidos acordada en el contrato de integración.

### Phase 0 — decisiones resueltas

[research.md](research.md) consolida las dos investigaciones delegadas y las fuentes oficiales.
Decisiones: evolución aditiva; marca monotónica de primer uso; política inicial; ejecutor y
resultado comunes; replay tras borrado; auditoría sin FK al maestro; mínimos frontend y
validación SQL real. No quedan aclaraciones técnicas abiertas.

### Phase 1 — modelo, interfaces y validación

Concreción US2 del 2026-11-17: separar ArticleDetail propio con ETag de ArticleContext
derivado sin ETag y comparar articleVersion al cargar. Resuelve el hallazgo U4 sin cambiar
identidad, entidades de stock ni el cálculo de disponibilidad; el contrato HTTP se actualiza
antes de exponer la nueva API de artículos.

- [data-model.md](data-model.md): mappings, relaciones, invariantes y transición de familia.
- [contracts/api.md](contracts/api.md): DTO, rutas, filtros, permisos, ETag, intención y errores.
- [contracts/ui.md](contracts/ui.md): navegación, estados, accesibilidad y resultado incierto.
- [contracts/integration.md](contracts/integration.md): ownership, bloqueos, registro de uso,
  marca de primer uso, historial/alertas y coordinación de migraciones.
- [quickstart.md](quickstart.md): preparación, comandos y evidencias Q-01–Q-11.

### Cortes previstos para descomposición posterior

1. Integrar la base H0 y acordar con B ejecutor/OperationResult, recursos de bloqueo,
   marca de primer uso y pertenencia al rol SQL. Se pueden preparar modelos y UI en paralelo.
2. Evolución de catálogos/auditoría y transición de datos H0 mediante mantenimiento HTTP
   autenticado con ICurrentActor/ejecutor común y conexión sin DDL; después exigir familia.
3. Familias/proveedores y artículos verticales con mutaciones reales completas; permisos,
   errores, ETags y replay. Antes de permitir cambios de definición, validar lectores y
   enlaces de primer uso de los consumidores presentes; no desplegar esa mutación sin su gate.
4. Navegación y formularios sobre sesión común; recuperación de intención, pruebas frontend
   y recorridos reales. Adaptar los fixtures de H0 al modelo final sin perder sus garantías.
5. Historial y lectores de referencias con consumidores integrados; pruebas de carreras,
   conservación y estados cruzados. Medir SC-005 y validar SC-006.

Esto define precedencias, no tareas ni estimaciones comprometidas. La implementación puede
comenzar en H1; el cierre íntegro de FR-009/010/013/014/019 requiere los consumidores de
HU-01/HU-03/HU-04/HU-05/HU-07 y la reacción de HU-06 cuando esté integrada.

La descomposición ejecutable está en [tasks.md](tasks.md). La expansión de esquema se
divide en incrementos seriales de familias, artículos y proveedores para mantener el MVP
de US1 independiente; la transición y el cierre de FamilyId conservan los pasos del modelo.
El seguimiento de primera referencia se integra antes del endpoint de edición de definición;
su ausencia en un consumidor presente causa DependencyUnavailable. En H1 sin consumidores
se verifica esa ausencia y las historias futuras completan su integración antes de habilitar
sus escritores. El contrato del mantenimiento se recoge en HTTP e integración; no usa un
script SQL de datos que acepte autoría manual ni concede credenciales DDL al proceso API.

### Revisión posterior a diseño

PASS de diseño para I–V y controles, sin cambio de stack, cálculo de disponibilidad o
estados de movimientos. La base global incorpora las concreciones de marca de primer uso,
resultado reproducible, auditoría y bloqueos de maestros; A+B deben revisar esos cambios
antes de fusionar. El estado pendiente de revisión no se presenta como aprobación realizada.

## Complexity Tracking

No hay violaciones constitucionales que justificar. La marca de primer uso conserva una
decisión ya aclarada; el registro de lectores evita omitir referencias al integrar módulos;
las cabeceras/permiso del resultado permiten replay tras borrar. Son ampliaciones del
monolito y del protocolo común, no servicios o fuentes de verdad adicionales.
