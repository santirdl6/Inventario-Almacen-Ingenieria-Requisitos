import { Injectable, inject, signal } from '@angular/core';
import { SessionService } from '../session/session.service';

export interface PendingOperation {
  operationId: string; actorId: string; method: string; resource: string;
  payload?: unknown; etag?: string;
}
const KEY = 'inventario.pending-operations';

export function mutationPermission(resource: string): string | null {
  if (/^\/api\/v1\/(families|articles|suppliers)(\/[0-9a-f-]{36})?$/i.test(resource)) return 'MasterData.Manage';
  if (/^\/api\/v1\/locations(\/[0-9a-f-]{36})?$/i.test(resource)) return 'Locations.Manage';
  return null;
}

@Injectable({ providedIn: 'root' })
export class PendingOperationStore {
  private readonly session = inject(SessionService);
  private readonly state = signal<PendingOperation[]>(this.restore());
  readonly operations = this.state.asReadonly();
  forResource(resource: string): PendingOperation | undefined { return this.operations().find(item => item.actorId === this.session.user()?.id && item.resource === resource); }
  constructor() {
    this.session.loggedOut.subscribe(() => {
      this.state.update(items => items.map(({ operationId, actorId, method, resource }) => ({ operationId, actorId, method, resource })));
      this.persist();
    });
  }
  begin(method: string, resource: string, payload: unknown, etag?: string): PendingOperation {
    const actorId = this.session.user()?.id;
    if (!actorId) throw new Error('Inicia sesión antes de guardar.');
    const permission = mutationPermission(resource);
    if (!permission || !this.session.has(permission))
      throw new Error('La intención requiere el permiso de la operación y una ruta válida.');
    const existing = this.forResource(resource);
    if (existing) {
      if (existing.method !== method || !('payload' in existing) || JSON.stringify(existing.payload) !== JSON.stringify(payload ?? null) || existing.etag !== etag)
        throw new Error('Comprueba la intención pendiente antes de enviar otros datos.');
      return existing;
    }
    const snapshot = freeze(structuredClone(payload ?? null));
    const operation = Object.freeze({ operationId: crypto.randomUUID(), actorId, method, resource, payload: snapshot, etag });
    this.state.update(items => [...items, operation]); this.persist(); return operation;
  }
  resolve(id: string): void { this.state.update(items => items.filter(item => item.operationId !== id)); this.persist(); }
  private restore(): PendingOperation[] {
    try {
      const value: unknown = JSON.parse(sessionStorage.getItem(KEY) ?? '[]');
      if (!Array.isArray(value)) return [];
      return value.filter((item): item is PendingOperation => item && typeof item.operationId === 'string'
        && typeof item.actorId === 'string' && ['POST', 'PUT', 'DELETE'].includes(item.method)
        && typeof item.resource === 'string' && mutationPermission(item.resource) !== null).map(item => freeze(item));
    } catch { return []; }
  }
  private persist(): void {
    try { sessionStorage.setItem(KEY, JSON.stringify(this.operations())); }
    catch { /* La referencia en memoria sigue disponible si el navegador limita almacenamiento. */ }
  }
}

function freeze<T>(value: T): T {
  if (value && typeof value === 'object') {
    for (const item of Object.values(value)) freeze(item);
    Object.freeze(value);
  }
  return value;
}
