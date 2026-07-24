import { chromium } from "playwright-core";
import { mkdir } from "node:fs/promises";

const baseUrl = process.env.SOLLANG_PLAYGROUND_URL ?? "http://127.0.0.1:3210";
const browser = await chromium.launch({
  executablePath: "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
  headless: true
});

async function openLocalizedPage(locale, readyText) {
  const context = await browser.newContext({
    locale,
    viewport: { width: 1440, height: 1080 }
  });
  const page = await context.newPage();
  page.on("console", message => console.log(`[browser:${message.type()}] ${message.text()}`));
  page.on("pageerror", error => console.error(`[browser:error] ${error.stack ?? error.message}`));
  page.on("response", response => {
    if (!response.ok()) console.error(`[browser:http] ${response.status()} ${response.url()}`);
  });
  await page.goto(baseUrl, { waitUntil: "networkidle" });
  await page.getByText(readyText).waitFor({ timeout: 60_000 });
  return { context, page };
}

try {
  const { context: englishContext, page } = await openLocalizedPage("en-US", "WASM ready");
  if (await page.locator("html").getAttribute("lang") !== "en") {
    throw new Error("English browser locale did not set html[lang=en].");
  }

  const sampleIds = await page.locator("#sample option").evaluateAll(
    options => options.map(option => option.value)
  );
  const expectedSampleIds = [
    "hello", "main-block", "arithmetic", "input", "flow", "local-functions",
    "loop", "when", "each-repeat", "custom-block", "fold", "containers",
    "immutable-containers", "compile-time-collections", "struct",
    "mutable-method", "enum", "traits-generics", "numeric-widths",
    "associated-types", "value-generics", "result-propagation", "async-await",
    "dynamic-trait", "effects", "readonly-references", "ownership",
    "raw-strings", "sensor-stream", "nested-stream", "risk-stream"
  ];
  if (JSON.stringify(sampleIds) !== JSON.stringify(expectedSampleIds)) {
    throw new Error(
      `syntax catalog mismatch\nexpected ${JSON.stringify(expectedSampleIds)}\nactual   ${JSON.stringify(sampleIds)}`
    );
  }
  const whileTokens = await page.locator(".monaco-editor").evaluate(() =>
    window.monaco.editor.tokenize("count! < 8 -> while {", "sollang")[0]
  );
  if (!whileTokens.some(token => token.offset === 14 && token.type.includes("keyword"))) {
    throw new Error(`while was not highlighted as a keyword: ${JSON.stringify(whileTokens)}`);
  }
  await page.locator("#sample").selectOption("loop");
  await page.screenshot({
    path: "artifacts/browser/playground-while-keyword.png",
    fullPage: true
  });

  const sampleFailures = [];
  let previousSource = await page.locator(".monaco-editor").evaluate(() =>
    window.monaco.editor.getModels()[0]?.getValue() ?? ""
  );
  for (const sampleId of sampleIds) {
    await page.locator("#sample").selectOption(sampleId);
    await page.waitForFunction(
      source => window.monaco.editor.getModels()[0]?.getValue() !== source,
      previousSource
    );
    const source = await page.locator(".monaco-editor").evaluate(() => {
      const monacoApi = window.monaco;
      return monacoApi?.editor.getModels()[0]?.getValue() ?? "";
    });
    previousSource = source;
    if (!source || /[가-힣]/u.test(source)) {
      throw new Error(`${sampleId} source is missing or is not English-only.`);
    }

    await page.getByRole("button", { name: /^Run/ }).click();
    await page.locator(".result-ok, .result-error").waitFor({ timeout: 120_000 });
    if (await page.locator(".result-error").isVisible()) {
      sampleFailures.push(
        `${sampleId}: ${(await page.locator(".terminal").innerText()).replace(/\s+/g, " ").trim()}`
      );
    } else if (!(await page.locator(".terminal pre").innerText()).trim()) {
      sampleFailures.push(`${sampleId}: execution succeeded without observable output`);
    }
  }
  if (sampleFailures.length > 0) {
    throw new Error(`browser sample failures:\n${sampleFailures.join("\n")}`);
  }

  await page.locator("#sample").selectOption("input");
  if (await page.locator("#stdin").inputValue() !== "12\n30") {
    throw new Error("Input sample did not populate stdin.");
  }
  await page.getByRole("button", { name: /^Run/ }).click();
  await page.getByText("Sum = 42", { exact: false }).waitFor({ timeout: 120_000 });

  await page.locator("#sample").selectOption("hello");
  await page.locator(".monaco-editor .view-lines").click();
  await page.keyboard.press(process.platform === "darwin" ? "Meta+A" : "Control+A");
  await page.keyboard.insertText('main {\n    "Browser edit succeeded." -> println\n}');
  await page.getByRole("button", { name: /^Run/ }).click();
  await page.getByText("Browser edit succeeded.", { exact: true }).waitFor();

  await page.locator(".monaco-editor .view-lines").click();
  await page.keyboard.press(process.platform === "darwin" ? "Meta+A" : "Control+A");
  await page.keyboard.insertText(
    'main {\n'
    + '    "dimohy" => dimohy\n'
    + '    "$dimohySuffix" -> println\n'
    + '}'
  );
  await page.getByRole("button", { name: /^Run/ }).click();
  await page.locator(".result-error").waitFor({ timeout: 120_000 });
  const interpolationDiagnostic = await page.locator(".terminal pre").innerText();
  if (
    !interpolationDiagnostic.includes("Unknown interpolation binding 'dimohySuffix'")
    || !interpolationDiagnostic.includes("'$(dimohy)Suffix'")
    || interpolationDiagnostic.includes("FS error")
  ) {
    throw new Error(`unfriendly English interpolation diagnostic: ${interpolationDiagnostic}`);
  }

  await page.locator(".monaco-editor .view-lines").click();
  await page.keyboard.press(process.platform === "darwin" ? "Meta+A" : "Control+A");
  await page.keyboard.insertText(
    'main {\n'
    + '    "Values flow from left to right." -> println2\n'
    + '}'
  );
  await page.getByRole("button", { name: /^Run/ }).click();
  await page.locator(".result-error").waitFor({ timeout: 120_000 });
  const unresolvedCallDiagnostic = await page.locator(".terminal pre").innerText();
  if (
    !unresolvedCallDiagnostic.includes("Unknown function call 'println2'")
    || unresolvedCallDiagnostic.includes("FS error")
  ) {
    throw new Error(`missing English unresolved-call diagnostic: ${unresolvedCallDiagnostic}`);
  }

  const tokenColors = await page.locator(".view-lines span[class*='mtk']").evaluateAll(
    nodes => new Set(nodes.map(node => getComputedStyle(node).color)).size
  );
  if (tokenColors < 3) {
    throw new Error(`expected syntax highlighting colors, got ${tokenColors}`);
  }

  await mkdir("artifacts/browser", { recursive: true });
  await page.screenshot({ path: "artifacts/browser/playground-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: "artifacts/browser/playground-mobile.png", fullPage: true });
  await englishContext.close();

  for (const [locale, readyText, htmlLang, localizedLabel, localizedSampleTitle] of [
    ["ko-KR", "WASM 준비됨", "ko", "입력 (stdin)", "인사와 문자열 보간"],
    ["ja-JP", "WASM 準備完了", "ja", "入力 (stdin)", "挨拶と文字列補間"],
    ["zh-CN", "WASM 已就绪", "zh", "输入 (stdin)", "问候与字符串插值"]
  ]) {
    const { context, page: localizedPage } = await openLocalizedPage(locale, readyText);
    if (await localizedPage.locator("html").getAttribute("lang") !== htmlLang) {
      throw new Error(`${locale} did not set html[lang=${htmlLang}].`);
    }
    await localizedPage.getByText(localizedLabel, { exact: true }).waitFor();
    if (await localizedPage.locator("#sample option:checked").innerText() !== localizedSampleTitle) {
      throw new Error(`${locale} did not localize sample titles.`);
    }
    await context.close();
  }

  console.log(`PASS browser playground (${sampleIds.length} samples, 4 locales, ${tokenColors} syntax colors)`);
} finally {
  await browser.close();
}
