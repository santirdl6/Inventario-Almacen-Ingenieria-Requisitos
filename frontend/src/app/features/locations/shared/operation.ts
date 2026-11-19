import { HttpErrorResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { ApiProblem, apiProblem } from '../../../core/http/api-problem';
import { isDefinitiveStatus } from '../data/idempotent-intent';

/** Tiempo máximo de espera de una mutación antes de considerarla «pendiente de comprobar». */
export const MUTATION_TIMEOUT_MS = 30_000;

/** Estados de una operación de escritura (contrato UI común, estados 1–7). */
export type OperationStatus = 'idle' | 'confirming' | 'pending-check';

export const PENDING_CHECK_MESSAGE =
  'Resultado pendiente de comprobar: no sabemos si el cambio se guardó. Pulsa «Comprobar» para consultarlo sin duplicarlo.';

export const FORBIDDEN_MESSAGE = 'Tu cuenta no tiene permiso para modificar ubicaciones. Puedes seguir consultándolas.';

export const LOCATIONS_MANAGE = 'Locations.Manage';

/** Red caída, timeout o 5xx: el servidor pudo guardar el cambio o no. */
export function isUncertain(error: unknown): boolean {
  if (error instanceof TimeoutError) return true;
  if (error instanceof HttpErrorResponse) return !isDefinitiveStatus(error.status);
  return true;
}

/**
 * Problema de la API con el modelo único de A (apiProblem): status, code, detail y errors.
 * No es un segundo modelo de errores; solo fija el mensaje de red y el de error inesperado.
 */
export function asProblem(error: unknown): ApiProblem {
  if (error instanceof HttpErrorResponse && error.status === 0)
    return { status: 0, code: 'NetworkUnavailable', detail: 'No hay conexión con el servidor. Comprueba tu red y vuelve a intentarlo.', errors: {} };
  if (!(error instanceof HttpErrorResponse))
    return { status: 0, code: 'UnknownResult', detail: 'No se pudo completar la operación.', errors: {} };
  return apiProblem(error);
}

/** Convierte los errores por campo del servidor en un mensaje por campo (claves camelCase). */
export function fieldMessages(problem: ApiProblem): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [field, messages] of Object.entries(problem.errors)) {
    if (Array.isArray(messages) && messages.length > 0) result[field.charAt(0).toLowerCase() + field.slice(1)] = messages[0];
  }
  return result;
}

/** Aviso que una pantalla deja a la siguiente al navegar (mensajes en la propia página). */
export function navigationNotice(): string | null {
  try {
    const state = globalThis.history?.state as { notice?: unknown } | null;
    return typeof state?.notice === 'string' ? state.notice : null;
  } catch {
    return null;
  }
}
