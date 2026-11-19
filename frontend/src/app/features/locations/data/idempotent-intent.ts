/**
 * Clave de idempotencia por intención (contrato UI común, estado 7; HU-02 FR-018).
 *
 * Una intención es «qué quiere hacer el usuario» (p. ej. `location:update:<id>`) con un cuerpo
 * concreto. Mientras el resultado sea incierto (red caída, 5xx, timeout) se reutiliza la misma
 * clave para comprobarlo sin duplicar. Si el cuerpo cambia, es otra intención y lleva clave
 * nueva. Tras una respuesta definitiva (2xx o 4xx) la clave se descarta.
 *
 * Solo se guarda la clave y el cuerpo de la ubicación (datos no sensibles) en sessionStorage;
 * si el almacenamiento no está disponible, se usa memoria y la página sigue funcionando.
 */
const PREFIX = 'inventario.intent.';

interface StoredIntent {
  readonly key: string;
  readonly body: string;
}

export interface IntentStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

const memory = new Map<string, string>();
const memoryStorage: IntentStorage = {
  getItem: key => memory.get(key) ?? null,
  setItem: (key, value) => void memory.set(key, value),
  removeItem: key => void memory.delete(key)
};

function defaultStorage(): IntentStorage {
  try {
    return globalThis.sessionStorage ?? memoryStorage;
  } catch {
    return memoryStorage;
  }
}

function read(storage: IntentStorage, intent: string): StoredIntent | null {
  let raw: string | null = null;
  try {
    raw = storage.getItem(PREFIX + intent);
  } catch {
    raw = null;
  }
  raw ??= memoryStorage.getItem(PREFIX + intent);
  try {
    return raw ? (JSON.parse(raw) as StoredIntent) : null;
  } catch {
    return null;
  }
}

function write(storage: IntentStorage, intent: string, value: StoredIntent): void {
  const raw = JSON.stringify(value);
  try {
    storage.setItem(PREFIX + intent, raw);
  } catch {
    memoryStorage.setItem(PREFIX + intent, raw);
  }
}

/** Devuelve la clave pendiente de esta intención si el cuerpo no cambió; si no, una nueva. */
export function intentKey(intent: string, body: unknown, storage: IntentStorage = defaultStorage()): string {
  const serialized = JSON.stringify(body ?? null);
  const stored = read(storage, intent);
  if (stored && stored.body === serialized) return stored.key;
  const key = globalThis.crypto.randomUUID();
  write(storage, intent, { key, body: serialized });
  return key;
}

/** Descarta la clave tras una respuesta definitiva. */
export function completeIntent(intent: string, storage: IntentStorage = defaultStorage()): void {
  try {
    storage.removeItem(PREFIX + intent);
  } catch {
    // El almacenamiento puede no estar disponible; la copia en memoria se borra igualmente.
  }
  memoryStorage.removeItem(PREFIX + intent);
}

/** Respuesta definitiva: 2xx o 4xx. Red caída (0), 5xx o timeout dejan el resultado incierto. */
export function isDefinitiveStatus(status: number): boolean {
  return status >= 200 && status < 500;
}
