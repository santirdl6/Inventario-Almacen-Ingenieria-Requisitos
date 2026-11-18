import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-query-state', standalone: true,
  template: `
    @if (loading()) { <p role="status">Cargando datos…</p> }
    @else if (error()) { <p role="alert">{{ error() }}</p><button type="button" (click)="retry.emit()">Volver a consultar</button> }
    @else if (empty()) { <p role="status">No hay coincidencias. Puedes cambiar los filtros.</p> }
  `
})
export class QueryStateComponent {
  readonly loading = input(false);
  readonly error = input('');
  readonly empty = input(false);
  readonly retry = output<void>();
}
