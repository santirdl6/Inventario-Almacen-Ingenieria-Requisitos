import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MessageService } from 'primeng/api';
import { catchError, throwError } from 'rxjs';
import { ApiError } from './api-error';

/**
 * Convierte los fallos HTTP en ApiError. Los errores de red y de servidor se avisan aquí
 * con un mensaje global; validaciones y conflictos los gestiona cada pantalla para conservar
 * los datos del formulario y mostrar el motivo junto al campo (contracts/ui.md, estados 5-7).
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const messages = inject(MessageService);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }
      const apiError = ApiError.from(error);
      if (apiError.kind === 'network' || apiError.kind === 'server') {
        messages.add({ severity: 'error', summary: 'Error', detail: apiError.message, life: 8000 });
      }
      return throwError(() => apiError);
    })
  );
};
