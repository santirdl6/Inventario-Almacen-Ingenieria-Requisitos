/**
 * Tipos del contrato HTTP de HU-02 (specs/003-gestionar-ubicaciones/contracts/locations-api.md).
 * Las cantidades llegan como cadenas decimales exactas ("6.000"): no se recalculan en el navegador.
 */
export type LocationKind = 'Storage' | 'Receiving';
export type LocationStatusFilter = 'active' | 'inactive' | 'all';

export const CODE_MAX_LENGTH = 32;
export const PART_MAX_LENGTH = 16;
export const UNTRACKED_LOT = 'SIN-LOTE';

export interface LocationSummary {
  readonly id: string;
  readonly code: string;
  readonly aisle: string;
  readonly rack: string;
  readonly level: string;
  readonly description: string;
  readonly kind: LocationKind;
  readonly kindLabel: string;
  readonly isActive: boolean;
  readonly statusLabel: string;
}

export interface LocationDetail extends LocationSummary {
  readonly hasMovements: boolean;
  readonly isEmpty: boolean;
  readonly hasReservations: boolean;
  readonly inOpenCount: boolean;
  readonly isLastActiveReceiving: boolean;
}

/** Recurso editable junto con su ETag (If-Match en la siguiente modificación). */
export interface Versioned<T> {
  readonly value: T;
  readonly etag: string;
}

export interface LocationFields {
  readonly code: string;
  readonly aisle: string;
  readonly rack: string;
  readonly level: string;
  readonly kind: LocationKind;
}

export type CreateLocationRequest = LocationFields;

export interface UpdateLocationRequest extends LocationFields {
  readonly isActive: boolean;
}

/** Respuesta de POST y PUT, con la misma forma que las mutaciones de maestros de A. */
export interface LocationMutation {
  readonly operationId: string;
  readonly resource: LocationDetail;
}

/** Códigos de error del contrato de HU-02 y de la base común que trata la interfaz. */
export type LocationProblemCode =
  | 'DuplicateCode'
  | 'DuplicateLocationPlace'
  | 'LocationIdentityLocked'
  | 'LocationNotEmpty'
  | 'CountScopeFrozen'
  | 'NoActiveReceivingLeft'
  | 'StateChangeMustBeSeparate'
  | 'VersionConflict'
  | 'IdempotencyConflict'
  | 'ValidationFailed'
  | 'Forbidden'
  | 'NotFound';

export interface Page<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

export interface LocationQuery {
  readonly search?: string;
  readonly kind?: LocationKind | '';
  readonly status?: LocationStatusFilter;
  readonly page?: number;
  readonly pageSize?: number;
}

export interface LocationRef {
  readonly id: string;
  readonly code: string;
  readonly description: string;
  readonly kind: LocationKind;
  readonly isActive: boolean;
}

export interface ArticleRef {
  readonly id: string;
  readonly sku: string;
  readonly name: string;
  readonly unit: string;
  readonly isActive: boolean;
}

export interface ArticleLocationBucket {
  readonly location: LocationRef;
  readonly lotCode: string;
  readonly physical: string;
  readonly reserved: string;
}

export interface ArticleLocations extends Page<ArticleLocationBucket> {
  readonly asOf: string;
  readonly article: ArticleRef;
  readonly totals: { readonly physical: string; readonly reserved: string };
}

export interface LocationStockLine {
  readonly article: ArticleRef;
  readonly lotCode: string;
  readonly physical: string;
  readonly reserved: string;
  readonly unit: string;
}

export interface LocationStock extends Page<LocationStockLine> {
  readonly asOf: string;
  readonly location: LocationRef;
  readonly hasReservations: boolean;
  readonly inOpenCount: boolean;
}

export const KIND_OPTIONS: readonly { readonly value: LocationKind; readonly label: string }[] = [
  { value: 'Storage', label: 'Almacenamiento' },
  { value: 'Receiving', label: 'Zona de recepción' }
];

export function kindLabel(kind: LocationKind): string {
  return KIND_OPTIONS.find(option => option.value === kind)?.label ?? kind;
}

export function lotLabel(code: string): string {
  return code === UNTRACKED_LOT ? 'Sin lote' : code;
}

/** Una cantidad "0.000" no indica reservas; se compara como texto decimal exacto. */
export function isZero(quantity: string): boolean {
  return /^0+(\.0+)?$/.test(quantity.trim());
}
