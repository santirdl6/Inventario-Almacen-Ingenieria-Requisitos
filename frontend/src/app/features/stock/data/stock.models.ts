// HU-05 · Contratos de specs/004-consultar-existencias/contracts/stock-api.md. Las cantidades
// llegan como cadenas con tres decimales y se muestran tal cual, siempre con la unidad.

/** Artículo elegible en el selector propio (GET /articles?q=&state=all), incluidos inactivos. */
export interface StockArticleOption {
  id: string; sku: string; name: string; baseUnit: string; isActive: boolean; tracksLot: boolean;
}

export interface StockLocation {
  id: string; code: string; aisle: string; rack: string; level: string; kind: string; isActive: boolean;
}

/** En artículos sin seguimiento, isUntracked: la UI muestra «sin lote», nunca SIN-LOTE. */
export interface StockLot { id: string; code: string; isUntracked: boolean }

export interface StockRow { location: StockLocation; lot: StockLot; physical: string; reserved: string; available: string }

export interface StockQueryResult {
  asOf: string;
  article: { id: string; sku: string; name: string; baseUnit: string; isActive: boolean; tracksLot: boolean };
  totals: { physical: string; reserved: string; available: string };
  items: StockRow[];
  page: number; pageSize: number; totalCount: number;
}

export interface StockFilter {
  articleId: string; locationId?: string; lotId?: string; includeEmpty: boolean; page: number;
}

export function lotText(lot: { code: string; isUntracked: boolean }): string {
  return lot.isUntracked ? 'sin lote' : lot.code;
}

export function isZeroQuantity(value: string): boolean {
  return Number(value) === 0;
}
