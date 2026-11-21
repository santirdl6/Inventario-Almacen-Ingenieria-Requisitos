import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom, Subject } from 'rxjs';

export interface CurrentUser { id: string; email: string; roles: string[]; permissions: string[] }

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  readonly user = signal<CurrentUser | null>(null);
  readonly state = signal<'loading' | 'authenticated' | 'anonymous' | 'unavailable'>('loading');
  private readonly ended = new Subject<void>();
  readonly loggedOut = this.ended.asObservable();
  private refreshing: Promise<boolean> | null = null;

  has(permission: string): boolean { return this.user()?.permissions.includes(permission) ?? false; }
  expire(): void { this.user.set(null); this.state.set('anonymous'); }

  refresh(): Promise<boolean> {
    if (this.refreshing) return this.refreshing;
    this.refreshing = firstValueFrom(this.http.get<CurrentUser>('/api/v1/auth/me')).then(user => {
      this.user.set(user);
      this.state.set('authenticated');
      return true;
    }).catch((error: unknown) => {
      this.user.set(null);
      this.state.set(error instanceof HttpErrorResponse && error.status === 401 ? 'anonymous' : 'unavailable');
      return false;
    }).finally(() => { this.refreshing = null; });
    return this.refreshing;
  }

  async login(email: string, password: string): Promise<boolean> {
    if (this.refreshing) await this.refreshing;
    await firstValueFrom(this.http.get('/api/v1/auth/csrf'));
    await firstValueFrom(this.http.post('/api/v1/auth/login', { email, password }));
    return this.refresh();
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.get('/api/v1/auth/csrf'));
    await firstValueFrom(this.http.post('/api/v1/auth/logout', {}));
    this.user.set(null);
    this.state.set('anonymous');
    this.ended.next();
  }
}
