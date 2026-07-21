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

Sollang compiles `.slg` source to LLVM IR and links native Windows x64 and Linux
x64 programs or browser WebAssembly. The reference compiler is written in C#;
the growing compiler written in Sollang already emits LLVM itself and passes
native Windows and Linux Stage 2 differential verification. The language favors
explicit value flow with `value -> target` and expression-first bindings with
`value => name`.

## Quick Look

- `.slg` source files
- value-flow calls and bindings, such as `"text" -> println` and
  `7 -> square => num`
- zero-input calls without ceremony: `nowMillis`, never `nowMillis()`
- `main { ... }` or omitted `main` with top-level executable statements
- block-function calls such as `1..9 -> each i { ... }` and compact guards such
  as `condition -> if continue`
- expression-oriented `if` and `when`, with contextual enum patterns such as
  `Ok(value)` and `Err(error)`
- nested structs, traits with associated types, `<T, R, E>` type generics, and
  compile-time value generics such as `<N: Int>`
- fixed and growable generic arrays (`[T; N]`, `[T; ~]`) and Swiss-table
  dictionaries (`{K: V}`), including contextual struct keys and elements
- compile-time collection expansion such as `[1..10]`,
  `[1..10 -> each { it + 1 }]`, and `{1..3 -> each { it: it * 10 }}`
- readonly views, mutable borrows, explicit ownership transfer, `box`, and
  move-path checking for arrays, dictionaries, structs, and async frames
- `Int8`/`Int16`/`Int32`/`Int64`, unsigned widths, `Float32`/`Float64`, and
  stable `Int`/`Float` aliases plus target-sized `Size` and `UIntSize`
- automatic stack promotion for small, non-escaping, readonly dynamic-array
  and dictionary literals, with heap promotion when ownership or size requires it
- lifetime-based function-entry stack slots reused across nested branches and
  loop iterations
- mutable owner names with `!` and checked indexed assignment
- both `data![index] = value` and `value => data![index]` assignment flow
- structured `async`/`await`, cancellation, deterministic parallel transforms,
  and explicit `uses Console, File, Clock, ...` effect capabilities
- language-level memory-mapped byte regions for data larger than ordinary heap
  collections
- strict UTF-8 `Text`, Unicode `CodePoint`, raw multiline strings, `Option`,
  `Result`, and `?` propagation
- import discovery with the final path segment as the default alias, local
  packages, products, and explicit workspaces
- a Sollang standard library under `stdlib/sys`
- source-generated lexer/parser code from compact grammar files
- LLVM-backed Windows x64, Linux x64, and browser WebAssembly output
- content-validated incremental builds whose exact-input warm path skips parsing,
  semantic analysis, LLVM emission, and linking, with byte-identical artifacts,
  transitive interface invalidation, stable cross-session semantic identities,
  dependency-safe function-body semantic reuse after partial source changes,
  and atomic cache publication

## Example

```sollang
getName: -> Text {
    "dimohy"
}

square: Int -> Int {
    it * it
}

main {
    getName => name
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
getName => name
7 -> square => num
"Hello, $name. square = $num" -> sys.io.print
```

Zero-input functions are values selected by name. Parentheses are reserved for
calls that actually supply input, so `getName()` is a compile-time error rather
than an alternative spelling of `getName`.

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

Compile-time ranges and transforms become ordinary collection literals before
runtime code generation:

```sollang
[1..10] => numbers
[1..10 -> each { it + 1 }] => incremented
{1..3 -> each { it: it * 10 }} => lookup
```

Structured async keeps the same left-to-right flow:

```sollang
square value: Int -> async Int => value * value
answer: -> async Int => 42

main {
    6 -> square -> await => squared
    answer -> await => value
    "$(squared), $(value)" -> println
}
```

The expected enum type supplies short `Ok` and `Err` patterns:

```sollang
valueOrZero result: Result<Int, Text> -> Int {
    result -> when {
        Ok(value) => value
        Err(error) => 0
    }
}
```

Comments use `#`. Triple-quoted strings preserve readable embedded source, and
their common indentation is removed:

```sollang
"""
namespace sample

public answer: -> Int => 42
""" => source
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
Ordinary builds keep a disposable `.sollang-cache` beside the selected output;
the compiler reports cold, reused, or rejected units on every build. Deleting
that directory always produces a clean rebuild and never removes source state.

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
dotted-path layout: `import sample.math` discovers `sample/math.slg` and binds
the alias `math`. Write `import sample.math as arithmetic` only when a different
alias is useful.
Module functions, structs, enums, and traits are internal by default; prefix
declarations with `public` to make them usable from an importing module.

A project root can be named without repeating its source path on every build:

```sollang
project {
    name: "hello"
    version: "0.1.0"
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
    version: "0.1.0"
    products: {
        compiler: "src/compiler.slg"
        formatter: "src/formatter.slg"
    }
    dependencies: {
        syntax: {
            path: "../syntax"
            version: "^1.2.0"
        }
        formatter_core: {
            git: "https://github.com/example/formatter-core.git"
            rev: "0123456789abcdef0123456789abcdef01234567"
            version: "^2.0.0"
        }
        text: {
            registry: "https://packages.example.com"
            version: "^1.0.0"
        }
    }
}
```

Use `sollang build --product compiler`. Every project has a canonical SemVer
identity such as `tools@0.1.0`. A dependency path points to the exact directory
containing another `sollang.project`; its name is also its first import segment,
for example `import syntax.tree` binds `tree`. Version requirements accept
exact versions, `^`, `~`, and comparator intersections such as
`>=1.2.0 <2.0.0`. The legacy path-only dependency value remains accepted as an
unconstrained local dependency.

Related local packages can share one explicit workspace without duplicating
their names in a second map:

```sollang
workspace {
    members: [
        "packages/syntax"
        "packages/compiler"
        "apps/sollang"
    ]
}
```

Save this as `sollang.workspace`, then run
`sollang build --package sollang`. Member paths are relative, confined, and
resolved in deterministic package-name order. Every dependency of the selected
package must be a declared workspace member. From inside a member directory,
plain `sollang build` discovers both the member project and its workspace.
Workspace outputs are separated as
`build/<target>/<package>/<product>[.exe|.wasm]` under the workspace root.
`sollang resolve --workspace <path>` writes one canonical, sorted
`sollang.lock` for all workspace members. Normal workspace builds refresh a
stale lock; `sollang build --locked` instead rejects a missing or stale lock,
which is the reproducible CI mode. Commit `sollang.lock` with the workspace.
Git dependencies require a full 40- or 64-digit commit hash—branches, tags,
and abbreviated hashes are deliberately rejected. Sollang checks out only
that commit into `.sollang/dependencies`, hashes the canonical source tree with
SHA-256, and records both revision and checksum in lock format 2.
Registry dependencies use a static language-shaped index and checksummed ZIP
archive. Normal builds preserve the locked version; only `sollang resolve`
selects the newest compatible non-yanked release. See the
[registry protocol](docs/PACKAGE_REGISTRY.md).

## Self-Hosting Progress

The measured roadmap is currently **54/60 equivalent gates (90.0%)**, with
**6 equivalent gates remaining**.

The Sollang-written compiler is split into lexer, parser/CST/AST, semantic,
typed-IR, ownership, module-cache, and LLVM modules. It builds a native Stage 2
compiler and passes Windows and Linux differential gates. The latest ownership
checkpoint infers readonly-reference origins stored in
user structs, enum payloads, fixed/growable array elements, and dictionary
values. The self-host LLVM backend now uses control bytes, integer H2 hashes,
wrapped eight-slot group scans with direct candidate selection, and an `Int`
dictionary `put` path that updates
existing keys in place, inserts into available slots, and doubles and rehashes
only when the next insertion would exceed 87.5% load. General key/value
families remain open. Local package
identities, SemVer requirements, content-pinned Git dependencies, shared
deterministic workspace locks, and self-host parsers for both versions and
lock manifests and registry-index selection are implemented. Exact
counts and the evidence behind every gate live in the
[self-hosting roadmap](docs/SELF_HOSTING_ROADMAP.md).

## License

Sollang is licensed under the [Apache License 2.0](LICENSE).
