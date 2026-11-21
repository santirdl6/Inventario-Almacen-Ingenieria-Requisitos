import { test, expect } from './fixtures/purchasing.fixture';
import { purchasingProfile } from './fixtures/master-data.fixture';

test.skip(!purchasingProfile, 'El soporte de compras se valida solo con el perfil HU-07 explícito.');

test('perfil HU-07 aísla cuentas y permite restaurar SMTP', async ({ comprador, gestor, smtp }) => {
  expect(comprador.me.roles).toEqual(['Comprador']);
  expect(comprador.me.permissions).toContain('Purchasing.Send');
  expect(comprador.me.permissions).not.toContain('MasterData.Manage');
  expect(gestor.me.roles).toEqual(['GestorMaestros']);
  smtp.stop();
  await smtp.start();
  expect(await smtp.messages()).toHaveProperty('messages');
});
