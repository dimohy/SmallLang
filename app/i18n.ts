export const supportedLocales = ["ko", "en", "ja", "zh"] as const;

export type Locale = typeof supportedLocales[number];

export function detectLocale(languages: readonly string[] = []): Locale {
  for (const language of languages) {
    const normalized = language.toLowerCase();
    if (normalized === "ko" || normalized.startsWith("ko-")) return "ko";
    if (normalized === "ja" || normalized.startsWith("ja-")) return "ja";
    if (normalized === "zh" || normalized.startsWith("zh-")) return "zh";
    if (normalized === "en" || normalized.startsWith("en-")) return "en";
  }
  return "en";
}

export const copy = {
  ko: {
    homeLabel: "Sollang 홈",
    wasmLoading: "WASM 로딩 중",
    wasmReady: "WASM 준비됨",
    wasmError: "WASM 오류",
    eyebrow: "FLOW-FIRST 언어",
    headlineFirst: "코드가 흐르는 방향으로",
    headlineSecond: "생각해 보세요.",
    introBeforeStrong: "샘플을 고르고 마음껏 수정하세요. Sollang 컴파일러가",
    introStrong: " 브라우저 안의 WebAssembly",
    introAfterStrong: "에서 코드를 검증하고 실행합니다. 소스 코드는 서버로 전송되지 않습니다.",
    sample: "예제",
    input: "입력 (stdin)",
    inputHint: "readInt가 호출될 때마다 한 줄씩 사용합니다.",
    inputPlaceholder: "프로그램 입력을 줄 단위로 입력하세요.",
    reset: "초기화",
    compiling: "컴파일 중…",
    run: "실행",
    editorLoading: "편집기 로딩 중…",
    output: "출력",
    runPrompt: "코드를 실행해 보세요",
    resultPrompt: "결과와 컴파일 진단이 여기에 표시됩니다.",
    compilingInBrowser: "브라우저에서 컴파일 중",
    compileTime: "컴파일",
    executeTime: "실행",
    totalTime: "전체",
    footer: "컴파일러: SLG Stage2 WebAssembly · 서버 전송 없음",
    loadStage2Failed: (status: number) => `Stage2 WASM을 불러오지 못했습니다 (${status})`,
    loadStdlibFailed: (status: number) => `표준 라이브러리를 불러오지 못했습니다 (${status})`,
    loadAssetFailed: (url: string, status: number) => `${url}을 불러오지 못했습니다 (${status})`,
    toolFailed: (stage: string, details?: string) =>
      `${stage}에 실패했습니다.${details ? `\n${details}` : ""}`,
    llvmStage: "LLVM 변환",
    linkStage: "WebAssembly 링크",
    unknownInterpolation: (name: string) => `알 수 없는 문자열 보간 변수 '${name}'`,
    interpolationBoundaryHint: (binding: string, suffix: string) =>
      `힌트: 변수 이름 뒤에 글자가 이어질 때는 '$(${binding})${suffix}'처럼 $(...)로 경계를 표시하세요.`,
    interpolationHint: "힌트: 문자열 보간의 변수 이름을 확인하고, 뒤에 글자가 이어지면 $(name) 형태로 경계를 표시하세요.",
    unknownCall: (name: string) => `알 수 없는 함수 호출 '${name}'`,
    callHint: "힌트: 함수 호출은 '-> 함수이름', 값 바인딩은 '=> 이름'을 사용합니다."
  },
  en: {
    homeLabel: "Sollang home",
    wasmLoading: "Loading WASM",
    wasmReady: "WASM ready",
    wasmError: "WASM error",
    eyebrow: "FLOW-FIRST LANGUAGE",
    headlineFirst: "Think in the direction",
    headlineSecond: "your code flows.",
    introBeforeStrong: "Choose a sample and make it your own. The Sollang compiler validates and runs it in",
    introStrong: " WebAssembly inside your browser",
    introAfterStrong: ". Your source code never leaves the device.",
    sample: "Sample",
    input: "Input (stdin)",
    inputHint: "Each readInt call consumes one line.",
    inputPlaceholder: "Enter program input, one value per line.",
    reset: "Reset",
    compiling: "Compiling…",
    run: "Run",
    editorLoading: "Loading editor…",
    output: "Output",
    runPrompt: "Run the code",
    resultPrompt: "Program output and compiler diagnostics appear here.",
    compilingInBrowser: "Compiling in your browser",
    compileTime: "Compile",
    executeTime: "Run",
    totalTime: "Total",
    footer: "Compiler: SLG Stage2 WebAssembly · No server round-trip",
    loadStage2Failed: (status: number) => `Failed to load Stage2 WASM (${status})`,
    loadStdlibFailed: (status: number) => `Failed to load the standard library (${status})`,
    loadAssetFailed: (url: string, status: number) => `Failed to load ${url} (${status})`,
    toolFailed: (stage: string, details?: string) =>
      `${stage} failed.${details ? `\n${details}` : ""}`,
    llvmStage: "LLVM lowering",
    linkStage: "WebAssembly linking",
    unknownInterpolation: (name: string) => `Unknown interpolation binding '${name}'`,
    interpolationBoundaryHint: (binding: string, suffix: string) =>
      `Hint: when text follows a binding name, mark its boundary as '$(${binding})${suffix}'.`,
    interpolationHint: "Hint: check the interpolation binding name and use $(name) when text follows it.",
    unknownCall: (name: string) => `Unknown function call '${name}'`,
    callHint: "Hint: use '-> functionName' for a call and '=> name' for a value binding."
  },
  ja: {
    homeLabel: "Sollang ホーム",
    wasmLoading: "WASM 読み込み中",
    wasmReady: "WASM 準備完了",
    wasmError: "WASM エラー",
    eyebrow: "FLOW-FIRST 言語",
    headlineFirst: "コードが流れる方向に",
    headlineSecond: "考えてみましょう。",
    introBeforeStrong: "サンプルを選んで自由に編集してください。Sollang コンパイラが",
    introStrong: "ブラウザ内の WebAssembly",
    introAfterStrong: "で検証・実行します。ソースコードは端末の外へ送信されません。",
    sample: "サンプル",
    input: "入力 (stdin)",
    inputHint: "readInt の呼び出しごとに1行を使います。",
    inputPlaceholder: "プログラム入力を1行ずつ入力してください。",
    reset: "リセット",
    compiling: "コンパイル中…",
    run: "実行",
    editorLoading: "エディタ読み込み中…",
    output: "出力",
    runPrompt: "コードを実行してください",
    resultPrompt: "実行結果とコンパイル診断がここに表示されます。",
    compilingInBrowser: "ブラウザでコンパイル中",
    compileTime: "コンパイル",
    executeTime: "実行",
    totalTime: "合計",
    footer: "コンパイラ: SLG Stage2 WebAssembly · サーバー送信なし",
    loadStage2Failed: (status: number) => `Stage2 WASM の読み込みに失敗しました (${status})`,
    loadStdlibFailed: (status: number) => `標準ライブラリの読み込みに失敗しました (${status})`,
    loadAssetFailed: (url: string, status: number) => `${url} の読み込みに失敗しました (${status})`,
    toolFailed: (stage: string, details?: string) =>
      `${stage}に失敗しました。${details ? `\n${details}` : ""}`,
    llvmStage: "LLVM 変換",
    linkStage: "WebAssembly リンク",
    unknownInterpolation: (name: string) => `不明な文字列補間変数 '${name}'`,
    interpolationBoundaryHint: (binding: string, suffix: string) =>
      `ヒント: 変数名の直後に文字が続く場合は '$(${binding})${suffix}' のように境界を示してください。`,
    interpolationHint: "ヒント: 補間変数名を確認し、後ろに文字が続く場合は $(name) を使用してください。",
    unknownCall: (name: string) => `不明な関数呼び出し '${name}'`,
    callHint: "ヒント: 関数呼び出しには '-> 関数名'、値の束縛には '=> 名前' を使用します。"
  },
  zh: {
    homeLabel: "Sollang 首页",
    wasmLoading: "正在加载 WASM",
    wasmReady: "WASM 已就绪",
    wasmError: "WASM 错误",
    eyebrow: "FLOW-FIRST 语言",
    headlineFirst: "沿着代码流动的方向",
    headlineSecond: "思考。",
    introBeforeStrong: "选择示例并自由修改。Sollang 编译器会在",
    introStrong: "浏览器内的 WebAssembly",
    introAfterStrong: "中验证并运行代码。源代码不会离开你的设备。",
    sample: "示例",
    input: "输入 (stdin)",
    inputHint: "每次调用 readInt 都会消费一行。",
    inputPlaceholder: "请逐行输入程序数据。",
    reset: "重置",
    compiling: "编译中…",
    run: "运行",
    editorLoading: "正在加载编辑器…",
    output: "输出",
    runPrompt: "运行代码",
    resultPrompt: "程序输出和编译诊断会显示在这里。",
    compilingInBrowser: "正在浏览器中编译",
    compileTime: "编译",
    executeTime: "运行",
    totalTime: "总计",
    footer: "编译器：SLG Stage2 WebAssembly · 不经过服务器",
    loadStage2Failed: (status: number) => `无法加载 Stage2 WASM (${status})`,
    loadStdlibFailed: (status: number) => `无法加载标准库 (${status})`,
    loadAssetFailed: (url: string, status: number) => `无法加载 ${url} (${status})`,
    toolFailed: (stage: string, details?: string) =>
      `${stage}失败。${details ? `\n${details}` : ""}`,
    llvmStage: "LLVM 转换",
    linkStage: "WebAssembly 链接",
    unknownInterpolation: (name: string) => `未知的字符串插值变量 '${name}'`,
    interpolationBoundaryHint: (binding: string, suffix: string) =>
      `提示：变量名后紧跟文字时，请写成 '$(${binding})${suffix}' 以标明边界。`,
    interpolationHint: "提示：请检查插值变量名；后面紧跟文字时使用 $(name)。",
    unknownCall: (name: string) => `未知的函数调用 '${name}'`,
    callHint: "提示：使用 '-> 函数名' 调用函数，使用 '=> 名称' 绑定值。"
  }
} satisfies Record<Locale, Record<string, unknown>>;

export type UiCopy = typeof copy.en;
