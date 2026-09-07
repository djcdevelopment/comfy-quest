// Exercises the shipping WebGPU renderer with real GPU picking across repeated
// mounts on one unchanged canvas. No game or Steward service is required.
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const { chromium } = await import(pathToFileURL(path.join(root,
  'src/Quest.Studio.E2E.Tests/bin/Release/net9.0/.playwright/package/index.mjs')));
const option = name => process.argv[process.argv.indexOf(name) + 1];
const sourceRef = process.argv.includes('--source-ref') ? option('--source-ref') : null;
const endpoint = process.argv.includes('--endpoint') ? option('--endpoint') : null;
const script = sourceRef ? execFileSync('git', ['show', `${sourceRef}:src/Quest.Studio/CreatorSceneRenderer.js`],
  { cwd: root, encoding: 'utf8' }) : fs.readFileSync(path.join(root, 'src/Quest.Studio/CreatorSceneRenderer.js'), 'utf8');
const instance = Buffer.alloc(80), identities = Buffer.alloc(4);
for (const index of [0, 5, 10, 15]) instance.writeFloatLE(1, index * 4);
[.9, .6, .2, 1].forEach((value, index) => instance.writeFloatLE(value, 64 + index * 4));
identities.writeUInt32LE(101);
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const manifest = Buffer.from(JSON.stringify({ schema: 'steward-zdo-authoring-scene/v1',
  renderInstances: 1, instanceBytes: 80, identityStride: 4, identityCount: 1, identityBytes: 4,
  absoluteOrigin: [0, 0, 0], home: { target: [0, 0, 0], radiusM: 1 },
  instanceSha256: hash(instance), identitySha256: hash(identities) }));
const offset = Math.ceil((20 + manifest.length) / 4) * 4;
const scene = Buffer.alloc(offset + 84);
scene.write('SVCA');scene.writeUInt32LE(1,4);scene.writeUInt32LE(manifest.length,8);
scene.writeUInt32LE(offset,12);scene.writeUInt32LE(offset+80,16);
manifest.copy(scene,20);instance.copy(scene,offset);identities.copy(scene,offset+80);
const browser = endpoint ? await chromium.connectOverCDP(endpoint) :
  await chromium.launch({ headless: true, args: ['--enable-unsafe-webgpu'] });
const context = await browser.newContext({ viewport: { width: 640, height: 480 } });
const page = await context.newPage();
const errors = [];
page.on('pageerror', error => errors.push(String(error)));
await page.route('http://127.0.0.1:54321/**', async route => {
  const url = new URL(route.request().url());
  if (url.pathname === '/renderer.js') return route.fulfill({ contentType: 'text/javascript', body: script });
  if (url.pathname === '/scene') return route.fulfill({ contentType: 'application/octet-stream', body: scene });
  return route.fulfill({ contentType: 'text/html', body: `<!doctype html><canvas style="width:400px;height:240px"></canvas>
    <script type="module">
    import { mountCreatorScene } from '/renderer.js';
    const canvas = document.querySelector('canvas');
    const bytes = await (await fetch('/scene')).arrayBuffer();
    window.picks = []; window.rendererErrors = [];
    window.mount = async () => {
      window.renderer?.dispose();
      window.renderer = await mountCreatorScene(canvas, bytes, result =>
        result.error ? window.rendererErrors.push(result.error) : window.picks.push(result.zdoIndex));
      await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    };
    await window.mount(); window.ready = true;
    </script>` });
});
const output = path.join(root, 'artifacts/creator-dm/renderer-' + (sourceRef ? 'baseline' : 'current') + '.json');
fs.mkdirSync(path.dirname(output), { recursive: true });
let failure;
try {
  await page.goto('http://127.0.0.1:54321/');
  await page.waitForFunction(() => window.ready, null, { timeout: 15000 });
  for (let mount = 0; mount < 4; mount++) {
    if (mount) await page.evaluate(() => window.mount());
    const box = await page.locator('canvas').boundingBox();
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    await page.waitForFunction(count => window.picks.length >= count, mount + 1, { timeout: 4000 });
    assert.deepEqual(await page.evaluate(() => window.picks), Array(mount + 1).fill(101));
    assert.deepEqual(errors, []);
    assert.deepEqual(await page.evaluate(() => window.rendererErrors), []);
  }
  await page.evaluate(() => window.renderer.dispose());
} catch (error) { failure = String(error); }
finally {
  const result = { proof_level: 'real-webgpu-renderer', source_ref: sourceRef,
    passed: !failure, failure, page_errors: errors, picks: await page.evaluate(() => window.picks || []) };
  fs.writeFileSync(output, JSON.stringify(result, null, 2) + '\n');
  console.log(JSON.stringify(result));
  await context.close(); await browser.close();
}
if (failure) process.exitCode = 1;
