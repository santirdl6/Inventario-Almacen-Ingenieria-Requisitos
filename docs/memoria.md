# Inventario y almacén

## Portada y resumen

- **Asignatura** Ingeniería de Requisitos
- **Práctica** Desarrollo de un sistema de información mediante Spec-Driven Development
- **Estado** Borrador de entrega · 21 de noviembre de 2026

Aplicación web para el personal de almacén y compras. Registra dónde se guarda cada artículo, distingue el físico de lo reservado y permite reconstruir los cambios de inventario. El corte de integración documentado incorpora maestros, ubicaciones, consultas, pedidos de cliente y reservas. Las salidas tienen un PR con evidencia propia. El estado de las demás historias figura en el análisis.

## URL del repositorio GitHub

[Inventario-Almacen-Ingenieria-Requisitos](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos)

## Introducción

El equipo extrajo ocho historias de la descripción del almacén. El objetivo es que el sistema refleje los movimientos físicos y conserve su origen, cantidad, responsable y documento. Las ubicaciones identificadas permiten encontrar lo registrado. La separación entre físico, reservado y disponible permite preparar pedidos sin utilizar compromisos de otros clientes.

La [constitución](../.specify/memory/constitution.md) fija las obligaciones de correspondencia física, localización, facilidad de uso, trazabilidad e integridad. Cada historia convierte esas obligaciones en criterios comprobables y documenta su ciclo de especificación, planificación, tareas, implementación y revisión.

## Análisis

### Historias de usuario y estado

| Historia | Responsable | Resultado funcional | Estado de este borrador |
| --- | --- | --- | --- |
| HU-01 · Recepciones | A | Registrar entregas parciales, diferencias y destinos reales contra compras. | [Spec local en borrador](../specs/008-registrar-recepcion-mercancia/spec.md). |
| HU-02 · Ubicaciones | B | Identificar ubicaciones y consultar cantidades de un artículo en cada una. | Integrada. [Revisión del PR #6](revisiones/pr-006-hu02.md). |
| HU-03 · Salidas | B | Confirmar una salida contra un pedido, consumir su reserva propia y actualizar lo servido. | [PR #10 abierto](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/10) con evidencia de su rama. |
| HU-04 · Inventarios cíclicos | B | Congelar un ámbito, comparar el conteo físico con el teórico y registrar un ajuste autorizado. | [Spec y aclaraciones publicadas](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/blob/b/hu-04-realizar-inventarios-ciclicos/specs/006-realizar-inventarios-ciclicos/spec.md). |
| HU-05 · Existencias y reservas | B | Consultar físico, reservado y disponible, y mantener el pedido de cliente mínimo y sus reservas. | [PR #9 integrado](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/9). |
| HU-06 · Alertas de mínimo | A | Configurar el mínimo por artículo y avisar cuando el disponible cae por debajo. | Historia asignada en [agrupamiento](agrupamiento.md). Falta acreditar su implementación. |
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
| HU-05 · `363de56` | 207 aprobadas, 1 omitida | 147/147 | 16/16 con el filtro documentado | [Informe de HU-05](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/blob/2894dad4cbc5d4be5c37454777064984bf5a135e/specs/004-consultar-existencias/quickstart.md#evidencia). |
| HU-03 · `f65fefe` | 291 aprobadas, 1 omitida | 187/187 | 17/17 | [Evidencia del PR #10](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/pull/10). |

La [restauración técnica V-14](../specs/002-gestionar-datos-maestros/validation/recovery.md) ensayó la recuperación de maestros, diario y resultados de operación en un entorno aislado. La [aceptación H5](../specs/001-reparto-specs-inventario/integration-tasks.md) mantiene sus propias condiciones de validación con operarios, restauración operativa y conciliación. Quedan por registrar aquí la regresión sobre el corte final y el resultado observado de los recorridos cruzados.

| Recorrido integrado previsto | Precondición y datos | Pasos | Resultado esperado | Resultado y versión |
| --- | --- | --- | --- | --- |
| Compra, recepción y consulta | Compra enviada de 10 unidades, artículo activo y destino habilitado. | Recibir 6 y consultar la compra y las existencias por ubicación. | 6 unidades añadidas al físico y 4 pendientes en la compra. | Pendiente de ejecutar en integración. |
| Recepción rechazada durante un conteo | Una ubicación de destino está en conteo abierto. La recepción incluye ese destino. | Confirmar la recepción y consultar compra, stock y diario. | `409 CountScopeFrozen`, con compra, stock y diario sin efectos de la recepción. | Pendiente de ejecutar en integración. |

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
| HU-03 · P-02/H-02 | El primer trigger admitía consumos posteriores con el mismo ID de operación. | converge → implement | Cabecera de salida, migración, triggers y pruebas de esquema. | C1 · [`a6ec4b2`](https://github.com/santirdl6/Inventario-Almacen-Ingenieria-Requisitos/commit/a6ec4b2e0fc62de3ac4655fceee143e791345df7). | H-02 comprueba la inserción tardía y los dos órdenes de escritura. H-14 comprueba la conciliación. | B | A | Diseño aceptado. PR abierto en este corte. |

El registro comienza con casos verificables. El cierre debe incorporar la matriz de trazabilidad de las ocho historias, sus revisiones, los informes iniciales y finales, y el balance de criterios verificados con sus denominadores.

## Conclusiones

La base integrada permite gestionar maestros y ubicaciones y registrar compromisos de clientes sin perder la relación con el saldo. El refinamiento aportó recorridos verificables para recuperar altas y reservar más allá de la primera página. El cierre de entrega requiere consolidar el estado de las ocho historias, verificar los recorridos cruzados y registrar las limitaciones operativas con su evidencia.

## Anexos

- [Enunciado de la práctica](https://drive.google.com/file/d/160hShHX_Xp8y3mAqIh653aqKPReyR_wA/view?usp=drivesdk).
- [Especificaciones](../specs/), [constitución](../.specify/memory/constitution.md) y [plan global](../.specify/memory/project-plan.md).
- **Físico** es la cantidad registrada en una ubicación y lote. **Reservado** es la cantidad comprometida. **Disponible** es la diferencia entre físico y reservado.
- **Conteo cíclico** es el recuento físico de un ámbito del almacén. **ETag** es la versión que comprueba una escritura. **Idempotencia** permite volver a consultar o repetir una intención sin duplicar sus efectos.
