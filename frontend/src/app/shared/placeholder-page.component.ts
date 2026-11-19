import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CardModule } from 'primeng/card';

/** Pantalla provisional de una historia aún no desarrollada. Se sustituye por la real en su spec. */
@Component({
  selector: 'app-placeholder-page',
  standalone: true,
  imports: [CardModule],
  template: `
    <h1>{{ data['title'] }}</h1>
    <p-card [header]="data['story']">
      <p>Pendiente de desarrollo en su spec. {{ data['description'] ?? '' }}</p>
    </p-card>
  `
})
export class PlaceholderPageComponent {
  readonly data = inject(ActivatedRoute).snapshot.data;
}
