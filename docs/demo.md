# Demo en vídeo de Persona B (HU-02, HU-05, HU-03, HU-04)

La grabó un agente de Claude Code el 2026-11-21. Primero, a partir de los E2E de Playwright que ya existían
(`locations.spec.ts`, `stock.spec.ts`, `shipping.spec.ts` y `hu04-cycle-counts.spec.ts`), sin cambiarlos. Después
se amplió con escenarios de error, permiso y estado bloqueante en specs nuevos de `frontend/e2e/demo/`, que
reutilizan el fixture de A y la siembra D-17. Esos specs solo se ejecutan con `playwright.demo.config.ts`: con la
config de E2E normal se saltan.

```bash
# 1. Grabar: vídeo 1280x720, slowMo 600 ms, un worker y sin reintentos.
cd frontend
npx playwright test -c playwright.demo.config.ts locations stock shipping hu04-cycle-counts e2e/demo

# 2. Montar con ffmpeg en Docker (jrottenberg/ffmpeg:6.1-alpine, contenedor efímero).
cd ..
bash demo/montar.sh          # CRF=28; con CRF=32 comprime más
```

- `playwright.demo.config.ts` amplía la config de E2E. Además, añade `recordVideo` a los contextos que
  los specs abren con `browser.newContext()`; solo para la demo.
- `montar.sh` genera:
  - `demo/video/NN-hu0X-….mp4` y `.jpg` por historia;
  - `demo/video/demo-completa.mp4`, con una pantalla «HU-0X · nombre» antes de cada historia y otra
    antes de cada escenario;
  - `demo/lista.txt` y `demo/datos.js`, con las marcas de tiempo.
- `demo/index.html` se abre con doble clic, sin servidor.

## Escenarios de la demo ampliada

Cada historia muestra primero su E2E existente y luego los escenarios de `frontend/e2e/demo/demo-hu0X.spec.ts`.
Lo que solo prepara un escenario (conteos, pedidos, reservas, salidas) se crea por API con
`frontend/e2e/demo/demo-helpers.ts`, para que el vídeo muestre lo que se demuestra. Cuando el servidor también debe
rechazar algo que la interfaz ya oculta, el spec lo comprueba por API (403/409).

| Historia | Escenarios nuevos |
| --- | --- |
| HU-02 | Código con caracteres no válidos; última zona de recepción activa (el diálogo lo impide y el servidor responde 409 `NoActiveReceivingLeft`); cuenta sin `Locations.Manage`. |
| HU-05 | Consulta con filtros de ubicación y lote, físico 0 oculto salvo con «Mostrar ubicaciones sin existencias», artículo inactivo marcado; reserva superior a lo disponible; liberación parcial con motivo obligatorio; operario sin `Reservations.Manage`. |
| HU-03 | Stock insuficiente (aviso «supera la posible» y rechazo del servidor); salida que consume toda la reserva; origen en conteo abierto (`CountScopeFrozen` al confirmar); cuenta sin `Shipping.Record`. |
| HU-04 | Ubicación congelada: reserva y desactivación rechazadas con `CountScopeFrozen` (ubicación y conteo en el mensaje) y cancelación del conteo; diferencias en dos ubicaciones con motivos y ajuste; aviso P-07 (salida de 6, salieron 4: el pedido sigue con 6 servidas); operario sin `Inventory.Adjust`. |

No incluidos:

- **Recepción bloqueada por un conteo (HU-04).** B no tiene pantalla ni endpoint de recepción (HU-01 es de A). La
  siembra D-17 de pruebas acepta entradas en una ubicación congelada (200), así que no sirve para demostrarlo.
- **Retirada de la última zona de recepción (HU-02).** Para prepararla, el spec retira por API las demás zonas de
  recepción de la base E2E y las reactiva al terminar. En el primer intento el test agotó el tiempo y no llegó a
  reactivarlas: en `InventoryHu08E2E` quedaron inactivas cuatro zonas `REC-…` de ejecuciones anteriores.

Los vídeos (`demo/video/`) no se versionan: se entregan en `DemoSSD_GXX.zip`.
