#!/usr/bin/env bash
# Monta la demo en vídeo de Persona B (HU-02, HU-05, HU-03, HU-04) a partir de la grabación de los E2E
# existentes con frontend/playwright.demo.config.ts. Usa ffmpeg en Docker (jrottenberg/ffmpeg) con
# --rm, así que no deja contenedores. Probado en Git Bash (Windows 11) con Docker Desktop.
#
#   cd frontend && npx playwright test -c playwright.demo.config.ts locations stock shipping hu04-cycle-counts
#   bash demo/montar.sh            # CRF=28 por defecto; CRF=32 bash demo/montar.sh para comprimir más
#
# Genera demo/video/NN-nombre.mp4 y .jpg por historia, demo/video/demo-completa.mp4, demo/lista.txt y
# demo/datos.js (con las marcas de tiempo de cada escenario dentro de demo-completa.mp4).
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
REPORT="$ROOT/frontend/test-results/demo/report.json"
WORK="$ROOT/frontend/test-results/demo-build"
OUT="$ROOT/demo/video"
CRF=${CRF:-28}
IMAGE=${FFMPEG_IMAGE:-jrottenberg/ffmpeg:6.1-alpine}

[ -f "$REPORT" ] || { echo "Falta $REPORT: graba antes los E2E con playwright.demo.config.ts" >&2; exit 1; }
rm -rf "$WORK" "$OUT"
mkdir -p "$WORK/in" "$WORK/txt" "$OUT"
FONTS=/c/Windows/Fonts
cp "$FONTS/segoeuib.ttf" "$WORK/bold.ttf"
cp "$FONTS/segoeui.ttf" "$WORK/regular.ttf"

# 1. Plan: historias en orden de entrega, tests de cada spec en orden de línea, vídeos de cada test
#    (uno por contexto de navegador) copiados a WORK/in. Escribe WORK/plan.json y WORK/run.sh.
node - "$REPORT" "$WORK" "$CRF" <<'JS'
const fs = require('fs'), path = require('path');
const [report, work, crf] = process.argv.slice(2);
const stories = [
  { id: 'HU-02', slug: '01-hu02-ubicaciones', spec: 'locations.spec.ts', titulo: 'Gestionar ubicaciones' },
  { id: 'HU-05', slug: '02-hu05-existencias-reservas', spec: 'stock.spec.ts', titulo: 'Consultar existencias y reservar' },
  { id: 'HU-03', slug: '03-hu03-salidas', spec: 'shipping.spec.ts', titulo: 'Registrar salida de mercancía' },
  { id: 'HU-04', slug: '04-hu04-inventarios-ciclicos', spec: 'hu04-cycle-counts.spec.ts', titulo: 'Realizar inventarios cíclicos' }
];
const data = JSON.parse(fs.readFileSync(report, 'utf8'));
const specs = [];
(function walk(suite) {
  for (const spec of suite.specs ?? []) specs.push(spec);
  for (const child of suite.suites ?? []) walk(child);
})({ suites: data.suites });
const q = s => `'${s.replace(/'/g, `'\\''`)}'`;
const enc = `-c:v libx264 -preset slow -crf ${crf} -pix_fmt yuv420p -r 25 -an -movflags +faststart`;
const sh = ['set -e', 'cd /w', `probe() { ffprobe -v error -show_entries format=duration -of csv=p=0 "$1"; }`, ': > durations.txt'];
const card = (file, big, small, seconds, size) => {
  fs.writeFileSync(path.join(work, 'txt', `${file}.big.txt`), big);
  fs.writeFileSync(path.join(work, 'txt', `${file}.small.txt`), small);
  sh.push(`ffmpeg -hide_banner -loglevel error -y -f lavfi -i color=c=0x14213d:s=1280x720:d=${seconds}:r=25 -vf ` +
    q(`drawtext=fontfile=bold.ttf:textfile=txt/${file}.big.txt:fontcolor=white:fontsize=${size}:x=(w-tw)/2:y=(h-th)/2-40,` +
      `drawtext=fontfile=regular.ttf:textfile=txt/${file}.small.txt:fontcolor=0xd8e1ee:fontsize=28:x=(w-tw)/2:y=(h/2)+30`) +
    ` ${enc} ${file}.mp4`, `echo "${file} $(probe ${file}.mp4)" >> durations.txt`);
};
const plan = [];
for (const story of stories) {
  const tests = specs.filter(spec => spec.file.endsWith(story.spec)).sort((a, b) => a.line - b.line);
  const entry = { ...story, escenarios: [], saltados: [] };
  const parts = [];
  card(`${story.slug}-title`, `${story.id} · ${story.titulo}`, 'Persona B · E2E de Playwright grabado', 4, 56);
  parts.push(`${story.slug}-title.mp4`);
  tests.forEach((spec, i) => {
    const result = spec.tests[0]?.results?.at(-1);
    const videos = (result?.attachments ?? []).filter(a => a.name === 'video' && a.path && fs.existsSync(a.path));
    if (result?.status !== 'passed' || videos.length === 0) {
      entry.saltados.push({ titulo: spec.title, estado: result?.status ?? 'sin ejecutar' });
      return;
    }
    const scenario = `${story.slug}-s${i + 1}`;
    card(`${scenario}-card`, spec.title.replace(/^E2E-\d+: /, ''), `${story.id} · escenario ${i + 1}`, 2.5,
      Math.min(40, Math.floor(1180 / (spec.title.length * 0.55))));
    parts.push(`${scenario}-card.mp4`);
    const segs = videos.map((video, j) => {
      const input = `in/${scenario}-v${j + 1}.webm`;
      fs.copyFileSync(video.path, path.join(work, input));
      const seg = `${scenario}-v${j + 1}.mp4`;
      sh.push(`ffmpeg -hide_banner -loglevel error -y -i ${input} -vf ` +
        q('scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2:color=white,fps=25') +
        ` ${enc} ${seg}`, `echo "${seg.replace('.mp4', '')} $(probe ${seg})" >> durations.txt`);
      parts.push(seg);
      return seg.replace('.mp4', '');
    });
    entry.escenarios.push({ titulo: spec.title, card: `${scenario}-card`, segs });
  });
  fs.writeFileSync(path.join(work, `${story.slug}.list`), parts.map(p => `file '${p}'`).join('\n') + '\n');
  sh.push(`ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i ${story.slug}.list -c copy -movflags +faststart out/${story.slug}.mp4`,
    `echo "${story.slug} $(probe out/${story.slug}.mp4)" >> durations.txt`);
  const first = entry.escenarios[0]?.segs[0];
  if (first) sh.push(`ffmpeg -hide_banner -loglevel error -y -ss 4 -i ${first}.mp4 -vf thumbnail=50 -frames:v 1 -q:v 4 out/${story.slug}.jpg`);
  plan.push(entry);
}
fs.writeFileSync(path.join(work, 'completa.list'), plan.filter(s => s.escenarios.length).map(s => `file 'out/${s.slug}.mp4'`).join('\n') + '\n');
sh.push('ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i completa.list -c copy -movflags +faststart out/demo-completa.mp4',
  'echo "demo-completa $(probe out/demo-completa.mp4)" >> durations.txt');
fs.writeFileSync(path.join(work, 'plan.json'), JSON.stringify(plan, null, 2));
fs.writeFileSync(path.join(work, 'run.sh'), sh.join('\n') + '\n');
JS

# 2. ffmpeg en un único contenedor efímero.
mkdir -p "$WORK/out"
MSYS_NO_PATHCONV=1 docker run --rm -v "$(cygpath -w "$WORK" 2>/dev/null || echo "$WORK"):/w" --entrypoint sh "$IMAGE" /w/run.sh
cp "$WORK"/out/*.mp4 "$WORK"/out/*.jpg "$OUT/"

# 3. datos.js y lista.txt con rutas relativas a demo/.
node - "$WORK" "$ROOT/demo" <<'JS'
const fs = require('fs'), path = require('path');
const [work, demo] = process.argv.slice(2);
const plan = JSON.parse(fs.readFileSync(path.join(work, 'plan.json'), 'utf8'));
const dur = Object.fromEntries(fs.readFileSync(path.join(work, 'durations.txt'), 'utf8').trim().split('\n')
  .map(line => line.split(' ')).map(([k, v]) => [k, Number(v)]));
const r = x => Math.round(x * 10) / 10;
const meta = {
  'HU-02': { rol: 'Operario de almacén (y cuenta de solo lectura)',
    historia: 'Como operario, quiero dar de alta, buscar, corregir y retirar ubicaciones para saber dónde está físicamente cada artículo.',
    previstos: ['Alta, búsqueda, revisión y retirada de ubicaciones (recorrido habitual)', 'Código duplicado o datos no válidos (error)',
      'Cuenta de solo lectura sin gestión (permisos)', 'Retirada bloqueada por existencias, reservas o conteo abierto (estado bloqueante)',
      'Respuesta perdida recuperada sin duplicar (fiabilidad)'] },
  'HU-05': { rol: 'Responsable de almacén y operario',
    historia: 'Como responsable, quiero consultar físico, reservado y disponible por ubicación y lote, y reservar o liberar existencias para un pedido de cliente.',
    previstos: ['Consulta de existencias, alta de pedido, reserva y liberación (recorrido habitual)', 'Reserva superior a lo disponible (error)',
      'Operario sin permiso de reservas (permisos)', 'Ubicación en conteo abierto rechaza la reserva (estado bloqueante)'] },
  'HU-03': { rol: 'Operario de almacén',
    historia: 'Como operario, quiero registrar la salida de mercancía de un pedido de cliente consumiendo su reserva, para que las existencias reflejen lo servido.',
    previstos: ['Salida que consume la reserva propia y deja el pedido servido (recorrido habitual)', 'Cantidad superior a la posible (error)',
      'Sesión sin permiso de salidas (permisos)', 'Pedido no abierto u origen en conteo abierto (estado bloqueante)'] },
  'HU-04': { rol: 'Operario (cuenta) y responsable de almacén (autoriza)',
    historia: 'Como responsable, quiero contar una parte del almacén, ver las diferencias y autorizar su ajuste con motivo, para que el sistema refleje lo que hay físicamente.',
    previstos: ['Conteo con una diferencia, revisión con motivo y ajuste confirmado (recorrido habitual)', 'Confirmar por debajo del reservado (error)',
      'Operario sin permiso de ajuste (permisos)', 'Ubicación congelada rechaza salidas, reservas y cambios (estado bloqueante)'] }
};
let inicio = 0;
const historias = [];
for (const story of plan.filter(s => s.escenarios.length)) {
  let t = inicio + dur[`${story.slug}-title`];
  const escenarios = story.escenarios.map(scenario => {
    const item = { titulo: scenario.titulo, t: r(t) };
    t += dur[scenario.card] + scenario.segs.reduce((sum, seg) => sum + dur[seg], 0);
    return item;
  });
  historias.push({ id: story.id, spec: `frontend/e2e/${story.spec}`, titulo: story.titulo, ...meta[story.id], escenarios,
    previstos: meta[story.id].previstos, saltados: story.saltados, duracion: r(dur[story.slug]), inicio: r(inicio),
    video: `video/${story.slug}.mp4`, poster: `video/${story.slug}.jpg` });
  inicio += dur[story.slug];
}
const demoData = { completa: 'video/demo-completa.mp4', duracion: r(dur['demo-completa']), historias };
fs.writeFileSync(path.join(demo, 'datos.js'), '// Generado por demo/montar.sh: no editar a mano.\nwindow.DEMO = ' + JSON.stringify(demoData, null, 2) + ';\n');
fs.writeFileSync(path.join(demo, 'lista.txt'), ['video/demo-completa.mp4', ...historias.flatMap(h => [h.video, h.poster])].join('\n') + '\n');
console.log(`demo-completa.mp4: ${r(dur['demo-completa'])} s; historias: ${historias.map(h => h.id).join(', ')}`);
for (const story of plan) for (const s of story.saltados) console.log(`Saltado ${story.id}: ${s.titulo} (${s.estado})`);
JS
du -ch "$OUT"/* | tail -1
