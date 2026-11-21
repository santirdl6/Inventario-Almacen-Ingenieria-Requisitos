import { afterNextRender, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SessionService } from './session.service';
import { apiProblem } from '../http/api-problem';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, ButtonModule, InputTextModule],
  template: `
    <h1>Iniciar sesión</h1>
    <form [formGroup]="form" (ngSubmit)="submit()">
      <fieldset disabled [disabled]="!ready() || busy()"><legend>Datos de acceso</legend>
      <p><label for="email">Correo electrónico (obligatorio)</label><br>
      <input pInputText id="email" type="email" autocomplete="username" formControlName="email" required></p>
      <p><label for="password">Contraseña (obligatoria)</label><br>
      <input pInputText id="password" type="password" autocomplete="current-password" formControlName="password" required></p>
      @if (error()) { <p role="alert">{{ error() }}</p> }
      <p-button type="submit" label="Entrar" [disabled]="form.invalid || busy()" />
      </fieldset>
    </form>
  `
})
export class LoginPageComponent {
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly busy = signal(false);
  readonly ready = signal(false);
  readonly error = signal('');
  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    password: new FormControl('', { nonNullable: true, validators: Validators.required })
  });
  constructor() { afterNextRender(() => this.ready.set(true)); }
  async submit(): Promise<void> {
    if (this.busy() || this.form.invalid) return;
    this.busy.set(true); this.error.set('');
    try {
      const { email, password } = this.form.getRawValue();
      if (!await this.session.login(email, password)) {
        this.error.set('La sesión no se pudo comprobar. Vuelve a intentarlo.'); return;
      }
      this.form.controls.password.reset();
      const destination = this.route.snapshot.queryParamMap.get('returnUrl');
      await this.router.navigateByUrl(destination?.startsWith('/') && !destination.startsWith('//') ? destination : '/');
    } catch (error) { this.error.set(apiProblem(error).detail); }
    finally { this.busy.set(false); }
  }
}
