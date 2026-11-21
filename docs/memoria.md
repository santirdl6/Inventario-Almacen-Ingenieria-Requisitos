# Inventario y almacén

## Portada y resumen

- **Asignatura** Ingeniería de Requisitos
- **Práctica** Desarrollo de un sistema de información mediante Spec-Driven Development
- **Equipo** Persona A (`santirdl6`) y Persona B (`nayara-cortes`)
- **Documento** Memoria final de entrega
- **Fecha** 21 de noviembre de 2026

Aplicación web para el personal de almacén y compras. Registra dónde se guarda cada artículo, distingue el físico de lo reservado y permite reconstruir los cambios de inventario. El equipo implementó ocho historias y reunió sus recorridos en una demo conjunta. Las verificaciones se atribuyen al corte que las produjo. Quedan abiertos el ensayo de rendimiento de algunas historias, la revisión compartida de recepciones y alertas y la regresión de un corte común que reúna todas las funcionalidades.

## URL del repositorio GitHub

[Inventario-Almacen-Ingenieria-Requisitos](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos)

## Introducción

El equipo extrajo ocho historias de la descripción del almacén. El objetivo es que el sistema refleje los movimientos físicos y conserve su origen, cantidad, responsable y documento. Las ubicaciones identificadas permiten encontrar lo registrado. La separación entre físico, reservado y disponible permite preparar pedidos sin utilizar compromisos de otros clientes.

La [constitución](../.specify/memory/constitution.md) fija las obligaciones de correspondencia física, localización, facilidad de uso, trazabilidad e integridad. Cada historia convierte esas obligaciones en criterios comprobables y documenta su ciclo de especificación, planificación, tareas, implementación y revisión.

## Análisis

### Historias de usuario y estado

| Historia | Responsable | Resultado funcional | Estado y evidencia |
| --- | --- | --- | --- |
| HU-01 · Recepciones | A | Registrar entregas parciales, diferencias y destinos reales contra compras. | [Implementada y validada en el corte A](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/blob/018cff83bb40d40b85003e5364fc639bed95de9d/specs/008-registrar-recepcion-mercancia/validation/acceptance.md), `018cff8`. T041 y T043 abiertas. |
| HU-02 · Ubicaciones | B | Identificar ubicaciones y consultar cantidades de un artículo en cada una. | Integrada. [Revisión del PR #6](revisiones/pr-006-hu02.md). |
| HU-03 · Salidas | B | Confirmar una salida contra un pedido, consumir su reserva propia y actualizar lo servido. | [PR #10 fusionado en la rama de integración de A](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/10) (`dad1a9a`). |
| HU-04 · Inventarios cíclicos | B | Congelar un ámbito, comparar el conteo físico con el teórico y registrar un ajuste autorizado. | [Implementada en el corte B](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/tree/fae4f7c7d942ef97a3ad3827339a634333ddbd25/specs/006-realizar-inventarios-ciclicos), con [PR #12 abierto](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/12). |
| HU-05 · Existencias y reservas | B | Consultar físico, reservado y disponible, y mantener el pedido de cliente mínimo y sus reservas. | [PR #9 integrado](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/9). |
| HU-06 · Alertas de mínimo | A | Configurar el mínimo por artículo y avisar cuando el disponible cae por debajo. | [Implementada y validada en el corte A](../specs/009-configurar-alertas-stock-minimo/validation/acceptance.md), `5718ce8`. 14/16 tareas completadas. |
| HU-07 · Pedidos a proveedores | A | Preparar y enviar pedidos a proveedores y consultar pendientes y estado. | [Corte funcional local](../specs/007-gestionar-pedidos-proveedores/spec.md). Rendimiento de guardados pendiente. |
| HU-08 · Datos maestros | A | Mantener artículos, familias y proveedores, y conservar sus referencias e historial. | Integrada con [cierre técnico](../specs/002-gestionar-datos-maestros/validation/acceptance.md). |

El [catálogo de historias](../specs/001-reparto-specs-inventario/spec.md) recoge los criterios de origen y el [agrupamiento](agrupamiento.md) registra el reparto de cuatro historias por persona.

### Actores y requisitos transversales

| Rol | Intervención prevista |
| --- | --- |
| Operario | Consultar inventario, gestionar ubicaciones y registrar recepciones, salidas y conteos según sus permisos. |
| Responsable de almacén | Gestionar reservas, autorizar ajustes y gestionar políticas de mínimo. |
| Comprador | Preparar y enviar compras y consultar su estado. |
| Gestor de maestros | Gestionar artículos, familias y proveedores. |
| Administrador | Gestionar usuarios según el contrato de identidad. El arranque local crea una cuenta inicial. |

Los permisos se verifican en la API. Las escrituras deben preservar trazabilidad y soportar concurrencia y reintentos sin duplicar efectos. La interfaz debe mostrar cantidades con sus unidades, estados en texto y mensajes que indiquen cómo corregir un rechazo. Los objetivos de latencia y las condiciones de recuperación se definen en el [plan global](../.specify/memory/project-plan.md) y la [guía de validación](../.specify/memory/project/quickstart.md).

## Diseño

La SPA Angular y la API ASP.NET Core se sirven desde el mismo origen. La API agrupa código por funcionalidad y separa comandos y consultas dentro del proceso. Identity mantiene las cuentas y sus permisos. EF Core usa una sola cadena de migraciones de SQL Server. Compose distingue la inicialización de la ejecución ordinaria de la aplicación.

```mermaid
flowchart LR
    Navegador["SPA Angular · PrimeNG"] --> API["API ASP.NET Core"]
    API --> SQL["SQL Server · documentos, diario y saldos"]
    API --> Cola["Worker de correo"]
    SQL --> Cola
    Cola --> SMTP["SMTP · Mailpit en desarrollo"]
```

El saldo se identifica por artículo, ubicación y lote. El disponible es el físico menos el reservado. Los documentos de negocio, el diario de movimientos, sus efectos sobre saldos y el resultado de operación se guardan en la misma transacción. Las operaciones toman bloqueos ordenados y vuelven a leer bajo bloqueo. Los documentos con ETag exigen la versión correspondiente al escribir. Para un resultado incierto, la intención se conserva y el cliente puede consultar la operación registrada.

En los pedidos de cliente, las asignaciones permiten atribuir el reservado a su pedido y combinación de stock. Una salida consume antes la reserva propia de su origen y respeta la de otros pedidos. Las compras conservan el estado de sus entregas por separado y el correo se tramita mediante una cola persistida. Las decisiones y sus alternativas se documentan en el [modelo de datos](../.specify/memory/project/data-model.md), el [contrato de stock](../.specify/memory/project/contracts/stock-operations.md) y las [decisiones comunes](../.specify/memory/project/research.md).

## Implementación

La pila usa Angular 22, PrimeNG 22 y TypeScript 6 en el navegador, ASP.NET Core 10 y EF Core 10 en el servidor, SQL Server 2025 para persistencia y Mailpit para capturar los correos en desarrollo. Las versiones y decisiones de entorno están en el [plan global](../.specify/memory/project-plan.md).

| Ruta | Contenido |
| --- | --- |
| `backend/src/Inventario.Api` | API, dominio, persistencia, identidad e integración con correo. |
| `backend/tests/Inventario.IntegrationTests` | Pruebas de integración sobre SQL Server. |
| `frontend/src/app` y `frontend/e2e` | Aplicación web y recorridos de navegador. |
| `specs` y `.specify/memory` | Artefactos de cada historia, constitución y acuerdos técnicos. |

HU-05 asumió el alta del pedido de cliente mínimo para que HU-03 pudiera consumirlo desde su primer corte. Durante la revisión de HU-05, el identificador del alta pasó a mostrarse en el detalle mediante el resultado persistido. El diálogo de reservas pasó a recorrer todas las páginas de existencias del artículo. En HU-03, la tabla por asignación hace trazable qué reservas concretas se consumieron. La cabecera declara el número y la suma de los consumos que inserta la confirmación, y SQL impide excederlos. El informe de HU-03 distingue esta protección de la comprobación de que se persistió el total declarado.

## Pruebas y validación

xUnit ejecuta integración con SQL Server. Vitest comprueba componentes y servicios Angular. Playwright prueba recorridos del navegador y permisos en un entorno aislado. Los resultados de distintas ramas o fechas se registran por separado.

| Corte | Backend | Vitest | Playwright | Evidencia |
| --- | --- | --- | --- | --- |
| HU-08 · cierre técnico | 101 aprobadas, 1 omitida | 41 aprobadas | 12/12 | [Aceptación técnica](../specs/002-gestionar-datos-maestros/validation/acceptance.md). |
| HU-02 · revisión de PR #6 | 121 aprobadas, 1 omitida, 0 fallos | 107/107 | 14/14 | [Informe de revisión](../specs/003-gestionar-ubicaciones/validation/pr-review-2026-11-19.md). |
| HU-05 · `363de56` | 207 aprobadas, 1 omitida | 147/147 | 16/16 con el filtro documentado | [Informe de HU-05](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/blob/2894dad4cbc5d4be5c37454777064984bf5a135e/specs/004-consultar-existencias/quickstart.md#evidencia). |
| HU-03 · `f65fefe` | 291 aprobadas, 1 omitida | 187/187 | 17/17 | [Evidencia del PR #10](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/10). |
| HU-04 · evidencia de B | 347 aprobadas, 1 omitida, 0 fallos | 230/230 | 18/18 | [PR #12](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/12). |
| HU-07 · integración de compras | 199 aprobadas, 1 omitida, 0 fallos; 200/200 con restauración aislada | 139/139 tras T061 | 6/6 de compras | [Registro de integración](../specs/007-gestionar-pedidos-proveedores/validation/integration.md). |
| HU-01 · `018cff8` | 11/11 en la selección inicial de recepción; ampliaciones y regresiones en el acta | Evidencias por historia y componente | Parcial y discrepancia grabadas | [US1](../specs/008-registrar-recepcion-mercancia/validation/us1.md), [acta de aceptación](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/blob/018cff83bb40d40b85003e5364fc639bed95de9d/specs/008-registrar-recepcion-mercancia/validation/acceptance.md). |
| HU-06 · `5718ce8` | 233 aprobadas, 2 omitidas, 0 fallos; EF sin cambios pendientes | 145/145 y build correcto | 1/1 del flujo de alertas | [Acta de aceptación](../specs/009-configurar-alertas-stock-minimo/validation/acceptance.md). |
| Demo · ZIP independiente | — | — | A: 13/15 grabaciones válidas. B: 21 escenas aportadas. Reproductor: nueve vídeos y 68 saltos verificados | Escenarios en el HTML de `DemoSSD_GXX.zip`.. |

La [restauración técnica V-14](../specs/002-gestionar-datos-maestros/validation/recovery.md) ensayó la recuperación de maestros, diario y resultados de operación en un entorno aislado. La [aceptación H5](../specs/001-reparto-specs-inventario/integration-tasks.md) mantiene sus propias condiciones de validación con operarios, restauración operativa y conciliación. La demo reúne ambos cortes. Sigue pendiente la regresión funcional sobre una revisión de código común que incorpore el PR #12 junto con HU-01 y HU-06.

### Recorridos entre A y B

| Recorrido | Precondiciones y datos | Pasos | Esperado | Observado y versión |
| --- | --- | --- | --- | --- |
| Recepción en ubicaciones gestionadas por B (HU-07 y HU-01 de A, HU-02 de B) | Compra enviada de 10 UD, un destino de almacenamiento y otro de recepción habilitados mediante la API de ubicaciones. | Confirmar 6 UD, repartidas 2 + 4 entre ambos destinos. Confirmar las 4 UD restantes en una segunda recepción. | Avanzar de 6 recibidas y 4 pendientes a 10 recibidas y 0 pendientes, con nueva versión del pedido. | Confirmado en Chromium sobre el código de `5718ce8` y grabado en `DemoSSD_GXX.zip`. La [prueba SQL](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/blob/018cff83bb40d40b85003e5364fc639bed95de9d/specs/008-registrar-recepcion-mercancia/validation/acceptance.md) comprobó también el saldo. |
| Consultar un artículo inactivo (HU-08 de A, HU-05 de B) | Artículo desactivado mediante maestros, con combinaciones de stock y filtros por ubicación y lote. | Buscar el artículo y consultar existencias con y sin el filtro de ubicaciones sin stock. | Identificar el artículo inactivo y ocultar las combinaciones con físico cero salvo petición explícita. | Comportamiento observado en el vídeo de B y documentado en [su escenario E2E](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/blob/fae4f7c7d942ef97a3ad3827339a634333ddbd25/frontend/e2e/demo/demo-hu05.spec.ts). |
| Rechazo de recepción por destino congelado (HU-01 de A, HU-04 de B) | Conteo abierto y una recepción con ese destino. | Confirmar y releer compra, diario y saldo. | `409 CountScopeFrozen` y cero efectos de la recepción. | Pendiente con el escritor real en el corte conjunto. B observó un 200 en la siembra de pruebas D-17, que no acredita el contrato de recepción. |

## Despliegue y uso

Con Docker Engine o Docker Desktop en marcha, ejecutar `docker compose up` desde la raíz y abrir `http://localhost:8081`. Compose construye la aplicación y ejecuta la inicialización de SQL Server. El [README](../README.md) documenta requisitos, puertos, configuración local y cuenta inicial. [El ejemplo de entorno](../.env.example) identifica las variables que pueden ajustarse.

Para el recorrido de usuario, iniciar sesión con una cuenta del rol que corresponda. Preparar artículo y ubicación desde sus catálogos. Consultar existencias por artículo y filtrar por ubicación o lote. Para un pedido de cliente, revisar líneas y cantidades antes de guardar. Al reservar, elegir explícitamente ubicación, lote y cantidad. Si se pierde la respuesta de una escritura, comprobar el resultado desde operaciones pendientes antes de repetirla.

## Planificación y seguimiento

Los [hitos H0–H5](../specs/001-reparto-specs-inventario/plan.md#hitos-y-coordinación) encadenan la base común, maestros y ubicaciones, pedidos y reservas, movimientos, conteos y alertas, y la aceptación. El [backlog de integración](../specs/001-reparto-specs-inventario/integration-tasks.md) asigna la verificación de cada dependencia al equipo que incorpora el escritor consumidor.

### Registro de refinamiento

| Historia y criterio | Hallazgo o decisión | Comando | Documentos o código | Tarea y commit | Prueba y resultado | Responsable | Revisor | Estado |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| HU-05 · FR-026 | El identificador del alta desaparecía al navegar al detalle. | converge → implement | Contrato de contexto y detalle de pedido. | C3 · [`8009807`](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/commit/800980779c54624e2fd38aa23d301ae401e7d341). | SQL muestra el ID correcto tras el replay. Vitest comprueba que el detalle lo muestra. | B | A | Cerrado. |
| HU-05 · FR-004/015 | La primera página limitaba las combinaciones para filtrar o reservar. | converge → implement | Servicio de stock, filtros y diálogo de reserva. | C4 · [`363de56`](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/commit/363de56ec5aad5aaa105dbea41ab4e1d1b417a89). | Vitest verifica filtros con 30 combinaciones y reserva en la combinación 126 de 130. | B | A | Cerrado. |
| HU-03 · P-02/H-02 | El primer trigger admitía consumos posteriores con el mismo ID de operación. | converge → implement | Cabecera de salida, migración, triggers y pruebas de esquema. | C1 · [`a6ec4b2`](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/commit/a6ec4b2e0fc62de3ac4655fceee143e791345df7). | H-02 comprueba la inserción tardía y los dos órdenes de escritura. H-14 comprueba la conciliación. | B | A | Corregido y fusionado en PR #10. |

### Matriz de trazabilidad

Cada fila relaciona un criterio representativo con su plan, la tarea y la prueba. Las tablas de aceptación de cada historia conservan la cobertura detallada. Una revisión de diseño registrada y la revisión cruzada de una fila son evidencias distintas.

| Historia y criterio | Decisión de plan | Tarea | Prueba y resultado | Responsable y revisión |
| --- | --- | --- | --- | --- |
| HU-01 · FR-001–004 / V-02 | Confirmar contra el pendiente de compra bajo bloqueo y en la misma transacción que el saldo. [Plan](../specs/008-registrar-recepcion-mercancia/plan.md) | T011, T015, T019 | SQL y Chromium ejercitaron 6 + 4 y comprobaron estados y versiones. [Evidencia](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/blob/018cff83bb40d40b85003e5364fc639bed95de9d/specs/008-registrar-recepcion-mercancia/validation/acceptance.md) | A · registro A+B pendiente en T043. |
| HU-02 · US4, concurrencia de edición | Validar la versión de la representación al actualizar. [Plan](../specs/003-gestionar-ubicaciones/plan.md) | T032, revisión `dfc1fd1` | La revisión reproduce el ETag que cambia con el contenido y comprueba replay y edición obsoleta. [Informe](../specs/003-gestionar-ubicaciones/validation/pr-review-2026-11-19.md) | B · A, PR #6. |
| HU-03 · FR-013 | Sellar la salida y su evidencia de consumo por asignación al confirmar. [Plan](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/tree/fae4f7c7d942ef97a3ad3827339a634333ddbd25/specs/005-registrar-salida-mercancia/plan.md) | C1 `a6ec4b2` | H-02 rechaza consumos tardíos. H-14 concilia el total declarado. [Convergencia](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/tree/fae4f7c7d942ef97a3ad3827339a634333ddbd25/specs/005-registrar-salida-mercancia/validation/converge-1.md) | B · A, PR #10. |
| HU-04 · FR-014 y D-04 | Congelar las ubicaciones del ámbito y consultar la congelación en los escritores. [Plan](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/tree/fae4f7c7d942ef97a3ad3827339a634333ddbd25/specs/006-realizar-inventarios-ciclicos/plan.md) | T005 y C1 | `CountScopeFrozenTests` y Chromium comprueban los bloqueos sobre reserva y ubicación. La recepción conjunta sigue pendiente. [PR #12](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/12) | B · A, diseño P-01–P-08 registrado en T001. |
| HU-05 · FR-026 | Cargar el resultado persistido del alta con su identificador de operación. [Plan](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/tree/fae4f7c7d942ef97a3ad3827339a634333ddbd25/specs/004-consultar-existencias/plan.md) | C3 `8009807` | Replay SQL y detalle en Vitest muestran el identificador esperado. [Convergencia](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/tree/fae4f7c7d942ef97a3ad3827339a634333ddbd25/specs/004-consultar-existencias/validation/converge-1.md) | B · A, PR #9. |
| HU-06 · Q06-05 | Consultar con permiso de alertas y construir la propuesta editable sin guardar. [Plan](../specs/009-configurar-alertas-stock-minimo/plan.md) | T010–T012 | Chromium comprueba permiso, edición, artículo inactivo y ausencia de pedido o correo. [Acta](../specs/009-configurar-alertas-stock-minimo/validation/acceptance.md) | A · registro A+B pendiente en T016. |
| HU-07 · SC-007 | Medir consulta y guardados con 25 sesiones y el perfil de carga documentado. [Plan](../specs/007-gestionar-pedidos-proveedores/plan.md) | T055 | p95 de consulta 1,25 s, alta 3,65 s y corrección 3,60 s. El resultado mantiene abierto el criterio. [Medición](../specs/007-gestionar-pedidos-proveedores/validation/performance.md) | A · revisión técnica A+B registrada en [el acta](../specs/007-gestionar-pedidos-proveedores/validation/shared-review.md). |
| HU-08 · Q-01, duplicados | Normalizar códigos y validar unicidad conservando la entrada del usuario. [Plan](../specs/002-gestionar-datos-maestros/plan.md) | T025 y T088 | Pruebas de maestro y demo de familias con duplicado y datos conservados. [Aceptación](../specs/002-gestionar-datos-maestros/validation/acceptance.md) | A · evidencia técnica en acta. |

### Balance de iteración y evidencias

- **Clarify, HU-04.** Q1 y Q2 quedaron respondidas en `4d0a7cd`, 2/2 preguntas de esa iteración. En la misma historia, la decisión P-07 distingue la diferencia física y el acumulado servido del pedido. La demo muestra un despacho registrado de 6 con 4 unidades realmente salidas. El ajuste devuelve 2 al físico y el pedido conserva 6 servidas. [Especificación y decisiones](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/tree/fae4f7c7d942ef97a3ad3827339a634333ddbd25/specs/006-realizar-inventarios-ciclicos).
- **Analyze, HU-04.** El primer informe registró 6 hallazgos altos y 0 críticos. El segundo registró 0 altos y 0 críticos tras corregir los 6 altos. [Informe inicial](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/tree/fae4f7c7d942ef97a3ad3827339a634333ddbd25/specs/006-realizar-inventarios-ciclicos/validation/analyze-1.md) · [informe final](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/tree/fae4f7c7d942ef97a3ad3827339a634333ddbd25/specs/006-realizar-inventarios-ciclicos/validation/analyze-2.md).
- **Converge, HU-04.** G1–G8 se abordaron en C1–C8. El segundo informe abrió una brecha menor G9, cerrada en `8548d79`. [Iteración 1](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/tree/fae4f7c7d942ef97a3ad3827339a634333ddbd25/specs/006-realizar-inventarios-ciclicos/validation/converge-1.md) · [iteración 2](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/tree/fae4f7c7d942ef97a3ad3827339a634333ddbd25/specs/006-realizar-inventarios-ciclicos/validation/converge-2.md).
- **Balance de esta matriz.** 6 de 8 filas tienen verificación positiva del criterio entero en su corte. La fila de HU-04 tiene cobertura parcial porque falta la recepción con destino congelado y la de HU-07 tiene un resultado de rendimiento desfavorable. Es una muestra de 8 criterios, con uno por historia. Los 34 escenarios de vídeo son 13 de A y 21 de B.
- **Registro de herramientas.** `.specify/init-options.json` registra Spec Kit 1.0.5 en el checkout de A. Los expedientes de B enlazados conservan las salidas publicadas. Esta memoria no atribuye a otras historias informes, versiones de herramienta o revisiones cruzadas sin evidencia específica. El historial de referencia incluye HU-07 `b13bcd9`, HU-03 `f65fefe`, HU-01 `018cff8`, HU-06 `5718ce8`. La demo se entrega en `DemoSSD_GXX.zip`.

## Conclusiones

El equipo implementó los recorridos de inventario, compras y aprovisionamiento y demostró las ocho historias en vídeo. El refinamiento mejoró la recuperación de altas, la cobertura de filtros y reservas y la trazabilidad del consumo de una salida. Quedan pendientes la regresión conjunta, la prueba de recepción con destino congelado y los registros de revisión de HU-01 y HU-06. El rendimiento de guardados de HU-07 superó el objetivo. Los ensayos completos de HU-01 y HU-06 y las condiciones operativas de H5 siguen abiertos. El E2E de compensación de HU-01 y el de alta y edición de artículos de HU-08 fallaron al grabar y sus escenas se omitieron.

## Anexos

- [Enunciado de la práctica](https://drive.google.com/file/d/160hShHX_Xp8y3mAqIh653aqKPReyR_wA/view?usp=drivesdk).
- [Especificaciones del corte A](../specs/), [expedientes del corte B](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/tree/fae4f7c7d942ef97a3ad3827339a634333ddbd25/specs), [constitución](../.specify/memory/constitution.md) y [plan global](../.specify/memory/project-plan.md).
- **Físico** es la cantidad registrada en una ubicación y lote. **Reservado** es la cantidad comprometida. **Disponible** es la diferencia entre físico y reservado.
- **Conteo cíclico** es el recuento físico de un ámbito del almacén. **ETag** es la versión que comprueba una escritura. **Idempotencia** permite volver a consultar o repetir una intención sin duplicar sus efectos.
