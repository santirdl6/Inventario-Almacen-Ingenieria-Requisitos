import { ElementRef, Injector, afterNextRender } from '@angular/core';

/**
 * HU-05 · Convergence C1 (G1; FR-027, FR-028; stock-ui § Accesibilidad): tras un rechazo, el foco
 * va al primer campo marcado con aria-invalid dentro de `host` y, si el error no es de un campo, al
 * aviso (role="alert"). Mismo patrón que focusFirstError() de location-form.page.ts (HU-02), tras
 * el siguiente render, para que el campo o el aviso ya existan en el DOM.
 */
export function focusFirstError(host: ElementRef<HTMLElement>, injector: Injector): void {
  afterNextRender(() => {
    const root = host.nativeElement;
    const field = root.querySelector<HTMLElement>('[aria-invalid="true"]');
    const alert = root.querySelector<HTMLElement>('[role="alert"]');
    if (!field && alert && !alert.hasAttribute('tabindex')) alert.setAttribute('tabindex', '-1');
    (field ?? alert)?.focus();
  }, { injector });
}

/** Índices de asignación con error del servidor: «allocations[i].campo» → i. */
export function allocationErrorIndexes(errors: Record<string, string[]>): Set<number> {
  const indexes = new Set<number>();
  for (const key of Object.keys(errors)) {
    const match = /^allocations\[(\d+)\]\./.exec(key);
    if (match) indexes.add(Number(match[1]));
  }
  return indexes;
}
