import { Component, input } from '@angular/core';

@Component({
  selector: 'app-field-errors', standalone: true,
  template: `@if (errors().length) { <div [id]="fieldId() + '-errors'" role="alert">@for (message of errors(); track $index) { <p>{{ message }}</p> }</div> }`
})
export class FieldErrorsComponent {
  readonly fieldId = input.required<string>();
  readonly errors = input<readonly string[]>([]);
}
