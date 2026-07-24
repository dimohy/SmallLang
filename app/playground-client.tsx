"use client";

import Editor, { BeforeMount } from "@monaco-editor/react";
import {
  Check,
  ChevronDown,
  CircleAlert,
  Clock3,
  Code2,
  LoaderCircle,
  Play,
  RotateCcw,
  Sparkles,
  TerminalSquare
} from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { compileAndRun, CompilerResult, preloadStage2 } from "./compiler-client";
import { samples } from "./samples";

const basePath = process.env.NEXT_PUBLIC_BASE_PATH ?? "";

const keywords = [
  "main", "public", "namespace", "import", "as", "struct", "enum", "trait",
  "impl", "for", "where", "type", "block", "stream", "state", "stop", "each",
  "in", "if", "else", "when", "fold", "while", "break", "continue", "return",
  "move", "mut", "async", "uses", "intrinsic", "box", "ref", "dyn", "and",
  "or", "not", "true", "false"
];

export default function PlaygroundPage() {
  const [sampleId, setSampleId] = useState(samples[0].id);
  const selected = useMemo(
    () => samples.find(sample => sample.id === sampleId) ?? samples[0],
    [sampleId]
  );
  const [code, setCode] = useState(selected.code);
  const [compilerState, setCompilerState] = useState<"loading" | "ready" | "failed">("loading");
  const [isRunning, setIsRunning] = useState(false);
  const [result, setResult] = useState<CompilerResult | null>(null);

  useEffect(() => {
    preloadStage2()
      .then(() => setCompilerState("ready"))
      .catch(error => {
        setCompilerState("failed");
        setResult({
          success: false,
          output: "",
          diagnostics: error instanceof Error ? error.message : String(error),
          compileMilliseconds: 0,
          executeMilliseconds: 0
        });
      });
  }, []);

  const run = useCallback(async () => {
    if (compilerState !== "ready" || isRunning) return;
    setIsRunning(true);
    setResult(null);
    const compiled = await compileAndRun(code);
    setResult(compiled);
    setIsRunning(false);
  }, [code, compilerState, isRunning]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
        event.preventDefault();
        run();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [run]);

  const chooseSample = (id: string) => {
    const next = samples.find(sample => sample.id === id) ?? samples[0];
    setSampleId(id);
    setCode(next.code);
    setResult(null);
  };

  const beforeMount: BeforeMount = monaco => {
    monaco.languages.register({ id: "sollang" });
    monaco.languages.setMonarchTokensProvider("sollang", {
      keywords,
      typeKeywords: [
        "Int", "Int8", "Int16", "Int64", "UInt8", "UInt16", "UInt32", "UInt64",
        "Bool", "Text", "Unit", "Float32", "Float64", "CodePoint", "Range"
      ],
      tokenizer: {
        root: [
          [/#.*$/, "comment"],
          [/"""/, { token: "string.quote", next: "@multilineString" }],
          [/"/, { token: "string.quote", next: "@string" }],
          [/[A-Za-z_\u0080-\uFFFF][A-Za-z0-9_!\u0080-\uFFFF]*(?=\s*(?:<[^>\n]+>)?\s*\()/, "function.call"],
          [/(->)(\s*)([A-Za-z_\u0080-\uFFFF][A-Za-z0-9_!\u0080-\uFFFF]*)/, ["operator", "white", "function.call"]],
          [/(=>)(\s*)([A-Za-z_\u0080-\uFFFF][A-Za-z0-9_!\u0080-\uFFFF]*)/, ["operator", "white", "variable.binding"]],
          [/^(\s*)([A-Za-z_\u0080-\uFFFF][A-Za-z0-9_!\u0080-\uFFFF]*)(?=\s+(?:[A-Za-z_\u0080-\uFFFF][A-Za-z0-9_!\u0080-\uFFFF]*\s*:|:))/, ["white", "function.declaration"]],
          [/\b(struct|enum|trait|namespace|import|type)\b(\s+)([A-Za-z_\u0080-\uFFFF][A-Za-z0-9_.\u0080-\uFFFF]*)/, ["keyword", "white", "type.declaration"]],
          [/(\.)([A-Za-z_\u0080-\uFFFF][A-Za-z0-9_!\u0080-\uFFFF]*)/, ["delimiter", "property"]],
          [/[A-Z][\w]*/, "type.identifier"],
          [/[a-zA-Z_][\w]*/, {
            cases: {
              "@keywords": "keyword",
              "@typeKeywords": "type.identifier",
              "@default": "variable"
            }
          }],
          [/[^\W\d][\w\u0080-\uFFFF]*/u, "variable"],
          [/\d+/, "number"],
          [/->|=>|\.\.|==|!=|<=|>=/, "operator"],
          [/[+\-*/%=<>!]/, "operator"],
          [/[{}()[\],.:;]/, "delimiter"]
        ],
        string: [
          [/\$\(/, { token: "interpolation.delimiter", next: "@interpolation" }],
          [/\$[A-Za-z_\u0080-\uFFFF][A-Za-z0-9_!\u0080-\uFFFF]*/, "variable.interpolation"],
          [/\\./, "string.escape"],
          [/[^\\$"]+/, "string"],
          [/"/, { token: "string.quote", next: "@pop" }]
        ],
        multilineString: [
          [/"""/, { token: "string.quote", next: "@pop" }],
          [/[^"]+/, "string"],
          [/"/, "string"]
        ],
        interpolation: [
          [/\)/, { token: "interpolation.delimiter", next: "@pop" }],
          [/[A-Za-z_\u0080-\uFFFF][A-Za-z0-9_!\u0080-\uFFFF]*(?=\s*\()/, "function.call"],
          [/[A-Za-z_\u0080-\uFFFF][A-Za-z0-9_!\u0080-\uFFFF]*/, {
            cases: {
              "@keywords": "keyword",
              "@default": "variable.interpolation"
            }
          }],
          [/\d+/, "number"],
          [/[-+*/%.]/, "operator"]
        ]
      }
    });
    monaco.editor.defineTheme("sollang-night", {
      base: "vs-dark",
      inherit: true,
      rules: [
        { token: "comment", foreground: "67816F", fontStyle: "italic" },
        { token: "keyword", foreground: "FFB454" },
        { token: "function.declaration", foreground: "82D2FF", fontStyle: "bold" },
        { token: "function.call", foreground: "70C1FF" },
        { token: "variable.binding", foreground: "D2A6FF", fontStyle: "bold" },
        { token: "variable", foreground: "C8B6FF" },
        { token: "variable.interpolation", foreground: "75C9F1", fontStyle: "bold" },
        { token: "interpolation.delimiter", foreground: "FF7AB2", fontStyle: "bold" },
        { token: "type.declaration", foreground: "79DCAA", fontStyle: "bold" },
        { token: "type.identifier", foreground: "79DCAA" },
        { token: "property", foreground: "EBCB8B" },
        { token: "number", foreground: "D2A6FF" },
        { token: "string", foreground: "A8D58D" },
        { token: "string.escape", foreground: "F28FAD" },
        { token: "variable", foreground: "75C9F1" },
        { token: "operator", foreground: "FF8C66" },
        { token: "delimiter", foreground: "8C959F" }
      ],
      colors: {
        "editor.background": "#111513",
        "editor.foreground": "#E7E8E5",
        "editorLineNumber.foreground": "#4F5A53",
        "editorLineNumber.activeForeground": "#A8B5AC",
        "editorCursor.foreground": "#FFB454",
        "editor.selectionBackground": "#33463B",
        "editor.inactiveSelectionBackground": "#26352C",
        "editorIndentGuide.background1": "#202A24",
        "editorIndentGuide.activeBackground1": "#405246"
      }
    });
  };

  const totalTime = result
    ? result.compileMilliseconds + result.executeMilliseconds
    : 0;

  return (
    <main className="site-shell">
      <header className="site-header">
        <a className="brand" href={`${basePath}/`} aria-label="Sollang home">
          <img src={`${basePath}/sollang-logo.svg`} alt="" width={42} height={42} />
          <span>
            <strong>Sollang</strong>
            <small>PLAYGROUND</small>
          </span>
        </a>
        <div className="header-actions">
          <span className={`runtime-badge runtime-${compilerState}`}>
            {compilerState === "loading" && <LoaderCircle size={14} className="spin" />}
            {compilerState === "ready" && <Check size={14} />}
            {compilerState === "failed" && <CircleAlert size={14} />}
            {compilerState === "loading" ? "WASM 로딩 중" : compilerState === "ready" ? "WASM 준비됨" : "WASM 오류"}
          </span>
          <a className="github-link" href="https://github.com/dimohy/Sollang" target="_blank" rel="noreferrer">
            <Code2 size={17} />
            <span>GitHub</span>
          </a>
        </div>
      </header>

      <section className="intro">
        <div>
          <div className="eyebrow"><Sparkles size={14} /> FLOW-FIRST LANGUAGE</div>
          <h1>코드가 흐르는 방향으로<br />생각해 보세요.</h1>
        </div>
        <p>
          샘플을 고르고 마음껏 수정하세요. Sollang 컴파일러가
          <strong> 브라우저 안의 WebAssembly</strong>에서 코드를 검증하고 실행합니다.
          소스 코드는 서버로 전송되지 않습니다.
        </p>
      </section>

      <section className="workbench">
        <div className="workbench-toolbar">
          <div className="sample-picker">
            <label htmlFor="sample">예제</label>
            <div className="select-wrap">
              <select id="sample" value={sampleId} onChange={event => chooseSample(event.target.value)}>
                {samples.map(sample => (
                  <option key={sample.id} value={sample.id}>{sample.title}</option>
                ))}
              </select>
              <ChevronDown size={16} />
            </div>
            <div className="sample-copy">
              <span>{selected.kicker}</span>
              <p>{selected.description}</p>
            </div>
          </div>
          <div className="run-actions">
            <button
              className="reset-button"
              type="button"
              onClick={() => {
                setCode(selected.code);
                setResult(null);
              }}
            >
              <RotateCcw size={15} />
              초기화
            </button>
            <button
              className="run-button"
              type="button"
              disabled={compilerState !== "ready" || isRunning}
              onClick={run}
            >
              {isRunning ? <LoaderCircle size={17} className="spin" /> : <Play size={17} fill="currentColor" />}
              {isRunning ? "컴파일 중…" : "실행"}
              <kbd>Ctrl ↵</kbd>
            </button>
          </div>
        </div>

        <div className="panels">
          <section className="panel editor-panel">
            <div className="panel-title">
              <span><Code2 size={15} /> main.slg</span>
              <span className="language-label">SOLLANG</span>
            </div>
            <div className="editor-host">
              <Editor
                height="100%"
                language="sollang"
                theme="sollang-night"
                value={code}
                beforeMount={beforeMount}
                onChange={value => setCode(value ?? "")}
                loading={<div className="editor-loading"><LoaderCircle className="spin" /> 편집기 로딩 중…</div>}
                options={{
                  minimap: { enabled: false },
                  fontFamily: "'Cascadia Code', 'SFMono-Regular', Consolas, monospace",
                  fontSize: 14,
                  lineHeight: 23,
                  fontLigatures: true,
                  padding: { top: 18, bottom: 18 },
                  scrollBeyondLastLine: false,
                  smoothScrolling: true,
                  automaticLayout: true,
                  tabSize: 4,
                  insertSpaces: true,
                  renderLineHighlight: "all",
                  bracketPairColorization: { enabled: true }
                }}
              />
            </div>
          </section>

          <section className="panel output-panel">
            <div className="panel-title">
              <span><TerminalSquare size={15} /> 출력</span>
              {result && (
                <span className={result.success ? "result-ok" : "result-error"}>
                  {result.success ? "EXIT 0" : "COMPILE ERROR"}
                </span>
              )}
            </div>
            <div className={`terminal ${result && !result.success ? "terminal-error" : ""}`}>
              {!result && !isRunning && (
                <div className="terminal-empty">
                  <Play size={23} />
                  <strong>코드를 실행해 보세요</strong>
                  <span>결과와 컴파일 진단이 여기에 표시됩니다.</span>
                </div>
              )}
              {isRunning && (
                <div className="terminal-empty">
                  <LoaderCircle size={23} className="spin" />
                  <strong>브라우저에서 컴파일 중</strong>
                  <span>SLG Stage2 → LLVM → WebAssembly</span>
                </div>
              )}
              {result && (
                <>
                  <pre>{result.success ? result.output : result.diagnostics}</pre>
                  <div className="timing">
                    <Clock3 size={13} />
                    컴파일 {result.compileMilliseconds.toFixed(1)}ms
                    <span>·</span>
                    실행 {result.executeMilliseconds.toFixed(1)}ms
                    <span>·</span>
                    전체 {totalTime.toFixed(1)}ms
                  </div>
                </>
              )}
            </div>
          </section>
        </div>
      </section>

      <footer>
        <span>Sollang 0.2.260725 · Apache-2.0</span>
        <span>Compiler: SLG Stage2 WebAssembly · No server round-trip</span>
      </footer>
    </main>
  );
}
