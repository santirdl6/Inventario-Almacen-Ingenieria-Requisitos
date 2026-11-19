import { Injectable, PendingTasks, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { firstValueFrom, Subject, timeout } from 'rxjs';
import { PendingOperationStore, PendingOperation, mutationPermission } from '../operations/pending-operation.store';
import { SessionService } from '../session/session.service';

export interface MutationResult<T> { body: T | null; status: number; etag: string | null; location: string | null }
export interface OperationOutcome { operationId: string; status: number; body: unknown; headers: Record<string, string> }
export interface RecoveredOperation { operation: PendingOperation; outcome: OperationOutcome }

@Injectable({ providedIn: 'root' })
export class MasterDataMutationsService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly tasks = inject(PendingTasks);
  readonly pending = inject(PendingOperationStore);
  private readonly inFlight = new Map<string, Promise<MutationResult<unknown>>>();
  private readonly retryAfter = new Map<string, number>();
  private readonly recoveredSource = new Subject<RecoveredOperation>();
  readonly recovered = this.recoveredSource.asObservable();

  mutate<T>(method: 'POST' | 'PUT' | 'DELETE', resource: string, payload: unknown, etag?: string): Promise<MutationResult<T>> {
    return this.send<T>(this.pending.begin(method, resource, payload, etag));
  }
  send<T>(operation: PendingOperation): Promise<MutationResult<T>> {
    const original = this.pending.operations().find(item => item.operationId === operation.operationId);
    if (!original || original.actorId !== operation.actorId)
      return Promise.reject(new Error('La intención ya no está pendiente. Comprueba el resultado antes de guardar de nuevo.'));
    // El almacén es la fuente de la intención vigente: cerrar sesión retira su payload,
    // aunque un componente todavía conserve el objeto anterior en memoria.
    operation = original;
    const permission = mutationPermission(operation.resource);
    if (operation.actorId !== this.session.user()?.id || !permission || !this.session.has(permission) || !('payload' in operation))
      return Promise.reject(new Error('Recupera tu sesión y comprueba el resultado; falta la petición original.'));
    const running = this.inFlight.get(operation.operationId);
    if (running) return running as Promise<MutationResult<T>>;
    const wait = (this.retryAfter.get(operation.operationId) ?? 0) - Date.now();
    if (wait > 0) return Promise.reject(new Error(`Espera ${Math.ceil(wait / 1000)} segundos antes de repetir esta intención.`));
    let headers = new HttpHeaders({ 'Idempotency-Key': operation.operationId });
    if (operation.etag) headers = headers.set('If-Match', operation.etag);
    const promise = firstValueFrom(this.http.request<T>(operation.method, operation.resource,
      { body: operation.payload, headers, observe: 'response' }).pipe(timeout(30_000))).then(response => {
      this.pending.resolve(operation.operationId);
      this.retryAfter.delete(operation.operationId);
      if (operation.actorId !== this.session.user()?.id) throw new Error('La operación se confirmó, pero cambió la sesión. Comprueba el registro con la identidad original.');
      return { body: response.body, status: response.status, etag: response.headers.get('ETag'), location: response.headers.get('Location') };
    }).catch((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) this.session.expire();
      if (error instanceof HttpErrorResponse && error.status === 503) {
        const header = error.headers.get('Retry-After');
        if (header) {
          const until = /^\d+$/.test(header) ? Date.now() + Number(header) * 1000 : Date.parse(header);
          if (Number.isFinite(until)) this.retryAfter.set(operation.operationId, until);
        }
      }
      if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500
        && error.status !== 401 && error.status !== 403) this.pending.resolve(operation.operationId);
      throw error;
    }).finally(() => this.inFlight.delete(operation.operationId));
    this.inFlight.set(operation.operationId, promise);
    return promise;
  }
  async check(operation: PendingOperation): Promise<OperationOutcome | null> {
    const complete = this.tasks.add();
    try {
      if (operation.actorId !== this.session.user()?.id) throw new Error('La intención pertenece a otra identidad.');
      try {
        const outcome = await firstValueFrom(this.http.get<OperationOutcome>(`/api/v1/operations/${operation.operationId}`));
        if (operation.actorId !== this.session.user()?.id) throw new Error('Cambió la sesión. Consulta con la identidad original.');
        this.pending.resolve(operation.operationId); this.recoveredSource.next({ operation, outcome }); return outcome;
      } catch (error) {
        if (error instanceof HttpErrorResponse && error.status === 404) return null;
        if (error instanceof HttpErrorResponse && error.status === 401) this.session.expire();
        throw error;
      }
    } finally { complete(); }
  }
  async retry(operation: PendingOperation): Promise<void> {
    const complete = this.tasks.add();
    try {
      const result = await this.send(operation);
      this.recoveredSource.next({ operation, outcome: { operationId: operation.operationId, status: result.status, body: result.body,
        headers: { ...(result.etag ? { ETag: result.etag } : {}), ...(result.location ? { Location: result.location } : {}) } } });
    } finally { complete(); }
  }
}
