import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ArticleFormComponent } from './article-form.component';
import { SessionService } from '../../../core/session/session.service';

describe('Catálogo de artículos', () => {
  let http: HttpTestingController;
  let route: { snapshot: { paramMap: ReturnType<typeof convertToParamMap> } };
  const family = { id: '20000000-0000-0000-0000-000000000001', code: 'GENERAL', name: 'General', isActive: true };
  const article = { id: '10000000-0000-0000-0000-000000000001', sku: 'ART-01', name: 'Artículo', familyId: family.id,
    baseUnit: 'UD', allowsFractional: false, tracksLot: true, isActive: true, isDefinitionLocked: true };
  const etag = '"AQIDBAUGBwg="';
  beforeEach(() => {
    sessionStorage.clear();
    route = { snapshot: { paramMap: convertToParamMap({}) } };
    TestBed.configureTestingModule({ imports: [ArticleFormComponent], providers: [provideHttpClient(), provideHttpClientTesting(),
      provideRouter([]), { provide: ActivatedRoute, useValue: route }] });
    TestBed.inject(SessionService).user.set({ id: 'actor', email: 'gestor@example.test', roles: ['GestorMaestros'], permissions: ['MasterData.Manage'] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });
  afterEach(() => { http.verify(); sessionStorage.clear(); });
  function families(items = [family]): void {
    http.expectOne(request => request.url === '/api/v1/families').flush({ items, page: 1, pageSize: 25, totalCount: items.length });
  }
  async function existing(readonly = false) {
    route.snapshot.paramMap = convertToParamMap({ id: article.id });
    if (readonly) TestBed.inject(SessionService).user.set({ id: 'reader', email: 'reader@example.test', roles: ['Operario'], permissions: ['Inventory.Read'] });
    const fixture = TestBed.createComponent(ArticleFormComponent); fixture.detectChanges();
    http.expectOne(`/api/v1/articles/${article.id}/stock-policy`).flush({ minimumAvailable: '0.000' }, { headers: { ETag: etag } });
    http.expectOne(`/api/v1/articles/${article.id}`).flush(article, { headers: { ETag: etag } });
    await vi.waitFor(() => http.expectOne(`/api/v1/articles/${article.id}/context`).flush({ articleVersion: etag,
      family: { ...family, isActive: false }, minimumAvailable: '0.000', capabilities: {
        canEditDefinition: false, definitionLockReason: 'FirstReference', canDelete: true, deleteBlockReason: null } }));
    await vi.waitFor(() => families([]));
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false)); fixture.detectChanges();
    return fixture;
  }

  it('exige familia y envía unidad y booleanos explícitos sin cantidades, ubicación ni mínimo', async () => {
    const fixture = TestBed.createComponent(ArticleFormComponent); fixture.detectChanges(); families();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    const component = fixture.componentInstance;
    component.form.patchValue({ sku: 'NUEVO', name: 'Nombre' });
    await component.submit(); http.expectNone(request => request.method === 'POST');
    expect(component.form.controls.familyId.invalid).toBe(true);
    component.form.controls.familyId.setValue(family.id);
    const saved = component.submit();
    const request = http.expectOne('/api/v1/articles');
    expect(request.request.body).toEqual({ sku: 'NUEVO', name: 'Nombre', familyId: family.id, baseUnit: 'UD',
      allowsFractional: false, tracksLot: false, isActive: true });
    expect(request.request.headers.has('Idempotency-Key')).toBe(true);
    request.flush({ operationId: 'op', resource: article }); await saved; fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#unit')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[formControlName=minimumAvailable], [formControlName=quantity], [formControlName=locationId]')).toBeNull();
  });

  it('conserva la familia actual inactiva y los valores bloqueados en el PUT', async () => {
    const fixture = await existing(); const component = fixture.componentInstance;
    expect(fixture.nativeElement.querySelector('#family').textContent).toContain('inactiva; asociación actual');
    expect(component.form.controls.baseUnit.disabled).toBe(true);
    expect(component.form.controls.tracksLot.disabled).toBe(true);
    expect(component.form.controls.allowsFractional.disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('aunque después se retire');
    component.form.controls.name.setValue('Descripción corregida');
    const saved = component.submit();
    const request = http.expectOne(`/api/v1/articles/${article.id}`);
    expect(request.request.headers.get('If-Match')).toBe(etag);
    expect(request.request.body).toEqual({ sku: article.sku, name: 'Descripción corregida', familyId: family.id,
      baseUnit: 'UD', allowsFractional: false, tracksLot: true, isActive: true });
    request.flush({ code: 'DuplicateCode', detail: 'Código ocupado.', errors: { sku: ['Introduce otro código.'] } }, { status: 409, statusText: 'Conflict' });
    await saved; fixture.detectChanges();
    expect(component.form.getRawValue().name).toBe('Descripción corregida');
    expect(fixture.nativeElement.textContent).toContain('Introduce otro código.');
  });

  it('permite consulta al lector sin habilitar escritura', async () => {
    const fixture = await existing(true);
    expect(fixture.nativeElement.querySelector('#sku').disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Solo consulta');
    expect(fixture.nativeElement.textContent).toContain('0.000 UD');
    expect(fixture.nativeElement.textContent).not.toContain('Guardar artículo');
    await fixture.componentInstance.submit(); http.expectNone(request => request.method === 'PUT');
  });

  it('conserva la familia seleccionada al buscar otra página de opciones', async () => {
    const fixture = TestBed.createComponent(ArticleFormComponent); fixture.detectChanges(); families();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    fixture.componentInstance.form.controls.familyId.setValue(family.id);
    fixture.componentInstance.familyQuery.setValue('otra familia');
    const searched = fixture.componentInstance.searchFamilies(); families([]); await searched; fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#family').value).toBe(family.id);
  });
});
