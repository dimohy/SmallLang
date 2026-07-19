const fs = require("fs");
const path = require("path");
const textmate = require("vscode-textmate");
const oniguruma = require("vscode-oniguruma");

const extensionRoot = path.resolve(__dirname, "..");
const repositoryRoot = path.resolve(extensionRoot, "..", "..");
const grammarPath = path.join(extensionRoot, "syntaxes", "sollang.tmLanguage.json");
const wasmPath = require.resolve("vscode-oniguruma/release/onig.wasm");

function collectFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) return collectFiles(fullPath);
    return entry.isFile() && entry.name.endsWith(".slg") ? [fullPath] : [];
  });
}

function scopesFor(tokens, line, text) {
  const start = line.indexOf(text);
  if (start < 0) throw new Error(`sample text not found: ${text}`);
  const token = tokens.find((candidate) => candidate.startIndex <= start && candidate.endIndex > start);
  if (!token) throw new Error(`token not found: ${text}`);
  return token.scopes;
}

async function main() {
  const wasm = fs.readFileSync(wasmPath);
  await oniguruma.loadWASM(wasm.buffer.slice(wasm.byteOffset, wasm.byteOffset + wasm.byteLength));
  const onigLib = Promise.resolve({
    createOnigScanner: (sources) => new oniguruma.OnigScanner(sources),
    createOnigString: (value) => new oniguruma.OnigString(value)
  });
  const registry = new textmate.Registry({
    onigLib,
    loadGrammar: async (scopeName) => scopeName === "source.sollang"
      ? textmate.parseRawGrammar(fs.readFileSync(grammarPath, "utf8"), grammarPath)
      : null
  });
  const grammar = await registry.loadGrammar("source.sollang");
  if (!grammar) throw new Error("Sollang grammar did not load");

  const assertions = [
    ["task -> await => result", "await", "keyword.control.async.sollang"],
    ["task -> cancel", "cancel", "keyword.control.async.sollang"],
    ["Result<File, Text>.Ok(reader)", "File", "entity.name.type.nominal.sollang"],
    ["inner! == 2 -> if continue", "continue", "keyword.control.loop.sollang"],
    ["import sollang.compiler.lexer", "sollang.compiler.lexer", "entity.name.namespace.sollang"],
    ["self -> inspect", "self", "variable.language.special.sollang"]
  ];
  for (const [line, text, expectedScope] of assertions) {
    const result = grammar.tokenizeLine(line);
    const scopes = scopesFor(result.tokens, line, text);
    if (!scopes.includes(expectedScope)) {
      throw new Error(`${JSON.stringify(text)} scopes ${scopes.join(" ")} do not include ${expectedScope}`);
    }
  }

  const roots = ["examples", "selfhost", "stdlib"].map((name) => path.join(repositoryRoot, name));
  const files = roots.flatMap(collectFiles);
  let lines = 0;
  let tokens = 0;
  for (const file of files) {
    let ruleStack = textmate.INITIAL;
    for (const line of fs.readFileSync(file, "utf8").split(/\r?\n/)) {
      const result = grammar.tokenizeLine(line, ruleStack);
      ruleStack = result.ruleStack;
      lines += 1;
      tokens += result.tokens.length;
    }
  }
  process.stdout.write(`textmate ok files=${files.length} lines=${lines} tokens=${tokens}\n`);
}

main().catch((error) => {
  console.error(error.stack || error);
  process.exitCode = 1;
});
