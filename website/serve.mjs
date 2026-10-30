import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';

const { values } = parseArgs({ options: { port: { type: 'string', default: '4173' } } });
const dist = path.join(path.dirname(fileURLToPath(import.meta.url)), 'dist');
const { basePath: base } = JSON.parse(fs.readFileSync(path.join(dist, 'build-info.json'), 'utf8'));
const mime = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.json': 'application/json', '.svg': 'image/svg+xml',
  '.png': 'image/png', '.woff2': 'font/woff2', '.txt': 'text/plain', '.xml': 'application/xml' };
http.createServer((request, response) => {
  let route;
  try { route = decodeURIComponent(new URL(request.url, 'http://localhost').pathname); }
  catch { response.writeHead(400).end(); return; }
  if (base && route !== base && !route.startsWith(base + '/')) { response.writeHead(404).end(); return; }
  const relative = route.slice(base.length);
  let file = path.resolve(dist, '.' + (relative || '/'));
  if (file !== dist && !file.startsWith(dist + path.sep)) { response.writeHead(403).end(); return; }
  if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, 'index.html');
  if (!fs.existsSync(file) || !fs.statSync(file).isFile()) {
    response.writeHead(404, { 'Content-Type': 'text/html; charset=utf-8' });
    response.end(fs.readFileSync(path.join(dist, '404.html'))); return;
  }
  response.writeHead(200, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream' });
  fs.createReadStream(file).pipe(response);
}).listen(Number(values.port), '127.0.0.1', () => console.log(`Preview: http://127.0.0.1:${values.port}${base}/`));
