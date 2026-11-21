import { Injectable, inject, signal } from '@angular/core';
import { SessionService } from '../session/session.service';

export interface PendingOperation {
  operationId: string; actorId: string; method: string; resource: string;
  payload?: unknown; etag?: string;
}
const KEY = 'inventario.pending-operations';

export function mutationPermission(resource: string, method?: string): string | null {
  if (/^\/api\/v1\/(families|articles|suppliers)(\/[0-9a-f-]{36})?$/i.test(resource)) return 'MasterData.Manage';
  if (/^\/api\/v1\/locations(\/[0-9a-f-]{36})?$/i.test(resource)) return 'Locations.Manage';
  if (/^\/api\/v1\/purchase-orders$/i.test(resource) && method === 'POST') return 'Purchasing.Manage';
  if (/^\/api\/v1\/purchase-orders\/[0-9a-f-]{36}$/i.test(resource) && method === 'PUT') return 'Purchasing.Manage';
  if (/^\/api\/v1\/purchase-orders\/[0-9a-f-]{36}\/send$/i.test(resource) && method === 'POST') return 'Purchasing.Send';
  if (/^\/api\/v1\/receipts(?:\/[0-9a-f-]{36}\/confirm)?$/i.test(resource) && method === 'POST') return 'Receiving.Record';
  if (/^\/api\/v1\/receipts\/[0-9a-f-]{36}$/i.test(resource) && method === 'PUT') return 'Receiving.Record';
  if (/^\/api\/v1\/movements\/[0-9a-f-]{36}\/corrections$/i.test(resource) && method === 'POST') return 'Inventory.Adjust';
  if (resource === '/api/v1/contingencies/reconcile' && method === 'POST') return 'Receiving.Reconcile';
  if (/^\/api\/v1\/contingencies\/receiving-scopes$/i.test(resource) && method === 'POST') return 'Inventory.Adjust';
  if (/^\/api\/v1\/contingencies\/receiving-scopes\/[0-9a-f-]{36}$/i.test(resource) && method === 'PUT') return 'Inventory.Adjust';
  if (/^\/api\/v1\/contingencies\/receiving-scopes\/[0-9a-f-]{36}\/reopen$/i.test(resource) && method === 'POST') return 'Inventory.Adjust';
  return null;
}

export function hasMutationPermission(session: Pick<SessionService, 'has'>, permission: string): boolean {
  return permission === 'Receiving.Reconcile'
    ? session.has('Receiving.Record') && session.has('Inventory.Adjust') : session.has(permission);
}

export function operationGroup(resource: string): string {
  const purchase = /^\/api\/v1\/purchase-orders\/([0-9a-f-]{36})(?:\/send)?$/i.exec(resource);
  const receipt = /^\/api\/v1\/receipts\/([0-9a-f-]{36})(?:\/confirm)?$/i.exec(resource);
  const scope = /^\/api\/v1\/contingencies\/receiving-scopes\/([0-9a-f-]{36})(?:\/reopen)?$/i.exec(resource);
  return purchase ? `purchase-order:${purchase[1].toLowerCase()}` : receipt ? `receipt:${receipt[1].toLowerCase()}`
    : scope ? `contingency-scope:${scope[1].toLowerCase()}` : resource;
}

@Injectable({ providedIn: 'root' })
export class PendingOperationStore {
  private readonly session = inject(SessionService);
  private readonly state = signal<PendingOperation[]>(this.restore());
  readonly operations = this.state.asReadonly();
  forResource(resource: string): PendingOperation | undefined { return this.operations().find(item => item.actorId === this.session.user()?.id && operationGroup(item.resource) === operationGroup(resource)); }
  constructor() {
    this.session.loggedOut.subscribe(() => {
      this.state.update(items => items.map(({ operationId, actorId, method, resource }) => ({ operationId, actorId, method, resource })));
      this.persist();
    });
  }
  begin(method: string, resource: string, payload: unknown, etag?: string): PendingOperation {
    const actorId = this.session.user()?.id;
    if (!actorId) throw new Error('Inicia sesión antes de guardar.');
    const permission = mutationPermission(resource, method);
    if (!permission || !hasMutationPermission(this.session, permission))
      throw new Error('La intención requiere el permiso de la operación y una ruta válida.');
    const existing = this.forResource(resource);
    if (existing) {
      if (existing.resource !== resource || existing.method !== method || !('payload' in existing) || JSON.stringify(existing.payload) !== JSON.stringify(payload ?? null) || existing.etag !== etag)
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
        && typeof item.resource === 'string' && mutationPermission(item.resource, item.method) !== null).map(item => freeze(item));
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
