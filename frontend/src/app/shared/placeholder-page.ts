import { Component, input } from '@angular/core';
import { CardModule } from 'primeng/card';

/** Pantalla provisional de una historia aún no desarrollada. Se sustituye por la real en su spec. */
@Component({
  selector: 'app-placeholder-page',
  standalone: true,
  imports: [CardModule],
  template: `
    <h1>{{ title() }}</h1>
    <p-card [header]="story()">
      <p>Pendiente de desarrollo en su spec. {{ description() }}</p>
    </p-card>
  `
})
export class PlaceholderPage {
  readonly title = input.required<string>();
  readonly story = input.required<string>();
  readonly description = input('');
}
