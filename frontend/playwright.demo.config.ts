import { defineConfig, test } from '@playwright/test';
import { chromium } from 'playwright-core';
import base from './playwright.config';

const SIZE = { width: 1280, height: 720 };

// Los E2E de HU-03, HU-04 y HU-05 abren sus sesiones con browser.newContext(), y la opción `video` solo
// graba los contextos de los fixtures. Sin tocar esos specs ni los fixtures de A, el navegador lanzado
// en esta configuración añade recordVideo a cada contexto creado durante un test y adjunta el vídeo de
// cada página al test en curso, igual que la grabación normal. Solo se usa para la demo.
const browserType = Object.getPrototypeOf(chromium);
const launch = browserType.launch;
let patched = false;
browserType.launch = async function (...args: unknown[]) {
  const browser = await launch.apply(this, args);
  const proto = Object.getPrototypeOf(browser);
  if (!patched) {
    patched = true;
    const newContext = proto.newContext;
    proto.newContext = async function (options: Record<string, unknown> = {}) {
      let info: ReturnType<typeof test.info> | undefined;
      try { info = test.info(); } catch { info = undefined; }
      if (!info || options['recordVideo']) return newContext.call(this, options);
      const context = await newContext.call(this, { ...options, recordVideo: { dir: info.outputPath('videos'), size: SIZE } });
      const newPage = context.newPage.bind(context);
      context.newPage = async () => {
        const page = await newPage();
        const video = page.video();
        if (video) info!.attachments.push({ name: 'video', contentType: 'video/webm', path: await video.path() });
        return page;
      };
      return context;
    };
  }
  return browser;
};

// Grabación de la demo (Persona B: HU-02, HU-05, HU-03, HU-04). Reutiliza la config de E2E y solo
// cambia la grabación: vídeo de cada test a 1280x720, acciones ralentizadas y una ejecución por vez.
// Uso: npx playwright test -c playwright.demo.config.ts locations stock shipping hu04-cycle-counts
export default defineConfig({
  ...base,
  workers: 1,
  retries: 0,
  timeout: 300_000,
  expect: { timeout: 20_000 },
  outputDir: 'test-results/demo',
  reporter: [['list'], ['json', { outputFile: 'test-results/demo/report.json' }]],
  use: {
    ...base.use,
    viewport: { width: 1280, height: 720 },
    video: { mode: 'on', size: { width: 1280, height: 720 } },
    launchOptions: { slowMo: 600 }
  },
  projects: (base.projects ?? []).map(project => ({
    ...project,
    use: { ...project.use, viewport: { width: 1280, height: 720 } }
  }))
});
