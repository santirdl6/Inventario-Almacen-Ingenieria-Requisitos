import { HttpClient } from '@angular/common/http';
import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { MasterDataMutationsService } from '../../core/http/master-data-mutations.service';
import { apiProblem } from '../../core/http/api-problem';
import { OperationStatusComponent } from '../../core/operations/operation-status.component';
import { SessionService } from '../../core/session/session.service';

interface Policy { minimumAvailable: string }

@Component({ selector: 'app-stock-policy', standalone: true, imports: [ReactiveFormsModule, OperationStatusComponent], template: `
  <section><h2>Política de existencias</h2>
    <app-operation-status [resource]="path" />
    @if (message()) { <p role="status">{{ message() }}</p> }
    @if (error()) { <p role="alert">{{ error() }}</p><button type="button" (click)="load(true)">Consultar política vigente</button> }
    <p>Mínimo vigente: {{ minimum() }} {{ unit }}</p>
    @if (canManage()) {
      <label for="minimum-stock">Nuevo mínimo ({{ unit }})</label>
      <input id="minimum-stock" inputmode="decimal" [formControl]="value" [readonly]="busy() || !etag || !!pending()">
      <button type="button" (click)="save()" [disabled]="busy() || !etag || !!pending()">Guardar mínimo</button>
    }
  </section>` })
export class StockPolicyComponent implements OnInit {
  @Input({ required: true }) articleId = '';
  @Input() unit = '';
  @Input() allowsFractional = true;
  private readonly http = inject(HttpClient); private readonly session = inject(SessionService);
  private readonly mutations = inject(MasterDataMutationsService);
  readonly value = new FormControl('', { nonNullable: true });
  readonly minimum = signal(''); readonly message = signal(''); readonly error = signal(''); readonly busy = signal(false);
  etag: string | null = null;
  constructor() {
    this.mutations.recovered.pipe(takeUntilDestroyed()).subscribe(({ operation }) => {
      if (operation.resource === this.path) { this.message.set('Mínimo guardado y confirmado.'); void this.load(false); }
    });
  }
  get path(): string { return `/api/v1/articles/${this.articleId}/stock-policy`; }
  pending() { return this.mutations.pending.forResource(this.path); }
  canManage() { return this.session.has('StockPolicy.Manage'); }
  ngOnInit(): void { void this.load(false); }
  async load(preserve: boolean): Promise<void> {
    this.busy.set(true);
    try {
      const response = await firstValueFrom(this.http.get<Policy>(this.path, { observe: 'response' }));
      this.minimum.set(response.body?.minimumAvailable ?? ''); this.etag = response.headers.get('ETag');
      if (!preserve) this.value.setValue((this.pending()?.payload as Policy | undefined)?.minimumAvailable ?? this.minimum());
      this.error.set('');
    } catch (error) { this.error.set(apiProblem(error).detail); }
    finally { this.busy.set(false); }
  }
  async save(): Promise<void> {
    const value = this.value.value.trim().replace(',', '.');
    if (!/^\d+(?:\.\d{1,3})?$/.test(value)) { this.error.set('Indica una cantidad no negativa con hasta tres decimales.'); return; }
    if (!this.allowsFractional && /\.\d*[1-9]/.test(value)) { this.error.set('El artículo requiere unidades enteras.'); return; }
    if (!this.canManage() || this.busy() || !this.etag || this.pending()) return;
    this.busy.set(true); this.error.set(''); this.message.set('');
    try { await this.mutations.mutate('PUT', this.path, { minimumAvailable: value }, this.etag); await this.load(false); this.message.set('Mínimo guardado y confirmado.'); }
    catch (error) { const issue = apiProblem(error); if (issue.status === 412) this.etag = null; this.error.set(issue.detail); }
    finally { this.busy.set(false); }
  }
}
