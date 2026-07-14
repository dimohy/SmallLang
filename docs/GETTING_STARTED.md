# SmallLang Guide

This guide keeps the longer project notes out of the README while preserving the
details needed to build, run, and understand the current SmallLang slice.

SmallLang is in an early compiler-building phase. The implementation is scoped
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
- SmallLang standard library functions `sys.io.print`, `sys.io.println`, and
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
- source-generated lexing from `syntax/smalllang.lexer`
- source-generated parsing from `syntax/smalllang.grammar`
- LLVM IR generation
- Windows x64 executable linking through `clang` and `lld-link`
- Linux x64 executable linking through Windows LLVM object generation and WSL
  `cc`
- browser WebAssembly module linking through `clang` and `wasm-ld`

With the current Windows linker settings, representative executable sizes are
**1,536 bytes** for `01-function-basic-hello.sl` and `05-function-local.sl`,
**2,048 bytes** for `08-block-each-default-it.sl`, and **2,560 bytes** for the
container and sorted-int-file workflow samples.

## Build And Run

The examples are named so a normal filename sort follows the grammar
progression. Start with the basic function/value-flow sample:

```powershell
.\scripts\smalllang.ps1 -Source examples\01-function-basic-hello.sl -Output artifacts\01-function-basic-hello.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\02-function-named-input.sl -Output artifacts\02-function-named-input.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\03-flow-call-parens.sl -Output artifacts\03-flow-call-parens.exe -KeepTemps
```

Top-level statements, local functions, arithmetic, comments, and block functions
are cumulative:

```powershell
.\scripts\smalllang.ps1 -Source examples\04-main-omitted-top-level.sl -Output artifacts\04-main-omitted-top-level.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\05-function-local.sl -Output artifacts\05-function-local.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\06-expression-arithmetic-comments.sl -Output artifacts\06-expression-arithmetic-comments.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\07-block-each-explicit-item.sl -Output artifacts\07-block-each-explicit-item.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\08-block-each-default-it.sl -Output artifacts\08-block-each-default-it.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\09-namespace-sys-io.sl -Output artifacts\09-namespace-sys-io.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\10-block-argument-omits-parens.sl -Output artifacts\10-block-argument-omits-parens.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\11-block-function-exec-block-repeat.sl -Output artifacts\11-block-function-exec-block-repeat.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\12-block-function-user-defined-yield.sl -Output artifacts\12-block-function-user-defined-yield.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\13-block-fold-sum.sl -Output artifacts\13-block-fold-sum.exe -KeepTemps
```

Conditionals are cumulative:

```powershell
.\scripts\smalllang.ps1 -Source examples\14-condition-if.sl -Output artifacts\14-condition-if.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\15-condition-when.sl -Output artifacts\15-condition-when.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\16-condition-when-subject.sl -Output artifacts\16-condition-when-subject.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\17-condition-when-range.sl -Output artifacts\17-condition-when-range.exe -KeepTemps
.\scripts\smalllang.ps1 -Source examples\18-condition-when-compact.sl -Output artifacts\18-condition-when-compact.exe -KeepTemps
```

The sorted-number workflow is also written in SmallLang. For quick verification,
the demo pair uses the same algorithm at 1,000 records:

```powershell
.\scripts\smalllang.ps1 -Source examples\19-stdlib-random-file-demo-generate.sl -Output artifacts\19-stdlib-random-file-demo-generate.exe -KeepTemps
.\artifacts\19-stdlib-random-file-demo-generate.exe
.\scripts\smalllang.ps1 -Source examples\20-stdlib-file-demo-query.sl -Output artifacts\20-stdlib-file-demo-query.exe -KeepTemps
.\artifacts\20-stdlib-file-demo-query.exe
```

The full generator creates 100,000,000 sorted 64-bit integer records in
`artifacts/random-sorted-100m.i64` by choosing one pseudo-random value from each
10-wide bucket in `1..1,000,000,000`:

```powershell
.\scripts\smalllang.ps1 -Source examples\21-stdlib-random-file-100m-generate.sl -Output artifacts\21-stdlib-random-file-100m-generate.exe -KeepTemps
.\artifacts\21-stdlib-random-file-100m-generate.exe

.\scripts\smalllang.ps1 -Source examples\22-stdlib-file-100m-query.sl -Output artifacts\22-stdlib-file-100m-query.exe -KeepTemps
.\artifacts\22-stdlib-file-100m-query.exe
```

Linux x64 output is available through WSL:

```powershell
.\scripts\smalllang.ps1 -Source examples\01-function-basic-hello.sl -Output artifacts\01-function-basic-hello-linux -Target linux-x64 -KeepTemps
wsl --exec /mnt/p/MyWorks/SmallLang/artifacts/01-function-basic-hello-linux
```

Structured async uses the same source syntax on Windows and Linux. For example,
the generic sendable-input coverage can be built and executed through WSL with:

```powershell
.\scripts\smalllang.ps1 `
  -Source examples\238-sendable-async-inputs.sl `
  -Output artifacts\238-sendable-async-inputs-linux `
  -Target linux-x64
wsl --exec /mnt/p/MyWorks/SmallLang/artifacts/238-sendable-async-inputs-linux
```

Async calls start cooperative affine tasks. `await` consumes a task and returns
its value; `cancel` consumes it without producing a value. Cancellation is a
final flow target and does not use parentheses:

```smalllang
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

```smalllang
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

```smalllang
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

```smalllang
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

```smalllang
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

```smalllang
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

```smalllang
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

```smalllang
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

```smalllang
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
generated module exports `smalllang_start` and `memory`, and imports
`env.smalllang_browser_write(ptr, len)` so the page can render stdout text:

```powershell
.\scripts\smalllang.ps1 -Source examples\23-webassembly-browser.sl -Output artifacts\23-webassembly-browser.wasm -Target wasm32-browser -KeepTemps
python -m http.server 5080
```

Open `http://localhost:5080/examples/browser/`.

The first container sample shows static arrays, dynamic arrays, checked
indexing, `fold`, `push`, dictionary `put`, `len`, `capacity`, and deterministic
native cleanup:

```powershell
.\scripts\smalllang.ps1 -Source examples\25-arrays-dictionaries.sl -Output artifacts\25-arrays-dictionaries.exe -KeepTemps
.\artifacts\25-arrays-dictionaries.exe
```

Move-consuming container transforms return a new owner while consuming the
source owner. The sample shows the short same-name form:

```powershell
.\scripts\smalllang.ps1 -Source examples\26-immutable-containers.sl -Output artifacts\26-immutable-containers.exe -KeepTemps
.\artifacts\26-immutable-containers.exe
```

The dictionary hash-table sample exercises update, growth, rehashing, lookup,
and capacity reporting:

```powershell
.\scripts\smalllang.ps1 -Source examples\27-dictionary-hash-table.sl -Output artifacts\27-dictionary-hash-table.exe -KeepTemps
.\artifacts\27-dictionary-hash-table.exe
```

On first use, the script downloads LLVM 22.1.8 into `.tools`. LLVM binaries,
build outputs, and generated executables are intentionally ignored by Git.

Example stdout tests compile and run the samples listed under
`examples/expected`:

```powershell
dotnet run --project tests\SmallLang.ExampleTests\SmallLang.ExampleTests.csproj --no-build
```

During development, repeat `--filter` for name fragments, use `--exact` for a
complete test name, or repeat `--affected` with repository-relative changed
paths. `--suite reference|semantic|selfhost|llvm|fast|full` selects a stable
layer. Add `--skip-bootstrap` only after the Release compiler and generated
grammar table have already been verified in the current checkout:

```powershell
dotnet run --project tests\SmallLang.ExampleTests\SmallLang.ExampleTests.csproj `
  -c Release --no-build -- --filter 219 --filter 220 --skip-bootstrap
```

The runner uses up to eight isolated test workers by default. It starts the
remaining expensive cases first and uses a load-balancing partitioner so a
worker that finishes a short case immediately takes the next remaining case.
Self-host LLVM emitter tests bootstrap one SL compiler driver and then reuse its
native executable for every Windows, Linux, and Wasm fixture. The driver is
rebuilt only when its compiler, manifest, SL sources, or standard library inputs
are newer; a current driver reports `[selfhost bootstrap] REUSE`. Thus the suite
does not compile the same self-host compiler modules once per fixture.
Bootstrap phases are printed as `[bootstrap n/total]`. Every scheduled case is
printed as `[start n/total]`, and every completion is flushed immediately as
`[n/total] PASS|FAIL name (seconds)`, so long LLVM runs never appear idle.
Use `--jobs 1` for deterministic sequential diagnosis or an explicit positive
worker count when measuring another machine. Compiler bootstrap and
grammar-table determinism are still checked once before the parallel section.
An unfiltered run always remains the commit-gate regression check.

Multiple user source files may form one compilation unit. Declarations from all
files are merged after each file independently resolves its namespace and import
aliases. Exactly one file may contain executable top-level statements:

```powershell
.\scripts\smalllang.ps1 `
  -SourcesFile examples\expected\52-multi-file-modules.sources.txt `
  -Output artifacts\52-multi-file-modules.exe
```

The source-list file contains one repository-relative `.sl` path per line.
Direct compiler use accepts the same files as positional arguments.

For a normal project, put the root module in a compact language-shaped
`smalllang.project` manifest:

```smalllang
project {
    name: "compiler"
    root: "src/main.sl"
}
```

Running `smalllang build` without source arguments searches the current
directory and its ancestors for that manifest. `--project` accepts either the
manifest or its directory. Relative roots are resolved inside the project and
may not escape it. Without `-o`, artifacts are written as
`build/<name>.exe`, `build/<name>`, or `build/<name>.wasm` for the selected
target. Command-line target, optimization, LLVM, and output options remain
explicit overrides.

Use `products` when one project produces several executables, and use
`dependencies` for exact local package paths:

```smalllang
project {
    name: "tools"
    products: {
        compiler: "src/compiler.sl"
        formatter: "src/formatter.sl"
    }
    dependencies: {
        syntax: "../syntax"
    }
}
```

`smalllang build --product compiler` selects one product. A project with one
product needs no selection; a project with several reports the sorted choices
instead of silently choosing. Product and dependency names are import-safe
identifiers. Every dependency path is relative to the declaring manifest and
must point directly to a directory or `smalllang.project` file; directory
traversal looking for a coincidental package is not performed.

The dependency key must equal the dependency project's `name`, and that package
must expose a product with the same name. Source imports use that first segment,
such as `import syntax.tree as tree`. Each package can see only its own direct
dependencies; a dependency's dependencies do not become ambient imports of the
parent. The compiler resolves the complete local graph in sorted order and
rejects dependency cycles and duplicate project names before parsing sources.
Versioned registries, Git sources, lock files, and workspaces are not yet part
of this bootstrap format.

When only the root file is supplied, each non-`sys` import is mapped from its
dotted module path to a `.sl` file relative to the root directory. For example,
`import sample.math as math` discovers `sample/math.sl` recursively.
Imported module functions and nominal declarations are internal unless
explicitly exported:

```smalllang
namespace sample.math

public double value: Int -> Int {
    value * 2
}
```

The same rule applies to `public struct`, `public enum`, and `public trait`.
Imported type paths use the import alias, for example `math.Counter`.

The compiler itself targets .NET 11 Preview and uses C# Preview.

## VS Code Extension

SmallLang includes a local VS Code language support extension:

```powershell
Push-Location tools\vscode-smalllang
npx --yes @vscode/vsce package --no-dependencies --allow-missing-repository
code --install-extension .\smalllang-language-support-0.1.2.vsix
Pop-Location
```

The extension registers `.sl`, highlights value-flow syntax, function
declarations, block-function calls, strings with interpolation, comments,
conditionals, types, and operators, and includes snippets for common forms.

See [tools/vscode-smalllang/README.md](../tools/vscode-smalllang/README.md) for
extension-specific notes.

## Pipeline

```mermaid
flowchart LR
    Source[SmallLang source] --> Lexer[Generated lexer]
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

`src/SmallLang.Compiler.Generators` reads `syntax/smalllang.lexer` as an MSBuild
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

The generator reads `syntax/smalllang.grammar` and emits the current recursive
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

The `sys.io` module is implemented in SmallLang under `stdlib/sys/io.sl`.
`stdlib/sys/runtime.sl` declares the lower `sys.runtime.*` intrinsic boundary.
These files use `namespace sys.io`, `namespace sys.runtime`, and
`import sys.runtime as rt` so the module body avoids repeated fully qualified
names. The compiler loads these standard library files before user code and
globally aliases `print`, `println`, and `readInt` to `sys.io.print`,
`sys.io.println`, and `sys.io.readInt`.

The grammar generator is intentionally narrow for the first language slice; it
validates the declared rules and produces the parser shape needed by the
approved syntax.

## Repository Layout

- `examples/01-function-basic-hello.sl`: first runtime function and value-flow
  sample
- `examples/02-function-named-input.sl`: cumulative explicit function
  input-name sample
- `examples/03-flow-call-parens.sl`: cumulative value-flow target `func()`
  sample
- `examples/04-main-omitted-top-level.sl`: cumulative omitted-main and
  `sys.io.print` sample
- `examples/05-function-local.sl`: cumulative local function sample
- `examples/06-expression-arithmetic-comments.sl`: cumulative parentheses,
  arithmetic, and comment sample
- `examples/07-block-each-explicit-item.sl`: cumulative input plus range loop
  sample
- `examples/08-block-each-default-it.sl`: cumulative range loop sample with
  default `it`
- `examples/09-namespace-sys-io.sl`: cumulative `sys.io.readInt` and
  `sys.io.println` sample
- `examples/10-block-argument-omits-parens.sl`: block-function call sample where
  the brace body is the argument and `()` is omitted
- `examples/11-block-function-exec-block-repeat.sl`: executable block argument
  sample using `count -> repeat item { ... }`
- `examples/12-block-function-user-defined-yield.sl`: user-defined
  block-function sample using `block item: Type` and `yield()`
- `examples/13-block-fold-sum.sl`: cumulative integer `fold` sample
- `examples/14-condition-if.sl`: cumulative flow-oriented `if` conditional
  sample
- `examples/15-condition-when.sl`: cumulative `when` expression sample
- `examples/16-condition-when-subject.sl`: cumulative subject-value `when`
  sample
- `examples/17-condition-when-range.sl`: cumulative subject-value range-arm
  `when` sample
- `examples/18-condition-when-compact.sl`: cumulative expression-body and
  compact `when` sample
- `examples/19-stdlib-random-file-demo-generate.sl`: small verification
  generator using the sorted bucket algorithm
- `examples/20-stdlib-file-demo-query.sl`: small nearest-value query sample
- `examples/21-stdlib-random-file-100m-generate.sl`: full sorted 100,000,000
  integer binary-file generator
- `examples/22-stdlib-file-100m-query.sl`: nearest-value query over the full
  generated integer file
- `examples/23-webassembly-browser.sl`: browser WebAssembly stdout sample
- `examples/24-string-interpolation-dollar.sl`: `$name` and `$(expr)` string
  interpolation sample
- `examples/172-raw-multiline-strings.sl`: triple-quoted raw strings with
  indentation trimming and literal quotes, backslashes, and `$()` text
- `examples/25-arrays-dictionaries.sl`: static array, dynamic array,
  dictionary, and deterministic cleanup sample
- `examples/26-immutable-containers.sl`: immutable dynamic-array and dictionary
  transforms that return new owners
- `examples/28-mutable-indexing.sl`: mutable owner suffixes and checked indexed
  assignment for fixed arrays, growable arrays, and dictionaries
- `examples/29-typed-empty-containers.sl`: typed empty growable array and
  dictionary literals
- `examples/35-mutable-int-dictionary-parameters.sl`: non-owning mutable
  dictionary parameters through flow and direct calls
- `examples/36-return-moved-container-parameters.sl`: returning consumed array
  and dictionary parameters with direct, transformed, `if`, and `when` paths
- `examples/37-readonly-int-dictionary-parameters.sl`: non-owning readonly
  dictionary parameters through nested, flow, and direct calls
- `examples/38-stack-promoted-dynamic-array.sl`: automatic stack placement for
  a small, non-escaping, readonly growable-array literal
- `examples/39-stack-promoted-int-dictionary.sl`: automatic stack placement for
  small, non-escaping, readonly Swiss-table dictionary literals
- `examples/40-nested-stack-slot-reuse.sl`: one function-entry stack slot reused
  by nested branch and loop-local array/dictionary payloads
- `examples/41-inline-function-stack-frame.sl`: a local inline function reusing
  its containing function's entry slot across loop iterations
- `examples/42-fixed-array-placement.sl`: small fixed-array entry placement and
  oversized fixed-array heap placement with deterministic cleanup
- `examples/54-associated-types.sl`: static trait associated-type binding and
  a generic equality constraint specialized to `Item = Int`
- `examples/55-multi-parameter-generics.sl`: two inferred type parameters with
  separate `Int` and `Text` LLVM monomorphizations
- `examples/56-generic-fixed-text-array.sl`: homogeneous fixed `Text` arrays
  with typed indexing and deterministic backing-storage cleanup
- `examples/57-user-value-fixed-arrays.sl`: parametric fixed arrays of copyable
  user structs and payload enums with exact LLVM aggregate layouts
- `examples/58-owned-element-fixed-arrays.sl`: owned struct elements with one
  recursive drop per initialized slot followed by backing-buffer cleanup
- `examples/59-generic-dynamic-text-array.sl`: typed empty `Text` array,
  aggregate-aware growth, indexing, length, and capacity
- `examples/60-generic-dynamic-user-array.sl`: copyable user-struct dynamic
  array with typed push and growth copying
- `examples/61-owned-generic-dynamic-array.sl`: move-only owned elements with
  runtime-length recursive drop
- `examples/62-generic-text-int-dictionary.sl`: `Text` hashing/equality, typed
  lookup/update, capacity growth, and Swiss-table rehash
- `examples/63-generic-int-text-dictionary.sl`: aggregate `Text` values in a
  typed `Int`-keyed dictionary
- `examples/64-owned-generic-dictionary-values.sl`: recursive destruction of
  owned user values stored in dictionary entries
- `examples/65-typed-empty-text-dictionary.sl`: capacity-hinted typed-empty
  `{Text: Text}` construction and mutation
- `examples/66-generic-dictionary-function-contracts.sl`: readonly, `mut`, and
  `move` function contracts for a concrete `{Text: Int}` specialization
- `examples/67-generic-dynamic-array-function-contracts.sl`: readonly, `mut`,
  and `move` function contracts for `[Text; ~]`, including callee-side growth
- `examples/68-owned-array-function-transfer.sl`: move-return of an owned
  user-element array with final recursive drop coverage
- `examples/69-generic-array-each.sl`: type-preserving `each` over fixed Text,
  dynamic user-value, and readonly-borrowed owned-element arrays
- `examples/70-generic-dictionary-iteration.sl`: Swiss live-slot `eachKey` and
  `eachValue` with Text keys, typed user values, and borrowed owned values
- `examples/71-user-defined-dictionary-keys.sl`: copyable nominal keys with
  statically dispatched `Hash.hash` and canonical `Eq.eq` implementations,
  plus contextual lookup syntax such as `map[{ scope: 1, id: 10 }]`
- `examples/88-grammar-table-module.sl`: compiles the generated grammar-table
  module as a separate source file and reads its public metadata
- `examples/browser`: static HTML/JS runner for the WebAssembly sample
- `examples/expected`: expected stdout/stdin fixtures for executable samples
- `stdlib/sys/runtime.sl`: standard library intrinsic boundary declarations
- `stdlib/sys/io.sl`: SmallLang implementation of `sys.io` wrappers
- `stdlib/sys/random.sl`: SmallLang wrappers for pseudo-random runtime
  intrinsics
- `stdlib/sys/file.sl`: legacy sorted-`Int` helpers plus generic canonical
  scalar `write<T>` and `read<T>` file intrinsics
- `scripts/smalllang.ps1`: local build/bootstrap script
- `tools/vscode-smalllang`: local VS Code extension for `.sl` syntax
  highlighting
- `syntax/smalllang.lexer`: concise lexer rule source
- `syntax/smalllang.grammar`: concise parser rule source
- `syntax/generated/smalllang_grammar.sl`: checked-in lexer descriptors and
  parser bytecode generated by `smalllang grammar build`
- `src/SmallLang.Compiler.Generators`: Roslyn incremental source generator
- `src/SmallLang.Compiler/Cli`: command line orchestration
- `src/SmallLang.Compiler/Lexing`: token model; Lexer and TokenKind are
  generated
- `src/SmallLang.Compiler/Parsing`: parser helpers; Parser is generated
- `src/SmallLang.Compiler/Syntax`: AST nodes
- `src/SmallLang.Compiler/Semantics`: current semantic lowering
- `src/SmallLang.Compiler/CodeGen`: common LLVM IR generation plus target
  runtime platform layers
- `src/SmallLang.Compiler/Tooling`: LLVM/lld tool integration
- `tests/SmallLang.ExampleTests`: expected stdout test runner for samples
- `docs/SPEC.md`: living language specification
- `docs/DECISIONS.md`: decision log

## Notes

This repository does not commit LLVM binaries or generated executables. The
current compiler backend supports Windows x64, Linux x64, and browser
WebAssembly. Linux linking uses WSL and requires a Linux `cc` in the WSL
distribution.
