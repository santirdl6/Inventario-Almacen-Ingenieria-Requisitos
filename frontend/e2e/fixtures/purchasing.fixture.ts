import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { test as base, expect, purchasingProfile } from './master-data.fixture';

export const test = base.extend<{ smtp: { stop(): void; start(): Promise<void>; messages(): Promise<unknown>;
  message(id: string): Promise<unknown>; part(id: string, partId: string): Promise<Buffer>; raw(id: string): Promise<Buffer> } }>({
  smtp: async ({ playwright, environment }, use) => {
    if (!purchasingProfile) throw new Error('Usa INVENTORY_E2E_PROFILE=hu07 y un proyecto inventario-hu07-e2e.');
    const project = process.env['HU08_E2E_PROJECT']!;
    const root = resolve(__dirname, '../../..');
    const api = await playwright.request.newContext({ baseURL: 'http://localhost:8027' });
    const compose = (action: 'stop' | 'start') => execFileSync('docker', ['compose', '--project-name', project,
      '--file', resolve(root, 'docker-compose.yml'), action, 'smtp'], { cwd: root, stdio: 'inherit', timeout: 20_000 });
    const ready = async () => {
      await expect.poll(async () => {
        try { return (await api.get('/api/v1/messages', { timeout: 1_000 })).ok(); }
        catch { return false; }
      }, { timeout: 15_000 }).toBe(true);
      // Probe the SMTP listener from its own container, without publishing it on the host.
      execFileSync('docker', ['compose', '--project-name', project, '--file', resolve(root, 'docker-compose.yml'),
        'exec', '-T', 'smtp', '/mailpit', 'readyz'], { cwd: root, timeout: 10_000, stdio: 'inherit' });
    };
    try {
      await ready();
      await use({ stop: () => { compose('stop'); }, start: async () => { compose('start'); await ready(); },
        messages: async () => { const response = await api.get('/api/v1/messages'); expect(response.ok()).toBe(true); return response.json(); },
        message: async id => { const response = await api.get(`/api/v1/message/${encodeURIComponent(id)}`); expect(response.ok()).toBe(true); return response.json(); },
        part: async (id, partId) => { const response = await api.get(`/api/v1/message/${encodeURIComponent(id)}/part/${encodeURIComponent(partId)}`); expect(response.ok()).toBe(true); return response.body(); },
        raw: async id => { const response = await api.get(`/api/v1/message/${encodeURIComponent(id)}/raw`); expect(response.ok()).toBe(true); return response.body(); }
      });
    } finally {
      try { compose('start'); await ready(); } finally { await api.dispose(); }
    }
  }
});

export { expect };
