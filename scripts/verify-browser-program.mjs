import fs from "node:fs";

const wasmPath = process.argv[2];
const expectedPath = process.argv[3];
const inputPath = process.argv[4];
if (!wasmPath || !expectedPath) {
  throw new Error("usage: verify-browser-program.mjs <program.wasm> <expected.txt> [input.txt]");
}

const decoder = new TextDecoder();
const chunks = [];
const encoder = new TextEncoder();
const inputLines = inputPath
  ? fs.readFileSync(inputPath, "utf8").replace(/\r\n?/g, "\n").split("\n")
  : [];
let inputLine = 0;
let memory;
let heapCursor = 0;

function allocate(rawLength) {
  const length = Math.max(Number(rawLength), 1);
  const pointer = Math.ceil(heapCursor / 16) * 16;
  const end = pointer + length;
  if (end > memory.buffer.byteLength) {
    memory.grow(Math.ceil((end - memory.buffer.byteLength) / 65536));
  }
  heapCursor = end;
  return pointer;
}

const { instance } = await WebAssembly.instantiate(fs.readFileSync(wasmPath), {
  env: {
    sollang_browser_alloc: allocate,
    sollang_browser_realloc: (_pointer, length) => allocate(length),
    sollang_browser_now_millis: () => BigInt(Date.now()),
    sollang_browser_source_count: () => 0,
    sollang_browser_source_pointer: () => 0,
    sollang_browser_source_length: () => 0,
    sollang_browser_read(pointer, capacity) {
      if (inputLine >= inputLines.length || capacity <= 0) return 0;
      const line = encoder.encode(`${inputLines[inputLine++]}\n`);
      const length = Math.min(line.byteLength, capacity);
      new Uint8Array(memory.buffer, pointer, length).set(line.subarray(0, length));
      return length;
    },
    sollang_browser_write(pointer, length) {
      chunks.push(new Uint8Array(memory.buffer.slice(pointer, pointer + length)));
      return 1;
    }
  }
});

memory = instance.exports.memory;
heapCursor = memory.buffer.byteLength;
const exitCode = instance.exports.sollang_start();
const actual = chunks.map(chunk => decoder.decode(chunk, { stream: true })).join("")
  + decoder.decode();
const expected = fs.readFileSync(expectedPath, "utf8").replaceAll("\r\n", "\n");
const normalizedActual = actual.replaceAll("\r\n", "\n").replace(/\n+$/, "");
const normalizedExpected = expected.replace(/\n+$/, "");

if (exitCode !== 0 || normalizedActual !== normalizedExpected) {
  throw new Error(
    `browser program mismatch: exit=${exitCode}\n`
    + `expected:\n${expected}\nactual:\n${actual}`
  );
}

console.log(`PASS browser program: ${actual.length} output characters`);
