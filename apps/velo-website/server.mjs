import http from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
await import('./build.mjs');
const root = path.join(path.dirname(fileURLToPath(import.meta.url)), 'dist');
const mime = { '.html':'text/html; charset=utf-8', '.css':'text/css', '.js':'text/javascript', '.png':'image/png', '.svg':'image/svg+xml' };
http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://localhost');
    if (url.pathname.startsWith('/health/')) { res.writeHead(200); return res.end('Healthy'); }
    let file = path.resolve(root, '.' + decodeURIComponent(url.pathname));
    if (!file.startsWith(root + path.sep) && file !== root) throw new Error('Invalid path');
    if ((await stat(file)).isDirectory()) file = path.join(file, 'index.html');
    const content = await readFile(file);
    res.writeHead(200, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream' }); res.end(content);
  } catch {
    res.writeHead(404, { 'Content-Type': 'text/html' }); res.end(await readFile(path.join(root, '404.html')));
  }
}).listen(5180, '127.0.0.1', () => console.log('Local: http://127.0.0.1:5180'));
