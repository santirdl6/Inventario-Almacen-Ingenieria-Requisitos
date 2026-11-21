import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IncidentWrite } from '../data/receipts-api.service';

@Component({ selector: 'app-receipt-discrepancies', standalone: true, imports: [FormsModule], template: `
  <fieldset [disabled]="readonly()"><legend>Incidencias sin entrada de stock</legend>
    @for (incident of incidents(); track incident.id; let index = $index) {
      <fieldset><legend>Incidencia {{index+1}}</legend>
        <label [for]="'incident-kind-'+index">Tipo de incidencia</label><select [id]="'incident-kind-'+index" [(ngModel)]="incident.kind"><option>Mercancía ajena</option><option>Sustitución</option><option>Otra diferencia</option></select>
        <label [for]="'incident-title-'+index">Descripción observada</label><input [id]="'incident-title-'+index" [(ngModel)]="incident.observedDescription" required>
        <label [for]="'incident-qty-'+index">Cantidad observada</label><input [id]="'incident-qty-'+index" inputmode="decimal" [(ngModel)]="incident.quantity">
        <label [for]="'incident-unit-'+index">Unidad observada</label><input [id]="'incident-unit-'+index" [(ngModel)]="incident.observedUnit">
        <label [for]="'incident-reason-'+index">Motivo de la incidencia</label><input [id]="'incident-reason-'+index" [(ngModel)]="incident.reason" required>
        <button type="button" (click)="remove.emit(incident.id)">Retirar incidencia {{index+1}}</button>
      </fieldset>
    }
    <button type="button" (click)="add.emit()">Añadir incidencia</button>
  </fieldset>
  ` })
export class ReceiptDiscrepanciesComponent {
  readonly incidents = input.required<IncidentWrite[]>();
  readonly readonly = input(false);
  readonly add = output<void>();
  readonly remove = output<string>();
}
