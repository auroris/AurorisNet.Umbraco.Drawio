import {test} from 'node:test';
import assert from 'node:assert/strict';
import {diagramBlob, maxBytes} from '../../src/wwwroot/App_Plugins/AurorisNet.Drawio/bridge.js';

test('a diagram with no Content-Length is read to completion', async () => {
    assert.equal(await (await diagramBlob(new Response('diagram'))).text(), 'diagram');
});

test('an oversized Content-Length cancels the download before the first read', async () => {
    let cancelled = false;
    const stream = new ReadableStream({pull() { assert.fail('unexpected read'); }, cancel() { cancelled = true; }},
        {highWaterMark:0});
    await assert.rejects(diagramBlob(new Response(stream, {headers:{'Content-Length':maxBytes + 1}})), /40 MiB/);
    assert.equal(cancelled, true);
});

test('decoded chunks are capped even when a small Content-Length was supplied', async () => {
    let cancelled = false;
    const chunk = new Uint8Array(1024 * 1024);
    const stream = new ReadableStream({pull(controller) { controller.enqueue(chunk); }, cancel() { cancelled = true; }});
    await assert.rejects(diagramBlob(new Response(stream, {headers:{'Content-Length':1024}})), /40 MiB/);
    assert.equal(cancelled, true);
});
