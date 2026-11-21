export interface SupplierWrite { code: string; name: string; email: string | null; taxIdentifier: string | null; isActive: boolean }
export interface Supplier extends SupplierWrite { id: string }
export interface SupplierPage { items: Supplier[]; page: number; pageSize: number; totalCount: number }
