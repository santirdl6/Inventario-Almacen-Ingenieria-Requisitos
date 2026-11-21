export interface ArticleMovement {
  id: string; movementId: string; kind: string; quantity: string; baseUnit: string;
  occurredAt: string; recordedAt: string; actor: { id: string; name: string }; reason: string;
  document: { type: string; id: string }; supportReference: string | null; supportNotApplicableReason: string | null;
  counterpartyReference: string | null; counterpartyNotApplicableReason: string | null;
  origin: { id: string; code: string; description: string } | null; destination: { id: string; code: string; description: string } | null;
  lot: { id: string; code: string } | null;
}
export interface ArticleMovementPage { items: ArticleMovement[]; totalCount: number; page: number; pageSize: number }
export interface MovementFilters { from: string; to: string; actorId: string; locationId: string; lotId: string }
export type MovementFilterField = 'actor' | 'location' | 'lot';
export interface MovementFilterOption { id: string; label: string }
export interface MovementFilterPage { items: MovementFilterOption[]; totalCount: number; page: number; pageSize: number }
