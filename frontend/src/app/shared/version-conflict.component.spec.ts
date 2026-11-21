import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { VersionConflictComponent } from './version-conflict.component';

describe('Revisión explícita de versión', () => {
  it('compara sin sobrescribir el borrador y solo adopta los valores elegidos', async () => {
    TestBed.configureTestingModule({ imports: [VersionConflictComponent] });
    const fixture = TestBed.createComponent(VersionConflictComponent); const draft = { code: 'LOCAL', name: 'Mi borrador' };
    fixture.componentRef.setInput('draft', draft); fixture.componentRef.setInput('loadCurrent', async () => ({ resource: { code: 'CURRENT', name: 'Otro gestor' }, etag: '"version"' }));
    const reviewed = vi.fn(); fixture.componentInstance.reviewed.subscribe(reviewed); fixture.detectChanges();
    await fixture.componentInstance.load(); fixture.detectChanges(); expect(draft.name).toBe('Mi borrador'); expect(reviewed).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Otro gestor');
    fixture.componentInstance.choices.set({ code: true }); fixture.componentInstance.review();
    expect(reviewed.mock.calls[0][0].values).toEqual({ code: 'CURRENT', name: 'Mi borrador' });
  });
});
