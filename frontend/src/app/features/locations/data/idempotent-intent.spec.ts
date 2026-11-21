import { IntentStorage, completeIntent, intentKey, isDefinitiveStatus } from './idempotent-intent';

function storage(): IntentStorage & { values: Map<string, string> } {
  const values = new Map<string, string>();
  return {
    values,
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => void values.set(key, value),
    removeItem: key => void values.delete(key)
  };
}

describe('idempotent-intent', () => {
  it('reutiliza la misma clave mientras el cuerpo no cambie (reintento tras error de red)', () => {
    const store = storage();
    const first = intentKey('location:create', { code: 'P01' }, store);
    expect(first).toMatch(/^[0-9a-f-]{36}$/);
    expect(intentKey('location:create', { code: 'P01' }, store)).toBe(first);
  });

  it('usa una clave nueva si cambia el cuerpo o tras una respuesta definitiva', () => {
    const store = storage();
    const first = intentKey('location:update:1', { kind: 'Storage' }, store);
    const changed = intentKey('location:update:1', { kind: 'Receiving' }, store);
    expect(changed).not.toBe(first);
    completeIntent('location:update:1', store);
    expect(store.values.size).toBe(0);
    expect(intentKey('location:update:1', { kind: 'Receiving' }, store)).not.toBe(changed);
  });

  it('funciona aunque el almacenamiento falle', () => {
    const broken: IntentStorage = {
      getItem: () => { throw new Error('bloqueado'); },
      setItem: () => { throw new Error('bloqueado'); },
      removeItem: () => { throw new Error('bloqueado'); }
    };
    const first = intentKey('location:create', { code: 'X' }, broken);
    expect(intentKey('location:create', { code: 'X' }, broken)).toBe(first);
    expect(() => completeIntent('location:create', broken)).not.toThrow();
  });

  it('solo 2xx y 4xx son respuestas definitivas', () => {
    expect([200, 201, 409, 412, 422].every(isDefinitiveStatus)).toBe(true);
    expect([0, 500, 503].some(isDefinitiveStatus)).toBe(false);
  });
});
