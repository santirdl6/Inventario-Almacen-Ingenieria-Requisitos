import { Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MasterDataMutationsService } from '../http/master-data-mutations.service';
import { PendingOperation, PendingOperationStore, mutationPermission } from './pending-operation.store';
import { SessionService } from '../session/session.service';
import { apiProblem } from '../http/api-problem';

@Component({
  selector: 'app-operation-status', standalone: true, imports: [RouterLink],
  template: `
    @if (message()) { <p role="status">{{ message() }}</p> }
    @for (operation of pending.operations(); track operation.operationId) {
      @if (operation.actorId === session.user()?.id && (!resource() || resource() === operation.resource)) {
        <section aria-label="Operación pendiente">
          <p role="status">Resultado pendiente de comprobar: {{ operation.operationId }}</p>
          @if (summaryOnly()) { <a [routerLink]="route(operation)">Revisar esta operación pendiente</a> } @else {
          @if (operation.appRoute) { <a [routerLink]="operation.appRoute">{{ openLabel(operation.appRoute) }}</a> }
          @if (!hasOriginal(operation)) { <p>La petición original se retiró al cerrar sesión. Solo puedes comprobar el resultado; no se reconstruirá ni se reenviará.</p> }
          @else if (!checked().includes(operation.operationId)) { <p>Comprueba primero el resultado antes de repetir la petición original.</p> }
          <button type="button" [disabled]="busy()" (click)="check(operation)">Comprobar resultado</button>
          @if (canRetry(operation)) { <button type="button" [disabled]="busy()" (click)="retry(operation)">Repetir la misma intención</button> }
          }
        </section>
      }
    }
  `
})
export class OperationStatusComponent {
  readonly pending = inject(PendingOperationStore); readonly session = inject(SessionService);
  readonly resource = input(''); readonly summaryOnly = input(false);
  private readonly mutations = inject(MasterDataMutationsService);
  readonly busy = signal(false); readonly message = signal('');
  readonly checked = signal<string[]>([]);
  hasOriginal(operation: PendingOperation): boolean { return 'payload' in operation; }
  /** HU-03: «Abrir salida» para /salidas/{id}; «Abrir pedido» para las rutas de pedido (HU-05 y alta de salida). */
  openLabel(appRoute: string): string { return appRoute.startsWith('/salidas/') && !appRoute.startsWith('/salidas/pedidos/') ? 'Abrir salida' : 'Abrir pedido'; }
  canRetry(operation: PendingOperation): boolean { const permission = mutationPermission(operation.resource); return this.hasOriginal(operation) && !!permission && this.session.has(permission) && this.checked().includes(operation.operationId); }
  route(operation: PendingOperation): string {
    if (operation.appRoute) return operation.appRoute; // HU-05: pedido de la reserva o liberación.
    const parts = operation.resource.split('/');
    if (parts[3] === 'locations') return parts[4] ? `/ubicaciones/${parts[4]}/editar` : '/ubicaciones/nueva';
    const catalogue = ({ families: 'familias', articles: 'articulos', suppliers: 'proveedores' } as Record<string, string>)[parts[3]];
    return `/maestros/${catalogue}/${parts[4] ?? 'nuevo'}`;
  }
  async check(operation: PendingOperation): Promise<void> {
    this.busy.set(true);
    try {
      const outcome = await this.mutations.check(operation);
      if (!outcome) this.checked.update(value => [...new Set([...value, operation.operationId])]);
      this.message.set(outcome ? 'Operación confirmada. Consulta el registro actualizado.' : 'Todavía no se observa un resultado. Puede seguir en curso.');
    }
    catch (error) { this.message.set(apiProblem(error).detail); }
    finally { this.busy.set(false); }
  }
  async retry(operation: PendingOperation): Promise<void> {
    if (!this.canRetry(operation) || this.busy()) return;
    this.busy.set(true);
    try { await this.mutations.retry(operation); this.message.set('Operación confirmada. Consulta el registro actualizado.'); }
    catch (error) { this.message.set(apiProblem(error).detail); }
    finally { this.busy.set(false); }
  }
}
