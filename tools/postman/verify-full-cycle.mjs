// Executes only the scripting subset used by Velo.FullCycle; no npm dependencies.
import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
const collection = JSON.parse(fs.readFileSync(new URL('./Velo.FullCycle.postman_collection.json', import.meta.url), 'utf8'));
const values = new Map(collection.variable.map(x => [x.key, x.value]));
values.set('baseUrl', process.env.VELO_RUNNER_BASE_URL || 'http://127.0.0.1:15080');
values.set('operationsAccessToken', process.env.VELO_RUNNER_OPERATIONS_TOKEN || '');
if (!values.get('operationsAccessToken')) throw new Error('Set VELO_RUNNER_OPERATIONS_TOKEN for the isolated test API.');
const origin = new URL(values.get('baseUrl')).origin;
if (!/^http:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(origin)) throw new Error('Local APIs only.');
const replace = value => value.replace(/\{\{([^}]+)\}\}/g, (_, key) => {
  if (key === '$guid') return randomUUID();
  if (!values.has(key)) throw new Error(`Unknown variable ${key}`);
  return String(values.get(key));
});
let checks = 0, requests = 0;
for (const item of collection.item) {
  let stopped = false;
  const pm = {
    variables: { get: key => values.get(key), replaceIn: replace },
    collectionVariables: { get: key => values.get(key), set: (key, value) => values.set(key, String(value)) },
    execution: { setNextRequest: () => { stopped = true; } },
    expect: actual => ({ to: { eql: expected => assert.deepStrictEqual(actual, expected) } }),
    test: (_name, fn) => { fn(); checks++; }
  };
  try {
    for (const event of item.event || []) if (event.listen === 'prerequest') vm.runInNewContext(event.script.exec.join('\n'), { pm }, { timeout: 2000 });
    const url = replace(item.request.url);
    if (new URL(url).origin !== origin) throw new Error('A request attempted to leave the local API.');
    const headers = Object.fromEntries(item.request.header.map(x => [x.key, replace(x.value)]));
    const authentication = item.request.auth || collection.auth;
    if (authentication.type === 'bearer') headers.Authorization = 'Bearer ' + replace(authentication.bearer.find(x => x.key === 'token').value);
    const response = await fetch(url, { method: item.request.method, headers, body: item.request.body ? replace(item.request.body.raw) : undefined, redirect: 'error', signal: AbortSignal.timeout(30000) });
    const text = await response.text();
    pm.response = { code: response.status, json: () => JSON.parse(text), headers: response.headers,
      to: { have: { status: expected => assert.equal(response.status, expected) } } };
    for (const event of item.event || []) if (event.listen === 'test') vm.runInNewContext(event.script.exec.join('\n'), { pm }, { timeout: 2000 });
    requests++;
    if (stopped) throw new Error('Collection requested a stop.');
  } catch (error) {
    throw new Error(`FullCycle failed at ${item.name}: ${error.message}`, { cause: error });
  }
}
console.log(`FullCycle passed: ${requests} real HTTP requests, ${checks} assertions.`);
