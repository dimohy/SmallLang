import { spawn } from 'node:child_process';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

if (process.argv.length < 3) {
    throw new Error('usage: node scripts/verify-language-tools.mjs <compiler> [compiler-prefix-args...]');
}

const compiler = process.argv[2];
const compilerPrefix = process.argv.slice(3);
const started = Date.now();

function run(args, input) {
    return new Promise((resolve, reject) => {
        const child = spawn(compiler, [...compilerPrefix, ...args], {
            windowsHide: true,
            stdio: ['pipe', 'pipe', 'pipe']
        });
        const stdout = [];
        const stderr = [];
        child.stdout.on('data', chunk => stdout.push(chunk));
        child.stderr.on('data', chunk => stderr.push(chunk));
        child.on('error', reject);
        child.on('close', code => resolve({
            code,
            stdout: Buffer.concat(stdout).toString('utf8'),
            stderr: Buffer.concat(stderr).toString('utf8')
        }));
        child.stdin.end(input, 'utf8');
    });
}

function frame(message) {
    const payload = Buffer.from(JSON.stringify(message), 'utf8');
    return Buffer.concat([Buffer.from(`Content-Length: ${payload.length}\r\n\r\n`, 'ascii'), payload]);
}

function decodeFrames(buffer) {
    const messages = [];
    let offset = 0;
    while (offset < buffer.length) {
        const headerEnd = buffer.indexOf('\r\n\r\n', offset, 'ascii');
        if (headerEnd < 0) throw new Error('incomplete LSP header');
        const header = buffer.subarray(offset, headerEnd).toString('ascii');
        const match = /^Content-Length:\s*(\d+)$/im.exec(header);
        if (!match) throw new Error(`missing Content-Length in ${header}`);
        const length = Number(match[1]);
        const bodyStart = headerEnd + 4;
        const bodyEnd = bodyStart + length;
        if (bodyEnd > buffer.length) throw new Error('incomplete LSP body');
        messages.push(JSON.parse(buffer.subarray(bodyStart, bodyEnd).toString('utf8')));
        offset = bodyEnd;
    }
    return messages;
}

function requireValue(condition, message) {
    if (!condition) throw new Error(message);
}

function offsetAt(text, position) {
    let line = 0;
    let character = 0;
    for (let offset = 0; offset <= text.length; offset++) {
        if (line === position.line && character === position.character) return offset;
        if (text[offset] === '\n') { line++; character = 0; }
        else character++;
    }
    throw new Error(`invalid LSP position ${JSON.stringify(position)}`);
}

function applyEdit(text, edit) {
    const start = offsetAt(text, edit.range.start);
    const end = offsetAt(text, edit.range.end);
    return text.slice(0, start) + edit.newText + text.slice(end);
}

console.log('[0/4] Running parser-backed language-tool verification.');
const source = 'main {\ntrue\nand true\nor false\n-> if {\n"ok"\n-> println\n}\n}\n\n';
const expected = 'main {\n    true\n        and true\n        or false\n        -> if {\n            "ok"\n                -> println\n        }\n}\n';
const first = await run(['format', '--stdin'], source);
requireValue(first.code === 0, first.stderr || `formatter exited ${first.code}`);
requireValue(first.stdout === expected, `unexpected formatter output: ${JSON.stringify(first.stdout)}`);
const second = await run(['format', '--stdin'], first.stdout);
requireValue(second.code === 0 && second.stdout === first.stdout, 'formatter is not idempotent');
console.log(`[1/4] 25.0% formatter syntax preservation and idempotence (${Date.now() - started} ms)`);

const invalid = await run(['format', '--stdin'], 'main {\n');
requireValue(invalid.code === 1 && invalid.stderr.includes('parse error at'), 'invalid source did not retain parser diagnostics');
console.log(`[2/4] 50.0% invalid-source parser diagnostic (${Date.now() - started} ms)`);

const temporary = await mkdtemp(join(tmpdir(), 'sollang-format-'));
try {
    const path = join(temporary, 'sample.slg');
    await writeFile(path, source, 'utf8');
    const dirtyCheck = await run(['format', '--check', path], '');
    requireValue(dirtyCheck.code === 1, 'format --check accepted a noncanonical file');
    const write = await run(['format', path], '');
    requireValue(write.code === 0, write.stderr || `file formatter exited ${write.code}`);
    requireValue(await readFile(path, 'utf8') === expected, 'file formatter output differed from stdin mode');
    const cleanCheck = await run(['format', '--check', path], '');
    requireValue(cleanCheck.code === 0, 'format --check rejected a canonical file');
} finally {
    await rm(temporary, { recursive: true, force: true });
}
console.log(`[3/4] 75.0% file rewrite and --check contract (${Date.now() - started} ms)`);

const lspInput = Buffer.concat([
    frame({ jsonrpc: '2.0', id: 1, method: 'initialize', params: {} }),
    frame({ jsonrpc: '2.0', method: 'textDocument/didOpen', params: { textDocument: { uri: 'file:///test.slg', languageId: 'sollang', version: 1, text: source } } }),
    frame({ jsonrpc: '2.0', id: 2, method: 'textDocument/formatting', params: { textDocument: { uri: 'file:///test.slg' }, options: { tabSize: 4, insertSpaces: true } } }),
    frame({ jsonrpc: '2.0', method: 'textDocument/didChange', params: { textDocument: { uri: 'file:///test.slg', version: 2 }, contentChanges: [{ text: 'main {\n' }] } }),
    frame({ jsonrpc: '2.0', id: 3, method: 'shutdown', params: null }),
    frame({ jsonrpc: '2.0', method: 'exit', params: null })
]);
const lsp = await run(['language-server'], lspInput);
requireValue(lsp.code === 0, lsp.stderr || `language server exited ${lsp.code}`);
const messages = decodeFrames(Buffer.from(lsp.stdout, 'utf8'));
const initialize = messages.find(message => message.id === 1)?.result;
requireValue(initialize?.capabilities?.documentFormattingProvider === true, 'initialize omitted formatting capability');
const formatting = messages.find(message => message.id === 2)?.result;
requireValue(formatting?.length === 1 && applyEdit(source, formatting[0]) === expected, 'LSP formatting result did not match CLI formatting');
const diagnostics = messages.filter(message => message.method === 'textDocument/publishDiagnostics');
requireValue(diagnostics.some(message => message.params.diagnostics.some(diagnostic => diagnostic.message.includes('parse error at'))), 'LSP omitted parser diagnostic');
requireValue(messages.some(message => message.id === 3 && message.result === null), 'LSP shutdown response missing');
console.log(`[4/4] 100.0% LSP framing, formatting, diagnostics, and shutdown (${Date.now() - started} ms)`);
