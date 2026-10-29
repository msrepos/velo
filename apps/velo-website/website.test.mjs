import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
await import('./build.mjs');
const root = path.join(path.dirname(fileURLToPath(import.meta.url)), 'dist');
const routes = ['/', '/fleet/', '/app/', '/contact/'];
for (const route of routes) {
  test(`${route} has working local links, anchors, and assets`, async () => {
    const html = await readFile(path.join(root, route, 'index.html'), 'utf8');
    assert.equal((html.match(/<h1[ >]/g) || []).length, 1);
    assert.match(html, /<main id="main"/);
    assert.match(html, /name="viewport"/);
    assert.match(html, /aria-current="page"/);
    for (const match of html.matchAll(/(?:href|src)="([^\"]+)"/g)) {
      const url = match[1];
      if (/^(https?:|mailto:|tel:)/.test(url)) continue;
      const [localPath, anchor] = url.split('#');
      let file = localPath ? path.join(root, localPath) : path.join(root, route, 'index.html');
      if ((await stat(file)).isDirectory()) file = path.join(file, 'index.html');
      assert.ok((await stat(file)).isFile(), `${url} must exist`);
      if (anchor) assert.ok((await readFile(file, 'utf8')).includes(`id="${anchor}"`), `${url} anchor must exist`);
    }
  });
}
