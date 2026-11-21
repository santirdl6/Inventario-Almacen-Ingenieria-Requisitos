import { TestBed } from '@angular/core/testing';
import { ReceiptDiscrepanciesComponent } from './receipt-discrepancies.component';

describe('Incidencias de recepción', () => {
  it('presenta una sustitución como evidencia consultable sin operación de stock', async () => {
    const fixture = TestBed.createComponent(ReceiptDiscrepanciesComponent);
    fixture.componentRef.setInput('incidents', [{ id: 'sustitución', kind: 'Sustitución', observedDescription: 'Pieza no pedida', reason: 'No aceptada' }]);
    fixture.componentRef.setInput('readonly', true);
    fixture.detectChanges();
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('fieldset')?.disabled).toBe(true);
    expect((root.querySelector('#incident-title-0') as HTMLInputElement).value).toBe('Pieza no pedida');
    expect((root.querySelector('#incident-reason-0') as HTMLInputElement).value).toBe('No aceptada');
    expect(root.textContent).toContain('sin entrada de stock');
  });
});
