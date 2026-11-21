import type { Family } from '../families/family.models';

export interface ArticleWrite { sku: string; name: string; familyId: string; baseUnit: string; allowsFractional: boolean; tracksLot: boolean; isActive: boolean }
export interface Article extends ArticleWrite { id: string; isDefinitionLocked: boolean }
export interface ArticleContext {
  articleVersion: string; family: Family; minimumAvailable: string;
  capabilities: { canEditDefinition: boolean; definitionLockReason: string | null; canDelete: boolean; deleteBlockReason: string | null };
}
export interface ArticleSummary extends Omit<ArticleWrite, 'familyId'> { id: string; family: Family }
export interface ArticlePage { items: ArticleSummary[]; page: number; pageSize: number; totalCount: number }
