import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const profile = process.argv[2];
if (!['smoke', 'full'].includes(profile) || (profile === 'full' && !process.env.Testing__SqlConnection)) {
  console.error('Uso: node scripts/measure-hu06-alerts.mjs smoke | full (full requiere Testing__SqlConnection de un SQL Server x64 dedicado y accesible desde Docker).');
  process.exit(2);
}
const connection = process.env.Testing__SqlConnection ? ['--env', 'Testing__SqlConnection'] : [];
const result = spawnSync('docker', ['compose', '--profile', 'test', 'run', '--rm', ...connection, '--env', 'HU06_BENCHMARK', '--env', 'Logging__LogLevel__Default=Warning',
  '--volume', `${root}backend:/source/backend`, '--entrypoint', 'dotnet', 'tests', 'test', 'Inventario.slnx',
  '--filter', 'FullyQualifiedName~StockAlertPerformanceTests', '--logger', 'console;verbosity=detailed'],
{ cwd: root, env: { ...process.env, HU06_BENCHMARK: profile }, stdio: 'inherit' });
process.exit(result.status ?? 1);
