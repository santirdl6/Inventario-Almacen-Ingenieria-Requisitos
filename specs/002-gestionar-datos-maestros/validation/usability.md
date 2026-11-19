# Validación de HU-08 con gestores

T091 · [SC-006](../spec.md#success-criteria-mandatory) · [guía de validación](../quickstart.md#5-rendimiento-y-validación-con-trabajadores)

**Estado**: convocatoria pendiente. La evidencia se completa durante las sesiones
con tres gestores representativos.

## Preparación de la sesión

Usar una base de pruebas separada con los cambios de HU-08. Asignar a cada persona
una cuenta `GestorMaestros` propia y códigos de prueba distintos para evitar cruces
entre sesiones. Registrar la versión de la aplicación y el navegador. La persona
observadora entrega las tareas y registra lo sucedido sin indicar qué botones pulsar.
Registrar cualquier intervención y comprobar los datos finales después de cada tarea.

## Tareas para cada gestor

1. Crear una familia con el código y nombre de su tarjeta.
2. Dar de alta un artículo en esa familia con la unidad base y las opciones de
   fracciones y lote indicadas en la tarjeta. Comprobar que aparece en el catálogo.
3. Dar de alta un proveedor con el código y nombre de la tarjeta, dejando vacío
   el correo opcional.
4. Introducir un correo inválido en el proveedor y corregirlo cuando la aplicación
   lo señale. Verificar que el nombre y el código siguen siendo correctos.
5. Desactivar el artículo anterior, confirmar el cambio y localizarlo en el catálogo
   de artículos inactivos.

La tarjeta contiene únicamente los datos y el resultado esperado. El observador
verifica si la persona completa las cinco tareas sin asistencia técnica, detecta y
corrige errores y deja los registros en el estado esperado. Si aparece una incidencia,
registrar la corrección y repetir el escenario afectado antes de aceptar la interfaz.

## Datos de las tarjetas

| Sesión | Familia | Artículo y definición | Proveedor | Correo inválido y corrección |
|--------|---------|-----------------------|-----------|------------------------------|
| G1 | G1-FAM · Ferretería | G1-ART · Tornillo M6 · UD · enteras · sin lote | G1-PRV · Suministros Norte | `pedidos` → `pedidos@norte.example` |
| G2 | G2-FAM · Material eléctrico | G2-ART · Cable flexible · M · fracciones · sin lote | G2-PRV · Eléctrica Centro | `ventas@` → `ventas@centro.example` |
| G3 | G3-FAM · Adhesivos | G3-ART · Adhesivo industrial · KG · fracciones · con lote | G3-PRV · Distribuciones Sur | `contacto sur.example` → `contacto@sur.example` |

Resultado esperado por tarjeta: familia y proveedor activos, correo corregido
y artículo inactivo en la familia indicada.

## Registro

| Participante y rol | Fecha, versión y navegador | Familia | Artículo | Proveedor | Corrección | Desactivación | Ayuda, errores y seguimiento |
|--------------------|----------------------------|---------|----------|-----------|------------|---------------|------------------------------|
| G1 · GestorMaestros | Pendiente | | | | | | |
| G2 · GestorMaestros | Pendiente | | | | | | |
| G3 · GestorMaestros | Pendiente | | | | | | |

En cada tarea anotar resultado, tiempo e incidencias. Conservar los identificadores
de prueba en la hoja de evidencia y utilizar seudónimos en el repositorio.
