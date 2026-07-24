import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const wasmPath = process.argv[2] ?? path.join(repoRoot, "artifacts", "sollangc-browser.wasm");
const sourcePath = process.argv[3] ?? path.join(repoRoot, "examples", "23-webassembly-browser.slg");
const outputPath = process.argv[4] ?? path.join(repoRoot, "artifacts", "browser-stage2-output.ll");
const expectedDiagnostic = process.argv[5];
const wasmBytes = fs.readFileSync(wasmPath);
const sourceBytes = fs.readFileSync(sourcePath);
const stdlibRoot = path.join(repoRoot, "stdlib");
const stdlibPaths = [];
function collectStdlib(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) collectStdlib(fullPath);
    else if (entry.isFile() && entry.name.endsWith(".slg")) stdlibPaths.push(fullPath);
  }
}
collectStdlib(stdlibRoot);
stdlibPaths.sort();
const stdlib = stdlibPaths.map(file => {
  const source = fs.readFileSync(file, "utf8");
  return {
    namespace: source.match(/^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)/m)?.[1] ?? "",
    source: Buffer.from(source)
  };
});
function importedNamespaces(source) {
  return [...source.toString("utf8").matchAll(/^\s*import\s+([A-Za-z_][A-Za-z0-9_.]*)/gm)]
    .map(match => match[1]);
}
const stdlibByNamespace = new Map(stdlib.map(entry => [entry.namespace, entry]));
const selectedStdlib = [];
const visitedNamespaces = new Set();
const pendingNamespaces = importedNamespaces(sourceBytes);
while (pendingNamespaces.length > 0) {
  const namespace = pendingNamespaces.shift();
  if (visitedNamespaces.has(namespace)) continue;
  visitedNamespaces.add(namespace);
  const entry = stdlibByNamespace.get(namespace);
  if (!entry) continue;
  selectedStdlib.push(entry.source);
  pendingNamespaces.push(...importedNamespaces(entry.source));
}
const sourceBuffers = [sourceBytes, ...selectedStdlib];
const decoder = new TextDecoder();
const outputChunks = [];

let memory;
let heapCursor = 0;
const sourcePointers = [];
const allocationSizes = new Map();

function sizeClass(byteLength) {
  const requested = Math.max(Number(byteLength), 1);
  let blockSize = 16;
  while (blockSize < requested) blockSize *= 2;
  return blockSize;
}

function allocate(byteLength) {
  const requested = Number(byteLength) === 0 ? 1024 : sizeClass(byteLength);
  const aligned = Math.ceil(heapCursor / 16) * 16;
  const end = aligned + requested;
  const available = memory.buffer.byteLength;
  if (end > available) {
    memory.grow(Math.ceil((end - available) / 65536));
  }
  heapCursor = end;
  allocationSizes.set(aligned, requested);
  return aligned;
}

function reallocate(oldPointer, byteLength) {
  const normalizedOldPointer = oldPointer >>> 0;
  const requested = sizeClass(byteLength);
  const oldLength = allocationSizes.get(normalizedOldPointer) ?? 0;
  if (normalizedOldPointer !== 0 && oldLength >= requested) {
    return normalizedOldPointer;
  }
  const newPointer = allocate(requested);
  const copyLength = Math.min(oldLength, Number(byteLength));
  if (normalizedOldPointer !== 0 && copyLength > 0) {
    new Uint8Array(memory.buffer, newPointer, copyLength)
      .set(new Uint8Array(memory.buffer, normalizedOldPointer, copyLength));
  }
  return newPointer;
}

const imports = {
  env: {
    sollang_browser_alloc(bytes) {
      return allocate(bytes);
    },
    sollang_browser_realloc(pointer, bytes) {
      return reallocate(pointer, bytes);
    },
    sollang_browser_now_millis() {
      return BigInt(Date.now());
    },
    sollang_browser_source_count() {
      return sourceBuffers.length;
    },
    sollang_browser_source_pointer(index) {
      return sourcePointers[index];
    },
    sollang_browser_source_length(index) {
      return sourceBuffers[index].byteLength;
    },
    sollang_browser_read() {
      return 0;
    },
    sollang_browser_write(pointer, length) {
      outputChunks.push(new Uint8Array(memory.buffer.slice(pointer, pointer + length)));
      return 1;
    },
    sollang_browser_panic(pointer, length) {
      console.error(`PANIC: ${decoder.decode(new Uint8Array(memory.buffer, pointer, length))}`);
    }
  }
};

const { instance } = await WebAssembly.instantiate(wasmBytes, imports);
memory = instance.exports.memory;
heapCursor = memory.buffer.byteLength;
for (const buffer of sourceBuffers) {
  const pointer = instance.exports.sollang_alloc(BigInt(buffer.byteLength));
  sourcePointers.push(pointer);
  new Uint8Array(memory.buffer, pointer, buffer.byteLength).set(buffer);
}

let exitCode;
try {
  exitCode = instance.exports.sollang_start();
} catch (error) {
  const partial = outputChunks.map(chunk => decoder.decode(chunk, { stream: true })).join("");
  console.error(`PARTIAL OUTPUT:\n${partial.slice(-2000)}`);
  console.error(`ALLOCATOR: ${allocationSizes.size} live allocations, ${heapCursor} heap bytes`);
  throw error;
}
const output = outputChunks.map(chunk => decoder.decode(chunk, { stream: true })).join("")
  + decoder.decode();
fs.writeFileSync(outputPath, output);

if (expectedDiagnostic) {
  if (!output.includes(expectedDiagnostic) || output.includes('target triple = "wasm32-unknown-unknown-wasm"')) {
    throw new Error(
      `Stage2 browser compiler diagnostic mismatch: expected=${JSON.stringify(expectedDiagnostic)}, `
      + `exit=${exitCode}, output=${output.slice(0, 800)}`
    );
  }
  console.log(`PASS Stage2 browser diagnostic: ${expectedDiagnostic}`);
  process.exit(0);
}

if (exitCode !== 0 || !output.includes('target triple = "wasm32-unknown-unknown-wasm"')) {
  throw new Error(`Stage2 browser compiler failed: exit=${exitCode}, output=${output.slice(0, 400)}`);
}

console.log(
  `PASS Stage2 browser compiler: ${sourceBytes.byteLength} source bytes -> ${output.length} LLVM characters; `
  + `${allocationSizes.size} allocations, ${heapCursor} heap bytes`
);
