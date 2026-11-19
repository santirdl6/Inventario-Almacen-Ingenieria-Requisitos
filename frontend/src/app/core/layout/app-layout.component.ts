import { Component, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { SessionService } from '../session/session.service';
import { apiProblem } from '../http/api-problem';
import { OperationStatusComponent } from '../operations/operation-status.component';
import { OPERATION_AREAS } from '../../operation-areas';

@Component({
  selector: 'app-root', standalone: true, imports: [RouterLink, RouterOutlet, ButtonModule, OperationStatusComponent],
  template: `
    <header><nav aria-label="Navegación principal">
      <a routerLink="/">Estado</a>
      @if (session.has('Inventory.Read')) { <a routerLink="/maestros/articulos">Artículos</a><a routerLink="/maestros/familias">Familias</a><a routerLink="/maestros/proveedores">Proveedores</a> }
      @for (area of areas; track area.path) { @if (session.has(area.permission)) { <a [routerLink]="'/' + area.path">{{ area.label }}</a> } }
      @if (session.user(); as user) {
        <span>{{ user.email }}</span>
        <p-button label="Cerrar sesión" (onClick)="logout()" />
      } @else { <a routerLink="/login">Iniciar sesión</a> }
    </nav>
    @if (error()) { <p role="alert">{{ error() }}</p> }
    </header>
    <app-operation-status [summaryOnly]="true" />
    <main><router-outlet /></main>
  `
})
export class AppLayoutComponent implements OnInit {
  readonly session = inject(SessionService);
  readonly areas = OPERATION_AREAS;
  private readonly router = inject(Router);
  readonly error = signal('');
  ngOnInit(): void { void this.session.refresh(); }
  async logout(): Promise<void> {
    try { await this.session.logout(); await this.router.navigateByUrl('/login'); }
    catch (error) { this.error.set(apiProblem(error).detail); }
  }
}
