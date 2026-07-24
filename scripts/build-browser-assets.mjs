import { access, mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import path from "node:path";

const repoRoot = path.resolve(import.meta.dirname, "..");
const stdlibRoot = path.join(repoRoot, "stdlib");
const publicRoot = path.join(repoRoot, "public");
const compilerDestination = path.join(publicRoot, "sollangc-stage2-0.2.260725.wasm");

async function slgFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const nested = await Promise.all(entries.map(async entry => {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) return slgFiles(fullPath);
    return entry.isFile() && entry.name.endsWith(".slg") ? [fullPath] : [];
  }));
  return nested.flat();
}

const paths = (await slgFiles(stdlibRoot)).sort((left, right) =>
  left.localeCompare(right, "en")
);
const sources = await Promise.all(paths.map(async sourcePath => ({
  namespace: (await readFile(sourcePath, "utf8"))
    .match(/^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)/m)?.[1] ?? "",
  source: await readFile(sourcePath, "utf8")
})));

await mkdir(publicRoot, { recursive: true });
await writeFile(
  path.join(publicRoot, "stdlib-0.2.260725.json"),
  JSON.stringify(sources),
  "utf8"
);
await access(compilerDestination);

console.log(`Verified Stage2 WASM and prepared ${sources.length} standard-library sources.`);
