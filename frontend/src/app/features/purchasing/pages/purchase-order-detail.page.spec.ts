import { TestBed } from '@angular/core/testing';
import { PurchaseOrderDetailPage } from './purchase-order-detail.page';
import { PurchaseOrdersApiService } from '../data/purchase-orders-api.service';
import { locationsTestProviders, signIn } from '../../locations/testing/locations-testing';
import { purchase, purchaseContext, delivery } from '../testing/purchasing-testing';
import { PurchaseDetail } from '../data/purchase-order.models';

describe('Detalle y seguimiento de compras', () => {
  beforeEach(() => { TestBed.configureTestingModule({ providers: locationsTestProviders({ id: purchase().id }) }); signIn(['Inventory.Read']); });
  it('distingue estado logístico y correo y no permite editar recibido', async () => {
    const order = purchase({ state: 'PartiallyReceived', sendStatus: 'Accepted' });
    order.lines[0].receivedQuantity = '6.000'; order.lines[0].pendingQuantity = '4.000';
    const api = TestBed.inject(PurchaseOrdersApiService);
    vi.spyOn(api, 'detail').mockResolvedValue({ resource: order, context: purchaseContext(), etag: '"v1"' });
    vi.spyOn(api, 'delivery').mockResolvedValue(delivery({ purchaseOrderVersion: '"v1"', sendStatus: 'Accepted' }));
    const fixture = TestBed.createComponent(PurchaseOrderDetailPage); fixture.detectChanges();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false)); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Recibido parcialmente');
    expect(fixture.nativeElement.textContent).toContain('Aceptado por el servicio de correo');
    expect(fixture.nativeElement.textContent).toContain('4.000'); expect(fixture.nativeElement.textContent).toContain('Inactivo');
    expect(fixture.nativeElement.querySelector('input')).toBeNull();
  });

  it('una respuesta antigua no sustituye una consulta más reciente', async () => {
    let release!: (value: PurchaseDetail) => void;
    const old = new Promise<PurchaseDetail>(resolve => { release = resolve; });
    const api = TestBed.inject(PurchaseOrdersApiService);
    vi.spyOn(api, 'detail').mockReturnValueOnce(old).mockResolvedValue({ resource: purchase({ state: 'Sent', sendStatus: 'Accepted' }),
      context: { ...purchaseContext(), purchaseOrderVersion: '"v2"' }, etag: '"v2"' });
    vi.spyOn(api, 'delivery').mockResolvedValue(delivery({ sendStatus: 'Accepted' }));
    const fixture = TestBed.createComponent(PurchaseOrderDetailPage); fixture.detectChanges();
    await fixture.componentInstance.load();
    release({ resource: purchase(), context: purchaseContext(), etag: '"v1"' }); await old; await Promise.resolve();
    expect(fixture.componentInstance.detail()?.resource.sendStatus).toBe('Accepted');
    expect(fixture.componentInstance.detail()?.etag).toBe('"v2"');
  });

  it('evita consultar en segundo plano y reanuda una sola consulta al volver', async () => {
    vi.useFakeTimers();
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    try {
      const api = TestBed.inject(PurchaseOrdersApiService);
      const read = vi.spyOn(api, 'detail').mockResolvedValue({ resource: purchase({ sendStatus: 'Pending' }),
        context: purchaseContext(), etag: '"v1"' });
      vi.spyOn(api, 'delivery').mockResolvedValue(delivery({ sendStatus: 'Pending', purchaseOrderVersion: '"v1"' }));
      const fixture = TestBed.createComponent(PurchaseOrderDetailPage);
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(0);
      expect(fixture.componentInstance.detail()?.resource.sendStatus).toBe('Pending');
      await vi.advanceTimersByTimeAsync(10_000);
      expect(read).toHaveBeenCalledTimes(1);
      visibility.mockReturnValue('visible');
      document.dispatchEvent(new Event('visibilitychange'));
      document.dispatchEvent(new Event('visibilitychange'));
      expect(read).toHaveBeenCalledTimes(2);
      fixture.destroy();
      await vi.advanceTimersByTimeAsync(5_000);
      expect(read).toHaveBeenCalledTimes(2);
    } finally {
      visibility.mockRestore();
      vi.useRealTimers();
    }
  });
});
