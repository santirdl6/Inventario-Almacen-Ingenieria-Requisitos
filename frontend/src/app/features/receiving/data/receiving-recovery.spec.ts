import { operationGroup, mutationPermission, hasMutationPermission } from '../../../core/operations/pending-operation.store';

describe('Intenciones de recepción', () => {
  const receipt = '/api/v1/receipts/10000000-0000-4000-8000-000000000001';
  it('protege edición y confirmación del mismo documento con el permiso de registro', () => {
    expect(mutationPermission(receipt, 'PUT')).toBe('Receiving.Record');
    expect(mutationPermission(`${receipt}/confirm`, 'POST')).toBe('Receiving.Record');
    expect(operationGroup(receipt)).toBe(operationGroup(`${receipt}/confirm`));
    expect(mutationPermission(`${receipt}/confirm`, 'PUT')).toBeNull();
  });
  it('exige ambos permisos al recuperar una intención de contingencia', () => {
    const policy = mutationPermission('/api/v1/contingencies/reconcile', 'POST')!;
    expect(policy).toBe('Receiving.Reconcile');
    const session = { has: (permission: string) => permission === 'Receiving.Record' };
    expect(hasMutationPermission(session, policy)).toBe(false);
    const both = { has: (permission: string) => ['Receiving.Record', 'Inventory.Adjust'].includes(permission) };
    expect(hasMutationPermission(both, policy)).toBe(true);
    const scope = '/api/v1/contingencies/receiving-scopes/10000000-0000-4000-8000-000000000001';
    expect(operationGroup(scope)).toBe(operationGroup(`${scope}/reopen`));
    expect(mutationPermission(`${scope}/reopen`, 'POST')).toBe('Inventory.Adjust');
  });
});
