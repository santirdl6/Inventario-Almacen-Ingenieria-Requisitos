import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OperationStatusComponent } from './operation-status.component';
import { PendingOperationStore } from './pending-operation.store';
import { MasterDataMutationsService } from '../http/master-data-mutations.service';
import { SessionService } from '../session/session.service';

describe('Comprobación explícita de operaciones', () => {
  let http: HttpTestingController;
  beforeEach(() => { sessionStorage.clear(); TestBed.configureTestingModule({ imports: [OperationStatusComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] }); http = TestBed.inject(HttpTestingController); TestBed.inject(SessionService).user.set({ id: 'actor', email: 'a@example.test', roles: [], permissions: ['MasterData.Manage'] }); });
  afterEach(() => { http.verify(); sessionStorage.clear(); });
  it('consulta antes de permitir replay y nunca sustituye el payload o la clave originales', async () => {
    const store = TestBed.inject(PendingOperationStore); const pending = store.begin('POST', '/api/v1/families', { code: 'A', name: 'Original' });
    const fixture = TestBed.createComponent(OperationStatusComponent); fixture.detectChanges();
    expect(fixture.componentInstance.canRetry(pending)).toBe(false); await fixture.componentInstance.retry(pending); http.expectNone('/api/v1/families');
    const check = fixture.componentInstance.check(pending);
    http.expectOne(`/api/v1/operations/${pending.operationId}`).flush({}, { status: 404, statusText: 'Not Found' }); await check;
    expect(store.operations()).toHaveLength(1); expect(fixture.componentInstance.canRetry(pending)).toBe(true);
    const recovered = vi.fn(); TestBed.inject(MasterDataMutationsService).recovered.subscribe(recovered);
    const retry = fixture.componentInstance.retry(pending); const request = http.expectOne('/api/v1/families');
    expect(request.request.headers.get('Idempotency-Key')).toBe(pending.operationId); expect(request.request.body).toEqual({ code: 'A', name: 'Original' });
    request.flush({ resource: { id: 'family' } }, { status: 201, statusText: 'Created' }); await retry;
    expect(store.operations()).toHaveLength(0); expect(recovered).toHaveBeenCalledOnce();
  });
});
