/**
 * Navegación común (contracts/ui.md). Cada persona añade aquí las rutas de sus historias.
 * La navegación solo orienta: los permisos reales los aplica el backend.
 */
export interface NavItem {
  readonly label: string;
  readonly route: string;
  /** Historia de origen, para trazabilidad. */
  readonly story?: string;
}

export const NAV_ITEMS: readonly NavItem[] = [
  { label: 'Estado', route: '/estado' },

  // Persona B — operación y disponibilidad
  { label: 'Ubicaciones', route: '/ubicaciones', story: 'HU-02' },
  { label: 'Existencias', route: '/existencias', story: 'HU-05' },
  { label: 'Salidas', route: '/salidas', story: 'HU-03' },
  { label: 'Inventarios', route: '/inventarios', story: 'HU-04' }

  // Persona A añadirá aquí: /maestros, /compras, /recepciones, /alertas.
];
