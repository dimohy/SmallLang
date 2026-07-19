<p align="center">
  <img src="assets/sollang-logo.svg" alt="Sollang logo" width="160" />
</p>

# Sollang

> **Bright code, harmonious design, clear solutions, original creation.**

Sollang is a native programming language designed to make complex ideas feel
clear, composable, and enjoyable to express. Its name deliberately carries four
meanings rather than reducing the language to a single metaphor:

- **The language of the sun** — `Sol` is light. Sollang aims for warm,
  transparent code whose intent is easy to see.
- **The language of harmony** — `Sol` is a musical note. Flowing expressions,
  balanced structure, and readable rhythm should make code pleasant to read and
  write.
- **The language of solutions** — `Sol` begins *solution*. Sollang exists to
  turn complicated problems into direct, understandable programs.
- **The language of creators** — `S·O·L` means **Simple, Original, Logical**.
  Simple forms should give original ideas a logical, powerful implementation.

These are four equal design commitments. None is merely a decorative story for
the name; they guide syntax, diagnostics, libraries, tooling, and documentation.
See [The Sollang Philosophy](docs/PHILOSOPHY.md).

## Created With

Sollang was created with **GPT-5.6 Sol Medium**. Its creator is satisfied with
the result and records that collaboration as part of the project's history.

It currently accepts a compact `.slg` language slice, lowers it to LLVM IR, and
links minimal Windows x64 or Linux x64 executables. The language favors explicit
value flow with `value -> target` syntax and expression-first bindings with
`value => name`.

## Quick Look

- `.slg` source files
- value-flow calls and bindings, such as `"text" -> print` and
  `7 -> square => num`
- `main { ... }` or omitted `main` with top-level executable statements
- block-function calls such as `1..9 -> each i { ... }`
- flow-oriented `if` and `when` conditionals
- fixed and growable `Int` arrays, such as `[1, 2, 3]`, `[1, 2, ~]`,
  `[Int; ~]`, and `[Int; 1024~]`
- compile-time `Int` value generics and size-checked `[Int; N]` parameters
- `{Int: Int}` dictionaries, such as `{ 1: 100, 2: 200 }`, `{Int: Int}`,
  and `{Int: Int; 1024~}`
- readonly `[Int]` function parameters for non-owning array views
- readonly `{Int: Int}` function parameters for non-owning dictionary views
- `mut [Int; ~]` and `mut {Int: Int}` function parameters for non-owning
  mutable container borrows
- explicit `move` growable array and dictionary parameters, including returning
  the consumed input owner to the caller
- automatic stack promotion for small, non-escaping, readonly dynamic-array
  and dictionary literals
- lifetime-based function-entry stack slots reused across nested branches and
  loop iterations
- small fixed arrays and mutable container metadata placed in entry slots, with
  oversized fixed arrays automatically moved to owned heap storage
- mutable owner names with `!` and checked indexed assignment
- move-consuming container transforms, such as `values -> append(3) => values`
- a Sollang standard library under `stdlib/sys`
- source-generated lexer/parser code from compact grammar files
- LLVM-backed Windows x64, Linux x64, and browser WebAssembly output

## Example

```sollang
getName: -> Text {
    "dimohy"
}

square: Int -> Int {
    it * it
}

main {
    getName() => name
    7 -> square => num
    "Hello, $name. square = $num" -> print
}
```

Verified output:

```text
Hello, dimohy. square = 49
```

Top-level executable statements can omit the `main` wrapper:

```sollang
getName() => name
7 -> square => num
"Hello, $name. square = $num" -> sys.io.print
```

A range can flow into a block function:

```sollang
"n = ? " -> readInt => n

1..9 -> each i {
    n * i => value
    "$n x $i = $value" -> println
}
```

Subject-style conditionals keep the tested value on the left:

```sollang
95 => score

score -> when {
    90..100 => "A"
    80..89 => "B"
    else => "Needs practice"
} => grade
```

## Install A Release

Sollang 0.1 provides self-contained compiler packages for Windows x64 and Linux
x64. Extract the archive for your operating system, keep the bundled `stdlib`
next to the compiler, and set `SOLLANG_LLVM_HOME` to an LLVM installation.
Linux uses `/usr` automatically when no explicit LLVM home is supplied.

```powershell
.\sollang.exe build hello.slg -o hello.exe --target windows-x64
```

```bash
./sollang build hello.slg -o hello --target linux-x64
```

Release archives and `SHA256SUMS.txt` are published on GitHub Releases.
Run `sollang --version` to verify the installed compiler version.

## Run A Sample

```powershell
.\scripts\sollang.ps1 -Source examples\01-function-basic-hello.slg -Output artifacts\01-function-basic-hello.exe -KeepTemps
.\artifacts\01-function-basic-hello.exe
```

On first use, the script downloads LLVM 22.1.8 into `.tools`. LLVM binaries,
build outputs, and generated executables are intentionally ignored by Git.

Build the browser WebAssembly sample and serve the repository root with any
static file server:

```powershell
.\scripts\sollang.ps1 -Source examples\23-webassembly-browser.slg -Output artifacts\23-webassembly-browser.wasm -Target wasm32-browser -KeepTemps
python -m http.server 5080
```

Then open `http://localhost:5080/examples/browser/`.

## Documentation

- [Getting started and implementation guide](docs/GETTING_STARTED.md)
- [Language specification](docs/SPEC.md)
- [Decision log](docs/DECISIONS.md)
- [Self-hosting roadmap and measured progress](docs/SELF_HOSTING_ROADMAP.md)
- [Array, dictionary, and ownership design](docs/ARRAYS.md)
- [VS Code language support extension](tools/vscode-sollang/README.md)
- [Example programs](examples)

## Repository Map

- `examples`: cumulative `.slg` programs that track the grammar progression
- `examples/browser`: static browser runner for the WebAssembly sample
- `stdlib/sys`: standard library modules written in Sollang
- `syntax`: lexer and grammar rule sources
- `src/Sollang.Compiler`: compiler CLI, semantic lowering, and LLVM codegen
- `src/Sollang.Compiler.Generators`: source generators for lexing/parsing
- `tests/Sollang.ExampleTests`: expected stdout test runner
- `tools/vscode-sollang`: local VS Code language support extension

Multiple user files can be compiled as one program. Library files contribute
namespaced declarations, and exactly one root file may contain executable
top-level statements:

```powershell
dotnet run --project src/Sollang.Compiler -- build `
  examples/modules/52-math.slg examples/52-multi-file-modules.slg `
  -o artifacts/52-multi-file-modules.exe
```

Supplying only the root file is sufficient when imported modules follow the
dotted-path layout: `import sample.math` discovers `sample/math.slg`.
Module functions, structs, enums, and traits are internal by default; prefix
declarations with `public` to make them usable from an importing module.

A project root can be named without repeating its source path on every build:

```sollang
project {
    name: "hello"
    root: "src/main.slg"
}
```

Save this as `sollang.project`, then run `sollang build`. The compiler
searches the current directory and its ancestors, or accepts an explicit
`--project <file-or-directory>`. Default artifacts are written under `build/`.

Projects with more than one executable or local packages can declare compact
maps instead of repeating compiler source arguments:

```sollang
project {
    name: "tools"
    products: {
        compiler: "src/compiler.slg"
        formatter: "src/formatter.slg"
    }
    dependencies: {
        syntax: "../syntax"
    }
}
```

Use `sollang build --product compiler`. A dependency path points to the exact
directory containing another `sollang.project`; its name is also its first
import segment, for example `import syntax.tree as tree`.

## License

Sollang is licensed under the [Apache License 2.0](LICENSE).
