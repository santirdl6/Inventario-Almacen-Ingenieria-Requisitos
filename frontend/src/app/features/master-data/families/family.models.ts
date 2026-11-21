export interface Family { id: string; code: string; name: string; isActive: boolean }
export interface FamilyWrite { code: string; name: string; isActive: boolean }
export interface FamilyMutation { operationId: string; resource: Family }
export interface FamilyPage { items: Family[]; page: number; pageSize: number; totalCount: number }
