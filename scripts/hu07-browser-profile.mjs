// Route preparation only. Keep the SPA/session warm instead of reloading Angular before
// every measured click. Native history/popstate follows the same router path as back/forward.
export async function prepareRoute(page, target, responseMatches) {
  const destination = new URL(target, page.url()); const current = new URL(page.url());
  if (destination.pathname === current.pathname && destination.search === current.search) return;
  const pending = page.waitForResponse(responseMatches, { timeout: 30_000 });
  pending.catch(() => {});
  await page.evaluate(target => {
    history.pushState(null, '', target);
    dispatchEvent(new PopStateEvent('popstate', { state: null }));
  }, target);
  const response = await pending;
  if (!response.ok()) throw new Error(`Preparación de ruta HTTP ${response.status()}`);
  await response.finished();
}
