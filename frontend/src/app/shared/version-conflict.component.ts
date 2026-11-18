import { Component, computed, input, output, signal } from '@angular/core';
import { apiProblem } from '../core/http/api-problem';

export interface VersionSnapshot { resource: object; etag: string; context?: unknown }
export interface VersionReview { snapshot: VersionSnapshot; values: Record<string, unknown> }

@Component({ selector: 'app-version-conflict', standalone: true, template: `
  <section aria-label="Conflicto de versión">
    <h2>El registro cambió en otra sesión</h2><p>Tu borrador se conserva. Carga la versión vigente, revisa las diferencias y elige explícitamente qué valores usar antes de volver a guardar.</p>
    <button type="button" [disabled]="busy()" (click)="load()">Cargar versión vigente para comparar</button>
    @if (error()) { <p role="alert">{{ error() }}</p> }
    @if (snapshot()) {
      <table><caption>Comparación de tu borrador con la versión vigente</caption><thead><tr><th>Campo</th><th>Tu borrador</th><th>Valor vigente</th><th>Elección</th></tr></thead><tbody>
        @for (field of fields(); track field) { <tr><td>{{ label(field) }}</td><td>{{ text(draft()[field]) }}</td><td>{{ text(current(field)) }}</td><td><label><input type="checkbox" [checked]="choices()[field] || false" (change)="choose(field, $event)"> Usar valor vigente de {{ label(field) }}</label></td></tr> }
      </tbody></table>
      <button type="button" (click)="review()">Aceptar revisión y conservar los valores elegidos</button>
    }
  </section>
` })
export class VersionConflictComponent {
  readonly draft = input.required<Record<string, unknown>>(); readonly loadCurrent = input.required<() => Promise<VersionSnapshot>>(); readonly reviewed = output<VersionReview>();
  readonly snapshot = signal<VersionSnapshot | null>(null); readonly choices = signal<Record<string, boolean>>({}); readonly busy = signal(false); readonly error = signal('');
  readonly fields = computed(() => Object.keys(this.draft()).filter(field => this.current(field) !== this.draft()[field]));
  current(field: string): unknown { return (this.snapshot()?.resource as Record<string, unknown> | undefined)?.[field]; }
  label(field: string): string { return ({ code: 'Código', sku: 'Código', name: 'Nombre', email: 'Correo', taxIdentifier: 'Identificador fiscal', isActive: 'Estado activo', familyId: 'Familia', baseUnit: 'Unidad base', allowsFractional: 'Admite fracciones', tracksLot: 'Seguimiento por lote' } as Record<string, string>)[field] ?? field; }
  text(value: unknown): string { return value === true ? 'Sí' : value === false ? 'No' : value == null || value === '' ? 'Sin valor' : String(value); }
  choose(field: string, event: Event): void { this.choices.update(value => ({ ...value, [field]: (event.target as HTMLInputElement).checked })); }
  async load(): Promise<void> { this.busy.set(true); this.error.set(''); try { this.snapshot.set(await this.loadCurrent()()); this.choices.set({}); } catch (error) { this.error.set(apiProblem(error).detail); } finally { this.busy.set(false); } }
  review(): void {
    const snapshot = this.snapshot(); if (!snapshot) return;
    const values = { ...this.draft() }; for (const field of Object.keys(values)) if (this.choices()[field]) values[field] = this.current(field);
    this.reviewed.emit({ snapshot, values });
  }
}
