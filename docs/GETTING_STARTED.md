# Sollang Guide

This guide keeps the longer project notes out of the README while preserving the
details needed to build, run, and understand the current Sollang slice.

Sollang is in an early compiler-building phase. The implementation is scoped
to the accepted language specification and decision log.

## What Works Today

- `main { ... }` or omitted `main` with top-level executable statements
- zero-argument functions with `getName: -> Text { ... }`
- one-input functions with default `it` or an explicit input name:
  `square: Int -> Int { ... }` and `square n: Int -> Int { ... }`
- single-expression function bodies with `name: Input -> Output => expression`
- local functions declared inside a function body, scoped to that containing
  function
- expression-first bindings with `"value" => name`, `getName() => name`, and
  `7 -> square => num`
- integer `+`, `-`, `*`, `/`, `%`, unary `-`, and parenthesized expressions
- line comments with `#`
- `Bool` values from `true`/`false`, integer comparisons, and `and`/`or`/`not`
- flow-oriented conditionals with `condition -> if { ... } else { ... }`
- multi-branch `when { condition { ... } else { ... } }` expressions
- subject-value `when` with `value -> when { >= limit { ... } else { ... } }`
- subject-value `when` range arms with `value -> when { start..end { ... } }`
- compact `when` arms with `condition => value`, including implicit `it`
  subject inside one-input functions
- string interpolation with `"Hello, $name"`
- interpolation of string and integer bindings
- value-flow calls with `value -> function`
- value-flow calls with extra receiver arguments, such as
  `values! -> push(30)`
- value-flow target-call syntax with `value -> function()`
- parenthesized calls with `function(value)`
- Sollang standard library functions `sys.io.print`, `sys.io.println`, and
  `sys.io.readInt` with global `print`, `println`, and `readInt` aliases
- `namespace` declarations and imports whose last path segment is the default
  alias (`import sample.math`), with optional explicit renaming through
  `import sample.math as mathApi`
- integer input with `"n = ? " -> readInt => n` or
  `"n = ? " -> sys.io.readInt => n`
- line output with `value -> println` or `value -> sys.io.println`
- block-function calls with `range -> each item { ... }` and
  `count -> repeat item { ... }`, where the brace block is the call argument and
  `each()`/`repeat()` are intentionally omitted
- closed integer range loops with `1..9 -> each i { ... }`
- default loop item binding with `1..9 -> each { ... }`, exposed as `it`
- integer folds with `range -> fold initial acc, item { nextAcc }`
- fixed `Int` arrays with `[1, 2, 3]` and `[0; 8]`
- growable `Int` arrays with `[1, 2, ~]`, typed empty `[Int; ~]`, and
  capacity hint `[Int; 1024~]`
- `{Int: Int}` dictionaries with `{ 1: 100, 2: 200 }` and typed empty
  `{Int: Int}` or capacity hint `{Int: Int; 1024~}`
- checked indexing with `array[index]` and `dictionary[key]`
- mutable owner bindings with `value => name!`
- checked indexed assignment with `value => owner![index]`
- move-consuming container transforms with `append(value)` and
  `updated(keyOrIndex, value)`
- readonly `[Int]` and `{Int: Int}`, mutable-borrow `mut [Int; ~]` and
  `mut {Int: Int}`, and explicit `move` growable array/dictionary parameters
  that may return the consumed input owner
- deterministic native cleanup for heap-owning dynamic arrays and dictionaries
- automatic allocation-free stack payloads for small dynamic-array and
  dictionary literals whose owners remain local and readonly
- function-entry stack-frame planning with lifetime-based slot reuse across
  nested branches and loop iterations
- automatic heap placement for fixed arrays that exceed the planned stack
  budget
- compile-time `Int` value generics with explicit fluent specialization, such
  as `value -> fill[4]`, including symbolic fixed-array repeat counts
- fixed-array value-generic parameters such as `[Int; N]`, with compile-time
  size checking at calls like `values -> fixedLength<3>`
- purpose-oriented pseudo-random integer generation with `seedRandom` and
  `randomBelow`
- binary sorted `Int` file writing and nearest-value lookup with
  `openIntWriter`, `writeInt`, `openIntReader`, and `closestInt`
- source-generated lexing from `syntax/sollang.lexer`
- source-generated parsing from `syntax/sollang.grammar`
- LLVM IR generation
- Windows x64 executable linking through `clang` and `lld-link`
- Linux x64 executable linking through Windows LLVM object generation and WSL
  `cc`
- browser WebAssembly module linking through `clang` and `wasm-ld`

With the current Windows linker settings, representative executable sizes are
**1,536 bytes** for `01-function-basic-hello.slg` and `05-function-local.slg`,
**2,048 bytes** for `08-block-each-default-it.slg`, and **2,560 bytes** for the
container and sorted-int-file workflow samples.

## Build And Run

The examples are named so a normal filename sort follows the grammar
progression. Start with the basic function/value-flow sample:

```powershell
.\scripts\sollang.ps1 -Source examples\01-function-basic-hello.slg -Output artifacts\01-function-basic-hello.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\02-function-named-input.slg -Output artifacts\02-function-named-input.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\03-flow-call-parens.slg -Output artifacts\03-flow-call-parens.exe -KeepTemps
```

Top-level statements, local functions, arithmetic, comments, and block functions
are cumulative:

```powershell
.\scripts\sollang.ps1 -Source examples\04-main-omitted-top-level.slg -Output artifacts\04-main-omitted-top-level.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\05-function-local.slg -Output artifacts\05-function-local.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\06-expression-arithmetic-comments.slg -Output artifacts\06-expression-arithmetic-comments.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\07-block-each-explicit-item.slg -Output artifacts\07-block-each-explicit-item.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\08-block-each-default-it.slg -Output artifacts\08-block-each-default-it.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\09-namespace-sys-io.slg -Output artifacts\09-namespace-sys-io.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\10-block-argument-omits-parens.slg -Output artifacts\10-block-argument-omits-parens.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\11-block-function-exec-block-repeat.slg -Output artifacts\11-block-function-exec-block-repeat.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\12-block-function-user-defined-yield.slg -Output artifacts\12-block-function-user-defined-yield.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\13-block-fold-sum.slg -Output artifacts\13-block-fold-sum.exe -KeepTemps
```

Conditionals are cumulative:

```powershell
.\scripts\sollang.ps1 -Source examples\14-condition-if.slg -Output artifacts\14-condition-if.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\15-condition-when.slg -Output artifacts\15-condition-when.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\16-condition-when-subject.slg -Output artifacts\16-condition-when-subject.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\17-condition-when-range.slg -Output artifacts\17-condition-when-range.exe -KeepTemps
.\scripts\sollang.ps1 -Source examples\18-condition-when-compact.slg -Output artifacts\18-condition-when-compact.exe -KeepTemps
```

The sorted-number workflow is also written in Sollang. For quick verification,
the demo pair uses the same algorithm at 1,000 records:

```powershell
.\scripts\sollang.ps1 -Source examples\19-stdlib-random-file-demo-generate.slg -Output artifacts\19-stdlib-random-file-demo-generate.exe -KeepTemps
.\artifacts\19-stdlib-random-file-demo-generate.exe
.\scripts\sollang.ps1 -Source examples\20-stdlib-file-demo-query.slg -Output artifacts\20-stdlib-file-demo-query.exe -KeepTemps
.\artifacts\20-stdlib-file-demo-query.exe
```

The full generator creates 100,000,000 sorted 64-bit integer records in
`artifacts/random-sorted-100m.i64` by choosing one pseudo-random value from each
10-wide bucket in `1..1,000,000,000`:

```powershell
.\scripts\sollang.ps1 -Source examples\21-stdlib-random-file-100m-generate.slg -Output artifacts\21-stdlib-random-file-100m-generate.exe -KeepTemps
.\artifacts\21-stdlib-random-file-100m-generate.exe

.\scripts\sollang.ps1 -Source examples\22-stdlib-file-100m-query.slg -Output artifacts\22-stdlib-file-100m-query.exe -KeepTemps
.\artifacts\22-stdlib-file-100m-query.exe
```

Linux x64 output is available through WSL:

```powershell
.\scripts\sollang.ps1 -Source examples\01-function-basic-hello.slg -Output artifacts\01-function-basic-hello-linux -Target linux-x64 -KeepTemps
wsl --exec /mnt/p/MyWorks/Sollang/artifacts/01-function-basic-hello-linux
```

Structured async uses the same source syntax on Windows and Linux. For example,
the generic sendable-input coverage can be built and executed through WSL with:

```powershell
.\scripts\sollang.ps1 `
  -Source examples\238-sendable-async-inputs.slg `
  -Output artifacts\238-sendable-async-inputs-linux `
  -Target linux-x64
wsl --exec /mnt/p/MyWorks/Sollang/artifacts/238-sendable-async-inputs-linux
```

Async calls start cooperative affine tasks. `await` consumes a task and returns
its value; `cancel` consumes it without producing a value. Cancellation is a
final flow target and does not use parentheses:

```sollang
5 -> parent => pending
0 -> gate => gateTask
gateTask -> await => ready
pending -> cancel
```

An unconsumed task is still joined by lexical scope cleanup. Explicit cancel is
for work whose result is no longer needed; it removes queued work and runs the
compiler-generated coroutine destroy path for initialized child and frame
owners.

`await` can suspend inside an `if` or `when` branch without flattening the source
control flow:

```sollang
choose value: Int -> async Int {
    value * 10 => saved
    value > 0 -> if {
        value -> step => pending
        pending -> await => next
        saved + next
    } else {
        saved
    }
}
```

The generated resume switch targets the branch continuation directly. Values
that cross the suspension move through the coroutine frame, and the branch join
merges their resumed representation before later statements execute.

The same suspension model applies inside `while`. Numbered states are stable
sites, not one-shot events, so a loop can revisit one site on every iteration:

```sollang
sum count: Int -> async Int {
    0 => index!
    0 => total!
    index! < count -> while {
        index! -> step => pending
        pending -> await => next
        total! + next => total!
        index! + 1 => index!
    }
    total!
}
```

The compiler carries mutable storage pointers through loop-header phis and
spills live values only while the child is pending. `break`, `continue`, and
their compact guarded forms preserve that state across early loop edges:

```sollang
pending -> await => next
index! + 1 => index!
index! == 2 -> if continue
total! + next => total!
index! == 4 -> if break
```

Body-local owners are dropped before either transfer. An outer owner required
by the next iteration or after loop exit must remain initialized on every
incoming edge; inconsistent consumption is a compile-time error.

Long CPU work can explicitly cooperate with the executor using bare `yield`:

```sollang
scan count: Int -> async Int {
    0 => index!
    index! < count -> while {
        # Perform one bounded piece of CPU work.
        index! + 1 => index!
        yield
    }
    index!
}
```

`yield` spills live state, places the current Task at the back of the ready
queue, and resumes at the following statement. If its owner cancels the queued
Task, the compiler-generated state destroy path drops the frame instead. This
bare async statement is distinct from `value -> yield`, which still transfers a
value from a user-defined block function.

Use typed durations for nonblocking time suspension:

```sollang
refresh: -> async Int {
    250 -> milliseconds -> sleep -> await
    1
}
```

`sleep` returns an affine `Task<Unit>`, so it follows the same `await` or
`cancel` ownership rule as every other Task. Sleeping work leaves the ready
queue and enters a deadline-ordered timer queue; the executor runs other work
and waits for the nearest timer only when nothing is runnable. `seconds` is
available when that unit reads better. Zero or negative durations complete
immediately.

Generic binary scalar reads can suspend without blocking the cooperative
executor:

```sollang
import sys.file as file

readHeader: -> async Result<Option<UInt16>, Text> {
    file.readAsync<UInt16> => pending
    pending -> await
}
```

`readAsync<T>` has the same `Ok(Some(value))`, `Ok(None)`, and Text error
contract as synchronous `read<T>`. All native reads share one background file
worker and return through the Task ready queue; cancellation uses the ordinary
affine `task -> cancel` rule. The current reader is cursor-based and global, so
await every submitted read before closing or reopening it.

For concurrent or random-access work, prefer the owned File surface:

```sollang
file.openReadAsync("values.bin") => opening
opening -> await => opened
opened -> when {
    Ok(reader) {
        reader -> readAtAsync<UInt16>(0) => header
        reader -> readAtAsync<UInt16>(4096) => record
        header -> await => headerValue
        record -> await => recordValue
    }
    Err(error) => error
}
```

The `UInt64` argument is a byte offset. `File` is affine and automatically
closed; each pending read Task owns a duplicated OS handle and is therefore
safe even if completion order differs from submission order. The open Task
owns its path bytes and transfers the new handle only through a successful
`await`.

Random-access output uses a distinct affine writer capability:

```sollang
file.openWriteAsync("values.bin") => opening
opening -> await => opened
opened -> when {
    Ok(writer) {
        writer -> writeAt(UInt16(513), 0)
        writer -> writeAtAsync<UInt16>(1027, 3) => pending
        pending -> await => written
        writer -> syncAsync => syncing
        syncing -> await => durable
    }
    Err(error) => error
}
```

The first form infers `UInt16`; the second contextually types the literal.
Every write is position-based and all-or-error. An asynchronous write copies
the scalar and owns a duplicated OS handle, so the original writer can close
independently; `await` returns `Result<Unit, Text>`. The writer closes
automatically at owner-scope exit. `syncAsync` is the explicit durability
barrier for data and metadata already submitted to the shared file worker.

Browser WebAssembly output is available through the `wasm32-browser` target. The
generated module exports `sollang_start` and `memory`, and imports
`env.slg_browser_write(ptr, len)` so the page can render stdout text:

```powershell
.\scripts\sollang.ps1 -Source examples\23-webassembly-browser.slg -Output artifacts\23-webassembly-browser.wasm -Target wasm32-browser -KeepTemps
python -m http.server 5080
```

Open `http://localhost:5080/examples/browser/`.

The first container sample shows static arrays, dynamic arrays, checked
indexing, `fold`, `push`, dictionary `put`, `len`, `capacity`, and deterministic
native cleanup:

```powershell
.\scripts\sollang.ps1 -Source examples\25-arrays-dictionaries.slg -Output artifacts\25-arrays-dictionaries.exe -KeepTemps
.\artifacts\25-arrays-dictionaries.exe
```

Move-consuming container transforms return a new owner while consuming the
source owner. The sample shows the short same-name form:

```powershell
.\scripts\sollang.ps1 -Source examples\26-immutable-containers.slg -Output artifacts\26-immutable-containers.exe -KeepTemps
.\artifacts\26-immutable-containers.exe
```

The dictionary hash-table sample exercises update, growth, rehashing, lookup,
and capacity reporting:

```powershell
.\scripts\sollang.ps1 -Source examples\27-dictionary-hash-table.slg -Output artifacts\27-dictionary-hash-table.exe -KeepTemps
.\artifacts\27-dictionary-hash-table.exe
```

On first use, the script downloads LLVM 22.1.8 into `.tools`. LLVM binaries,
build outputs, and generated executables are intentionally ignored by Git.

Example stdout tests compile and run the samples listed under
`examples/expected`:

```powershell
dotnet run --project tests\Sollang.ExampleTests\Sollang.ExampleTests.csproj --no-build
```

During development, repeat `--filter` for name fragments, use `--exact` for a
complete test name, or repeat `--affected` with repository-relative changed
paths. `--suite reference|semantic|selfhost|llvm|fast|full` selects a stable
layer. Add `--skip-bootstrap` only after the Release compiler and generated
grammar table have already been verified in the current checkout:

```powershell
dotnet run --project tests\Sollang.ExampleTests\Sollang.ExampleTests.csproj `
  -c Release --no-build -- --filter 219 --filter 220 --skip-bootstrap
```

The runner uses up to eight isolated test workers by default. It starts the
remaining expensive cases first and uses a load-balancing partitioner so a
worker that finishes a short case immediately takes the next remaining case.
Self-host LLVM emitter tests bootstrap one Sollang compiler driver and then reuse its
native executable for every Windows, Linux, and Wasm fixture. The driver is
rebuilt only when its compiler, manifest, Sollang sources, or standard library inputs
are newer; a current driver reports `[selfhost bootstrap] REUSE`. Thus the suite
does not compile the same self-host compiler modules once per fixture.
The generated parser commits successful optional/repeated branches and keeps a
per-rule negative-result table for the current token stream. Large self-host
modules therefore do not repeat the same failed parse from the same token.
Bootstrap phases are printed as `[bootstrap n/total]`. Every scheduled case is
printed as `[start n/total]`, and every completion is flushed immediately as
`[n/total] PASS|FAIL name (seconds)`, so long LLVM runs never appear idle.
Use `--jobs 1` for deterministic sequential diagnosis or an explicit positive
worker count when measuring another machine. Compiler bootstrap and
grammar-table determinism are still checked once before the parallel section.
Add `--compare-compilers` to an executable self-host LLVM fixture when
diagnosing backend drift. The runner materializes the fixture's embedded Sollang
sources once, emits LLVM with both the C# reference compiler and the native Sollang
compiler, keeps both `.ll` files under `artifacts/example-tests`, links both,
and requires identical exit codes and normalized stdout. It intentionally
compares observable behavior rather than raw LLVM text because the C# and Sollang
backends use different platform-runtime implementations.

Use the dedicated stage-2 gate to prove that a C#-bootstrapped Sollang compiler and
the Sollang compiler rebuilt by that compiler produce the same normalized LLVM:

```powershell
.\scripts\verify-selfhost-stage2.ps1
```

The script reports `[stage2 n/6]`, caches the complete stage-2 executable until
one of its 28 source inputs changes, compares normalized LLVM SHA-256 for both a
single source and an imported two-file program, assembles and links both stage-2
outputs, executes them, and finally runs the C#/Sollang runtime differential check.
Pass `-Rebuild` to force the complete stage-2 emission. These gates are opt-in
so the ordinary parallel edit loop does not pay for another full compiler
generation.
An unfiltered run always remains the commit-gate regression check.

Run the complete Linux x64 gate through WSL with the same 523-case inventory:

```powershell
.\scripts\verify-linux-full.ps1
```

The runner compiles ordinary examples and diagnostics for `linux-x64`, executes
native binaries inside WSL, and makes reusable self-host tests emit Linux LLVM.
Those self-host outputs are assembled for every case and linked/executed when
an observable runtime expectation exists. Linux uses four test workers by
default because higher concurrent WSL process counts made asynchronous file
readiness stress runs nondeterministic; this bounds the launcher without
serializing compiler and runtime coverage.

Multiple user source files may form one compilation unit. Declarations from all
files are merged after each file independently resolves its namespace and import
aliases. Exactly one file may contain executable top-level statements:

```powershell
.\scripts\sollang.ps1 `
  -SourcesFile examples\expected\52-multi-file-modules.sources.txt `
  -Output artifacts\52-multi-file-modules.exe
```

The source-list file contains one repository-relative `.slg` path per line.
Direct compiler use accepts the same files as positional arguments.

For a normal project, put the root module in a compact language-shaped
`sollang.project` manifest:

```sollang
project {
    name: "compiler"
    version: "0.1.0"
    root: "src/main.slg"
}
```

Running `sollang build` without source arguments searches the current
directory and its ancestors for that manifest. `--project` accepts either the
manifest or its directory. Relative roots are resolved inside the project and
may not escape it. Without `-o`, artifacts are written as
`build/<name>.exe`, `build/<name>`, or `build/<name>.wasm` for the selected
target. Command-line target, optimization, LLVM, and output options remain
explicit overrides.

Use `products` when one project produces several executables, and use
`dependencies` for exact local package paths:

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
    }
}
```

`sollang build --product compiler` selects one product. A project with one
product needs no selection; a project with several reports the sorted choices
instead of silently choosing. Product and dependency names are import-safe
identifiers. Every dependency path is relative to the declaring manifest and
must point directly to a directory or `sollang.project` file; directory
traversal looking for a coincidental package is not performed.

The dependency key must equal the dependency project's `name`, and that package
must expose a product with the same name. Source imports use that first segment,
such as `import syntax.tree as tree`. Each package can see only its own direct
dependencies; a dependency's dependencies do not become ambient imports of the
parent. The compiler resolves the complete local graph in sorted order and
rejects dependency cycles, duplicate project names, malformed SemVer values,
and unsatisfied dependency versions before parsing sources. Requirements accept
`*`, exact versions, `^`, `~`, and whitespace-separated comparator
intersections such as `>=1.2.0 <2.0.0`.

Put tightly coupled local projects in one `sollang.workspace`:

```sollang
workspace {
    members: [
        "packages/base"
        "packages/math"
        "apps/app"
    ]
}
```

The members array deliberately carries paths only; each member's
`sollang.project` remains the single source of its package name, products, and
dependencies. Paths must be relative and remain inside the workspace. Duplicate
paths and names, empty workspaces, missing projects, and dependencies on an
undeclared local project are errors.

At the workspace root, select a package with
`sollang build --package app`; use `--product` as well only when that project
has multiple products. `--workspace <file-or-directory>` selects an explicit
workspace. From a member directory, a source-free build finds the nearest
project and enclosing workspace and selects that member automatically. Default
outputs live under
`build/<target>/<package>/<product>[.exe|.wasm]`, so different packages and
targets cannot overwrite one another. The workspace manifest participates in
the persistent frontend-cache identity. Run
`sollang resolve --workspace <file-or-directory>` to write the workspace's
canonical, sorted `sollang.lock`. Normal workspace builds update a stale lock;
`sollang build --locked` rejects a missing or stale lock for reproducible CI.
The lock records exact `name@version` dependency identities and normalized
local `path:` sources. A remote dependency uses a content-pinned Git record:

```sollang
dependencies: {
    syntax: {
        git: "https://github.com/example/syntax.git"
        rev: "0123456789abcdef0123456789abcdef01234567"
        version: "^1.2.0"
    }
}
```

`rev` must be a complete 40- or 64-digit commit hash. The compiler materializes
that exact commit without a working-tree `.git` directory, rejects symbolic
links, hashes sorted paths, lengths, and bytes with SHA-256, and writes the
revision plus checksum to lock format 2. Cached bytes whose digest changes are
rejected. Package registries remain the later distribution layer.

When only the root file is supplied, each non-`sys` import is mapped from its
dotted module path to a `.slg` file relative to the root directory. For example,
`import sample.math as math` discovers `sample/math.slg` recursively.
Imported module functions and nominal declarations are internal unless
explicitly exported:

```sollang
namespace sample.math

public double value: Int -> Int {
    value * 2
}
```

The same rule applies to `public struct`, `public enum`, and `public trait`.
Imported type paths use the import alias, for example `math.Counter`.

The compiler itself targets .NET 11 Preview and uses C# Preview.

## VS Code Extension

Sollang includes a local VS Code language support extension:

```powershell
Push-Location tools\vscode-sollang
npx --yes @vscode/vsce package --no-dependencies --allow-missing-repository
code --install-extension .\sollang-language-support-0.1.2.vsix
Pop-Location
```

The extension registers `.slg`, highlights value-flow syntax, function
declarations, block-function calls, strings with interpolation, comments,
conditionals, types, and operators, and includes snippets for common forms.

See [tools/vscode-sollang/README.md](../tools/vscode-sollang/README.md) for
extension-specific notes.

## Pipeline

```mermaid
flowchart LR
    Source[Sollang source] --> Lexer[Generated lexer]
    Lexer --> Parser[Generated parser]
    Parser --> AST[AST]
    AST --> Semantics[Semantic lowering]
    Semantics --> LLVM[LLVM IR]
    LLVM --> Object[COFF, ELF, or WASM object]
    Object --> Output[Native executable or WebAssembly module]
```

## Lexer Rules

Lexer rules are written in a compact DSL:

```text
token Identifier = identifier
token String = quoted_string
token Number = number
token LeftBrace = "{"
token RightBrace = "}"
token LeftParen = "("
token RightParen = ")"
token Range = ".."
token Tilde = "~"
token Dot = "."
token Comma = ","
token Semicolon = ";"
token Plus = "+"
token Minus = "-"
token Star = "*"
token Slash = "/"
token Percent = "%"
token Arrow = "->"
token FatArrow = "=>"
token Colon = ":"
token EqualEqual = "=="
token BangEqual = "!="
token Bang = "!"
token LessEqual = "<="
token GreaterEqual = ">="
token Less = "<"
token Greater = ">"
token Equal = "="
token NewLine = newline
token End = end
```

`src/Sollang.Compiler.Generators` reads `syntax/sollang.lexer` as an MSBuild
`AdditionalFiles` input and generates `TokenKind` and `Lexer` during the C#
build.

## Grammar Rules

Parser rules are also written in a compact DSL:

```text
rule SourceFile = NewLine* NamespaceDeclaration? ImportDeclaration* FunctionDeclaration* (MainBlock | Statement*) NewLine* End
rule NamespaceDeclaration = Identifier("namespace") Path StatementEnd
rule ImportDeclaration = Identifier("import") Path (Identifier("as") Identifier)? StatementEnd
rule FunctionDeclaration = Path Identifier? Colon FunctionSignature FunctionBody
rule FunctionSignature = Arrow Identifier("async")? TypeAnnotation | InputTypeAnnotation Arrow Identifier("async")? TypeAnnotation
rule InputTypeAnnotation = (Identifier("move") | Identifier("mut"))? TypeAnnotation
rule FunctionBody = LeftBrace NewLine* FunctionDeclaration* Statement* Expression NewLine* RightBrace | FatArrow Expression | Arrow Expression | Equal Identifier("intrinsic")
rule MainBlock = Identifier("main") LeftBrace NewLine* Statement* RightBrace
rule Statement = BlockFunctionCallStatement | EachStatement | BindingStatement | ExpressionStatement
rule BlockFunctionCallStatement = RangeOrLogicalExpression Arrow Path Identifier? LeftBrace NewLine* Statement* RightBrace (FatArrow MutableName)?
rule EachStatement = Identifier("each") Identifier Identifier("in") RangeExpression LeftBrace NewLine* Statement* RightBrace
rule BindingStatement = Identifier Equal Expression StatementEnd
rule RangeExpression = LogicalOrExpression Range LogicalOrExpression
rule Expression = FlowExpression
rule FlowExpression = RangeOrLogicalExpression (Arrow (Path FlowTargetCall? | IfFlowTarget | WhenFlowTarget | FoldFlowTarget))*
rule FlowTargetCall = LeftParen RightParen
rule RangeOrLogicalExpression = RangeExpression | LogicalOrExpression
rule IfFlowTarget = Identifier("if") LeftBrace BlockBody RightBrace (Identifier("else") LeftBrace BlockBody RightBrace)?
rule WhenFlowTarget = Identifier("when") LeftBrace NewLine* SubjectWhenArm+ Identifier("else") WhenArmBody NewLine* RightBrace
rule FoldFlowTarget = Identifier("fold") Expression Identifier Comma Identifier LeftBrace BlockBody RightBrace
rule WhenExpression = Identifier("when") LeftBrace NewLine* WhenArm+ Identifier("else") WhenArmBody NewLine* RightBrace
rule WhenArm = WhenCondition WhenArmBody
rule WhenCondition = SubjectWhenCondition | LogicalOrExpression
rule SubjectWhenArm = SubjectWhenCondition WhenArmBody
rule SubjectWhenCondition = (EqualEqual | BangEqual | Less | LessEqual | Greater | GreaterEqual) Expression | RangeExpression
rule WhenArmBody = Arrow Expression | BlockBody
rule BlockBody = NewLine* Statement* Expression? NewLine*
rule LogicalOrExpression = LogicalAndExpression (Identifier("or") LogicalAndExpression)*
rule LogicalAndExpression = EqualityExpression (Identifier("and") EqualityExpression)*
rule EqualityExpression = ComparisonExpression ((EqualEqual | BangEqual) ComparisonExpression)*
rule ComparisonExpression = AdditiveExpression ((Less | LessEqual | Greater | GreaterEqual) AdditiveExpression)*
rule AdditiveExpression = MultiplicativeExpression ((Plus | Minus) MultiplicativeExpression)*
rule MultiplicativeExpression = UnaryExpression ((Star | Slash | Percent) UnaryExpression)*
rule UnaryExpression = Identifier("not") UnaryExpression | Minus UnaryExpression | PrimaryExpression
rule PrimaryExpression = WhenExpression | CallExpression | StringExpression | NumberExpression | NameExpression
rule TypeName = Identifier
rule TypeAnnotation = TypeName | LeftBracket TypeName RightBracket | LeftBracket TypeName Semicolon Tilde RightBracket | LeftBrace TypeName Colon TypeName RightBrace
```

The generator reads `syntax/sollang.grammar` and emits the current recursive
descent parser at compile time. Bindings use `=>`, so `n * i => value` is the
preferred binding style for new samples. Function targets in value-flow calls
omit empty parentheses, such as `7 -> square => num`; target parentheses are
reserved for additional receiver arguments, such as `values! -> push(30)`.
Block-function targets use the following brace block as the function's code
block argument: `1..9 -> each i { ... }`.

Range loops prefer `start..end -> each item { ... }`; when the item name is
omitted as `start..end -> each { ... }`, the loop item is available as `it`.
One-input functions follow the same naming shape: `square: Int -> Int` exposes
the input as `it`, while `square n: Int -> Int` exposes it as `n`. A function
whose body is a single expression may use `name: Input -> Output => expression`
instead of an outer body block.

Function declarations may appear at the start of another function body. These
local functions are visible only inside that containing function and nested
functions below it; the backend currently inlines them instead of emitting
separate global LLVM functions.

`each` is modeled as the first built-in block function: `1..9 -> each i { ... }`
means the range flows into `each` and the block is passed as its executable body.
`repeat` is the integer-count variant: `3 -> repeat turn { ... }` flows the count
into `repeat`, then invokes the block with `turn` values `1..3`. The common LLVM
emitter lowers these built-ins directly to LLVM basic blocks rather than
emitting a runtime closure, function pointer, or block-call dispatch.

Conditions follow the same flow style as block functions:
`condition -> if { ... } else { ... }` receives a `Bool` value on the left, and
`when { ... }` handles multi-branch value selection. When the same value is
tested in every arm, prefer `value -> when { >= limit { ... } else { ... } }` or
range arms such as `value -> when { 90..100 => ... else => ... }`; the subject
value is evaluated once and reused by the arm checks. Inside a one-input
function that uses the default `it` binding, subject-style `when` arms may omit
`it ->` entirely. Single-value arms may use `condition => value`; block arms
remain available for multi-statement bodies. Condition expressions lower to LLVM
branches and phi nodes, not runtime dispatch. The parser treats `true` and
`false` as `Bool` literals.

`fold` is the second built-in block function.
`1..100 -> fold 0 sum, i { sum + i }` lowers directly to LLVM loop blocks with
an SSA accumulator phi and returns the final accumulator value.

The `sys.io` module is implemented in Sollang under `stdlib/sys/io.slg`.
`stdlib/sys/runtime.slg` declares the lower `sys.runtime.*` intrinsic boundary.
These files use `namespace sys.io`, `namespace sys.runtime`, and
`import sys.runtime as rt` so the module body avoids repeated fully qualified
names. The compiler loads these standard library files before user code and
globally aliases `print`, `println`, and `readInt` to `sys.io.print`,
`sys.io.println`, and `sys.io.readInt`.

The grammar generator is intentionally narrow for the first language slice; it
validates the declared rules and produces the parser shape needed by the
approved syntax.

## Repository Layout

- `examples/01-function-basic-hello.slg`: first runtime function and value-flow
  sample
- `examples/02-function-named-input.slg`: cumulative explicit function
  input-name sample
- `examples/03-flow-call-parens.slg`: cumulative value-flow target `func()`
  sample
- `examples/04-main-omitted-top-level.slg`: cumulative omitted-main and
  `sys.io.print` sample
- `examples/05-function-local.slg`: cumulative local function sample
- `examples/06-expression-arithmetic-comments.slg`: cumulative parentheses,
  arithmetic, and comment sample
- `examples/07-block-each-explicit-item.slg`: cumulative input plus range loop
  sample
- `examples/08-block-each-default-it.slg`: cumulative range loop sample with
  default `it`
- `examples/09-namespace-sys-io.slg`: cumulative `sys.io.readInt` and
  `sys.io.println` sample
- `examples/10-block-argument-omits-parens.slg`: block-function call sample where
  the brace body is the argument and `()` is omitted
- `examples/11-block-function-exec-block-repeat.slg`: executable block argument
  sample using `count -> repeat item { ... }`
- `examples/12-block-function-user-defined-yield.slg`: user-defined
  block-function sample using `block item: Type` and `yield()`
- `examples/13-block-fold-sum.slg`: cumulative integer `fold` sample
- `examples/14-condition-if.slg`: cumulative flow-oriented `if` conditional
  sample
- `examples/15-condition-when.slg`: cumulative `when` expression sample
- `examples/16-condition-when-subject.slg`: cumulative subject-value `when`
  sample
- `examples/17-condition-when-range.slg`: cumulative subject-value range-arm
  `when` sample
- `examples/18-condition-when-compact.slg`: cumulative expression-body and
  compact `when` sample
- `examples/19-stdlib-random-file-demo-generate.slg`: small verification
  generator using the sorted bucket algorithm
- `examples/20-stdlib-file-demo-query.slg`: small nearest-value query sample
- `examples/21-stdlib-random-file-100m-generate.slg`: full sorted 100,000,000
  integer binary-file generator
- `examples/22-stdlib-file-100m-query.slg`: nearest-value query over the full
  generated integer file
- `examples/23-webassembly-browser.slg`: browser WebAssembly stdout sample
- `examples/24-string-interpolation-dollar.slg`: `$name` and `$(expr)` string
  interpolation sample
- `examples/172-raw-multiline-strings.slg`: triple-quoted raw strings with
  indentation trimming and literal quotes, backslashes, and `$()` text
- `examples/25-arrays-dictionaries.slg`: static array, dynamic array,
  dictionary, and deterministic cleanup sample
- `examples/26-immutable-containers.slg`: immutable dynamic-array and dictionary
  transforms that return new owners
- `examples/28-mutable-indexing.slg`: mutable owner suffixes and checked indexed
  assignment for fixed arrays, growable arrays, and dictionaries
- `examples/29-typed-empty-containers.slg`: typed empty growable array and
  dictionary literals
- `examples/35-mutable-int-dictionary-parameters.slg`: non-owning mutable
  dictionary parameters through flow and direct calls
- `examples/36-return-moved-container-parameters.slg`: returning consumed array
  and dictionary parameters with direct, transformed, `if`, and `when` paths
- `examples/37-readonly-int-dictionary-parameters.slg`: non-owning readonly
  dictionary parameters through nested, flow, and direct calls
- `examples/38-stack-promoted-dynamic-array.slg`: automatic stack placement for
  a small, non-escaping, readonly growable-array literal
- `examples/39-stack-promoted-int-dictionary.slg`: automatic stack placement for
  small, non-escaping, readonly Swiss-table dictionary literals
- `examples/40-nested-stack-slot-reuse.slg`: one function-entry stack slot reused
  by nested branch and loop-local array/dictionary payloads
- `examples/41-inline-function-stack-frame.slg`: a local inline function reusing
  its containing function's entry slot across loop iterations
- `examples/42-fixed-array-placement.slg`: small fixed-array entry placement and
  oversized fixed-array heap placement with deterministic cleanup
- `examples/54-associated-types.slg`: static trait associated-type binding and
  a generic equality constraint specialized to `Item = Int`
- `examples/55-multi-parameter-generics.slg`: two inferred type parameters with
  separate `Int` and `Text` LLVM monomorphizations
- `examples/56-generic-fixed-text-array.slg`: homogeneous fixed `Text` arrays
  with typed indexing and deterministic backing-storage cleanup
- `examples/57-user-value-fixed-arrays.slg`: parametric fixed arrays of copyable
  user structs and payload enums with exact LLVM aggregate layouts
- `examples/58-owned-element-fixed-arrays.slg`: owned struct elements with one
  recursive drop per initialized slot followed by backing-buffer cleanup
- `examples/59-generic-dynamic-text-array.slg`: typed empty `Text` array,
  aggregate-aware growth, indexing, length, and capacity
- `examples/60-generic-dynamic-user-array.slg`: copyable user-struct dynamic
  array with typed push and growth copying
- `examples/61-owned-generic-dynamic-array.slg`: move-only owned elements with
  runtime-length recursive drop
- `examples/62-generic-text-int-dictionary.slg`: `Text` hashing/equality, typed
  lookup/update, capacity growth, and Swiss-table rehash
- `examples/63-generic-int-text-dictionary.slg`: aggregate `Text` values in a
  typed `Int`-keyed dictionary
- `examples/64-owned-generic-dictionary-values.slg`: recursive destruction of
  owned user values stored in dictionary entries
- `examples/65-typed-empty-text-dictionary.slg`: capacity-hinted typed-empty
  `{Text: Text}` construction and mutation
- `examples/66-generic-dictionary-function-contracts.slg`: readonly, `mut`, and
  `move` function contracts for a concrete `{Text: Int}` specialization
- `examples/67-generic-dynamic-array-function-contracts.slg`: readonly, `mut`,
  and `move` function contracts for `[Text; ~]`, including callee-side growth
- `examples/68-owned-array-function-transfer.slg`: move-return of an owned
  user-element array with final recursive drop coverage
- `examples/69-generic-array-each.slg`: type-preserving `each` over fixed Text,
  dynamic user-value, and readonly-borrowed owned-element arrays
- `examples/70-generic-dictionary-iteration.slg`: Swiss live-slot `eachKey` and
  `eachValue` with Text keys, typed user values, and borrowed owned values
- `examples/71-user-defined-dictionary-keys.slg`: copyable nominal keys with
  statically dispatched `Hash.hash` and canonical `Eq.eq` implementations,
  plus contextual lookup syntax such as `map[{ scope: 1, id: 10 }]`
- `examples/88-grammar-table-module.slg`: compiles the generated grammar-table
  module as a separate source file and reads its public metadata
- `examples/browser`: static HTML/JS runner for the WebAssembly sample
- `examples/expected`: expected stdout/stdin fixtures for executable samples
- `stdlib/sys/runtime.slg`: standard library intrinsic boundary declarations
- `stdlib/sys/io.slg`: Sollang implementation of `sys.io` wrappers
- `stdlib/sys/random.slg`: Sollang wrappers for pseudo-random runtime
  intrinsics
- `stdlib/sys/file.slg`: legacy sorted-`Int` helpers plus generic canonical
  scalar `write<T>` and `read<T>` file intrinsics
- `scripts/sollang.ps1`: local build/bootstrap script
- `tools/vscode-sollang`: local VS Code extension for `.slg` syntax
  highlighting
- `syntax/sollang.lexer`: concise lexer rule source
- `syntax/sollang.grammar`: concise parser rule source
- `syntax/generated/sollang_grammar.slg`: checked-in lexer descriptors and
  parser bytecode generated by `sollang grammar build`
- `src/Sollang.Compiler.Generators`: Roslyn incremental source generator
- `src/Sollang.Compiler/Cli`: command line orchestration
- `src/Sollang.Compiler/Lexing`: token model; Lexer and TokenKind are
  generated
- `src/Sollang.Compiler/Parsing`: parser helpers; Parser is generated
- `src/Sollang.Compiler/Syntax`: AST nodes
- `src/Sollang.Compiler/Semantics`: current semantic lowering
- `src/Sollang.Compiler/CodeGen`: common LLVM IR generation plus target
  runtime platform layers
- `src/Sollang.Compiler/Tooling`: LLVM/lld tool integration
- `tests/Sollang.ExampleTests`: expected stdout test runner for samples
- `docs/SPEC.md`: living language specification
- `docs/DECISIONS.md`: decision log

## Notes

This repository does not commit LLVM binaries or generated executables. The
current compiler backend supports Windows x64, Linux x64, and browser
WebAssembly. Linux linking uses WSL and requires a Linux `cc` in the WSL
distribution.
