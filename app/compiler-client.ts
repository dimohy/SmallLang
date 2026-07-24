import { copy, type Locale } from "./i18n";

export type CompilerResult = {
  success: boolean;
  output: string;
  diagnostics: string;
  compileMilliseconds: number;
  executeMilliseconds: number;
};

const decoder = new TextDecoder("utf-8");
const encoder = new TextEncoder();
const basePath = process.env.NEXT_PUBLIC_BASE_PATH ?? "";
const assetUrl = (path: string) => `${basePath}${path}`;
let stage2BytesPromise: Promise<ArrayBuffer> | undefined;
type StandardLibrarySource = {
  namespace: string;
  source: string;
};

let standardLibraryPromise: Promise<StandardLibrarySource[]> | undefined;
const toolBinaryPromises = new Map<string, Promise<ArrayBuffer>>();
const llvmAssetRoot =
  "https://raw.githubusercontent.com/dimohy/Sollang/0db4acd36d9a1204afed2b67ba74035653d13485/public/llvm-16";

type ToolModule = {
  FS: {
    writeFile(path: string, data: string | Uint8Array): void;
    readFile(path: string): Uint8Array;
  };
  callMain(arguments_: string[]): Promise<void> | void;
};

type ToolFactory = (options?: Record<string, unknown>) => Promise<ToolModule>;
const importToolFactory = (url: string) =>
  (new Function("url", "return import(url)") as (value: string) => Promise<{ default: ToolFactory }>)(url);

export function preloadStage2(locale: Locale = "en"): Promise<ArrayBuffer> {
  stage2BytesPromise ??= fetch(assetUrl("/sollangc-stage2-0.2.260725.wasm")).then(response => {
    if (!response.ok) {
      throw new Error(copy[locale].loadStage2Failed(response.status));
    }
    return response.arrayBuffer();
  });
  return stage2BytesPromise;
}

function loadStandardLibrary(locale: Locale): Promise<StandardLibrarySource[]> {
  standardLibraryPromise ??= fetch(assetUrl("/stdlib-0.2.260725.json")).then(async response => {
    if (!response.ok) {
      throw new Error(copy[locale].loadStdlibFailed(response.status));
    }
    return await response.json() as StandardLibrarySource[];
  });
  return standardLibraryPromise;
}

function importedNamespaces(source: string): string[] {
  return [...source.matchAll(/^\s*import\s+([A-Za-z_][A-Za-z0-9_.]*)/gm)]
    .map(match => match[1]);
}

function selectStandardLibrary(
  source: string,
  library: StandardLibrarySource[]
): StandardLibrarySource[] {
  const byNamespace = new Map(library.map(entry => [entry.namespace, entry]));
  const selected: StandardLibrarySource[] = [];
  const visited = new Set<string>();
  const pending = importedNamespaces(source);
  while (pending.length > 0) {
    const namespace = pending.shift()!;
    if (visited.has(namespace)) continue;
    visited.add(namespace);
    const entry = byNamespace.get(namespace);
    if (!entry) continue;
    selected.push(entry);
    pending.push(...importedNamespaces(entry.source));
  }
  return selected;
}

function loadToolBinary(url: string, locale: Locale): Promise<ArrayBuffer> {
  let pending = toolBinaryPromises.get(url);
  if (!pending) {
    pending = fetch(url).then(response => {
      if (!response.ok) throw new Error(copy[locale].loadAssetFailed(url, response.status));
      return response.arrayBuffer();
    });
    toolBinaryPromises.set(url, pending);
  }
  return pending;
}

async function compileWithStage2(source: string, locale: Locale): Promise<string> {
  const library = selectStandardLibrary(source, await loadStandardLibrary(locale));
  const sourceBuffers = [source, ...library.map(entry => entry.source)]
    .map(value => encoder.encode(value));
  const outputChunks: Uint8Array[] = [];
  let memory: WebAssembly.Memory;
  let heapCursor = 0;
  const sourcePointers: number[] = [];
  const allocationSizes = new Map<number, number>();

  const sizeClass = (rawLength: bigint | number) => {
    const requested = Math.max(Number(rawLength), 1);
    let blockSize = 16;
    while (blockSize < requested) blockSize *= 2;
    return blockSize;
  };

  const allocate = (rawLength: bigint | number) => {
    const byteLength = Number(rawLength) === 0 ? 1024 : sizeClass(rawLength);
    const pointer = Math.ceil(heapCursor / 16) * 16;
    const end = pointer + byteLength;
    if (end > memory.buffer.byteLength) {
      memory.grow(Math.ceil((end - memory.buffer.byteLength) / 65536));
    }
    heapCursor = end;
    allocationSizes.set(pointer, byteLength);
    return pointer;
  };

  const reallocate = (oldPointer: number, rawLength: bigint | number) => {
    const normalizedOldPointer = oldPointer >>> 0;
    const oldLength = allocationSizes.get(normalizedOldPointer) ?? 0;
    const newLength = sizeClass(rawLength);
    if (normalizedOldPointer !== 0 && oldLength >= newLength) {
      return normalizedOldPointer;
    }
    const newPointer = allocate(newLength);
    const copyLength = Math.min(oldLength, Number(rawLength));
    if (normalizedOldPointer !== 0 && copyLength > 0) {
      new Uint8Array(memory.buffer, newPointer, copyLength)
        .set(new Uint8Array(memory.buffer, normalizedOldPointer, copyLength));
    }
    return newPointer;
  };

  const imports = {
    env: {
      sollang_browser_alloc: allocate,
      sollang_browser_realloc: reallocate,
      sollang_browser_now_millis: () => BigInt(Math.trunc(performance.now())),
      sollang_browser_source_count: () => sourceBuffers.length,
      sollang_browser_source_pointer: (index: number) => sourcePointers[index],
      sollang_browser_source_length: (index: number) => sourceBuffers[index].byteLength,
      sollang_browser_read: () => 0,
      sollang_browser_write: (pointer: number, length: number) => {
        outputChunks.push(new Uint8Array(memory.buffer.slice(pointer, pointer + length)));
        return 1;
      },
      sollang_browser_panic: (pointer: number, length: number) => {
        outputChunks.push(encoder.encode(
          `\n; sollang browser panic: ${decoder.decode(new Uint8Array(memory.buffer, pointer, length))}\n`
        ));
      }
    }
  };

  const { instance } = await WebAssembly.instantiate(await preloadStage2(locale), imports);
  memory = instance.exports.memory as WebAssembly.Memory;
  heapCursor = memory.buffer.byteLength;
  for (const buffer of sourceBuffers) {
    const pointer = Number((instance.exports.sollang_alloc as (length: bigint) => number)(
      BigInt(buffer.byteLength)
    ));
    sourcePointers.push(pointer);
    new Uint8Array(memory.buffer, pointer, buffer.byteLength).set(buffer);
  }
  const exitCode = (instance.exports.sollang_start as () => number)();
  const llvm = outputChunks.map(chunk => decoder.decode(chunk, { stream: true })).join("")
    + decoder.decode();

  if (exitCode !== 0 || !llvm.includes('target triple = "wasm32-unknown-unknown-wasm"')) {
    throw new Error(formatCompilerDiagnostic(source, llvm.trim() || `Stage2 compiler exited ${exitCode}`, locale));
  }
  return llvm;
}

async function lowerLlvmToWasm(llvm: string, locale: Locale): Promise<Uint8Array> {
  const llcModuleUrl = new URL(assetUrl("/llvm-16/llc.js"), window.location.href).href;
  const lldModuleUrl = new URL(assetUrl("/llvm-16/lld.js"), window.location.href).href;
  const [{ default: createLlc }, { default: createLld }, llcBinary, lldBinary] = await Promise.all([
    importToolFactory(llcModuleUrl),
    importToolFactory(lldModuleUrl),
    loadToolBinary(`${llvmAssetRoot}/llc.wasm`, locale),
    loadToolBinary(`${llvmAssetRoot}/lld.wasm`, locale)
  ]);

  const llcDiagnostics: string[] = [];
  const llc = await createLlc({
    noInitialRun: true,
    wasmBinary: llcBinary,
    locateFile: (file: string) => `${llvmAssetRoot}/${file}`,
    print: (message: string) => llcDiagnostics.push(message),
    printErr: (message: string) => llcDiagnostics.push(message)
  });
  llc.FS.writeFile("main.ll", llvm);
  try {
    await llc.callMain([
      "-mtriple=wasm32-unknown-unknown-wasm",
      "-filetype=obj",
      "main.ll",
      "-o",
      "main.o"
    ]);
  } catch (error) {
    throwToolError(copy[locale].llvmStage, llcDiagnostics, error, locale);
  }
  let object: Uint8Array;
  try {
    object = llc.FS.readFile("main.o");
  } catch (error) {
    throwToolError(copy[locale].llvmStage, llcDiagnostics, error, locale);
  }

  const lldDiagnostics: string[] = [];
  const lld = await createLld({
    noInitialRun: true,
    wasmBinary: lldBinary,
    locateFile: (file: string) => `${llvmAssetRoot}/${file}`,
    print: (message: string) => lldDiagnostics.push(message),
    printErr: (message: string) => lldDiagnostics.push(message)
  });
  lld.FS.writeFile("main.o", object);
  try {
    await lld.callMain([
      "-flavor",
      "wasm",
      "--no-entry",
      "--export=sollang_start",
      "--export=__heap_base",
      "--export-memory",
      "--allow-undefined",
      "--gc-sections",
      "main.o",
      "-o",
      "main.wasm"
    ]);
    return lld.FS.readFile("main.wasm");
  } catch (error) {
    throwToolError(copy[locale].linkStage, lldDiagnostics, error, locale);
  }
}

function throwToolError(
  stage: string,
  diagnostics: string[],
  error: unknown,
  locale: Locale
): never {
  const details = diagnostics.map(line => line.trim()).filter(Boolean).join("\n");
  if (details) {
    throw new Error(copy[locale].toolFailed(stage, details));
  }
  const message = error instanceof Error ? error.message : String(error);
  throw new Error(copy[locale].toolFailed(
    stage,
    message === "FS error" ? undefined : message
  ));
}

function formatCompilerDiagnostic(source: string, raw: string, locale: Locale): string {
  const text = copy[locale];
  const lines = raw.split(/\r?\n/)
    .map(line => line.replace(/^;\s?/, "").trim())
    .filter(line => line && !line.startsWith("sollang browser compilation failed"));
  const locationPattern = /\(source 0, byte (\d+), length (\d+)\)/;
  const errorLine = lines.find(line => line.includes("semantic error")) ?? lines[0] ?? raw;
  const location = errorLine.match(locationPattern);
  const prefix = location ? `${sourceLocation(source, Number(location[1]))}: ` : "";
  const unknownInterpolation = errorLine.match(/unknown interpolation binding '([^']+)'/);
  if (unknownInterpolation) {
    const unknownName = unknownInterpolation[1];
    const knownBinding = bindingNames(source)
      .filter(name => unknownName.startsWith(name) && name.length < unknownName.length)
      .sort((left, right) => right.length - left.length)[0];
    const hint = knownBinding
      ? text.interpolationBoundaryHint(knownBinding, unknownName.slice(knownBinding.length))
      : text.interpolationHint;
    return `${prefix}${text.unknownInterpolation(unknownName)}\n${hint}`;
  }
  const unresolvedCall = errorLine.match(/unresolved call target '([^']+)'/);
  if (unresolvedCall) {
    return `${prefix}${text.unknownCall(unresolvedCall[1])}\n${text.callHint}`;
  }
  return lines.join("\n") || raw;
}

function bindingNames(source: string): string[] {
  return [...source.matchAll(/=>\s*([\p{L}_][\p{L}\p{N}_]*!?)/gu)].map(match => match[1]);
}

function sourceLocation(source: string, byteOffset: number): string {
  const prefix = decoder.decode(encoder.encode(source).slice(0, byteOffset));
  const lines = prefix.split(/\r?\n/);
  return `main.slg:${lines.length}:${[...lines.at(-1)!].length + 1}`;
}

async function executeWasm(wasm: Uint8Array, input: string): Promise<string> {
  const chunks: Uint8Array[] = [];
  const inputLines = input.replace(/\r\n?/g, "\n").split("\n");
  let inputLine = 0;
  let memory: WebAssembly.Memory;
  let heapCursor = 0;
  const allocationSizes = new Map<number, number>();
  const allocationSize = (rawLength: bigint | number) => {
    const requested = Math.max(Number(rawLength), 1);
    return Math.ceil(requested / 16) * 16;
  };
  const allocate = (rawLength: bigint | number) => {
    const byteLength = allocationSize(rawLength);
    const pointer = Math.ceil(heapCursor / 16) * 16;
    const end = pointer + byteLength;
    if (end > memory.buffer.byteLength) {
      memory.grow(Math.ceil((end - memory.buffer.byteLength) / 65536));
    }
    heapCursor = end;
    allocationSizes.set(pointer, byteLength);
    return pointer;
  };
  const reallocate = (oldPointer: number, rawLength: bigint | number) => {
    const pointer = oldPointer >>> 0;
    const oldLength = allocationSizes.get(pointer) ?? 0;
    const newLength = allocationSize(rawLength);
    if (pointer !== 0 && oldLength >= newLength) return pointer;
    const replacement = allocate(newLength);
    if (pointer !== 0 && oldLength > 0) {
      new Uint8Array(memory.buffer, replacement, Math.min(oldLength, newLength))
        .set(new Uint8Array(memory.buffer, pointer, Math.min(oldLength, newLength)));
    }
    return replacement;
  };
  const write = (pointer: number, length: number) => {
    chunks.push(new Uint8Array(memory.buffer.slice(pointer, pointer + length)));
  };
  const wasmBuffer = new Uint8Array(wasm.byteLength);
  wasmBuffer.set(wasm);
  const module = await WebAssembly.compile(wasmBuffer.buffer);
  const instance = await WebAssembly.instantiate(module, {
    env: {
      sollang_write: write,
      sollang_browser_write: write,
      sollang_browser_alloc: allocate,
      sollang_browser_realloc: reallocate,
      sollang_browser_free() {},
      sollang_browser_read(pointer: number, capacity: number) {
        if (inputLine >= inputLines.length || capacity <= 0) return 0;
        const line = encoder.encode(`${inputLines[inputLine++]}\n`);
        const length = Math.min(line.byteLength, capacity);
        new Uint8Array(memory.buffer, pointer, length).set(line.subarray(0, length));
        return length;
      },
      sollang_browser_now_millis() {
        return BigInt(Math.trunc(performance.now()));
      }
    }
  });
  memory = instance.exports.memory as WebAssembly.Memory;
  const heapBase = instance.exports.__heap_base as WebAssembly.Global | undefined;
  heapCursor = heapBase ? Number(heapBase.value) : memory.buffer.byteLength;
  const exitCode = (instance.exports.sollang_start as () => number)();
  if (exitCode !== 0) {
    throw new Error(`program exited ${exitCode}`);
  }
  return chunks.map(chunk => decoder.decode(chunk, { stream: true })).join("") + decoder.decode();
}

export async function compileAndRun(
  source: string,
  locale: Locale = "en",
  input = ""
): Promise<CompilerResult> {
  const compileStarted = performance.now();
  try {
    const llvm = await compileWithStage2(source, locale);
    const wasm = await lowerLlvmToWasm(llvm, locale);
    const compileMilliseconds = performance.now() - compileStarted;
    const executeStarted = performance.now();
    const output = await executeWasm(wasm, input);
    return {
      success: true,
      output,
      diagnostics: "",
      compileMilliseconds,
      executeMilliseconds: performance.now() - executeStarted
    };
  } catch (error) {
    return {
      success: false,
      output: "",
      diagnostics: error instanceof Error ? error.message : String(error),
      compileMilliseconds: performance.now() - compileStarted,
      executeMilliseconds: 0
    };
  }
}
