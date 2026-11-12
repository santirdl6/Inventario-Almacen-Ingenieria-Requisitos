import { HttpErrorResponse } from '@angular/common/http';

/** Formato ProblemDetails (RFC 9457) que devuelve la API. */
interface ProblemDetails {
  type?: string;
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
}

export type ApiErrorKind = 'network' | 'validation' | 'conflict' | 'forbidden' | 'not-found' | 'server';

/**
 * Error normalizado. Las features lo usan para mostrar mensajes por campo (validation)
 * o explicar un conflicto (stock insuficiente, versión obsoleta) sin leer HttpErrorResponse.
 */
export class ApiError extends Error {
  constructor(
    readonly kind: ApiErrorKind,
    message: string,
    readonly status: number,
    readonly code?: string,
    readonly fieldErrors: Readonly<Record<string, string[]>> = {}
  ) {
    super(message);
    this.name = 'ApiError';
  }

  static from(error: HttpErrorResponse): ApiError {
    if (error.status === 0) {
      return new ApiError('network', 'No hay conexión con el servidor. Comprueba tu red y vuelve a intentarlo.', 0);
    }
    const problem = (typeof error.error === 'object' && error.error ? error.error : {}) as ProblemDetails;
    const message = problem.detail ?? problem.title ?? 'Se ha producido un error inesperado.';
    return new ApiError(ApiError.kindOf(error.status), message, error.status, problem.code, problem.errors);
  }

  private static kindOf(status: number): ApiErrorKind {
    switch (status) {
      case 400:
      case 422: return 'validation';
      case 403: return 'forbidden';
      case 404: return 'not-found';
      case 409: return 'conflict';
      default: return 'server';
    }
  }
}
