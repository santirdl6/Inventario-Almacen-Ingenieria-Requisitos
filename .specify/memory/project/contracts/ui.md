# Contrato de interacción común

Angular + PrimeNG; español y unidades explícitas. Base normativa: constitución III.
**Base compartida**: [plan global](../../project-plan.md).
Las rutas siguientes son de diseño; no se crean componentes en esta fase.

## Navegación y responsabilidad vertical

| Ruta de pantalla | Historia | Responsable | Acción principal |
|------------------|----------|-------------|------------------|
| /maestros | HU-08 | A | Editar artículos, familias y proveedores; desactivar sin borrar historial. |
| /compras | HU-07 | A | Crear borrador, revisar líneas y enviar; ver pendientes y estado de envío. |
| /recepciones | HU-01 | A | Elegir compra enviada, registrar aceptado/diferencias y destinos, confirmar. |
| /alertas | HU-06 | A | Configurar mínimo y consultar alertas con sugerencia. |
| /ubicaciones | HU-02 | B | Código único y pasillo/estantería/nivel; cantidades almacenadas. |
| /existencias | HU-05 | B | Buscar artículo y filtrar por ubicación/lote; físico, reservado y disponible. Incluye los flujos de alta/consulta del pedido mínimo y reservar/liberar, con rutas particulares definidas en su plan. |
| /salidas | HU-03 | B | Elegir un pedido existente de HU-05, origen, lote y cantidad; revisar reserva propia/ajena y confirmar la salida. |
| /inventarios | HU-04 | B | Seleccionar ubicaciones, iniciar conteo, registrar, revisar y autorizar ajustes. |

Inicio de sesión y layout son comunes. La navegación muestra funciones permitidas,
sin sustituir las políticas del backend. No hay panel comercial adicional.

## Estados de toda operación

1. Formulario con etiquetas visibles, obligatoriedad y unidad junto a la cantidad.
2. Revisión de artículo, cantidad, unidad, origen/destino y documento antes de confirmar.
3. Confirmando: bloquear doble clic y conservar OperationId y petición para un reintento.
4. Confirmado: mostrar identificador y resultado persistido; refrescar los datos afectados.
5. Error validable: mantener campos válidos, mostrar mensajes por campo y foco accesible.
6. Conflicto: explicar stock insuficiente, conteo abierto o versión obsoleta; permitir
   consultar el estado actual antes de corregir y emitir una nueva intención.
7. Respuesta perdida: «Resultado pendiente de comprobar»; consultar la operación y
   conservar la clave al reintentar. Nunca mostrar éxito por haber enviado la petición.

Un timeout de red no debe borrar la referencia de la operación en curso al recargar.
Puede conservarse esa referencia no sensible en sessionStorage; la autenticación permanece
en cookies HttpOnly. No persistir contraseñas ni tokens de autenticación en almacenamiento web.

## Información que no debe ocultarse

- Recepción parcial: pedido, recibido previo, aceptado ahora, diferencias y pendiente resultante.
- Salida: físico, reservado propio, reservado ajeno y cantidad posible; el servidor revalida.
- Stock: totales del filtro separados de la página; actualización manual visible y asOf.
- Compra: diferenciar «envío pendiente», «aceptado por servidor de correo» y estado logístico.
- Conteo: ubicaciones congeladas, teórico, contado, diferencia y autor de aprobación.
- Artículo inactivo: indicador textual y consulta del historial; no desaparece de documentos.
- Alerta: disponible, mínimo y sugerencia; no presentar la sugerencia como pedido ya creado.

## Concreción de compras y recuperación HU-07

Diseño 2026-11-20, revisión A+B pendiente: Compras reutiliza el transporte/almacén de
intenciones común y amplía permisos por acción, navegación y exclusión local por pedido
entre edición y envío. Recuperar respuesta incierta conserva intención; retry de Failed
crea otra acción sobre entrega congelada. Detalle histórico y contacto actual se separan;
la revisión confirma destinatario/documento exactos. Rutas/estados/accesibilidad en
[UI HU-07](../../../../specs/007-gestionar-pedidos-proveedores/contracts/ui.md).
Esta ampliación conserva entradas previas de maestros/ubicaciones y separación por actor.

## Accesibilidad y validación con operarios

Etiquetas y mensajes en español, foco tras error, operación por teclado, estados con texto
e icono además de color. Tablas y diálogos PrimeNG con nombres accesibles y orden de foco.

Con trabajadores representativos se validan recepción y salida sobre datos de prueba,
sin asistencia técnica. Deben completar los recorridos sin errores de registro no detectados.
Se documentan participantes por rol, tareas, resultado, incidencias y correcciones.
No se declara validada la usabilidad solo porque pase Playwright.
