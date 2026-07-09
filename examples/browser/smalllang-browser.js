const statusEl = document.querySelector("#status");
const outputEl = document.querySelector("#output");
const runButton = document.querySelector("#run");
const decoder = new TextDecoder("utf-8");

const params = new URLSearchParams(window.location.search);
const wasmUrl = params.get("wasm") ?? "../../artifacts/23-webassembly-browser.wasm";

async function runSmallLang() {
  runButton.disabled = true;
  statusEl.textContent = "Loading";
  outputEl.textContent = "";

  let memory;
  let output = "";
  const imports = {
    env: {
      smalllang_browser_write(ptr, len) {
        if (!(memory instanceof WebAssembly.Memory)) {
          return 0;
        }

        const bytes = new Uint8Array(memory.buffer, ptr, len);
        output += decoder.decode(bytes);
        outputEl.textContent = output;
        return 1;
      },
      smalllang_browser_now_millis() {
        return BigInt(Math.trunc(performance.now()));
      }
    }
  };

  try {
    const response = await fetch(wasmUrl);
    if (!response.ok) {
      throw new Error(`Could not fetch ${wasmUrl}: ${response.status}`);
    }

    const bytes = await response.arrayBuffer();
    const { instance } = await WebAssembly.instantiate(bytes, imports);
    memory = instance.exports.memory;

    if (!(memory instanceof WebAssembly.Memory)) {
      throw new Error("The WebAssembly module did not export memory.");
    }

    const exitCode = instance.exports.smalllang_start();
    statusEl.textContent = exitCode === 0 ? "Exited 0" : `Exited ${exitCode}`;
  } catch (error) {
    statusEl.textContent = "Failed";
    outputEl.textContent = error instanceof Error ? error.message : String(error);
  } finally {
    runButton.disabled = false;
  }
}

runButton.addEventListener("click", runSmallLang);
runSmallLang();
