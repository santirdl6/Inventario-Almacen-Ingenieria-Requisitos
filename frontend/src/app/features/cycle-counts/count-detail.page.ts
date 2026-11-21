import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, Injector, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { apiProblem } from '../../core/http/api-problem';
import { MasterDataMutationsService } from '../../core/http/master-data-mutations.service';
import { OperationStatusComponent } from '../../core/operations/operation-status.component';
import { SessionService } from '../../core/session/session.service';
import { LocationSummary } from '../locations/data/location.models';
import { isUncertain } from '../stock/data/customer-orders-api.service';
import { StockApiService } from '../stock/data/stock-api.service';
import { StockArticleOption } from '../stock/data/stock.models';
import { focusFirstError } from '../stock/shared/focus-first-error';
import { CountChangedError, CycleCountsApiService } from './data/cycle-counts-api.service';
import {
  COUNT_STATE_LABELS, CountArticle, CountDetail, CountLineView, CountLinesWrite, MAX_LOCATIONS, countResource, differenceText, trimQuantity
} from './data/cycle-counts.models';

/** Hallazgo escrito y aún sin guardar. */
export interface PendingFound { articleId: string; sku: string; name: string; unit: string; locationId: string; lotCode: string; countedPhysical: string }

/** Rechazos que dejan el conteo en otro estado: se avisa y se vuelve a leer (contrato UI § Resultados y errores). */
const STATE_CONFLICTS = new Set(['CountScopeFrozen', 'CountStateConflict', 'CountTheoreticalChanged', 'ReservedStockConflict',
  'DocumentAlreadyConfirmed']);

const UNTRACKED_LOT = 'SIN-LOTE';

/**
 * HU-04 T015 · /inventarios/:id en borrador y en conteo (US1, US5; contracts/cycle-counts-ui.md § Contenido
 * por estado). Borrador: alcance editable e «Iniciar conteo» con aviso de congelación. En conteo: «Contado»
 * vacío por defecto (el cero se escribe), diferencia en texto, hallazgos y «Enviar a aprobación». El motivo
 * no se pide aquí, sino en la revisión (H6). 412 y 409 de estado: aviso, relectura y lo escrito se conserva.
 */
@Component({
  selector: 'app-count-detail-page',
  imports: [DatePipe, OperationStatusComponent],
  styleUrl: '../stock/stock.css',
  template: `
    @if (message()) { <p role="status">{{ message() }}</p> }
    @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }
    @for (resource of resources(); track resource) { <app-operation-status [resource]="resource" /> }
    @if (detail(); as current) {
      <h1>Conteo {{ current.count.countCode }}</h1>
      <p>Estado: <strong data-testid="count-state">{{ stateLabel(current.count.state) }}</strong>
        · Creado {{ current.count.createdAt | date: 'dd/MM/yyyy HH:mm' }} por {{ user(current.count.createdBy) }}
        @if (current.count.startedAt) { · Iniciado {{ current.count.startedAt | date: 'dd/MM/yyyy HH:mm' }} }</p>
      <p data-testid="scope">Ubicaciones: {{ scopeCodes() }}</p>

      @if (current.count.state === 'Draft' && canRecord()) {
        <section aria-labelledby="scope-title">
          <h2 id="scope-title">Alcance</h2>
          <fieldset [attr.aria-invalid]="scopeError() ? 'true' : null" tabindex="-1">
            <legend>Ubicaciones del conteo ({{ scope().length }} de {{ max }} como máximo)</legend>
            @if (scopeError()) { <span class="field-error">{{ scopeError() }}</span> }
            @for (location of scopeOptions(); track location.id) {
              <div>
                <input type="checkbox" [id]="'scope-' + location.id" [value]="location.id" [checked]="scope().includes(location.id)"
                  (change)="toggleScope(location.id, $any($event.target).checked)" />
                <label [for]="'scope-' + location.id">{{ location.code }}</label>
              </div>
            }
          </fieldset>
          <button type="button" [disabled]="busy()" (click)="saveScope()">Guardar alcance</button>
          <p data-testid="freeze-notice">Al iniciar el conteo, {{ scopeCodes() }} quedarán congeladas: no admitirán salidas, reservas
            ni cambios de la ubicación hasta confirmar o cancelar el conteo. Liberar reservas sigue permitido.</p>
          <button type="button" [disabled]="busy()" (click)="start()">Iniciar conteo</button>
        </section>
      }

      @if (current.count.state !== 'Draft') {
        @for (group of groups(); track group.locationId) {
          <section [attr.aria-label]="'Ubicación ' + group.code">
            <h2>Ubicación {{ group.code }}</h2>
            <table [attr.aria-label]="'Líneas de ' + group.code">
              <thead><tr><th scope="col">Artículo</th><th scope="col">Lote</th><th scope="col">Teórico</th>
                <th scope="col">Contado</th><th scope="col">Diferencia</th>@if (editable()) { <th scope="col">Acción</th> }</tr></thead>
              <tbody>
                @for (row of group.lines; track row.line.id) {
                  <tr>
                    <td>{{ article(row.line.articleId).sku }} · {{ article(row.line.articleId).name }}
                      @if (row.line.isFound) { <span class="meta">Hallazgo</span> }</td>
                    <td>{{ lot(row.line.lotCode) }}</td>
                    <td>{{ quantity(row.line.theoreticalPhysical) }} {{ article(row.line.articleId).unit }}</td>
                    <td>
                      @if (editable()) {
                        <label class="visually-hidden" [for]="'counted-' + row.line.id">Contado de {{ article(row.line.articleId).sku }} en {{ group.code }}</label>
                        <input [id]="'counted-' + row.line.id" [attr.data-line]="row.line.id" inputmode="decimal" [value]="input(row.line.id)"
                          [attr.aria-invalid]="lineError(row.index) ? 'true' : null"
                          [attr.aria-describedby]="lineError(row.index) ? 'counted-error-' + row.line.id : null"
                          (input)="write(row.line.id, $any($event.target).value)" /> {{ article(row.line.articleId).unit }}
                        @if (lineError(row.index)) { <span class="field-error" [id]="'counted-error-' + row.line.id">{{ lineError(row.index) }}</span> }
                      } @else {
                        @if (row.line.countedPhysical !== null) { {{ quantity(row.line.countedPhysical) }} {{ article(row.line.articleId).unit }} } @else { — }
                      }
                    </td>
                    <td [attr.data-diff]="row.line.id">{{ difference(row.line) }}</td>
                    @if (editable()) {
                      <td>@if (row.line.isFound) { <button type="button" [disabled]="busy()" (click)="removeFound(row.line)">Retirar hallazgo</button> }</td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </section>
        }
      }

      @if (editable()) {
        <section aria-labelledby="found-title">
          <h2 id="found-title">Hallazgos</h2>
          <p>Registra aquí un artículo que encuentres en el alcance y no figure en la lista.</p>
          <label for="found-article">Artículo (SKU o nombre)</label>
          <input id="found-article" type="search" [value]="articleQuery()" (input)="searchArticle($any($event.target).value)" />
          @if (articleOptions().length) {
            <ul class="options" aria-label="Artículos encontrados">
              @for (option of articleOptions(); track option.id) {
                <li><button type="button" (click)="chooseArticle(option)">{{ option.sku }} · {{ option.name }}</button></li>
              }
            </ul>
          }
          @if (foundArticle(); as chosen) { <p>Artículo elegido: {{ chosen.sku }} · {{ chosen.name }} ({{ chosen.baseUnit }})</p> }
          <label for="found-location">Ubicación</label>
          <select id="found-location" [value]="foundLocation()" (change)="foundLocation.set($any($event.target).value)">
            <option value="">Elige la ubicación</option>
            @for (location of current.count.locations; track location.locationId) {
              <option [value]="location.locationId">{{ code(location.locationId) }}</option>
            }
          </select>
          @if (foundArticle()?.tracksLot) {
            <label for="found-lot">Lote</label>
            <input id="found-lot" [value]="foundLot()" (input)="foundLot.set($any($event.target).value)" />
          }
          <label for="found-quantity">Cantidad</label>
          <input id="found-quantity" inputmode="decimal" [value]="foundQuantity()" [attr.aria-invalid]="foundError() ? 'true' : null"
            (input)="foundQuantity.set($any($event.target).value)" />
          @if (foundError()) { <span class="field-error">{{ foundError() }}</span> }
          <button type="button" (click)="addFound()">Añadir hallazgo</button>
          @if (pendingFound().length) {
            <ul data-testid="pending-found" aria-label="Hallazgos sin guardar">
              @for (item of pendingFound(); track $index) {
                <li>{{ item.sku }} · {{ item.name }} en {{ code(item.locationId) }} · {{ lot(item.lotCode || null) }} ·
                  {{ item.countedPhysical }} {{ item.unit }}
                  <button type="button" (click)="dropFound($index)">Quitar</button></li>
              }
            </ul>
          }
        </section>
        <p>
          <button type="button" [disabled]="busy()" (click)="save()">Guardar</button>
          <button type="button" [disabled]="busy()" (click)="submit()">Enviar a aprobación</button>
        </p>
      }
    }
  `
})
export class CountDetailPage implements OnInit {
  readonly countId = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  protected readonly api = inject(CycleCountsApiService);
  protected readonly session = inject(SessionService);
  protected readonly host = inject(ElementRef<HTMLElement>);
  protected readonly injector = inject(Injector);
  private readonly stock = inject(StockApiService);
  private readonly mutations = inject(MasterDataMutationsService);
  readonly max = MAX_LOCATIONS;

  readonly detail = signal<CountDetail | null>(null);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly busy = signal(false);
  readonly errors = signal<Record<string, string[]>>({});
  /** Contado escrito por línea (texto tal como se escribe). */
  readonly inputs = signal<Record<string, string>>({});
  readonly pendingFound = signal<PendingFound[]>([]);
  readonly scope = signal<string[]>([]);
  readonly scopeError = signal<string | null>(null);
  readonly activeLocations = signal<LocationSummary[]>([]);
  readonly articleQuery = signal('');
  readonly articleOptions = signal<StockArticleOption[]>([]);
  readonly foundArticle = signal<StockArticleOption | null>(null);
  readonly foundLocation = signal('');
  readonly foundLot = signal('');
  readonly foundQuantity = signal('');
  readonly foundError = signal<string | null>(null);

  readonly canRecord = computed(() => this.session.has('Counts.Record'));
  readonly editable = computed(() => this.detail()?.count.state === 'Counting' && this.canRecord());
  readonly resources = computed(() => {
    const base = countResource(this.countId);
    return [base, `${base}/start`, `${base}/lines`, `${base}/submit`, `${base}/review`, `${base}/confirm`, `${base}/cancel`];
  });
  readonly sortedLines = computed(() => [...(this.detail()?.count.lines ?? [])].sort((a, b) => a.id.localeCompare(b.id)));
  readonly groups = computed(() => {
    const current = this.detail();
    if (!current) return [];
    const rows = this.sortedLines().map((line, index) => ({ line, index }));
    return current.count.locations.map(location => ({
      locationId: location.locationId, code: this.code(location.locationId),
      lines: rows.filter(row => row.line.locationId === location.locationId)
    }));
  });
  readonly scopeCodes = computed(() => this.detail()?.count.locations.map(location => this.code(location.locationId)).join(', ') ?? '');
  readonly scopeOptions = computed(() => {
    const known = new Map(this.activeLocations().map(location => [location.id, location.code]));
    for (const location of this.detail()?.context.locations ?? []) if (!known.has(location.id)) known.set(location.id, location.code);
    return [...known].map(([id, code]) => ({ id, code })).sort((a, b) => a.code.localeCompare(b.code));
  });

  constructor() {
    this.mutations.recovered.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => void this.load(true));
  }

  async ngOnInit(): Promise<void> { await this.load(true); }

  /** Lee el detalle y su contexto; con `reset` sustituye lo escrito por lo guardado. */
  async load(reset: boolean): Promise<void> {
    try {
      const detail = await this.api.detail(this.countId);
      this.detail.set(detail);
      if (reset) {
        this.inputs.set(Object.fromEntries(detail.count.lines.map(line => [line.id, trimQuantity(line.countedPhysical)])));
        this.pendingFound.set([]);
        this.errors.set({});
      }
      this.scope.set(detail.count.locations.map(location => location.locationId));
      if (detail.count.state === 'Draft' && this.canRecord() && !this.activeLocations().length)
        this.activeLocations.set(await firstValueFrom(this.api.activeLocations()));
    } catch (error) {
      this.error.set(error instanceof CountChangedError ? error.message
        : apiProblem(error).status === 404 ? 'El conteo no existe.' : apiProblem(error).detail);
    }
  }

  stateLabel(state: string): string { return COUNT_STATE_LABELS[state] ?? state; }
  code(locationId: string): string { return this.detail()?.context.locations.find(item => item.id === locationId)?.code ?? locationId; }
  user(id: string | null): string { return id ? this.detail()?.context.users.find(item => item.id === id)?.name ?? id : ''; }
  lot(lotCode: string | null): string { return !lotCode || lotCode === UNTRACKED_LOT ? 'Sin lote' : lotCode; }
  quantity(value: string | null): string { return trimQuantity(value); }
  article(id: string): CountArticle {
    return this.detail()?.context.articles.find(item => item.id === id)
      ?? { id, sku: '', name: 'Artículo no disponible', unit: '', allowsFractions: true, tracksLot: false, isActive: true };
  }
  input(lineId: string): string { return this.inputs()[lineId] ?? ''; }

  write(lineId: string, value: string): void {
    this.inputs.update(inputs => ({ ...inputs, [lineId]: value }));
  }

  /** Diferencia de lo escrito (o de lo guardado) frente al teórico, en texto. */
  difference(line: CountLineView): string {
    const unit = this.article(line.articleId).unit;
    if (!this.editable()) return differenceText(line.difference, unit);
    const typed = this.input(line.id).trim().replace(',', '.');
    if (!typed || !Number.isFinite(Number(typed))) return 'Sin contar';
    return differenceText((Number(typed) - Number(line.theoreticalPhysical)).toFixed(3), unit);
  }

  /** Error de una línea: del servidor (`lines[i]` al enviar, `counted[j]` al guardar) o de la validación local. */
  lineError(index: number): string | null {
    const line = this.sortedLines()[index];
    const errors = this.errors();
    return errors[`lines[${index}].countedPhysical`]?.[0] ?? (line ? errors[`line:${line.id}`]?.[0] : undefined) ?? null;
  }

  toggleScope(id: string, checked: boolean): void {
    this.scope.update(ids => checked ? [...new Set([...ids, id])] : ids.filter(item => item !== id));
    this.scopeError.set(null);
  }

  async saveScope(): Promise<void> {
    const current = this.detail();
    if (!current || this.busy()) return;
    const ids = [...this.scope()].sort();
    if (!ids.length || ids.length > MAX_LOCATIONS) {
      this.scopeError.set(ids.length ? `Elige como máximo ${MAX_LOCATIONS} ubicaciones.` : 'Elige al menos una ubicación.');
      focusFirstError(this.host, this.injector);
      return;
    }
    await this.run(() => this.api.update(this.countId, ids, current.etag), 'Alcance guardado.', 'el alcance se guardó', problem => {
      const messages = Object.entries(problem).filter(([key]) => key.startsWith('locationIds')).flatMap(([, value]) => value);
      this.scopeError.set(messages.length ? messages.join(' ') : null);
    });
  }

  async start(): Promise<void> {
    const current = this.detail();
    if (!current || this.busy()) return;
    await this.run(() => this.api.start(this.countId, current.etag), 'Conteo iniciado: las ubicaciones quedan congeladas.', 'el conteo se inició');
  }

  /** Cuerpo de PUT …/lines con lo escrito que cambia lo guardado y los hallazgos nuevos; null si hay errores locales. */
  private linesBody(): CountLinesWrite | null {
    const errors: Record<string, string[]> = {};
    const counted: CountLinesWrite['counted'] = [];
    for (const line of this.sortedLines()) {
      const typed = this.input(line.id).trim().replace(',', '.');
      if (!typed || typed === trimQuantity(line.countedPhysical)) continue;
      const problem = this.quantityProblem(typed, this.article(line.articleId), line.isFound);
      if (problem) errors[`line:${line.id}`] = [problem];
      else counted.push({ lineId: line.id, countedPhysical: typed });
    }
    this.errors.set(errors);
    if (Object.keys(errors).length) return null;
    const found = this.pendingFound().map(({ articleId, locationId, lotCode, countedPhysical }) =>
      ({ articleId, locationId, ...(lotCode ? { lotCode } : {}), countedPhysical }));
    return { counted, found };
  }

  private quantityProblem(typed: string, article: Pick<CountArticle, 'allowsFractions'>, positive: boolean): string | null {
    const value = Number(typed);
    if (!/^\d+(\.\d{1,3})?$/.test(typed) || !Number.isFinite(value)) return 'Escribe una cantidad igual o mayor que cero (el cero se escribe).';
    if (!article.allowsFractions && !Number.isInteger(value)) return 'Este artículo solo admite cantidades enteras.';
    if (positive && value === 0) return 'Un hallazgo tiene una cantidad mayor que cero; retíralo si no existe.';
    return null;
  }

  private hasChanges(body: CountLinesWrite): boolean { return body.counted.length > 0 || body.found.length > 0; }

  async save(): Promise<boolean> {
    const current = this.detail();
    if (!current || this.busy()) return false;
    const body = this.linesBody();
    if (!body) {
      this.error.set('Revisa las cantidades marcadas.');
      focusFirstError(this.host, this.injector);
      return false;
    }
    if (!this.hasChanges(body)) { this.message.set('No hay cambios que guardar.'); return true; }
    return this.run(() => this.api.saveLines(this.countId, body, current.etag), 'Conteo guardado.', 'el conteo se guardó');
  }

  /** Envía a aprobación; si hay cambios sin guardar, los guarda antes. */
  async submit(): Promise<void> {
    if (!this.detail() || this.busy()) return;
    const body = this.linesBody();
    if (!body) {
      this.error.set('Revisa las cantidades marcadas.');
      focusFirstError(this.host, this.injector);
      return;
    }
    if (this.hasChanges(body) && !(await this.save())) return;
    const current = this.detail()!;
    await this.run(() => this.api.submit(this.countId, current.etag), 'Conteo enviado a aprobación.', 'el conteo se envió');
  }

  async removeFound(line: CountLineView): Promise<void> {
    const current = this.detail();
    if (!current || this.busy()) return;
    await this.run(() => this.api.removeLine(this.countId, line.id, current.etag), 'Hallazgo retirado.', 'el hallazgo se retiró');
  }

  searchArticle(query: string): void {
    this.articleQuery.set(query);
    if (query.trim().length < 2) { this.articleOptions.set([]); return; }
    this.stock.searchArticles(query.trim()).subscribe({
      next: options => this.articleOptions.set(options),
      error: error => this.foundError.set(apiProblem(error).detail)
    });
  }

  chooseArticle(option: StockArticleOption): void {
    this.foundArticle.set(option);
    this.articleOptions.set([]);
    this.articleQuery.set(`${option.sku} · ${option.name}`);
    this.foundError.set(null);
  }

  addFound(): void {
    const article = this.foundArticle();
    const typed = this.foundQuantity().trim().replace(',', '.');
    const lotCode = this.foundLot().trim().toUpperCase();
    let problem: string | null = null;
    if (!article) problem = 'Elige el artículo del hallazgo.';
    else if (!this.foundLocation()) problem = 'Elige la ubicación del hallazgo.';
    else if (article.tracksLot && !lotCode) problem = `El artículo ${article.sku} exige lote.`;
    // La búsqueda no trae si admite fracciones: lo decide el servidor al guardar (found[i].countedPhysical).
    else problem = this.quantityProblem(typed, { allowsFractions: true }, true);
    this.foundError.set(problem);
    if (problem || !article) { focusFirstError(this.host, this.injector); return; }
    this.pendingFound.update(items => [...items, { articleId: article.id, sku: article.sku, name: article.name, unit: article.baseUnit,
      locationId: this.foundLocation(), lotCode: article.tracksLot ? lotCode : '', countedPhysical: typed }]);
    this.foundArticle.set(null);
    this.articleQuery.set('');
    this.foundLot.set('');
    this.foundQuantity.set('');
  }

  dropFound(index: number): void { this.pendingFound.update(items => items.filter((_, position) => position !== index)); }

  /**
   * Ejecuta una escritura: éxito → relectura completa; incierto → pendiente de comprobar; 412 → aviso y
   * relectura conservando lo escrito; 409 de estado → motivo y relectura; 422 → errores por campo con foco.
   */
  protected async run(action: () => Promise<unknown>, success: string, what: string,
    fields?: (errors: Record<string, string[]>) => void): Promise<boolean> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      await action();
      this.message.set(success);
      await this.load(true);
      return true;
    } catch (error) {
      await this.fail(error, what, fields);
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  protected async fail(error: unknown, what: string, fields?: (errors: Record<string, string[]>) => void): Promise<void> {
    if (isUncertain(error)) {
      this.error.set(`Resultado pendiente de comprobar: no sabemos si ${what}. Comprueba el resultado antes de repetir.`);
      return;
    }
    if (error instanceof HttpErrorResponse && error.status === 412) {
      this.error.set('Otra persona ha cambiado el conteo. Se ha vuelto a leer; lo que escribiste se conserva: revísalo y guarda de nuevo.');
      await this.load(false);
    } else if (!(error instanceof HttpErrorResponse)) {
      this.error.set(error instanceof Error ? error.message : 'No se pudo completar la operación.');
    } else if (error.status === 409 && STATE_CONFLICTS.has(apiProblem(error).code)) {
      this.error.set(apiProblem(error).detail);
      await this.load(false);
    } else {
      const problem = apiProblem(error);
      this.errors.set(this.mapLineErrors(problem.errors));
      fields?.(problem.errors);
      this.error.set(problem.status === 403 ? 'Tu cuenta no tiene permiso para esta acción del conteo.' : problem.detail);
    }
    focusFirstError(this.host, this.injector);
  }

  /** `counted[j]` del guardado se traduce a la línea que se envió en la posición j. */
  private mapLineErrors(errors: Record<string, string[]>): Record<string, string[]> {
    const mapped: Record<string, string[]> = { ...errors };
    const sent = this.linesSent();
    for (const [key, value] of Object.entries(errors)) {
      const match = /^counted\[(\d+)\]\./.exec(key);
      if (match && sent[Number(match[1])]) mapped[`line:${sent[Number(match[1])]}`] = value;
    }
    return mapped;
  }

  private linesSent(): string[] {
    return this.sortedLines().filter(line => {
      const typed = this.input(line.id).trim().replace(',', '.');
      return typed && typed !== trimQuantity(line.countedPhysical);
    }).map(line => line.id);
  }
}
