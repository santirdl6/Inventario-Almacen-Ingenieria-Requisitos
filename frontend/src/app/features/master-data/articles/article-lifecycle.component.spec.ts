import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ConfirmationService } from 'primeng/api';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ArticleLifecycleComponent } from './article-lifecycle.component';
import { SessionService } from '../../../core/session/session.service';

describe('Retirada de artículos', () => {
  let http: HttpTestingController;
  const article = { id: '10000000-0000-0000-0000-000000000001', sku: 'ART-01', name: 'Identidad que se elimina', isActive: true };
  beforeEach(() => {
    sessionStorage.clear(); TestBed.configureTestingModule({ imports: [ArticleLifecycleComponent], providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SessionService).user.set({ id: 'actor', email: 'g@example.test', roles: ['GestorMaestros'], permissions: ['MasterData.Manage'] }); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); sessionStorage.clear(); });
  function setup() {
    const fixture = TestBed.createComponent(ArticleLifecycleComponent);
    fixture.componentRef.setInput('article', article); fixture.componentRef.setInput('context', { capabilities: { canDelete: true, deleteBlockReason: null } }); fixture.componentRef.setInput('etag', '"AQIDBAUGBwg="'); fixture.detectChanges(); return fixture;
  }
  it('exige revisar identidad y consecuencia y permite cancelar sin petición', async () => {
    const fixture = setup(); const confirmation = fixture.debugElement.injector.get(ConfirmationService);
    vi.spyOn(confirmation, 'confirm').mockImplementation(options => { expect(options.message).toContain('ART-01'); expect(options.message).toContain('nunca se borran movimientos'); options.reject?.(); return confirmation; });
    await fixture.componentInstance.remove(); http.expectNone(request => request.method === 'DELETE');
  });
  it('ofrece desactivar si el servidor descubre una referencia nueva sin emitir éxito', async () => {
    const fixture = setup(); const confirmation = fixture.debugElement.injector.get(ConfirmationService);
    vi.spyOn(confirmation, 'confirm').mockImplementation(options => { options.accept?.(); return confirmation; });
    const deleted = vi.fn(); fixture.componentInstance.deleted.subscribe(deleted);
    const result = fixture.componentInstance.remove();
    await vi.waitFor(() => http.expectOne(`/api/v1/articles/${article.id}`).flush({ code: 'HistoryPreventsDeletion', detail: 'Existen referencias. Puedes desactivarlo.' }, { status: 409, statusText: 'Conflict' }));
    await result; fixture.detectChanges(); expect(deleted).not.toHaveBeenCalled(); expect(fixture.nativeElement.textContent).toContain('Desactivar en su lugar');
  });
});
