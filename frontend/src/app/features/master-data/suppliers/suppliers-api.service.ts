import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { MasterDataMutationsService } from '../../../core/http/master-data-mutations.service';
import { Supplier, SupplierPage, SupplierWrite } from './supplier.models';

@Injectable({ providedIn: 'root' })
export class SuppliersApiService {
  private readonly http = inject(HttpClient); private readonly mutations = inject(MasterDataMutationsService);
  list(q: string, state: string, page: number): Promise<SupplierPage> {
    return firstValueFrom(this.http.get<SupplierPage>('/api/v1/suppliers', { params: new HttpParams().set('q', q).set('state', state).set('page', page).set('pageSize', 25) }));
  }
  async detail(id: string): Promise<{ resource: Supplier; etag: string }> {
    const response = await firstValueFrom(this.http.get<Supplier>(`/api/v1/suppliers/${id}`, { observe: 'response' }));
    const etag = response.headers.get('ETag');
    if (!response.body || !etag) throw new Error('Consulta de nuevo el proveedor para obtener su versión.');
    return { resource: response.body, etag };
  }
  async save(id: string | null, value: SupplierWrite, etag?: string): Promise<Supplier> {
    const result = await this.mutations.mutate<{ operationId: string; resource: Supplier }>(id ? 'PUT' : 'POST', id ? `/api/v1/suppliers/${id}` : '/api/v1/suppliers', value, etag);
    if (!result.body?.resource) throw new Error('Comprueba el resultado antes de repetir la operación.');
    return result.body.resource;
  }
}
