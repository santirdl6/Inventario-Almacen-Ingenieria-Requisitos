import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { PurchaseOrderSendPage } from './purchase-order-send.page';
import { PurchaseOrdersApiService } from '../data/purchase-orders-api.service';
import { locationsTestProviders, signIn } from '../../locations/testing/locations-testing';
import { purchase, purchaseContext, delivery } from '../testing/purchasing-testing';
import { SessionService } from '../../../core/session/session.service';
import { PurchaseSendResult } from '../data/purchase-order.models';

describe('Revisión y solicitud de correo de compra', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: locationsTestProviders({ id: purchase().id }) });
    signIn(['Inventory.Read', 'Purchasing.Send']);
    vi.spyOn(TestBed.inject(PurchaseOrdersApiService), 'detail').mockResolvedValue({ resource: purchase(), context: purchaseContext(), etag: '"v1"' });
    vi.spyOn(TestBed.inject(PurchaseOrdersApiService), 'delivery').mockResolvedValue(delivery());
    vi.spyOn(TestBed.inject(PurchaseOrdersApiService), 'preview').mockResolvedValue({ purchaseOrderVersion: '"v1"', supplierVersion: '"s1"',
      recipientEmail: 'revisado@test', senderEmail: 'inventario@test', reviewToken: 'token', blockReasons: [], document: purchase(), documentHtml: '<p>Documento</p>' });
  });
  it('confirma la versión y el token revisados y comunica solo solicitud pendiente', async () => {
    const fixture = TestBed.createComponent(PurchaseOrderSendPage); fixture.detectChanges();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    const api = TestBed.inject(PurchaseOrdersApiService);
    const send = vi.spyOn(api, 'requestSend').mockImplementation(async () => {
      vi.mocked(api.detail).mockResolvedValue({ resource: purchase({ sendStatus: 'Pending', deliveryId: delivery().deliveryId }), context: { ...purchaseContext(), purchaseOrderVersion: '"v2"' }, etag: '"v2"' });
      return { operationId: 'operation', purchaseOrderId: purchase().id, deliveryId: delivery().deliveryId!, state: 'Draft', sendStatus: 'Pending' };
    });
    await fixture.componentInstance.confirm(); fixture.detectChanges();
    expect(send).toHaveBeenCalledWith(purchase().id, { kind: 'initial', reviewToken: 'token' }, '"v1"');
    expect(fixture.nativeElement.textContent).toContain('Envío pendiente');
    expect(fixture.nativeElement.textContent).not.toContain('Proveedor lo ha recibido');
  });

  it('un contacto sustituido bloquea confirmar hasta volver a revisar', async () => {
    const fixture = TestBed.createComponent(PurchaseOrderSendPage); fixture.detectChanges();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    const api = TestBed.inject(PurchaseOrdersApiService);
    vi.spyOn(api, 'requestSend').mockRejectedValue(new HttpErrorResponse({ status: 409,
      error: { status: 409, code: 'SendReviewChanged', detail: 'El correo cambió', errors: {} } }));
    await fixture.componentInstance.confirm();
    expect(fixture.componentInstance.review()).toBeNull();
    expect(fixture.componentInstance.canConfirm()).toBe(false);
    expect(fixture.componentInstance.problem()?.code).toBe('SendReviewChanged');
    await fixture.componentInstance.load();
    expect(fixture.componentInstance.canConfirm()).toBe(true);
  });

  it('los impedimentos de revisión no permiten emitir solicitud', async () => {
    const api = TestBed.inject(PurchaseOrdersApiService);
    vi.mocked(api.preview).mockResolvedValue({ purchaseOrderVersion: '"v1"', supplierVersion: '"s1"', recipientEmail: null,
      senderEmail: 'inventario@test', reviewToken: 'token', blockReasons: ['Completa el correo del proveedor'], document: purchase(), documentHtml: '' });
    const send = vi.spyOn(api, 'requestSend');
    const fixture = TestBed.createComponent(PurchaseOrderSendPage); fixture.detectChanges();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    await fixture.componentInstance.confirm(); expect(send).not.toHaveBeenCalled();
    fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('Completa el correo');
  });

  it('una respuesta posterior al logout no reintroduce documento ni destinatario', async () => {
    const fixture = TestBed.createComponent(PurchaseOrderSendPage); fixture.detectChanges();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    let resolve!: (result: PurchaseSendResult) => void;
    vi.spyOn(TestBed.inject(PurchaseOrdersApiService), 'requestSend').mockReturnValue(new Promise(done => { resolve = done; }));
    const saving = fixture.componentInstance.confirm();
    const closing = TestBed.inject(SessionService).logout();
    const http = TestBed.inject(HttpTestingController); http.expectOne('/api/v1/auth/csrf').flush({});
    await Promise.resolve(); http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await closing;
    resolve({ operationId: 'operation', purchaseOrderId: purchase().id, deliveryId: delivery().deliveryId!, state: 'Draft', sendStatus: 'Pending' });
    await saving; fixture.detectChanges();
    expect(fixture.componentInstance.current()).toBeNull(); expect(fixture.componentInstance.review()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('revisado@test');
    expect(fixture.nativeElement.textContent).not.toContain(purchase().number);
    http.verify();
  });
});
