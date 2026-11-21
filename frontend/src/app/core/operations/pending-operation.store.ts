import { Injectable, inject, signal } from '@angular/core';
import { SessionService } from '../session/session.service';

export interface PendingOperation {
  operationId: string; actorId: string; method: string; resource: string;
  payload?: unknown; etag?: string;
  /** HU-05: ruta de la aplicación para «Abrir pedido»; se conserva al cerrar sesión. */
  appRoute?: string;
}
const KEY = 'inventario.pending-operations';
const UUID = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';
// HU-05: /existencias/pedidos/…; HU-03: /salidas/pedidos/{id} (alta) y /salidas/{id} (salida);
// HU-04: /inventarios/nuevo (alta) y /inventarios/{id} (conteo).
const APP_ROUTE = new RegExp(`^(/existencias/pedidos/(nuevo|${UUID})|/salidas/(pedidos/)?${UUID}|/inventarios/(nuevo|${UUID}))$`, 'i');

export function mutationPermission(resource: string): string | null {
  if (/^\/api\/v1\/(families|articles|suppliers)(\/[0-9a-f-]{36})?$/i.test(resource)) return 'MasterData.Manage';
  if (/^\/api\/v1\/locations(\/[0-9a-f-]{36})?$/i.test(resource)) return 'Locations.Manage';
  // HU-05: alta de pedido, reservar y liberar.
  if (/^\/api\/v1\/customer-orders$/i.test(resource)) return 'Shipping.Record';
  if (/^\/api\/v1\/reservations(\/[0-9a-f-]{36}\/release)?$/i.test(resource)) return 'Reservations.Manage';
  // HU-03: crear, editar, eliminar y confirmar una salida.
  if (/^\/api\/v1\/shipments(\/[0-9a-f-]{36}(\/confirm)?)?$/i.test(resource)) return 'Shipping.Record';
  // HU-04: crear, editar, iniciar, registrar líneas y enviar (Counts.Record); revisar, confirmar y cancelar (Inventory.Adjust).
  if (/^\/api\/v1\/cycle-counts(\/[0-9a-f-]{36}(\/(start|submit|lines(\/[0-9a-f-]{36})?))?)?$/i.test(resource)) return 'Counts.Record';
  if (/^\/api\/v1\/cycle-counts\/[0-9a-f-]{36}\/(review|confirm|cancel)$/i.test(resource)) return 'Inventory.Adjust';
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
      this.state.update(items => items.map(({ operationId, actorId, method, resource, appRoute }) =>
        ({ operationId, actorId, method, resource, ...(appRoute ? { appRoute } : {}) })));
      this.persist();
    });
  }
  begin(method: string, resource: string, payload: unknown, etag?: string, appRoute?: string): PendingOperation {
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
    const operation = Object.freeze({ operationId: crypto.randomUUID(), actorId, method, resource, payload: snapshot, etag,
      ...(appRoute && APP_ROUTE.test(appRoute) ? { appRoute } : {}) });
    this.state.update(items => [...items, operation]); this.persist(); return operation;
  }
  resolve(id: string): void { this.state.update(items => items.filter(item => item.operationId !== id)); this.persist(); }
  private restore(): PendingOperation[] {
    try {
      const value: unknown = JSON.parse(sessionStorage.getItem(KEY) ?? '[]');
      if (!Array.isArray(value)) return [];
      return value.filter((item): item is PendingOperation => item && typeof item.operationId === 'string'
        && typeof item.actorId === 'string' && ['POST', 'PUT', 'DELETE'].includes(item.method)
        && typeof item.resource === 'string' && mutationPermission(item.resource) !== null).map(item => freeze(withValidRoute(item)));
    } catch { return []; }
  }
  private persist(): void {
    try { sessionStorage.setItem(KEY, JSON.stringify(this.operations())); }
    catch { /* La referencia en memoria sigue disponible si el navegador limita almacenamiento. */ }
  }
}

function withValidRoute(item: PendingOperation): PendingOperation {
  if (!('appRoute' in item) || (typeof item.appRoute === 'string' && APP_ROUTE.test(item.appRoute))) return item;
  const { appRoute: _discarded, ...rest } = item;
  return rest;
}

function freeze<T>(value: T): T {
  if (value && typeof value === 'object') {
    for (const item of Object.values(value)) freeze(item);
    Object.freeze(value);
  }
  return value;
}
