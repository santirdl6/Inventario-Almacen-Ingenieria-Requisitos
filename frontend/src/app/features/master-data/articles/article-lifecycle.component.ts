import { Component, inject, input, output, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ConfirmationService } from 'primeng/api';
import { SessionService } from '../../../core/session/session.service';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { apiProblem } from '../../../core/http/api-problem';
import { Article, ArticleContext } from './article.models';

@Component({ selector: 'app-article-lifecycle', standalone: true, imports: [ButtonModule, ConfirmDialogModule], providers: [ConfirmationService], template: `
  @if (!context().capabilities.canDelete) { <p>El artículo tiene {{ context().capabilities.deleteBlockReason === 'History' ? 'historial' : 'referencias' }}. Se conserva y no se puede eliminar.</p> }
  @if (message()) { <p role="alert" tabindex="-1" id="lifecycle-error">{{ message() }}</p> }
  @if (session.has('MasterData.Manage')) {
    @if (context().capabilities.canDelete) { <p-button type="button" severity="danger" label="Eliminar artículo" [disabled]="disabled() || busy()" (onClick)="remove()" /> }
    @if ((!context().capabilities.canDelete || blocked()) && article().isActive) { <p-button type="button" label="Desactivar en su lugar" [disabled]="disabled() || busy()" (onClick)="deactivate.emit()" /> }
  }
  <p-confirmdialog />
` })
export class ArticleLifecycleComponent {
  readonly article = input.required<Article>(); readonly context = input.required<ArticleContext>(); readonly etag = input.required<string>(); readonly disabled = input(false);
  readonly deleted = output<void>(); readonly deactivate = output<void>(); readonly working = output<boolean>();
  readonly session = inject(SessionService); private readonly mutations = inject(MasterDataMutationsService); private readonly confirmation = inject(ConfirmationService);
  readonly busy = signal(false); readonly blocked = signal(false); readonly message = signal('');
  async remove(): Promise<void> {
    if (this.disabled() || this.busy() || !this.session.has('MasterData.Manage') || !this.context().capabilities.canDelete) return;
    this.busy.set(true); this.working.emit(true); this.message.set('');
    try {
      const article = this.article();
      const confirmed = await new Promise<boolean>(resolve => this.confirmation.confirm({ header: 'Revisar eliminación del artículo',
        message: `Eliminar ${article.sku} — ${article.name}. Dejará de aparecer en el catálogo. Se conserva la evidencia de autoría; nunca se borran movimientos ni documentos.`,
        acceptLabel: 'Eliminar definitivamente', rejectLabel: 'Volver', accept: () => resolve(true), reject: () => resolve(false) }));
      if (!confirmed) return;
      await this.mutations.mutate('DELETE', `/api/v1/articles/${article.id}`, null, this.etag()); this.deleted.emit();
    } catch (error) { const issue = apiProblem(error); this.message.set(issue.detail); this.blocked.set(issue.code === 'HistoryPreventsDeletion'); setTimeout(() => document.getElementById('lifecycle-error')?.focus()); }
    finally { this.busy.set(false); this.working.emit(false); }
  }
}
