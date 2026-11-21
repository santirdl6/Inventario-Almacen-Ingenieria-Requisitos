import { HttpErrorResponse } from '@angular/common/http';

export interface ApiProblem {
  status: number;
  code: string;
  detail: string;
  errors: Record<string, string[]>;
}

export function apiProblem(error: unknown): ApiProblem {
  if (error instanceof HttpErrorResponse) {
    const body = error.error;
    return {
      status: error.status,
      code: body && typeof body.code === 'string' ? body.code : 'RequestFailed',
      detail: body && typeof body.detail === 'string' ? body.detail : 'No se pudo completar la solicitud. Vuelve a comprobar la conexión.',
      errors: body && typeof body.errors === 'object' && body.errors !== null ? body.errors : {}
    };
  }
  return { status: 0, code: 'UnknownResult', detail: error instanceof Error ? error.message
    : 'Resultado pendiente de comprobar. Consulta la operación antes de repetir.', errors: {} };
}
