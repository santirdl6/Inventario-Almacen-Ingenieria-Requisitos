import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { SessionService } from '../../../core/session/session.service';
import { ReceiptContingencyPage } from './receipt-contingency.page';

describe('Autorización de reapertura', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SessionService).user.set({ id: 'responsable', email: 'resp@test', roles: ['ResponsableAlmacen'], permissions: ['Inventory.Read', 'Inventory.Adjust'] });
  });

  it('mantiene el alcance suspendido y comunica el rechazo devuelto por el servidor', async () => {
    const fixture = TestBed.createComponent(ReceiptContingencyPage); const page = fixture.componentInstance;
    page.scope.set({ id: 'scope', state: 'Suspended', expectedVouchers: ['CB-1'], purchaseOrderIds: ['purchase'], locationIds: ['location'],
      differences: [], journalEvidence: null, stockEvidence: null, purchaseEvidence: null });
    const mutate = vi.spyOn(TestBed.inject(MasterDataMutationsService), 'mutate').mockRejectedValue(new HttpErrorResponse({ status: 409,
      error: { status: 409, code: 'ContingencyScopeNotReconciled', detail: 'Faltan comprobantes por conciliar.' } }));
    await page.reopen();
    expect(mutate).toHaveBeenCalledOnce();
    expect(page.scope()?.state).toBe('Suspended');
    expect(page.error()).toContain('Faltan comprobantes');
    expect(page.message()).toBe('');
  });

  it('no concede la política de contingencia a un usuario con solo uno de los permisos', () => {
    const page = TestBed.createComponent(ReceiptContingencyPage).componentInstance;
    expect(page.session.has('Inventory.Adjust')).toBe(true);
    expect(page.session.has('Receiving.Record')).toBe(false);
  });
});
