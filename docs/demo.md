# Demo en vídeo de Persona B (HU-02, HU-05, HU-03, HU-04)

La grabó un agente de Claude Code el 2026-11-21 a partir de los E2E de Playwright que ya existían:
`locations.spec.ts`, `stock.spec.ts`, `shipping.spec.ts` y `hu04-cycle-counts.spec.ts`. No se escribió
ningún escenario nuevo ni se cambiaron los E2E ni su configuración.

```bash
# 1. Grabar: vídeo 1280x720, slowMo 600 ms, un worker y sin reintentos.
cd frontend
npx playwright test -c playwright.demo.config.ts locations stock shipping hu04-cycle-counts

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

Los vídeos (`demo/video/`) no se versionan: se entregan en `DemoSSD_GXX.zip`.
