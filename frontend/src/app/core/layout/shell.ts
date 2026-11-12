import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ToastModule } from 'primeng/toast';
import { NAV_ITEMS } from '../navigation/nav-items';

/** Layout común: cabecera, navegación lateral y zona de contenido. */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ToastModule],
  template: `
    <a class="skip-link" href="#contenido">Saltar al contenido</a>
    <header class="shell-header">
      <strong>Inventario y almacén</strong>
    </header>
    <div class="shell-body">
      <nav class="shell-nav" aria-label="Principal">
        <ul>
          @for (item of items; track item.route) {
            <li>
              <a [routerLink]="item.route" routerLinkActive="active" ariaCurrentWhenActive="page">
                <span>{{ item.label }}</span>
              </a>
            </li>
          }
        </ul>
      </nav>
      <main id="contenido" tabindex="-1">
        <router-outlet />
      </main>
    </div>
    <p-toast position="top-right" />
  `
})
export class Shell {
  readonly items = NAV_ITEMS;
}
