import { Component } from '@angular/core';
import { Routes } from '@angular/router';
import { AppComponent } from './app.component';
import { operationAreaRoutes } from './operation-areas';

@Component({ standalone: true, template: '<h1>Sin permiso</h1><p>Tu cuenta no tiene permiso para esta función.</p>' })
class AccessDeniedComponent {}

export const routes: Routes = [
  { path: '', component: AppComponent, pathMatch: 'full' },
  { path: 'login', loadComponent: () => import('./core/session/login-page.component').then(module => module.LoginPageComponent) },
  { path: 'sin-permiso', component: AccessDeniedComponent },
  { path: 'maestros', loadChildren: () => import('./features/master-data/master-data.routes').then(module => module.masterDataRoutes) },
  ...operationAreaRoutes,
  { path: '**', redirectTo: '' }
];
