import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { Family, FamilyMutation, FamilyPage, FamilyWrite } from './family.models';

@Injectable({ providedIn: 'root' })
export class FamiliesApiService {
  private readonly http = inject(HttpClient);
  private readonly mutations = inject(MasterDataMutationsService);
  list(q: string, state: string, page: number): Promise<FamilyPage> {
    const params = new HttpParams().set('q', q).set('state', state).set('page', page).set('pageSize', 25);
    return firstValueFrom(this.http.get<FamilyPage>('/api/v1/families', { params }));
  }
  async detail(id: string): Promise<{ resource: Family; etag: string }> {
    const response = await firstValueFrom(this.http.get<Family>(`/api/v1/families/${id}`, { observe: 'response' }));
    const etag = response.headers.get('ETag');
    if (!response.body || !etag) throw new Error('Consulta de nuevo el registro para obtener su versión.');
    return { resource: response.body, etag };
  }
  async save(id: string | null, value: FamilyWrite, etag?: string): Promise<Family> {
    const result = await this.mutations.mutate<FamilyMutation>(id ? 'PUT' : 'POST',
      id ? `/api/v1/families/${id}` : '/api/v1/families', value, etag);
    if (!result.body?.resource) throw new Error('Consulta el resultado de la operación antes de repetir.');
    return result.body.resource;
  }
}
