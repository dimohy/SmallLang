# Sollang Decision Log

This file records accepted or working decisions so the language design can
evolve without losing context.

## D001 - Specification Before Implementation

Status: accepted
Date: 2026-07-07

Sollang remains in specification mode until the user explicitly asks for
implementation. No compiler, parser, LLVM backend, runtime, or build
infrastructure should be created before that.

## D002 - Compiler Implementation Stack

Status: accepted
Date: 2026-07-07

When implementation begins, the compiler should use the latest .NET and latest
C# Preview features unless a later constraint requires otherwise. Actual SDK,
C# language, and LLVM versions must be verified against official sources before
implementation starts.

## D003 - Native LLVM Output

Status: accepted
Date: 2026-07-07

Sollang compiles through LLVM and ultimately produces highly optimized native
executables. The language and compiler pipeline should be designed around
efficient LLVM lowering rather than treating LLVM as an afterthought.

## D004 - First Program Syntax

Status: working decision
Date: 2026-07-07

The first complete Sollang program is:

```sollang
main {
    name = "dimohy"
    print("Hello, $name")
}
```

The `main` block is explicit, short, and parser-friendly. `name = value` is the
initial local binding form. Full function syntax is not decided yet.

## D005 - `print` As Platform-Bound Primitive

Status: working decision
Date: 2026-07-07

`print` appears as a simple prelude call in source code, but resolves internally
to `core.io.print` and lowers to a platform-specific stdout backend selected by
the target triple.

The design must preserve:

- simple source syntax
- cross-platform correctness
- Unicode-correct Windows console behavior
- efficient static string output
- no silent fallback for unsupported targets
- explicit handling of output failure

## D006 - Bindings Without Declaration Keywords

Status: working decision
Date: 2026-07-07

Local variables are introduced with the smallest readable form:

```sollang
name = "dimohy"
```

There is no `let`, `var`, or declaration keyword in the initial syntax. For now,
the first `name = expression` in a block introduces a binding, and reusing the
same name in the same scope is a compile-time error. Reassignment and mutability
remain open design questions.

## D007 - Minimal String Interpolation

Status: superseded by D046
Date: 2026-07-07

Double-quoted strings originally supported simple binding/path interpolation:

```sollang
"Hello, $name"
```

The initial interpolation form accepts names and reserved path syntax only, not
arbitrary expressions. This keeps tokenization, parsing, semantic analysis, and
LLVM lowering simple while still making the first useful program expressive.

D046 keeps the `$name` shorthand but adds `$(expr)` and removes brace-based
interpolation.

## D008 - Local LLVM Bootstrap

Status: accepted
Date: 2026-07-07

LLVM binaries must not be committed to the repository. The build script
downloads LLVM 22.1.8 into `.tools` when missing and uses that local toolchain
for native code generation. `.tools`, `artifacts`, and build outputs are ignored
by Git.

## D009 - First Native Backend

Status: working decision
Date: 2026-07-07

The first native backend targets Windows x64 and links with `lld-link` without
the C runtime. The generated executable imports only `GetStdHandle` and
`WriteFile` from `kernel32.dll` for the first `print` program. The verified
first executable size is 752 bytes.

## D010 - Modular Compiler Source Layout

Status: accepted
Date: 2026-07-07

Even small requests should keep the compiler modular. The compiler source is
split by responsibility into CLI, Lexing, Parsing, Syntax, Semantics, CodeGen,
and Tooling modules. `Program.cs` must stay as a minimal entry point, not a
container for lexer/parser/codegen/linker implementation.

## D011 - Lexer Source Generation From Sollang Rules

Status: accepted
Date: 2026-07-07

Sollang lexer rules are expressed in `syntax/sollang.lexer`. A Roslyn incremental
source generator in `src/Sollang.Compiler.Generators` reads that rules file and
generates `TokenKind` and `Lexer` during compiler build. This keeps the language
surface concise and regular while producing deterministic C# tokenization code.

## D012 - Parser Source Generation From Sollang Grammar

Status: accepted
Date: 2026-07-07

Sollang parser rules are expressed in `syntax/sollang.grammar`. A Roslyn incremental
source generator reads that grammar file as an MSBuild `AdditionalFiles` input
and emits the current token-to-AST parser during compiler build.

ANTLR, parser combinators, and C# embedded parser generators remain valid future
options, but they are not the best fit for the first Sollang slice. ANTLR adds a
separate grammar toolchain and C# runtime dependency. Parser combinators and
attribute-based C# parser generators keep grammar inside C# code instead of a
small language-owned syntax file. The current source-generator approach keeps
the repo small, dependency-light, modular, and aligned with the existing lexer
generation model.

The first parser generator intentionally supports only the approved initial
grammar shape. Broader grammar features should be added when the language
surface actually needs them.

## D013 - Value-Flow Calls As Preferred Call Style

Status: implemented, superseded in parser shape by D016
Date: 2026-07-07

Sollang adopts `value -> function()` as the preferred call style when a primary
input value flows into a function:

```sollang
"Hello, $name" -> print()
```

This form makes data flow visually explicit. The expression on the left is the
first input to the callable path on the right. For the initial unary case, it is
semantically equivalent to:

```sollang
print("Hello, $name")
```

Parenthesized calls remain valid as a conventional compatibility syntax and for
cases where the value-flow form is not expressive enough. The preferred Sollang
style is value-flow first:

```sollang
result = value -> transform
```

Function type notation should use the same left-to-right direction:

```sollang
print: Text -> Io<Unit>
```

The first implementation lowered unary value-flow calls to the same call AST
shape used by the existing parenthesized call. D016 keeps value-flow as its own
AST node so a final target can bind the result.

## D014 - Initial Integer Addition And Scalar Interpolation

Status: accepted
Date: 2026-07-07

Sollang supports decimal integer literals and left-associative integer `+` in the
current compiler slice:

```sollang
sum = 20 + 22
```

The first numeric model is intentionally narrow. Integer literals evaluate to
signed 64-bit values in the semantic evaluator, and `+` performs checked integer
addition. Mixed string/integer `+`, floating point values, arbitrary precision
numbers, suffixes, and final overflow policy syntax are not decided yet.

String interpolation can display integer bindings:

```sollang
"Number: $sum" -> print()
```

This first numeric step initially allowed the compiler to fold the whole sample
to output bytes. D015 changes the current sample to exercise runtime function
calls and runtime integer-to-decimal output instead.

## D015 - Runtime Function Sample

Status: implemented, superseded as the current sample by D016
Date: 2026-07-07

The D015 sample should not be represented only as one compile-time output
buffer. It used zero-argument functions so the generated LLVM contained real
runtime calls:

```sollang
getName: -> Text {
    "dimohy"
}

getNum: -> Int {
    20 + 22
}

main {
    name = getName()
    num = getNum()
    "Hello, $name. getNum() = $num" -> print()
}
```

The D015 implementation parsed `getName: -> Text { ... }` and
`getNum: -> Int { ... }` as zero-argument expression functions. Semantic
analysis type-checks the function bodies and main bindings. The Windows LLVM
backend emits `@sollang_fn_getName`, `@sollang_fn_getNum`, runtime `i64` addition,
segmented `WriteFile` output, and a runtime integer decimal conversion helper
instead of one full static output string. D016 is the current sample and
supersedes the `getNum` naming.

## D016 - Flow Binding And One-Input Square Function

Status: implemented, superseded by D036
Date: 2026-07-07

Sollang adopts statement-level value-flow binding for the current sample:

```sollang
getName: -> Text {
    "dimohy"
}

square: Int -> Int {
    it * it
}

main {
    getName() -> name
    7 -> square() -> num
    "Hello, $name. square = $num" -> print()
}
```

`getName() -> name` calls the zero-input function and binds its result to `name`.
`7 -> square() -> num` passes `7` into a one-input function, where the implicit
input binding is named `it`, then binds the returned value to `num`.

The parser now preserves value-flow as `FlowExpression` rather than immediately
lowering it to `CallExpression`. Semantic analysis resolves each target as a
known one-input function, `print`, or a final statement-level binding target.
Unknown intermediate targets are compile-time errors, and flow expression
statements must either end in `print` or bind their result.

The current implementation adds the `*` token/operator, parses
`square: Int -> Int { it * it }`, emits `@sollang_fn_square(i64 %it)`, lowers the
body to `mul nsw i64 %it, %it`, and calls it as `@sollang_fn_square(i64 7)`.
The verified output at D016 time was `Hello, dimohy. square = 49`; the Windows
x64 executable size at D016 time remained 1,088 bytes.

## D017 - Input Primitive And Inclusive Range Loop

Status: implemented, loop spelling superseded by D019
Date: 2026-07-08

Sollang samples are cumulative. New samples should be added alongside earlier
samples instead of replacing `examples/01-function-basic-hello.slg`.

The next implemented sample reads an integer and prints that multiplication
table:

```sollang
main {
    "n = ? " -> readInt() -> n

    each i in 1..9 {
        n * i -> value
        "$n x $i = $value" -> println()
    }
}
```

`readInt` mirrors output value-flow style: a `Text` prompt flows into the input
primitive and the resulting `Int` can flow into a binding target. The
parenthesized form `readInt("n = ? ")` remains valid. Internally, `readInt`
resolves to a selected backend primitive, currently the Windows stdin path using
`ReadFile`; this keeps the source syntax independent from the platform backend
and leaves room for POSIX/WASI input lowering later.

`println` is accepted as the first newline-output convenience. It shares the
same output backend as `print`, then emits one line-feed byte in the current
runtime slice.

The first loop syntax is:

```sollang
each i in 1..9 {
    ...
}
```

This is an inclusive integer range loop. The loop variable is scoped to the loop
body, and bindings created inside the body do not escape the body. The current
implementation lowers the loop to LLVM basic blocks with an SSA phi value for
the loop variable. Descending ranges are not specified yet; a range whose start
is greater than its end executes zero times.

After adding the input and loop runtime, the verified executable sizes are 1,104
bytes for `examples/01-function-basic-hello.slg` and 1,584 bytes for
`examples/07-block-each-explicit-item.slg`.

## D018 - Arrow Binding As Preferred Assignment Direction

Status: accepted
Date: 2026-07-08

Sollang should prefer arrow-oriented binding even for local assignment-like
introductions. New samples should use:

```sollang
expression -> name
n * i -> value
```

instead of leading with:

```sollang
name = expression
value = n * i
```

This keeps local binding aligned with the language's value-flow direction:
values are written first, then flow into their binding target. The existing
`name = expression` form remains accepted as a compatibility syntax for now, but
it is no longer the preferred style for examples or documentation. This decision
supersedes D004/D006 as the current surface-language direction while preserving
those entries as historical context.

## D019 - Arrow Range Loop With Optional Item Name

Status: superseded by D036
Date: 2026-07-08

Sollang's preferred range loop syntax is now flow-oriented:

```sollang
1..9 -> each i {
    n * i -> value
    "$n x $i = $value" -> println()
}
```

The range expression flows into `each`, and the optional identifier after `each`
names the current item. When the identifier is omitted, the loop item is bound as
`it`:

```sollang
1..9 -> each {
    n * it -> value
    "$n x $it = $value" -> println()
}
```

This gives the language two final current forms:

- `start..end -> each { ... }` for the default item binding `it`
- `start..end -> each item { ... }` for an explicit item binding

The older `each item in start..end { ... }` spelling remains accepted as a
compatibility form, but new samples and documentation should prefer the
flow-oriented loop syntax.

## D020 - Optional Function Input Names

Status: implemented
Date: 2026-07-08

Sollang one-input functions now follow the same naming shape as `each`.

When the input name is omitted, the function body receives the value as `it`:

```sollang
square: Int -> Int {
    it * it
}
```

When the input name is supplied after the function name, the body receives the
value through that binding:

```sollang
square n: Int -> Int {
    n * n
}
```

This mirrors the loop forms:

```sollang
1..9 -> each {
    it
}

1..9 -> each i {
    i
}
```

The older `square: Int -> Int { it * it }` form remains valid and is the default
input-binding form. The explicit form `square n: Int -> Int { n * n }` is
available when naming the input improves readability.

## D021 - Built-In Block Functions And Optimized `each`

Status: implemented
Date: 2026-07-08

`each` should be understood as the first built-in block function rather than as
only a hard-coded loop statement:

```sollang
1..9 -> each i {
    n * i -> value
    "$n x $i = $value" -> println()
}
```

Semantically, the range value flows into the block function `each`, `i` names the
block invocation input, and the brace body is the executable block argument.
The default item form follows the same rule:

```sollang
1..9 -> each {
    n * it -> value
    "$n x $it = $value" -> println()
}
```

The compiler now represents this preferred form as a generic block-function call
AST. The current semantic layer only accepts `each` as a block-function target;
future built-ins or user-defined block functions can build on the same model.

This language model must not force inefficient runtime lowering. The Windows
LLVM backend specializes the built-in `each` block function directly into loop
basic blocks with an SSA phi value for the item binding. It does not allocate a
closure, emit a function pointer, or perform dynamic block dispatch for the
current built-in loop.

The older `each item in start..end { ... }` spelling remains accepted as a
compatibility syntax and is converted into the same internal block-function call
shape before semantic analysis.

## D022 - `sys.io` Is A Sollang Standard Library Module

Status: implemented
Date: 2026-07-08

`print`, `println`, and `readInt` should not be compiler-owned special functions
under the `sys.io` name. They are ordinary functions provided by the standard
library and imported globally through aliases:

```text
print   -> sys.io.print
println -> sys.io.println
readInt -> sys.io.readInt
```

The actual `sys.io` module is implemented in Sollang:

```sollang
namespace sys.io

import sys.runtime as rt

print value: Text -> Unit {
    value -> rt.print()
}

println value: Text -> Unit {
    value -> rt.println()
}

readInt prompt: Text -> Int {
    prompt -> rt.readInt()
}
```

The lower runtime boundary is declared separately in the standard library with
`= intrinsic`:

```sollang
namespace sys.runtime

print value: Text -> Unit = intrinsic
println value: Text -> Unit = intrinsic
readInt prompt: Text -> Int = intrinsic
```

This required two syntax additions for the current implementation slice:

- path-qualified function declarations, such as
  `sys.io.print value: Text -> Unit { ... }`
- intrinsic declarations, such as
  `sys.runtime.print value: Text -> Unit = intrinsic`
- namespace declarations and import aliases, such as `namespace sys.io` and
  `import sys.runtime as rt`

The compiler loads `stdlib/sys/runtime.slg` and `stdlib/sys/io.slg` before user
source, then adds only alias entries for `print`, `println`, and `readInt`.
The semantic model resolves `sys.io` through the same function table as user
functions. The Windows LLVM backend inlines standard library wrappers and lowers
only the `sys.runtime` intrinsic boundary to direct `ReadFile`/`WriteFile`
runtime code, so verified IR does not emit `sys.io` function calls.

## D023 - Local Functions Inside Functions

Status: implemented
Date: 2026-07-08

Function declarations may appear at the start of another function body:

```sollang
scale n: Int -> Int {
    double value: Int -> Int {
        value * 2
    }

    addBase value: Int -> Int {
        value + n
    }

    n -> double -> addBase
}
```

Local functions use the same function input naming rules as top-level functions.
Their names are scoped to the containing function and functions nested below it;
they are not added to the global function table and cannot be called from
`main` or unrelated functions.

Local functions can read bindings from the containing function. In the sample
above, `addBase` reads the outer input binding `n`. The current Windows LLVM
backend lowers local functions by inlining their bodies at the call site, so no
global LLVM symbol is emitted for `double` or `addBase`. Recursive local
functions are not part of the current runtime slice.

## D024 - Namespace And Import Aliases For Standard Library Modules

Status: implemented
Date: 2026-07-08

Standard library files should not repeat their full namespace and dependency
paths on every function declaration and runtime call. Sollang now accepts an
optional file-level namespace declaration followed by import declarations:

```sollang
namespace sys.io

import sys.runtime as rt
```

Within a namespaced file, top-level single-segment function declarations are
qualified by that namespace:

```sollang
print value: Text -> Unit {
    value -> rt.print()
}
```

This defines `sys.io.print`. The import alias rewrites `rt.print` to
`sys.runtime.print` before semantic analysis, so the semantic model and LLVM
lowering still work with fully qualified canonical names.

The lower runtime boundary uses the same namespace rule:

```sollang
namespace sys.runtime

print value: Text -> Unit = intrinsic
println value: Text -> Unit = intrinsic
readInt prompt: Text -> Int = intrinsic
```

Local function declarations are not namespace-qualified. They keep the D023
scope rule and remain visible only inside the containing function.

## D025 - Linux x64 Target Through WSL

Status: implemented
Date: 2026-07-08

Sollang now supports a `linux-x64` compiler target in addition to the default
`windows-x64` target:

```powershell
.\scripts\sollang.ps1 -Source examples\01-function-basic-hello.slg -Output artifacts\01-function-basic-hello-linux -Target linux-x64 -KeepTemps
```

The compiler selects the LLVM target triple from the requested target. Windows
continues to emit `x86_64-pc-windows-msvc`, import `GetStdHandle`/`ReadFile`/
`WriteFile`, expose the native entry point as `sollang_start`, and link with
`lld-link` without the C runtime.

Linux emits `x86_64-unknown-linux-gnu`, imports libc `read` and `write`, exposes
the native entry point as `main`, and links through WSL. The link path uses the
downloaded Windows LLVM `clang` to produce a Linux ELF object, then invokes WSL
`cc` to produce the final executable. This keeps one Windows-hosted compiler
bootstrap while still validating the final binary inside Linux.

The Linux runtime backend lowers `sys.runtime.print`, `sys.runtime.println`, and
`sys.runtime.readInt` to direct `write`/`read` calls. Standard library `sys.io`
wrappers are still ordinary Sollang functions and are inlined before the
intrinsic runtime boundary is lowered.

Verification on WSL produced ELF x86-64 executables for `01-function-basic-hello.slg`,
`09-namespace-sys-io.slg`, and `05-function-local.slg`. The `01-function-basic-hello` sample printed
`Hello, dimohy. square = 49`, and the `09-namespace-sys-io` sample accepted stdin
`9` and printed the 9-times table.

## D026 - Shared LLVM Emitter With Target Runtime Platforms

Status: implemented
Date: 2026-07-08

Windows and Linux output must share the same source-language lowering wherever
the semantics are identical. The compiler now routes target-specific LLVM
runtime details through `LlvmRuntimePlatform` implementations instead of keeping
Windows/Linux branches inside the main emitter.

`LlvmEmitter` owns the common lowering:

- function calls and value-flow bindings
- string interpolation
- standard library `sys.io` wrapper inlining
- local-function inlining
- optimized `each` block-function lowering
- runtime integer decimal output
- `readInt` parsing and failure handling

Platform runtime classes own only the target-specific boundary:

- target triple
- native entry point name
- external OS declarations
- stdin/stdout handle setup
- byte-level `sollang_write`
- byte-level `sollang_read_stdin`

`WindowsLlvmRuntimePlatform` supplies `GetStdHandle`, `ReadFile`, and
`WriteFile`. `LinuxLlvmRuntimePlatform` supplies libc `read` and `write`.
The linker layer remains target-specific: `WindowsLinker` uses `lld-link`, and
`WslLinuxLinker` uses Windows LLVM `clang` for the ELF object followed by WSL
`cc` for the final Linux executable.

## D027 - Flow-Oriented Conditionals And Bool Expressions

Status: implemented
Date: 2026-07-08

Sollang adopts conditionals that match the existing value-flow style instead
of adding a separate parenthesized statement form:

```sollang
condition -> if {
    thenBody
} else {
    elseBody
}
```

The left-side value must be `Bool`. When `if` is used only for effects, the
`else` branch may be omitted if the then branch returns `Unit`. When `if`
produces a value, both branches must be present and must produce the same type.
Branch-local bindings stay scoped to the branch body.

For ordered multi-branch value selection, Sollang uses `when`:

```sollang
when {
    score >= 90 { "A" }
    score >= 80 { "B" }
    score >= 70 { "C" }
    else { "F" }
}
```

`when` evaluates arms in order, requires every arm condition to be `Bool`, and
requires an `else` branch in the current expression form. All branch values must
have the same type.

The current `Bool` expression slice includes `true`, `false`, integer
comparisons (`==`, `!=`, `<`, `<=`, `>`, `>=`), short-circuit `and`/`or`, and
unary `not`. Code generation lowers comparisons to LLVM `icmp`, logical
operators to direct control flow where needed, and `if`/`when` expressions to
branches and phi nodes instead of runtime dispatch.

## D028 - Subject-Value When Shorthand

Status: implemented
Date: 2026-07-08

When every `when` arm compares the same value, repeating that value in each
condition is unnecessary noise. Sollang now supports a subject-value form that
keeps the existing flow direction:

```sollang
score -> when {
    >= 90 { "A" }
    >= 80 { "B" }
    >= 70 { "C" }
    else { "F" }
} -> grade
```

This is the preferred style for ordered threshold checks. The full-condition
form remains valid for cases where each arm has a different condition shape.

The subject expression is evaluated once, then reused by each arm comparison.
The current shorthand supports integer comparisons with `==`, `!=`, `<`, `<=`,
`>`, and `>=`. Code generation emits one subject value followed by direct LLVM
`icmp`/`br i1` branch checks and the same phi-based value join used by the full
`when` form.

## D029 - Expression Basics And Line Comments

Status: implemented
Date: 2026-07-08

Sollang now supports the expression basics needed before broader library and
control-flow work:

- parenthesized expressions
- integer `+`, `-`, `*`, `/`, `%`
- unary integer `-`
- line comments beginning with `#`

The parser keeps the usual precedence shape: parentheses, unary operators,
multiplicative operators, additive operators, comparison, logical `and`, and
logical `or`. The current arithmetic slice is integer-only. LLVM lowering emits
`add`, `sub`, `mul`, `sdiv`, and `srem` for the new operations.

`examples/06-expression-arithmetic-comments.slg` verifies parentheses, comments, division,
modulo, and `not (...)` grouping.

## D030 - Integer Fold Block Function

Status: implemented
Date: 2026-07-08

`fold` is the second built-in block function and returns a value:

```sollang
1..100 -> fold 0 sum, i {
    sum + i
} -> total
```

The first expression after `fold` is the initial accumulator value. The first
name is the accumulator binding inside the block, and the second name is the
range item binding. The block must return the next integer accumulator value.

The backend specializes `fold` directly to LLVM loop basic blocks with phi
values for both the current item and accumulator. It does not allocate a runtime
closure, function pointer, or dynamic block-call dispatch. If the range is empty
because the start is greater than the end, the fold expression returns the
initial accumulator value.

`examples/13-block-fold-sum.slg` verifies `1..100 -> fold 0 sum, i { sum + i }` and prints
`sum = 5050`.

## D031 - Subject When Range Arms

Status: implemented
Date: 2026-07-08

Subject-value `when` can now use inclusive integer range arms:

```sollang
score -> when {
    90..100 { "A" }
    80..89 { "B" }
    70..79 { "C" }
    else { "F" }
} -> grade
```

This keeps threshold tables compact when a contiguous range is clearer than a
single-sided comparison. The subject expression is still evaluated once. Each
range arm lowers to two integer comparisons and an `and i1`, followed by the
same `br i1` and phi-based value join used by existing `when`.

`examples/17-condition-when-range.slg` verifies this form.

## D032 - Expected Stdout Example Tests

Status: implemented
Date: 2026-07-08

Executable samples now have a lightweight expected-output test runner. Expected
fixtures live under `examples/expected` as:

- `{sample}.stdout.txt`
- optional `{sample}.stdin.txt`

The runner is a no-dependency .NET console project at
`tests/Sollang.ExampleTests`. It compiles each listed sample through
`scripts/sollang.ps1`, executes the generated Windows binary sequentially, and
compares normalized stdout exactly against the fixture.

Current verified fixtures cover arithmetic/comments, subject `when`, range-arm
`when`, `fold`, `08-block-each-default-it` input/default loop item behavior, and local
functions. The runner is included in `Sollang.slnx` and is invoked with:

```powershell
dotnet run --project tests\Sollang.ExampleTests\Sollang.ExampleTests.csproj --no-build
```

## D033 - Compact Function And When Expression Bodies

Status: implemented
Date: 2026-07-08

To avoid nested braces for single-expression functions whose body is a `when`,
Sollang now allows expression-bodied function declarations:

```sollang
grade: Int -> Text -> when {
    90..100 -> "A"
    80..89 -> "B"
    70..79 -> "C"
    else -> "F"
}
```

This is equivalent to a braced function body with the same final expression.
Local functions remain available only inside braced function bodies because
there is no block in the expression-bodied form.

`when` arms now support single-value shorthand:

```sollang
condition -> value
else -> fallback
```

Block arms remain valid when the arm needs statements before the final value.

Subject-style `when` arms can also omit an explicit subject inside a one-input
function that uses the default input binding `it`. Explicitly named inputs keep
the subject visible:

```sollang
grade score: Int -> Text -> score -> when {
    >= 90 -> "A"
    >= 80 -> "B"
    >= 70 -> "C"
    else -> "F"
}
```

The semantic rule is intentionally narrow: subject-style arms without an
explicit `value -> when` require an integer `it` binding in scope. This keeps the
compact form beautiful for default-input functions while making named-input
data flow explicit. Code generation still lowers `when` to direct comparisons,
branches, and phi joins.

`examples/18-condition-when-compact.slg` verifies both compact forms.

## D034 - Purpose-Oriented Sorted Int File Workflow

Status: implemented
Date: 2026-07-08

Sollang now supports the first large-data workflow requested by the user:
generate 100,000,000 pseudo-random numbers from `1..1,000,000,000`, store them
sorted in a file, and query the value closest to `500,000,000`.

The implementation deliberately avoids opening a general array/sort feature
first. The generator uses a sorted bucket strategy:

```sollang
1..100000000 -> each bucket {
    bucket - 1 -> zeroBased
    zeroBased * 10 -> base
    10 -> randomBelow() -> offset
    base + offset + 1 -> value
    value -> writeInt()
}
```

Each 10-wide bucket contributes one pseudo-random value, so output is sorted and
unique as it is written. This is purpose-fit for the workflow but is not a
uniform sample over all possible 100,000,000-element subsets.

New standard-library surface:

- `seedRandom: Int -> Unit`
- `randomBelow: Int -> Int`
- `openIntWriter: Text -> Unit`
- `writeInt: Int -> Unit`
- `closeIntWriter: -> Unit`
- `openIntReader: Text -> Unit`
- `closestInt: Int -> Int`
- `closeIntReader: -> Unit`

The file format is binary little-endian signed 64-bit records. `writeInt` uses a
global 8,192-record buffer before calling the platform write primitive.
`closestInt` assumes the current reader file is sorted ascending and performs a
binary search through fixed-width random access reads.

Runtime lowering remains split by responsibility:

- common LLVM helper: deterministic LCG, buffered writer, path copy helper, and
  sorted-file closest search
- Windows platform layer: `CreateFileA`, `WriteFile`, `ReadFile`,
  `SetFilePointerEx`, `GetFileSizeEx`, and `CloseHandle`
- Linux platform layer: `open`, `write`, `read`, `lseek`, and `close`

The Windows linker no longer uses the previous `/align:16` and `/filealign:16`
settings because mutable data sections made those ultra-small PE settings fail
at process launch. A no-op `__chkstk` symbol is emitted for this CRT-free
runtime slice because generated stack frames remain intentionally small.

Verification:

- `examples/19-stdlib-random-file-demo-generate.slg` produced
  `artifacts/random-sorted-demo.i64` with 1,000 records / 8,000 bytes.
- `examples/20-stdlib-file-demo-query.slg` printed `closest = 4995`; independent
  PowerShell binary-search verification matched.
- `examples/21-stdlib-random-file-100m-generate.slg` produced
  `artifacts/random-sorted-100m.i64` with 100,000,000 records / 800,000,000
  bytes.
- `examples/22-stdlib-file-100m-query.slg` printed `closest = 500000006`;
  independent verification found candidates `499999991` and `500000006`, so
  the closest difference is `6`.
- The demo generator/query pair also compiled and ran on `linux-x64` through WSL,
  with query output `closest = 4995`.
- `dotnet build Sollang.slnx` passed with 0 warnings and 0 errors.
- `tests/Sollang.ExampleTests` still passed all 7 existing expected-stdout
  tests.

## D035 - Empty Parentheses On Value-Flow Function Targets

Status: implemented
Date: 2026-07-08

Sollang accepted empty call syntax on value-flow function targets:

```sollang
7 -> square() -> num
"Hello, $name. square = $num" -> print()
```

This was only valid immediately after `->`. The parentheses do not carry
arguments; the value on the left remains the argument to the target function.
`7 -> square(7)` is rejected by the parser, and ordinary `square()` outside a
flow target remains a normal zero-argument parenthesized call subject to the
function signature.

The parser records each flow target as a `FlowTarget` with `Path` and
`UsesCallSyntax`. At the time of this decision, semantic analysis and code
generation treated `-> square()` as the same function call as `-> square`, while
a target with `UsesCallSyntax=true` could not become a final flow binding. D036
later changed this so function targets must use `func()`.

`examples/03-flow-call-parens.slg` verifies the accepted syntax. Verification:
`dotnet build Sollang.slnx` passed, `tests/Sollang.ExampleTests` passed all
8 expected-stdout samples, `7 -> square(7)` failed in parsing with an empty
parentheses-only diagnostic, and `7 -> value()` failed semantically as an
unknown function instead of becoming a binding.

## D036 - Function Calls Require Parentheses In Flow Syntax

Status: implemented
Date: 2026-07-08

D035 allowed `-> func()` but still accepted the older `-> func` function-call
target. That left the function/binding distinction visually ambiguous. Sollang
now requires functions to be visibly called:

```sollang
getName() -> name
7 -> square() -> num
"Hello, $name. square = $num" -> print()
```

The old flow-call spelling is now rejected:

```sollang
7 -> square -> num
```

Semantic analysis resolves a target path that names a function only when
`UsesCallSyntax=true`; otherwise it reports that the function target must use
`func()`. A final bare single identifier without `()` remains a binding target,
so `value -> name` still introduces a binding. Bare zero-input function names in
expression/source position are no longer treated as implicit calls; use
`getName()`.

All `.slg` examples and standard-library wrappers were updated to use `func()` for
function targets. Verification: all 19 example `.slg` files compiled, the 8
expected-stdout samples passed, `7 -> square -> num` failed with the required
`square()` diagnostic, and `getName -> name` failed as a missing function-call
parentheses case.

## D037 - Block Arguments Omit Empty Function Parentheses

Status: implemented
Date: 2026-07-08

D036 requires ordinary function targets to use `func()` in value-flow syntax, but
code block arguments are intentionally an exception. When the target is followed
by a brace block, the block itself marks the target as a function-like call:

```sollang
1..3 -> each {
    it -> println()
}

1..9 -> each i {
    "$i" -> println()
}
```

The parser handles `source -> path item? { ... }` before ordinary expression
flows, so this form never becomes an ambiguous bare `-> func` call. At this
decision point the semantic slice supported only the built-in block function
`each`; D038 adds `repeat`. Ordinary flow targets such as `7 -> square -> num`
continue to fail and must be written as `7 -> square() -> num`.

## D038 - Repeat Block Function

Status: implemented
Date: 2026-07-08

To make code-block arguments visible outside range iteration, Sollang now
supports a second built-in block function:

```sollang
3 -> repeat turn {
    "repeat turn $turn" -> println()
}
```

The integer on the left is the count argument, and the brace body is the code
block argument. `repeat` invokes the block with `turn` values `1..count`; when no
item name is given, the default binding is `it`. Counts less than or equal to
zero execute the block zero times. This keeps the D037 rule: block-argument calls
omit `()`, while ordinary value-flow function targets still require `func()`.

## D039 - Grammar-Ordered Example Filenames

Status: implemented
Date: 2026-07-08

Examples are now named with a two-digit order plus the leading grammar topic so
ordinary filename sorting shows the intended learning/progression order:

```text
01-function-basic-hello.slg
02-function-named-input.slg
03-flow-call-parens.slg
04-main-omitted-top-level.slg
...
22-stdlib-file-100m-query.slg
```

Expected stdout/stdin fixtures under `examples/expected` use the same basename as
their source example. `scripts/sollang.ps1` defaults to
`examples/01-function-basic-hello.slg`. New cumulative examples should continue
this naming style instead of appending unnumbered names.

## D041 - User-Defined Block Functions

Status: implemented
Date: 2026-07-08

Block functions are no longer limited to built-ins. A user-defined block function
declares a normal input, a `Unit` return, and a block input:

```sollang
runTimes count: Int -> Unit block turn: Int {
    1..count -> each turn {
        turn -> yield()
    }
}

main {
    3 -> runTimes step {
        "custom block step $step" -> println()
    }
}
```

The declaration body is inlined at the call site. Inside the declaration,
`value -> yield()` invokes the executable block passed by the caller, binding the
yielded value to the caller's block item name. `yield()` is valid only inside a
user-defined block function and must be the final value-flow target.

## D040 - VS Code Language Support Extension

Status: implemented
Date: 2026-07-08

Sollang now includes a local declarative VS Code language support extension
under `tools/vscode-sollang`. It registers `.slg` as `sollang`, contributes a
TextMate grammar for comments, strings, interpolation, function declarations,
flow calls, block-function calls, keywords, types, constants, numbers, and
operators, and provides snippets for `main`, functions, flow calls, `each`,
`repeat`, and `when`.

Install locally with:

```powershell
Push-Location tools\vscode-sollang
npx --yes @vscode/vsce package --no-dependencies --allow-missing-repository
code --install-extension .\sollang-language-support-0.1.2.vsix
Pop-Location
```

## D042 - Browser WebAssembly Target

Status: implemented
Date: 2026-07-09

Sollang now supports a browser WebAssembly target in addition to the native
Windows and Linux targets:

```powershell
.\scripts\sollang.ps1 -Source examples\23-webassembly-browser.slg -Output artifacts\23-webassembly-browser.wasm -Target wasm32-browser -KeepTemps
python -m http.server 5080
```

The compiler emits LLVM IR with the `wasm32-unknown-unknown-wasm` target triple,
uses the existing common LLVM lowering for functions, flow bindings,
interpolation, conditionals, `fold`, and output calls, compiles the IR with
Windows LLVM `clang`, and links the final `.wasm` with `wasm-ld`.

The generated module exports `sollang_start` and `memory`. It imports a single
browser-hosted output boundary:

```text
env.slg_browser_write(ptr, len) -> i32
```

The static runner under `examples/browser` implements this import by reading
UTF-8 bytes from exported linear memory and appending them to the page output.
The current browser target intentionally supports stdout-style text output
first. `readInt` and sorted-int file runtime primitives are present as explicit
failure stubs for this target rather than silently mapping to browser prompts or
storage.

`examples/23-webassembly-browser.slg` is cumulative and also runs as a native
example. Its expected output fixture verifies:

```text
Hello from Sollang WebAssembly
8 squared = 64
1..5 sum = 15
```

## D043 - Arrays Without A Garbage Collector

Status: working decision
Date: 2026-07-09

Sollang's static and dynamic array design should follow Rust's ownership
model rather than a garbage-collected reference model. The core split is:

```text
[T; N]      owned fixed-size array
[T; ~]      owned growable heap array
&[T]        shared borrowed slice view
&mut [T]    exclusive mutable borrowed slice view
```

Static arrays own a fixed number of initialized elements, with the length known
at compile time. They use Rust-like type syntax such as `[Int; 3]`, literals
such as `[1, 2, 3]`, and repeat literals such as `[0; 8]`. Safe indexing is
bounds-checked, and array elements are dropped deterministically when the array
owner is dropped. `[T; N]` means fixed-size inline storage inside the owner, not
"always stack". A local fixed array is a stack allocation candidate, while a
fixed array inside a heap-owned value lives inline inside that heap allocation.
Large fixed arrays can later use an explicit owned heap placement flow such as
`[0; 1000000] -> heap() -> buffer`.

Dynamic arrays use a Rust `Vec<T>`-like internal model, but the Sollang source
surface is `[T; ~]`. The value owns payload storage, length, and capacity. Heap
storage is the normal placement; D063 later permits proven local readonly
literals to use stack storage. Moving a dynamic array moves ownership of the
buffer. Dropping a dynamic array deallocates heap-placed storage only. A dynamic
array is not implicitly copied, reference-counted, or garbage-collected.

Dynamic array literals use an open tail marker:

```sollang
[1, 2, 3, ~] => values!
```

Sollang should not use `{ ... }` for dynamic arrays. Braces already delimit
blocks and remain the better future fit for dictionaries or maps. Dynamic arrays
stay in the `[]` syntax family and use `..` to mean open/growable.

Memory leak prevention is a compile-time guarantee for safe Sollang, not a
best-effort runtime convention. If the compiler cannot prove ownership,
lifetime, and drop coverage for an allocation, the program does not compile.
Leak prevention is based on deterministic ownership, not GC:

- every owned array has one owner at a time;
- leaving a drop scope recursively drops initialized elements and frees owned
  heap storage;
- moving an owned value transfers the drop obligation and makes the source
  binding unusable;
- slices never deallocate and cannot outlive the owner they borrow from;
- a dynamic array cannot reallocate while an element or slice borrow into its
  buffer is active;
- safe code does not expose raw `alloc/free` ownership pairs;
- arrays do not use implicit shared ownership, so cyclic ownership leaks are not
  part of the first safe model;
- safe code cannot allocate without immediately capturing the allocation in an
  owned value;
- safe code cannot expose raw `alloc/free`, raw owning pointers, `forget`, or
  `leak`;
- features that cannot preserve compile-time leak freedom remain outside the
  safe language surface until a static model exists.

Borrowed slices are the common parameter type for read-only array APIs. A
function that only reads elements should prefer `&[T]` so it can accept a static
array, dynamic array, or sub-slice without taking ownership. Mutating APIs that
can change dynamic array length or capacity require `&mut [T; ~]`.

Sollang should introduce explicit mutable bindings with the existing
flow-first binding direction:

```sollang
[Int; ~] => values!
values! -> push(10)
99 => values![1]
```

Array support should also extend value-flow target calls to allow additional
arguments. The value on the left remains the primary first argument, and
parentheses on the target may contain extra arguments:

```sollang
values! -> push(10)
values! -> reserve(1024)
```

The callee signature decides whether the flowed value is moved, shared-borrowed,
or mutably borrowed. For example, `len` can accept `&[T]`, while `push` accepts
`&mut [T; ~], T`.

The first implementation slice should stay narrow: `Int` static arrays,
read-only indexing, array `each`/`fold`, and `value => name!`. `[Int; ~]`,
mutable indexing, slice borrowing, and dynamic array growth can follow once the
compiler has the basic ownership and borrow checks in place. `pop`, `get`, and
fallible allocation APIs should wait until `Option` and `Result` exist.

The detailed draft is in `docs/ARRAYS.md`.

## D044 - Web-Reviewed Array And Flow Call Syntax Direction

Status: working decision
Date: 2026-07-09

Follow-up web research confirms that the D043 array shape is the right base:
Rust's `[T; N]`, borrowed slices, and `Vec<T>` ownership model are the strongest
mainstream reference points for a no-GC static/dynamic array design. However,
Sollang's safe surface must be stricter than Rust because Rust explicitly
allows leak-safe constructs such as `mem::forget` and can leak through
reference-count cycles. Sollang should therefore keep safe `forget`, safe
`leak`, raw owning allocation, implicit shared ownership, and unproven cyclic
ownership out of the safe language surface.

Zig is a useful allocator reference because it makes allocation explicit and
keeps memory-management responsibility visible, but that is not enough for
Sollang's goal: safe Sollang must statically prove owner/drop coverage
rather than leaving leak prevention to programmer discipline or test-time leak
detection. Austral's linear-resource checking is a better reference for the
strict part of the design: values that own resources must be consumed exactly
once, and a function cannot return while owned linear resources remain
unconsumed.

The same research argues against using `func!` as the ordinary function-call
marker. Rust already uses `name!(...)` for macros, Elixir uses trailing bang for
raising function variants, and Julia uses trailing bang for mutating functions.
Sollang should avoid assigning ordinary function-call meaning to `!`.

The preferred next syntax direction is to remove empty parentheses from
value-flow calls when the left value is the only explicit input:

```sollang
getName() => name
7 -> square => num
values -> len => count
```

Parentheses remain useful when additional arguments are present:

```sollang
values! -> push(10)
values! -> reserve(1024)
```

This supersedes the design direction of D036 for future work. The important
correction is that `->` must not also mean binding. Result binding is separated
to `=>`, so `->` can remain a fluent pipeline/call operator and `=>` can mean
binding, definition, or pattern-result resolution.

## D045 - Fluent Flow Calls And Fat-Arrow Binding

Status: implemented
Date: 2026-07-09

Sollang now gives the two arrows separate jobs:

```text
->   flow/apply/transform
=>   bind/define/resolve
```

The parser accepts receiver-only value-flow calls without empty parentheses:

```sollang
7 -> square => num
"Hello, $num" -> println
```

The previous empty-parentheses flow form remains accepted as compatibility
syntax for a function that receives the flowed value:

```sollang
7 -> square() => num
```

Statement-level bindings now use `=>`:

```sollang
getName => name
n * i => value
1..100 -> fold 0 sum, i {
    sum + i
} => total
```

The old implicit final flow binding form is no longer part of the preferred
semantic model. A bare target after `->` is resolved as a flow target, not as a
new binding. This keeps `->` visually close to fluent APIs and functional
pipelines, while avoiding the ambiguity where `value -> name` might be read as
either a call or a binding.

Single-expression function bodies and compact `when` arms now prefer `=>`:

```sollang
square: Int -> Int => it * it

grade: Int -> Text => when {
    90..100 => "A"
    else => "F"
}
```

The implementation changed the lexer, generated parser logic, semantic flow
checking, LLVM emission, standard library source, and examples. The parser still
accepts old `-> expression` function bodies and `value -> function()` flow
targets as compatibility syntax in this slice.

## D046 - Dollar String Interpolation And Literal Braces

Status: implemented
Date: 2026-07-09

String interpolation now uses `$name` for the common identifier case and
`$(expr)` for general expressions:

```sollang
"Hello, $name"
"next = $(score + 1)"
"object = { name: $name, score: $score }"
```

Literal `{` and `}` characters in string literals are ordinary text and do not
need an escape form. This intentionally removes brace-based interpolation from
the preferred language surface because braces are common in JSON-like text,
CSS-like text, block syntax, and future dictionary/set syntax.

The parser represents interpolation as expression segments. `$name` is parsed
as a `NameExpression`, while `$(expr)` is parsed by the same expression parser
used by normal Sollang source. Semantic analysis checks that interpolated
expressions are displayable (`Text` or `Int` in the current slice), and LLVM
emission writes each interpolated value with the normal runtime value output
path.

## D047 - First Int Containers With Deterministic Native Drop

Status: implemented
Date: 2026-07-09

Sollang now has the first `Int` container slice:

```sollang
[1, 2, 3] => numbers
[10, 20, ~] => values!
{ 1: 100, 2: 200 } => scores!
```

The implemented surface includes fixed `Int` arrays, growable `Int` arrays,
`{Int: Int}` dictionaries, checked indexing, `len`, `capacity`, dynamic-array
`push(value)`, dictionary `put(key, value)`, array `each`, array `fold`, and
`value => name!` mutable owner bindings.

Heap-owning containers must be created directly at a binding site in this
slice. This is intentionally strict: the compiler must know the owner so it can
emit exactly one deterministic drop at scope exit. Windows and Linux native
targets lower dynamic arrays and dictionaries through explicit allocation/free
runtime primitives. Browser WebAssembly rejects heap-owning containers until a
linear-memory allocator exists for that target.

The D047 implementation was not the final container model. Generic element
types, slices, ownership moves, container parameters/returns, and full nested
drop scopes were left as future work at that point.

## D048 - Move-Consuming Container Transforms Return New Owners

Status: implemented
Date: 2026-07-09

`!` on an owning binding name means that owner may be changed in place. It does
not mean Sollang objects are mutable by default. Immutable bindings remain the
default:

```sollang
[1, 2, ~] => values
values -> append(3) => values
values -> updated(0, 9) => values
```

The implemented immutable operations are:

- `array -> append(value) => nextArray`
- `array -> updated(index, value) => nextArray`
- `dictionary -> updated(key, value) => nextDictionary`

These operations consume a named source owner and bind the moved owner as the
result. After the transform, the source binding is no longer live. The target
may reuse the same name, as in `values -> append(3) => values`, because the old
owner is consumed before the new owner is bound.

The native lowering reuses storage when it can:

- dynamic-array `append` reuses spare capacity or grows/free-replaces the
  buffer when full
- dynamic-array `updated` performs a bounds check and writes into the moved
  buffer
- dictionary `updated` reuses the existing `put` path, updating in place or
  appending/growing as needed

These are not structure-sharing persistent collections yet, because implicit
shared ownership would complicate compile-time leak freedom.

The result of `append` or `updated` must be bound directly with `=>`. It cannot
be used as an anonymous intermediate flow target, because a heap-owning
temporary without a stable binding would not yet have a proven drop point in
the current compiler slice.

## D049 - Functional First Container Transforms Must Carry Optimization Notes

Status: implemented
Date: 2026-07-09

The first `append` and `updated` implementation copied initialized container
storage into a new owner. That was intentionally a functional-first
implementation, not the final speed or memory model. It has now been replaced
with move-consuming lowering for unique owned containers.

The old tradeoff was explicit:

- Copying on every immutable `append` is correct but can be O(n) per append.
- Repeated append through this path can become O(n^2).
- Dictionary `updated` used to copy all entries before changing one entry.
- This was accepted only to establish source-level immutable owner semantics,
  checked access, and deterministic drop behavior before move/borrow checking
  and shared structural nodes exist.

Implemented optimization direction:

- `values -> append(3) => nextValues` consumes `values`; after that move,
  `values` is unusable unless the same name is immediately rebound.
- If the buffer has capacity, lowering appends in place and transfers ownership
  to `nextValues`; if capacity is full, it grows and frees the old allocation.
- Dynamic-array `updated` and dictionary `updated` mutate the moved owner in
  place where possible.

Remaining future direction:

- Preserve `push`, `put`, and indexed assignment as the explicit `=> name!`
  in-place mutation surface.
- Add builder/transient containers for efficient bulk construction while still
  producing immutable final owners.
- If multiple immutable versions must remain alive and share storage, introduce
  a separate persistent container design rather than silently turning ordinary
  growable arrays into shared structures. HAMT/RRB-vector-style designs are
  candidates, but they require an explicit static ownership/drop story in
  Sollang because there is no garbage collector.
- Add benchmarks before replacing the lowering: repeated append, random update,
  iteration/fold throughput, and dictionary update/lookup.

General coding rule: a feature may be implemented functionally first, but if
speed or memory work is intentionally deferred, the implementation must leave a
durable optimization note in the repo that names the tradeoff and the intended
follow-up direction.

## D050 - Dictionaries Use Scalar Swiss-Style Hash Tables

Status: implemented
Date: 2026-07-09

Sollang's `{Int: Int}` dictionary lowering no longer uses a contiguous
key-value buffer with linear search. The runtime representation is now one heap
allocation owned by the dictionary handle:

```text
base pointer
len
capacity

allocation layout:
  control bytes: capacity bytes
  padding: align entries to 8 bytes
  entries: capacity slots, each slot = key i64 + value i64
```

Control byte `0` means empty. A full slot stores a nonzero 7-bit `h2`
fingerprint derived from the key hash. Lookup hashes the key, chooses the start
slot from `h1`, probes linearly, filters candidates by `h2`, and compares the
actual key only for matching fingerprints. This follows the SwissTable family
shape used by Rust/hashbrown, Abseil, and Go's newer map design, but the first
Sollang implementation scans scalar control bytes instead of SIMD groups.

`put` and dictionary `updated` share the same path:

- existing keys update in place
- missing keys insert into the first empty probed slot
- insertion grows when `(len + 1) / capacity` would exceed 75%
- growth doubles capacity, allocates a new single buffer, rehashes live entries,
  and frees the old buffer

This preserves the no-GC ownership story: moving the dictionary moves one base
pointer and one drop obligation, and scope exit frees exactly that allocation.
The next performance step is target-specific grouped/SIMD control-byte probing,
not another semantic change.

## D051 - LLVM Emitter Name And Partial Boundaries

Status: implemented
Date: 2026-07-09

The main LLVM emitter was renamed from `ConsoleLlvmEmitter` to `LlvmEmitter`.
The old name was no longer accurate after the same emitter became responsible
for Windows, Linux, browser WebAssembly, heap-owning containers, file/runtime
intrinsics, and target-independent source-language lowering.

The class is intentionally split into partial files by responsibility:

- `LlvmEmitter.cs` keeps shared state, construction, and the top-level `Emit`
  orchestration.
- `LlvmEmitter.FunctionDeclarations.cs` emits user function definitions.
- `LlvmEmitter.FunctionCalls.cs` handles function-call lowering and inlining.
- `LlvmEmitter.Statements.cs` emits main statements and block-function calls.
- `LlvmEmitter.Expressions.*.cs` emits scalar expressions, conditionals,
  folds, and phi construction.
- `LlvmEmitter.Flow.cs` owns value-flow lowering.
- `LlvmEmitter.Containers*.cs` owns array/dictionary layout, indexing,
  ownership-moving transforms, and hash-table operations.
- `LlvmEmitter.Runtime*.cs` owns runtime helper IR and runtime intrinsic calls.
- `LlvmEmitter.Utilities.cs` keeps local runtime value records and small shared
  helpers.

This is a structural refactor only. It does not change emitted IR semantics.
Future emitter work should add code to the closest partial by behavior instead
of growing one monolithic file again.

## D052 - Bang-Suffixed Mutable Owners And Indexed Assignment

Status: implemented
Date: 2026-07-10

Mutable owner bindings now use a `!` suffix on the local name:

```sollang
[Int; ~] => values!
values! -> push(10)
99 => values![0]
```

The suffix is part of the binding name. That makes mutability visible at every
use site, instead of requiring a reader to remember that an earlier declaration
used a hidden modifier. The older modifier-before-name spelling is removed from
the language surface rather than kept as compatibility syntax.

The choice follows the broad convention used by Julia, Ruby, Scheme, and
Clojure-family code where `!` marks destructive mutation or stateful change,
but applies it to Sollang's mutable owner name instead of ordinary function
names. This avoids spending `!` on normal calls and keeps `-> push` readable as
a receiver operation while the receiver itself carries the mutation signal.

Indexed assignment is now implemented for current `Int` containers:

```sollang
[1, 2, 3] => fixed!
99 => fixed![1]

[10, 20, ~] => values!
77 => values![1]

{ 1: 100, 2: 200 } => scores!
250 => scores![2]
```

Array indexed assignment performs the same bounds check as indexed reads.
Dictionary indexed assignment updates an existing key and traps if the key is
missing; insertion remains the job of `put`, and owner-returning insert/update
remains the job of `updated`.

## D053 - Typed Empty Int Containers

Status: implemented
Date: 2026-07-10

Sollang now supports typed empty literals for the current `Int` container
slice:

```sollang
[Int; ~] => values!
{Int: Int} => scores!
```

`[Int; ~]` creates an empty growable `Int` array owner with length and capacity
zero. It gives the source surface a stable typed form before generic arrays
arrive.

`{Int: Int}` creates an empty `{Int: Int}` dictionary owner with the initial
hash-table allocation. This removes the previous need to seed dictionaries with
a dummy entry such as `{ 0: 0 }` before calling `put`.

Only `Int` element/key/value types are accepted in this slice. Generic
`[T; ~]` and `{K: V}` remain future work.

## D054 - Tilde Growable Array Marker And Capacity Hints

Status: implemented
Date: 2026-07-10

Growable arrays now use `~` instead of the earlier array-specific `..` marker:

```sollang
[Int; ~] => values!
[Int; 1024~] => buffered!
[1, 2, ~] => seeded!
{Int: Int; 1024~} => scores!
```

`~` means the container is open/growable. `N~` means the initial capacity should
be at least `N` elements while the initial length remains zero for typed empty
containers. The parser no longer accepts the previous bracket-dot-dot array
forms. The `..` token remains reserved for inclusive ranges such as `1..9`.

## D055 - Block-Local Drop Scopes For Owned Containers

Status: implemented
Date: 2026-07-10

Heap-owning containers may now be created inside nested blocks. The compiler
drops block-local growable arrays and dictionaries at the end of the block:

```sollang
1..3 -> each i {
    [Int; 2~] => row!
    row! -> push(i)
}
```

When the block's final expression returns a block-local owner, the block does
not drop that owner. Ownership and the drop obligation move to the surrounding
binding:

```sollang
true -> if {
    [Int; 2~] => values!
    values! -> push(10)
    values!
} else {
    [Int; 2~] => values!
    values! -> push(20)
    values!
} => selected!
```

The implementation is intentionally conservative. A block result may move a
growable array or dictionary created inside that block, or the result of a
move-consuming transform from such a block-local owner. Moving an owner from an
outer scope through an inner block result is rejected for now, because that
would create an implicit alias unless broader move analysis consumes the outer
binding. Static array block results are also rejected in this slice.

## D056 - Owned Container Function Returns

Status: implemented
Date: 2026-07-10

User functions may now return growable array and dictionary owners:

```sollang
makeValues: -> [Int; ~] {
    [Int; 4~] => values!
    values! -> push(10)
    values!
}

main {
    makeValues() => values!
}
```

The function body may contain statements before its final expression. If the
final expression returns a heap-owning container, the function does not drop
that owner; the drop obligation moves to the caller. The caller must bind the
returned owner directly so the compiler has a deterministic drop point.

The same rule applies to `{Int: Int}`. Function return values use small runtime
handles containing pointer, length, and capacity. Anonymous use of a returned
owned container as a flow source, such as `makeValues() -> len`, is rejected in
this slice because no owner binding would exist to receive the drop obligation.

## D057 - Owned Container Function Parameters

Status: implemented
Date: 2026-07-10

User functions may now accept growable array and dictionary owners:

```sollang
sumValues values: move [Int; ~] -> Int {
    values -> fold 0 sum, value {
        sum + value
    }
}

main {
    [1, 2, 3, ~] => values!
    values! -> sumValues => total
}
```

Passing `move [Int; ~]` or `move {Int: Int}` moves ownership into the callee.
The caller's source binding is removed after the call, and the callee owns the
drop obligation for that parameter. Both direct calls such as
`sumValues(values!)` and value-flow calls such as `values! -> sumValues` are
supported.

This slice is intentionally move-only and explicit. A heap-owning container
parameter without `move`, such as `[Int; ~]`, is rejected so ordinary parameter
syntax remains available for readonly views.

## D058 - Readonly Int View Function Parameters

Status: implemented
Date: 2026-07-10

User functions may now accept `[Int]` as a non-owning readonly view:

```sollang
sumValues values: [Int] -> Int {
    values -> fold 0 sum, value {
        sum + value
    }
}

main {
    [Int; 4~] => values!
    values! -> push(10)
    values! -> push(20)

    values! -> sumValues => total
    values! -> len => count
}
```

`[Int]` does not own or drop storage. Static `Int` arrays and growable `Int`
arrays can be passed as `[Int]` by lowering them to a small `{ptr, len}` view.
The caller keeps ownership, so the source binding remains usable after the
call. The callee can use indexing, `len`, `each`, and `fold`; mutation and
owner-moving operations remain unavailable on the view.

The view is deliberately limited to function input in this slice. Returning or
storing `[Int]` is rejected until the compiler has a broader borrow-lifetime
model that can prove the view cannot outlive its owner.

## D059 - Mutable Growable Int Array Function Parameters

Status: implemented
Date: 2026-07-10

User functions may now accept `mut [Int; ~]` as a non-owning mutable borrow:

```sollang
addTail values: mut [Int; ~] -> Unit {
    values -> push(30)
}

main {
    [Int; 4~] => values!
    values! -> push(10)
    values! -> addTail
    values! -> len => count
}
```

The caller must pass a named mutable growable-array owner such as `values!`.
The callee receives access to the caller's mutable owner handle, so operations
such as `push` and indexed assignment update the original owner without moving
ownership. The caller keeps the owner after the call.

The implementation lowers the mutable borrow to a small runtime handle
containing addresses of the caller's pointer, length, and capacity slots. The
callee registers those addresses as its local mutable slot, and borrowed
parameters are excluded from deterministic drop emission so the owner is still
dropped exactly once by the caller.

This initial slice supports mutable borrows only for `[Int; ~]`. D060 extends
the same model to `{Int: Int}` dictionaries. Nested borrow conflict tracking and
returning or storing mutable borrows remain future work.

## D060 - Mutable Int Dictionary Function Parameters

Status: implemented
Date: 2026-07-10

User functions may accept `mut {Int: Int}` as a non-owning mutable borrow:

```sollang
addScore scores: mut {Int: Int} -> Unit {
    scores -> put(3, 300)
}

main {
    {Int: Int; 4~} => scores!
    scores! -> put(1, 100)
    scores! -> addScore
    scores! -> len => count
}
```

As with `mut [Int; ~]`, the caller must pass a named mutable owner and keeps
ownership after the call. The callee may use `put` and indexed assignment, may
observe the updated pointer, length, and capacity after hash-table growth, and
never drops the borrowed dictionary.

Both mutable container kinds use the same runtime handle containing addresses
of the caller's pointer, length, and capacity slots. The bound parameter keeps
its concrete container type, so array-only operations such as `push` and
dictionary-only operations such as `put` remain statically separated.

## D061 - Returning Consumed Container Parameters

Status: implemented
Date: 2026-07-10

A function may return its own `move [Int; ~]` or `move {Int: Int}` input rather
than dropping it at function exit:

```sollang
appendTail values: move [Int; ~] -> [Int; ~] {
    values -> append(30) => values
    values
}

main {
    [1, 2, ~] => values
    values -> appendTail => values
}
```

Direct return, same-name rebinding after `append` or `updated`, direct calls,
and value-flow calls all transfer the drop obligation to the caller's result
binding. The caller's source owner becomes unavailable as soon as the call
consumes it. Ignoring an owned result remains a compile-time error.

For `if` and `when` results, every return branch must transfer the same move
input or no branch may transfer it. Mixed paths are rejected because an
unconditional function-exit drop would otherwise either leak or double-drop.
When all paths transfer the input, LLVM emission omits the parameter drop and
returns the merged owner value; otherwise normal function-exit cleanup remains.

## D062 - Readonly Dictionary Parameters And Handle-Only Calls

Status: implemented
Date: 2026-07-10

An undecorated `{Int: Int}` function input is a readonly non-owning view:

```sollang
findScore scores: {Int: Int} -> Int {
    scores[2]
}

main {
    { 1: 100, 2: 200 } => scores
    scores -> findScore => score
    scores[1] => stillOwnedHere
}
```

This completes the parameter policy: undecorated input is readonly, `mut`
grants mutation for the call, and `move` transfers ownership. A readonly
dictionary parameter has its own semantic/runtime view type, so `put`, indexed
assignment, `updated`, return, and storage are rejected without relying on
conventions. The callee never emits a drop for the view.

The ABI representation remains `%sollang.int_dictionary = { ptr, i64, i64 }`.
Only this three-word handle is passed by value; the Swiss-style table stays in
its existing owner storage. D064 later permits proven local readonly literals
to use stack storage. The call itself performs no dictionary copy, allocation,
rehash, reference-count update, or free. LLVM is free to pass the handle words
in registers or use stack slots according to the native ABI.

The baseline placement policy is intentionally concrete: local `[Int; N]`
storage is inline, while growable arrays and dictionaries normally keep only
their owner handles in local state and allocate payloads on the heap. D063 and
D064 later add automatic stack placement for proven local readonly literals.
Moving heap-placed containers copies the handle, not the payload. Very large
fixed arrays still need an explicit heap-placement feature or future compiler
placement policy before they should be used as large local values.

As part of D062, typed empty `{Int: Int}` values without a capacity hint now use
the same lazy-allocation rule as `[Int; ~]`: `{ null, 0, 0 }` is stored in the
owner handle, and the first `put` allocates capacity 4. An empty dictionary that
is only inspected or passed as a readonly view therefore performs zero heap
allocations. Explicit capacity syntax such as `{Int: Int; 1024~}` still
preallocates because the programmer requested that tradeoff.

## D063 - Automatic Stack Promotion For Readonly Dynamic Arrays

Status: implemented
Date: 2026-07-10

Small dynamic-array literals keep their existing `[Int; ~]` source type and
syntax, but their payload may be placed on the stack when the compiler proves
that the owner remains local and readonly:

```sollang
sumValues values: [Int] -> Int {
    values -> fold 0 sum, value { sum + value }
}

main {
    [10, 20, 30, ~] => values
    values -> sumValues => total
    values[1] => middle
}
```

Placement analysis currently promotes a direct, nonempty dynamic-array literal
bound to an immutable top-level local in `main` or a non-inline user function.
Every later use must be checked indexing, `len`, `capacity`, `each`, `fold`, or
a call through a readonly `[Int]` parameter. A mutable binding, `move`,
`append`, `updated`, function-result escape, or any unrecognized owner use keeps
the normal heap representation.

The first implementation applies a cumulative 4096-byte payload budget per
analyzed function frame. It intentionally excludes local/standard-library
inline functions and block-function bodies, where call-site inlining or loop
execution could otherwise place a dynamic `alloca` repeatedly in one frame.
This is an optimizer placement policy, not a source-language fallback.

LLVM lowering uses `alloca [N x i64]` for a promoted payload while preserving
the dynamic-array `ptr`, `len`, and `capacity` interface used by indexing and
readonly slices. Runtime values track whether payload storage is stack or heap;
scope cleanup emits `sollang_free` only for heap storage. Example 38 verifies
both the program output and LLVM requirements: the stack allocation must be
present, while calls to `sollang_alloc` and `sollang_free` must be absent.
Because this path needs no allocator, the same example also compiles for the
browser WebAssembly target.

## D064 - Automatic Stack Promotion For Readonly Int Dictionaries

Status: implemented
Date: 2026-07-10

Small nonempty `{Int: Int}` literals now join dynamic arrays in automatic
storage placement analysis:

```sollang
findScore scores: {Int: Int} -> Int {
    scores[2]
}

main {
    { 1: 100, 2: 200, 3: 300 } => scores
    scores -> findScore => second
    scores -> len => count
}
```

The compiler promotes the literal only when it is bound to an immutable
top-level local in `main` or a non-inline user function and every later owner
use is checked lookup, `len`, `capacity`, or a call through a readonly
`{Int: Int}` parameter. Mutable bindings, `put`, `updated`, `move`, function
result escape, and unrecognized uses retain heap placement.

The promoted Swiss table is one `alloca [N x i8]` aligned to 8 bytes. `N`
includes control bytes, control-to-entry alignment padding, and 16 bytes for
each key/value slot. The compiler zeroes only the control-byte region before
inserting literal entries. Runtime dictionary values track stack versus heap
storage, and deterministic drop emits `sollang_free` only for heap storage.

D063's 4096-byte function-frame budget is now shared by promoted dynamic-array
and dictionary payloads. The dictionary's full table allocation, rather than
only its live entry count, is charged to that budget. Example 39 checks the
expected 72-byte and 136-byte stack blocks and rejects generated calls to
`sollang_alloc` or `sollang_free`. It also compiles for browser WebAssembly
without adding a linear-memory allocator.

## D065 - Lifetime-Based Function Stack-Frame Planning

Status: implemented
Date: 2026-07-10

Stack-promoted dynamic-array and dictionary payloads are no longer allocated at
the source binding's current LLVM block. The semantic placement pass assigns
each candidate a creation position, last-use position, size, and lifetime-end
unit. A linear-scan frame allocator assigns non-overlapping intervals to the
same physical slot and charges only the final slot sizes against the 4096-byte
frame budget.

Every physical slot is emitted once in the function `entry` block as an aligned
`alloca [N x i8]`. Literal construction emits `llvm.lifetime.start`; the last
statement or block-result expression that can use the owner emits
`llvm.lifetime.end`. A loop-local literal therefore reuses the same entry slot
on every iteration instead of executing a new `alloca` inside the loop.

The analysis recursively indexes `if`, `when`, fold bodies, built-in block
function bodies, and loop bodies. Branches occupy disjoint static position
ranges, while an owner live across a complete control expression spans its
nested ranges and prevents unsafe reuse. Storage placement remains conservative:
any mutation, move, growth, owner escape, or unrecognized use retains heap
placement.

Example 40 proves that a 24-byte array, a later 72-byte Swiss dictionary, and a
16-byte loop-local array all reuse `%stack_slot0 = alloca [72 x i8]`. Its LLVM
fixture requires all three lifetime sizes and rejects `%stack_slot1`, allocator
calls, and free calls. Local/standard-library inline function bodies, fixed
arrays, and mutable-container handle slots remain separate follow-up work.

Local functions and standard-library Sollang wrappers are emitted inline and
therefore have no independent runtime frame. Their placement plans are now
merged into each containing non-inline function or `main` frame with dedicated
slot ranges. Repeated inline calls restart and end lifetime on the same entry
slot. If adding an inline plan would exceed the 4096-byte frame budget, its
container candidates conservatively remain heap-placed. Example 41 verifies a
local function called inside a loop with one 16-byte entry slot and repeated
lifetime markers.

Fixed arrays now participate in the same physical frame plan. Small literals
and `[value; N]` repetitions use entry slots even when created in nested loops.
If their complete inline byte size does not fit the remaining planned frame
budget, lowering allocates owned heap storage instead and deterministic drop
frees it. Example 42 verifies a 64-byte fixed array on the stack and a
4,800-byte fixed array on the heap without an `alloca [600 x i64]`.

Mutable dynamic-array and dictionary owners also reserve their three-word
`ptr`/`len`/`capacity` metadata as one 24-byte planned entry slot. Scope drop or
ownership transfer ends that slot's lifetime after the final metadata load.
Example 30 verifies that loop-local mutable owners repeatedly reuse one metadata
slot and that legacy `%mutable_ptr_addr` allocas are absent.

## D066 - Nominal Inline Structs And Static Methods

Status: implemented
Date: 2026-07-10

User-defined `struct` declarations are nominal value types. The semantic model
assigns every declaration a stable compilation-local `TypeId` and stores field
definitions in one type table. Struct literals must initialize every declared
field exactly once; unknown, duplicate, missing, and incorrectly typed fields
are compile errors. Inline recursive cycles are rejected until an explicit
heap reference type can break the size cycle.

```sollang
struct Point {
    x: Int
    y: Int
}

impl Point {
    translated: self -> Self {
        Point { x: self.x + 10, y: self.y + 20 }
    }
}
```

LLVM lowering uses named aggregate types such as
`%sollang.struct.1024 = type { i64, i64 }`. Literals use `insertvalue`, field
reads use `extractvalue`, and user functions pass and return the aggregate
directly. This representation introduces no object header, heap allocation,
reference counting, garbage collection, or vtable.

`impl` methods are registered as type-qualified functions and resolved from the
receiver's nominal type. Both `point.translated` and `point -> translated`
lower to direct statically selected function calls. Readonly `self` is the first
implemented receiver mode; mutable and consuming receivers remain separate
ownership slices. Example 43 verifies nested structs, `Text` fields, dot and
flow method calls, output, LLVM aggregate shape, and absence of allocator/free
calls. Diagnostic fixtures verify complete initialization, field typing, and
recursive-size rejection.

Generated functions use the `nounwind` attribute but no longer carry the
prototype-era `noinline optnone` attributes. Windows `-O3` and Linux/WebAssembly
`-Oz` compilation can therefore inline statically resolved methods, scalarize
aggregate values, eliminate temporary copies, and devirtualize any later
provably concrete dynamic calls.

## D067 - Parenthesis-Free Queries And Payload Enums

Status: implemented
Date: 2026-07-10

Sollang avoids empty call parentheses. A readonly method with no additional
arguments is a computed member and uses uniform member access:

```sollang
point.translated
point -> translated
counter! -> increment
```

Stored fields and computed members share one namespace and cannot have the same
name. Dot syntax is reserved for stored data and readonly query-like members;
mutation and other actions use value-flow syntax. Parentheses remain available
only when a call carries actual arguments. Calling a zero-argument method as
`point.translated()` is a compile error with the canonical spelling in the
diagnostic.

This combines Scala's parameterless uniform-access rule, Swift-style computed
properties, and Sollang's existing flow calls. It keeps query expressions
compact without hiding mutation or ownership transfer behind property syntax.

User `enum` declarations are tagged unions with optional per-variant payloads.
Payload constructors use `Reading.Value(42)`, while payload-free variants use
`Reading.Missing` without empty parentheses. LLVM represents an enum as an
inline tag plus an aligned maximum-size payload area; it has no object header,
heap allocation, or vtable.

Subject `when` supports variant patterns and payload bindings. Omitting `else`
requires every variant exactly once; missing and duplicate variants are compile
errors. An `else` arm is permitted only when explicit patterns do not already
cover every variant. Example 44 verifies `Int`, empty, and `Text` variants,
payload extraction, scalar/text phi results, runtime output, and no allocator
calls.

## D068 - Associated Members And Explicit Self Modes

Status: implemented
Date: 2026-07-10

An `impl` member without `self` is statically associated with its type.
Zero-argument constructors use computed type-member syntax, while constructors
with an actual argument keep parentheses:

```sollang
Point.origin
Point.fromX(5)
```

Readonly `self` remains the default and uses an inline aggregate value. `mut
self` receives an address to an explicitly mutable owner and can update fields
with the normal assignment direction, for example `next => self.value`. Only
mutable struct bindings become addressable stack values; immutable structs stay
in SSA aggregate form. A call such as `counter! -> increment` passes the stack
address directly and introduces no heap allocation.

`move self` consumes the receiver. Inline user values are passed by value, the
source binding is removed after the call, and any later use is a compile error.
This preserves the language-wide rule that readonly is the default while
mutation and ownership transfer are explicit at the declaration and call-flow
boundaries. Example 45 verifies mutable field updates, pointer ABI, consuming
self, use-after-move rejection, and absence of allocator/free calls.

## D069 - Nominal Traits And Monomorphized Type Generics

Status: implemented
Date: 2026-07-10

Traits declare nominal method contracts and are implemented explicitly:

```sollang
trait Measure {
    measure: self -> Int
}

impl Measure for Point {
    measure: self -> Int { self.x + self.y }
}
```

Trait conformance checks method presence, receiver ownership, and return type.
An unambiguous `point.measure` or explicit `point -> Measure.measure` resolves to
the concrete implementation function. No trait metadata, object header, fat
pointer, or vtable is emitted for this static path.

One checked type parameter and optional trait bound are supported on global
functions:

```sollang
identity<T> value: T -> T { value }
measureOf<T: Measure> value: T -> Int { value -> Measure.measure }
```

Every used concrete type produces a separately checked specialization. Trait
bounds are proven before specialization, and the specialized body is then
type-checked with the concrete binding types. LLVM receives direct functions
such as `identity$Point`, `identity$Int`, and `measureOf$Point`; calls do not use
type erasure or runtime dispatch. Example 46 verifies direct trait dispatch and
example 47 verifies multiple monomorphizations plus bound failure diagnostics.

## D070 - Explicit Box And Recursive Drop Glue

Status: implemented
Date: 2026-07-10

`box T` is an explicit owning heap reference. `box value` allocates exactly the
inline size and alignment of `T`; ordinary structs and enums remain inline and
do not acquire an object header. A boxed field or enum payload breaks recursive
inline sizing, so definitions such as `enum Chain { End; More(box Chain) }` are
finite and statically laid out.

Owned values cannot be copied into a second binding. The default function input
is a readonly borrow even when the value transitively contains a box, while
`move box T` and `move` user values transfer ownership and invalidate the source
binding. This keeps readonly access as the unadorned path and makes ownership
transfer explicit.

The compiler emits type-specific internal drop functions for reachable owned
user types. Struct drop glue visits owned fields, enum drop glue switches on the
active tag, and box drop glue recursively drops the pointee before calling
`sollang_free`. These helpers are statically selected by concrete type and do
not use metadata or vtables. Examples 48 and 49 verify single-owner box transfer,
readonly repeated access, recursive enum destruction, copy rejection, and
use-after-move rejection.

## D071 - Explicit Compile-Time Int Value Generics

Status: implemented
Date: 2026-07-11

A global function may declare one compile-time `Int` value parameter:

```sollang
sumFilled<N: Int> value: Int -> Int {
    [value; N] => values
    values -> fold 0 total, item { total + item }
}
```

The value argument is explicit at the fluent call boundary, such as
`7 -> sumFilled<3>`. It is not passed at runtime. Every used value produces a
separately checked and emitted specialization, and symbolic fixed-repeat counts
become LLVM constants in that specialization. Omitting the value argument is a
compile error. Example 50 verifies distinct `3` and `5` specializations and
their fixed LLVM array shapes.

Value parameters also participate in fixed-array input types:

```sollang
fixedLength<N: Int> values: [Int; N] -> Int {
    values -> len
}

[10, 20, 30] -> fixedLength<3>
```

`[Int; 3]` and `[Int; 5]` are distinct compile-time size contracts at this
boundary even though the readonly native ABI remains the compact `{ pointer,
length }` slice pair. The compiler proves that the fixed source length equals
the explicit specialization value before emitting a call. A dynamic array or a
fixed array with another length is rejected. Example 51 verifies the two valid
specializations and a `3` versus `4` size-mismatch diagnostic.

## D072 - Rooted Multi-File Compilation

Status: implemented
Date: 2026-07-11

A compiler invocation may contain multiple user `.slg` files. Every file parses
its own `namespace` and import aliases, then all declarations enter one semantic
compilation unit. Exactly one user file may contain executable top-level
statements; files without such statements are library modules. Example 52
compiles a namespaced library file and a separate root file into one executable
and verifies the direct namespaced LLVM call.

This is the first module-system substrate, not the final package model. The
compiler now follows non-`sys` imports from the root source directory by mapping
`sample.math` to `sample/math.slg`. Discovery is recursive and reports missing
files, declared-namespace mismatch, import cycles with the full chain, and
duplicate module declarations. The next slice adds internal-by-default
visibility and explicit public exports. The design follows Zig's explicit
root-module graph and Swift's module/API boundary while retaining Sollang namespace
and fluent-call syntax.

Module functions are internal by default. A caller in another module may use a
function only when its declaration begins with `public`; same-module calls and
local functions remain available without annotation. The parser records both
the declaration module and visibility, and semantic call resolution compares
them before code generation. Standard-library functions remain externally
visible through their bootstrap status. Example 52 marks `sample.math.double`
public, while the module-internal-access diagnostic proves that importing a
module does not expose its internal implementation functions.

The same internal-by-default boundary now applies to nominal structs, enums,
and traits. Their semantic identity is the module-qualified declaration name,
so two modules may own same-spelled types without collapsing them into one
type. Import aliases resolve in type annotations, `impl` headers, struct
literals, enum constructors, and qualified trait calls. Cross-module use
requires `public`; examples 53 and the internal-type/internal-trait diagnostics
exercise both the exported and rejected paths.

## D073 - Static Associated Types And Equality Constraints

Status: implemented
Date: 2026-07-11

Traits may declare compile-time type members and each implementation must bind
them explicitly:

```sollang
trait Source {
    type Item
    read: self -> Item
}

impl Source for NumberSource {
    type Item = Int
    read: self -> Int { self.value }
}
```

An associated type is part of the static conformance contract; it does not add
a runtime field, metadata pointer, or vtable. Trait method signatures may name
the associated type, and implementation validation substitutes the concrete
binding before comparing the method signature. Generic bounds can require an
equality such as `<T: Source<Item = Int>>`. Monomorphization checks the selected
concrete implementation and rejects a different or missing binding before LLVM
emission. Example 54 verifies static dispatch through the constrained generic;
the associated-type diagnostics verify missing bindings and equality failure.

## D074 - Multi-Parameter Generic Inference

Status: implemented
Date: 2026-07-11

Generic functions may declare two compile-time type parameters. Constraints
that relate them use a separate `where` clause:

```sollang
readAny<T, Item> where T: Source<Item = Item> value: T -> Item {
    value -> Source.read
}
```

The primary type is inferred from the flowed input. The secondary type is
inferred from the selected concrete implementation's associated-type binding.
Every resulting type tuple has its own checked monomorphization and LLVM
signature; no type descriptors are passed at runtime. A secondary parameter
that cannot be inferred is a compile error rather than an arbitrary default.
Example 55 verifies `(NumberSource, Int)` and `(TextSource, Text)` specializations
with different LLVM return ABIs, plus a no-vtable assertion.

## D075 - Typed Fixed-Array Layout Foundation

Status: implemented for `Int` and `Text`; broader `[T; N]` remains active
Date: 2026-07-11

Fixed array literals now infer one homogeneous element type instead of forcing
every element to `Int`. `Text` arrays allocate `N * 16` bytes, store LLVM
`%sollang.text` values, return `Text` from checked indexing, expose `len`, and
release the backing buffer exactly once at owner-scope exit. Mixed element types
are rejected. This establishes the element-layout seam that future inline user
types and recursively owned values will extend; it does not yet claim complete
general `[T; N]` support. Example 56 verifies the `Text` layout, indexed value,
length, and allocation/free pair.

## D076 - Parametric Fixed Arrays For User Values

Status: implemented for copyable inline user values
Date: 2026-07-11

`TypeDefinitionTable` now creates a stable fixed-array type definition per
element `TypeId`. Each definition records element type, inline size, and
alignment. Homogeneous arrays of copyable structs and enums therefore lower to
typed LLVM aggregate GEP/store/load operations, and indexing recovers the exact
element type for subsequent field access or enum matching. The backing buffer
is an owned allocation released once at scope exit.

An element type that transitively owns a box or another heap value is rejected
for now. Accepting it without element-wise recursive drop would permit leaks or
double frees, so this remains outside safe Sollang until the next slice. Example 57
verifies distinct struct and payload-enum array layouts; the owned-element
diagnostic verifies the safety boundary.

## D077 - Recursive Drop For Owned Fixed-Array Elements

Status: implemented
Date: 2026-07-11

Parametric fixed arrays now accept elements that transitively own boxes or other
heap values. At owner-scope exit the emitter loads each initialized element,
calls its existing concrete struct/enum/box drop glue exactly once, and only
then frees the array backing allocation. Array types participate in owned-storage
classification, so rebinding by copy is rejected.

Indexing an owned element is deliberately rejected until move extraction can
invalidate the source slot and adjust drop coverage. Returning a copied aggregate
would create two owners and is therefore not admitted as a temporary shortcut.
Example 58 verifies two element-drop calls followed by one backing free; copy
and owned-index diagnostics verify the static ownership boundary.

## D078 - Parametric Growable Arrays

Status: implemented for scalar and user-value literals plus mutable `push`
Date: 2026-07-11

`TypeDefinitionTable` interns a dynamic-array definition per element `TypeId`,
recording element size and alignment. Typed empty arrays such as `[Text; 2~]`
and growable literals of `Text`, structs, enums, and owned user values retain
their element type through indexing, `len`, and `capacity`.

Mutable `push` checks the exact element type. Growth doubles capacity, allocates
`capacity * elementSize`, copies initialized aggregates with typed LLVM
load/store, frees the old backing buffer without dropping transferred elements,
then stores the new element. Final destruction iterates over runtime length,
calls recursive drop glue for owned elements, and frees the current backing
buffer. A named owned value cannot be pushed by implicit copy; a fresh value is
accepted as a direct ownership transfer until explicit move arguments exist.
Examples 59-61 verify `Text`, copyable struct, and owned struct arrays.

## D079 - Parametric Swiss-Table Dictionaries

Status: implemented for built-in `Int`/`Text` keys and inline value types
Date: 2026-07-11

Dictionary literals infer one homogeneous key type and one homogeneous value
type. `TypeDefinitionTable` interns each `Dictionary<K, V>` specialization and
records key/value size, alignment, value offset, and entry stride. The existing
Swiss-table control-byte scheme remains shared, while entry addressing and LLVM
load/store operations use the concrete K/V layouts.

`Int` keys retain their integer mixer. `Text` keys use deterministic byte-wise
FNV-1a hashing and length-plus-byte equality, so dictionary identity never
depends on text pointer identity. Checked lookup, mutable `put`, load-threshold
growth, and rehash preserve exact key/value types. Growth transfers entries to
the new table before freeing the old allocation. Final destruction walks live
control bytes, recursively drops owned key/value payloads exactly once, then
frees the table.

Examples 62-65 verify `Text -> Int`, `Int -> Text`, owned user-value payloads,
and typed-empty `Text -> Text` dictionaries. User-defined key types remain
closed until static `Hash` and `Eq` trait dispatch is wired into collection
specialization; generic dictionary function contracts and iterators remain the
next collection boundary.

## D080 - Parametric Dictionary Function Contracts

Status: implemented
Date: 2026-07-11

Concrete dictionary types may cross user-function boundaries without erasing
their K/V specialization. Function annotations use `{K: V}` directly. A
default input is a readonly borrow, `mut {K: V}` passes addressable owner-handle
slots, and `move {K: V}` transfers the owner and may return the same concrete
dictionary type.

All specializations share the three-word LLVM handle ABI `{ ptr, len,
capacity }`; key/value TypeIds and entry layouts remain compile-time metadata
and never become runtime descriptors. Readonly parameters are excluded from
drop ownership, mutable borrows update the caller's handle after growth, and
move parameters transfer exactly one final drop obligation. Example 66 verifies
readonly lookup, mutable insertion, and move-return for `{Text: Int}`. Separate
diagnostics reject mutation through a readonly parameter and calls with a
different dictionary specialization.

## D081 - Parametric Dynamic-Array Function Contracts

Status: implemented
Date: 2026-07-11

Concrete `[T; ~]` types now cross user-function boundaries with the same
ownership modes as other owned containers. A default parameter is readonly,
`mut [T; ~]` borrows addressable handle slots, and `move [T; ~]` transfers the
owner and may return the same element specialization.

Every specialization uses the three-word `%sollang.dynamic_int_array` LLVM
handle ABI while the element TypeId, size, alignment, and recursive drop glue
remain compile-time facts. A mutable callee that grows the buffer writes the new
pointer/length/capacity back to the caller's owner slots. A move-return does not
drop the transferred parameter inside the callee; the caller receives the one
drop obligation. Example 67 verifies readonly/`mut`/`move` `[Text; ~]` calls and
growth. Example 68 verifies that an owned user-element array survives a move
round trip and recursively drops its elements exactly once at the final owner.

## D082 - Type-Preserving Array Each

Status: implemented for fixed and dynamic arrays
Date: 2026-07-12

The fluent block iterator `array -> each item { ... }` binds `item` to the
concrete array element type rather than forcing `Int`. Fixed and dynamic
`Text`, struct, enum, box, and other inline values use their typed LLVM load
path on every iteration. Range iteration remains `Int`.

An element that transitively owns storage is a readonly borrow from its array
slot for one block invocation. Block cleanup does not drop that borrowed item;
the array remains the sole owner and later performs its normal element-wise
recursive destruction. Existing semantic copy restrictions prevent rebinding
the borrowed owner by value. Example 69 verifies fixed `Text`, dynamic
copyable-struct, and dynamic owned-struct iteration without runtime type tables
or vtables.

## D083 - Swiss-Table Key And Value Iteration

Status: implemented
Date: 2026-07-12

Dictionaries expose two fluent block iterators:

```sollang
symbols -> eachKey key { ... }
symbols -> eachValue value { ... }
```

Both scan the concrete table capacity and execute the block only for slots
whose Swiss control byte is nonzero. `eachKey` binds K and `eachValue` binds V,
using the specialization's exact offset, alignment, and LLVM load type. Table
order is deterministic for a given hash implementation but is not insertion
order and is not part of the source-level contract.

Transitively owned keys or values are readonly per-slot borrows. Iterator block
cleanup never drops them; the dictionary remains the sole owner and its final
live-control-byte scan performs recursive destruction. Example 70 verifies
`Text` keys, copyable struct values, and owned struct values. Diagnostics reject
owned iterator-item copying and dictionary iterators on non-dictionary sources.

## D084 - Static Hash And Equality-Key Dispatch

Status: implemented for copyable nominal keys
Date: 2026-07-12

A copyable struct or enum may become a dictionary key by implementing two
static traits with exact signatures:

```sollang
trait Hash { hash: self -> Int }
trait Eq { eq: self -> Int }
```

`Hash.hash` returns the table hash. `Eq.eq` returns a canonical integer for the
key's equality class; two keys are equal exactly when those canonical integers
match. Implementations must obey the usual hash law: equal keys return the same
hash. This canonical-key form fits Sollang's current one-input function ABI. A later
general multi-argument function slice may add the familiar
`equals(self, other)` surface without changing dictionary storage.

Dictionary specialization verifies both conformances at compile time. LLVM
lookup, insertion, update, and rehash directly call the concrete impl functions;
there is no type descriptor, interface object, or vtable. Owned nominal keys are
rejected until temporary-key move/drop coverage is explicit. Example 71 verifies
nominal-key insertion, equality-based replacement, lookup, growth, and static
calls. Missing or incorrectly typed contracts are compile errors.

## D085 - Contextual Nominal-Key Literals

Status: implemented for dictionary indexing and literals
Date: 2026-07-12

When a dictionary's key type K is a nominal struct, an index expression may
omit the repeated type name:

```sollang
symbols[{ scope: 1, id: 10 }]
```

The dictionary supplies the expected K type, so the brace expression is
contextually interpreted as `K { scope: 1, id: 10 }`. Outside that expected
struct-key position, brace syntax keeps its existing dictionary-literal
meaning. Semantic analysis requires every K field exactly once, rejects unknown
fields, and checks each value against the declared field type. LLVM constructs
the same concrete aggregate as an explicitly named struct literal before the
normal statically dispatched hash/equality lookup. Example 71 uses the concise
form for all lookups; diagnostics cover missing and unknown fields.

A typed dictionary literal establishes K and V once in its header. Every struct
key may therefore omit the repeated nominal name symmetrically:

```sollang
{SymbolKey: Text;
  { scope: 1, id: 10 }: "lexer",
  { scope: 1, id: 20 }: "parser",
  { scope: 2, id: 10 }: "semantic"
}
```

This extends the existing typed-empty `{K: V}` form: the semicolon separates
the one-time type header from initialized entries. Unlike first-element-driven
inference, no entry is syntactically special and reordering entries cannot
change their interpretation. Each abbreviated key receives the same
field-completeness, unknown-field, and field-type checks as an abbreviated
lookup key.

The same expected K applies to mutation, so `symbols! -> put({ scope: 1,
id: 20 }, "syntax")` also omits the nominal key name. If V is a struct, the
value argument receives the corresponding contextual treatment as well.

## D086 - Standard Option And Result Specializations

Status: implemented
Date: 2026-07-12

`Option<T>` and `Result<T, E>` are compiler-known parametric tagged values that
reuse the ordinary enum ABI, exhaustive `when` analysis, typed payload binding,
and recursive static drop glue. Their source constructors and patterns keep the
specialization visible:

```sollang
Option<Int>.Some(42)
Option<Int>.None
Result<Int, Text>.Ok(7)
Result<Int, Text>.Err("invalid")
```

This foundation provides explicit absence and typed success/error values
without nulls, exceptions, runtime type descriptors, or vtables. Example 72
verifies function contracts, exhaustive matching, both Result payloads, and an
owned `Option<OwnedNode>` payload. Concise propagation syntax remains a later
compiler-construction gate.

## D087 - Contextual Struct Elements In Typed Arrays

Status: implemented
Date: 2026-07-12

Typed initialized arrays declare their element type once before a semicolon:

```sollang
[Point; { x: 1, y: 2 }, { x: 3, y: 4 }, { x: 5, y: 6 }]
```

The header supplies the expected type for every element, so a struct element
uses the same contextual brace form as typed dictionary keys. `[value; count]`
remains the repeat-array form; the typed initialized form is unambiguous when
its entries are struct literals. A trailing `~` keeps the existing growable
array meaning. Example 73 verifies the growable form, contextual `push`, and
iteration, with a diagnostic for a missing contextual field.

Growable typed arrays carry the same T into mutation calls, allowing
`points! -> push({ x: 7, y: 8 })`. Thus construction, indexing/iteration, and
mutation use one contextual-literal rule rather than separate conveniences.

## D088 - Fixed-Width Numeric Types And Stable Defaults

Status: implemented
Date: 2026-07-12

Sollang exposes numeric width directly when layout matters:

```sollang
Int8  Int16  Int32  Int64
UInt8 UInt16 UInt32 UInt64
Float32 Float64
```

`Int` is exactly `Int32` on every target and `Float` is exactly `Float32`.
These are stable aliases, not platform-word types. This keeps ordinary source
concise, fits 32-bit microcontrollers naturally, and preserves deterministic
cross-target LLVM layouts. `UInt8` is
included because byte buffers, UTF-8 lexing, object data, and binary file I/O
need the full 0...255 domain.

`Long` is the concise `Int64` alias and `Double` is the concise `Float64` alias.
Code that wants the width visible at the use site can always spell the exact
fixed-width names. Pointer-sized indexing types (`Size` and `UIntSize`) remain a
separate target-ABI feature rather than making ordinary `Int` target-dependent.

Integer literals default to `Int`; fractional/exponent literals default to
`Float`. Conversions are explicit constructor-like expressions such as
`Int8(42)`, `UInt64(value)`, and `Float32(1.5)`. Literal conversions are checked
at compile time, runtime integer narrowing emits range checks, and mixed-width
arithmetic is rejected until the programmer chooses the intended conversion.
Arithmetic and comparisons lower to the concrete LLVM integer or IEEE-754
type. Example 74 covers scalar functions, structs, fixed arrays, signed and
unsigned full-width values, floating-point arithmetic, and conversions.

## D089 - Compile-Time Collection Expansion

Status: implemented
Date: 2026-07-12

An inclusive constant integer range can initialize an array directly:

```sollang
[1..10]
[1..10 -> each { it + 1 }]
[1..3 -> each item { item * item }]
```

The parser evaluates the bounds and pure integer selector expressions and
rewrites them to ordinary array literal elements before semantic analysis and
LLVM emission. Dictionaries use the corresponding `key: value` selector:

```sollang
{1..3 -> each { it: it * 10 }}
```

No runtime range or `each` loop remains in generated LLVM. The first slice is
deliberately strict: bounds and selector expressions must be compile-time
integer expressions, arithmetic is checked, descending ranges are rejected,
and one expansion is limited to 100,000 elements to bound compiler memory use.
Future constant evaluation may admit immutable constants and pure functions
without changing this collection syntax.

## D090 - Zero-Argument Functions Use Property Syntax

Status: implemented
Date: 2026-07-12

A function with no input is invoked by naming it:

```sollang
nowMillis => arrayScanStart
getName => name
```

Empty parentheses are an error:

```sollang
nowMillis() => arrayScanStart # Error
```

This rule applies to user functions, standard-library wrappers, runtime
intrinsics, imported functions, and zero-argument associated/method members.
Parentheses communicate that actual arguments follow. A fluent call still
receives its left-hand value, so `value -> transform()` remains compatibility
syntax for that one-input call; the preferred spelling is `value -> transform`.

## D091 - Struct-Scoped Nested Structs

Status: implemented
Date: 2026-07-12

A struct may declare a helper value type inside its body:

```sollang
struct Lexer {
    struct Cursor {
        offset: Int
        line: Int
    }

    cursor: Cursor
}
```

The nested declaration has the collision-free nominal identity `Lexer.Cursor`,
while fields and `impl Lexer` bodies may use the short name `Cursor`. It is
private to `Lexer` by default, matching the intent that implementation helper
types should not expand the surrounding module API. Writing `public struct`
for the nested declaration explicitly exposes `Lexer.Cursor`.

Nested structs participate in the ordinary exact-layout, move, recursive drop,
cycle checking, field initialization, and LLVM lowering rules. They are not a
runtime object or namespace wrapper.

## D092 - Angle Brackets Separate Generics From Arrays

Status: implemented
Date: 2026-07-12

Type and compile-time value parameters use angle brackets:

```sollang
Result<Int, Text>
Option<Int>
identity<T> value: T -> T => value
fixedLength<N: Int> values: [Int; N] -> Int { N }
values -> fixedLength<3>
```

Square brackets are reserved for arrays, indexing, fixed lengths, and
compile-time collection expansion. This removes the visual ambiguity between
`Result<T, E>` and an array expression. Unlike the earlier Mojo-inspired
surface, Sollang follows the familiar Rust/Swift/Kotlin type-application shape while
still allowing type and value parameters in the same compile-time list. The old
generic `[...]` spelling is removed rather than retained as compatibility
syntax.

## D093 - Typed Result Propagation With Postfix Question Mark

Status: implemented
Date: 2026-07-12

Postfix `?` unwraps `Result<T, E>.Ok` and immediately returns an `Err` from the
enclosing `Result<U, E>` function:

```sollang
doubleChecked value: Int -> Result<Int, Text> {
    validate(value)? => checked
    Result<Int, Text>.Ok(checked * 2)
}
```

The operand must be `Result<T, E>`, the enclosing function must return a
`Result` with the exact same error type, and LLVM lowering emits an explicit tag
branch rather than exceptions or stack unwinding. The error branch drops live
owned locals before returning. Owned payloads are supported when `?` consumes a
fresh Result temporary or the enclosing function's explicit `move Result<T, E>`
input. Extraction transfers the active payload, removes the consumed Result
owner, and enum construction consumes a named owned payload so exactly one final
drop obligation remains. Applying `?` to a named non-move owned Result remains a
compile-time error.

## D094 - Target-ABI Size Integers

Status: implemented
Date: 2026-07-12

`Size` and `UIntSize` are distinct integer types whose representation follows
the target pointer width. `Size` is signed so pointer differences and relative
offsets can be negative; `UIntSize` is unsigned for allocation sizes,
capacities, and non-negative native counts. They are not aliases of `Int`:
`Int` remains the portable `Int32` default on x64 and wasm32.

The selected compilation target is passed into semantic analysis before type
layout is calculated. Consequently literal bounds, checked conversions,
struct/enum/container layout, function ABI, arithmetic, comparison, hashing,
and interpolation all agree on one width. LLVM lowers both types to `i64` on
Windows/Linux x64 and `i32` on wasm32 while retaining signedness in operations
and extensions. Example 79 verifies x64 execution and generated LLVM for both
widths.

## D095 - UTF-8 Text Iterates Unicode Code Points

Status: implemented
Date: 2026-07-12

Sollang distinguishes UTF-8 storage from decoded Unicode scalar values. The
`CodePoint` value type has an `i32` ABI on every target and excludes UTF-16
surrogates and values above `U+10FFFF`. `Text -> each scalar { ... }` decodes
one scalar per iteration and never exposes continuation bytes as characters.

This follows Rust's small compiler-friendly `char` model rather than making
Swift-style extended grapheme clusters the primitive. Grapheme segmentation is
important for user interfaces but requires Unicode property tables and is not
the right unit for tokenization. The generated decoder validates continuation
bytes, truncation, overlong encodings, surrogate values, and the Unicode upper
bound before the loop body runs. `CodePoint` conversion enforces the same value
invariant, and direct arithmetic is rejected so it cannot manufacture invalid
scalars. Example 80 covers ASCII, Hangul, a decomposed combining mark, and a
supplementary-plane emoji.

## D096 - Owned Offset-Based Byte Arena

Status: implemented
Date: 2026-07-12

Sollang provides `Arena` for compiler data whose allocations share one lifetime.
The owner stores a backing pointer, used byte count, and capacity, but safe
source code receives only stable `UIntSize` offsets. This preserves memory
safety across growth without exposing raw addresses or pretending that an
arena allocation has an independent destructor.

Allocation is a checked aligned pointer bump. A nonzero power-of-two alignment
is required; padding and end arithmetic trap on overflow. Capacity grows to at
least the larger of twice the old capacity and the required end, used bytes are
copied, and the old block is freed immediately. Checked byte load/store reject
offsets outside the initialized prefix. Reset retains the backing allocation
and rewinds the bump position. Moving an arena transfers its one drop
obligation; mutable borrowing updates the caller's three-word handle in place.

`box T` deliberately keeps its conventional meaning: it always creates one
individually owned heap allocation. Automatic stack/inline placement applies to
ordinary values and existing compiler-selected container backing storage, not
to an explicit `box`. Example 48 continues to assert heap allocation/free for
boxes, while example 81 verifies arena alignment, growth, stable offsets,
checked access, reset, mutable borrowing, move transfer, and final one-shot
release.

## D097 - Memory Mapping Is Native Syntax With Contextual Sizes

Status: implemented
Date: 2026-07-12

Large-file access uses `map read` and `map write` expressions rather than a
library object constructor. The result is an affine owned byte view, immutable
for read mappings and mutable for write mappings. Checked indexing exposes
`UInt8`, `len` exposes `UIntSize`, `each` streams over the view, `flush`
requests synchronous writeback, and scope exit unmaps exactly once.

Numeric literals use the meaning of their syntax position. `at` and `size`
infer `UInt64`, because file offsets and file lengths must remain portable even
on 32-bit targets. `for` and byte indices infer `UIntSize`, because they address
the current process view. Thus `at 4_000_000_000` is equivalent to
`at UInt64(4_000_000_000)` without requiring repetitive constructors; explicit
constructors remain accepted. Nonliteral expressions stay strictly typed so a
variable cannot silently change width.

Windows lowering uses file mappings and compensates for allocation granularity;
Linux lowering uses shared `mmap` views and page alignment. Handles/file
descriptors are closed after the view is established, while the mapped base and
aligned length are retained for flush/unmap. Example 82 verifies literal
inference beyond signed 32-bit range, mutable indexed syntax, read/write
lowering, iteration, writeback, and deterministic unmapping.

## D098 - Process Arguments Are A Read-Only Host View

Status: implemented
Date: 2026-07-12

Sollang exposes launch arguments as `sys.process.arguments: -> Arguments`. The type
is deliberately not an owned dynamic Text array: the host already owns the
Linux argument bytes, while the Windows runtime owns one conversion lifetime.
Treating either as an ordinary independently movable array would invent the
wrong ownership and mutation semantics. `Arguments` is therefore a copyable,
read-only, process-lifetime view with checked `UIntSize` indexing, `UIntSize`
`len`, and borrowed `Text` iteration.

Windows uses `CommandLineToArgvW`, so quoting and backslash behavior follows the
operating system instead of a hand-written partial parser. Each item is
converted with strict UTF-8 output, retained for the program lifetime, and
freed with the records table at exit. Linux accepts `argc` and `argv` in the
generated `main` ABI and measures each selected argument without copying.
Arguments can contain spaces and non-ASCII text on both targets. The first item
remains host-provided and is not a trusted canonical executable path.

The compiler detects whether the main AST reaches this intrinsic. Argument
helpers, Windows allocations, initialization, and teardown are omitted when it
does not, preserving stack-placement LLVM assertions and small programs. This
follows Rust's distinction between process-provided argument views and ordinary
owned collections, and Zig's explicit process initialization context. Example
83 and its argument fixture verify count, checked indexing, spaces, Hangul,
Windows execution, and Linux execution. Environment access and structured
child-process execution remain the next host-boundary slices.

## D099 - Environment Lookup Distinguishes Missing From Empty

Status: implemented
Date: 2026-07-12

`sys.process.environment: Text -> Option<Text>` returns `None` only when a name
is absent and `Some(text)` for every present value, including empty text. A
plain `Text` return with an empty fallback was rejected because it would erase
this distinction and force compiler/build logic to guess whether configuration
was intentionally empty.

The returned text is a process-lifetime borrow. Linux uses `getenv` storage,
which remains stable because safe Sollang exposes no environment mutation. Windows
converts the Unicode value to UTF-8 and records every successful conversion in
a runtime-owned linked allocation list. Program exit frees both the values and
tracking nodes exactly once. Missing, empty, non-ASCII, allocation failure, and
invalid embedded-zero names have distinct runtime paths; operational failures
trap and are never disguised as `None`.

Host runtime code is generated only when an environment intrinsic occurs in
main or a user function. Example 84 uses a per-example environment fixture and
verifies Hangul, a present empty value, and a missing value on Windows and
Linux. Browser wasm has a targeted unsupported diagnostic. This completes the
command-line/environment host-context gate; structured argv-based child
execution remains next.

## D100 - Generic File Output Is Constrained To Canonical Scalars

Status: implemented
Date: 2026-07-12

The Int-only bootstrap writer remains for the existing sorted Int64 examples,
but new code uses `sys.file.openWriter`, `write<T>`, and `closeWriter`.
`write<T>` is a true semantic specialization rather than an `Int` overload:
the inferred scalar type determines its LLVM storage type, alignment, and exact
byte count.

Supported specializations are `Bool`, `CodePoint`, every fixed-width numeric
type, and target-sized `Size`/`UIntSize`. Pointer-bearing or variable-length
types are rejected. This avoids the unsafe and unstable behavior of treating
all copyable values as raw bytes. The native targets currently serialize the
specialized scalar's canonical little-endian bit pattern. The legacy Int64
buffer is flushed before a generic scalar write, preserving call order.

Example 85 writes `UInt8`, `UInt16`, `UInt32`, and `Bool`, maps the resulting
file, and verifies all eight bytes on Windows and Linux. A diagnostic rejects
`Text`. User structs require an explicit serialization trait rather than
implicit ABI dumping.

## D101 - Zero-Input Generic Reads Use Explicit Type Application

Status: implemented
Date: 2026-07-12

`sys.file.read<T>` has no value argument from which to infer `T`, so callers use
`file.read<UInt16>` while retaining Sollang's property syntax for zero-input
functions. `file.read<UInt16>()` is a compile error. Parser lookahead recognizes
the closed type application without consuming ordinary comparisons such as
`left < right`.

The result is `Result<Option<T>, Text>`: `Ok(None)` is clean EOF, while the
stable errors `"truncated"`, `"invalid"`, and `"io"` distinguish incomplete
scalars, invalid Bool/Unicode scalar encodings, and host failures. Native
Windows and Linux readers consume the exact scalar byte width sequentially.
Example 86 covers a value, Bool, EOF, truncation, and invalid Bool encoding;
diagnostics reject `Text` and empty parentheses.

## D102 - Child Execution Is Shell-Free And Argv-Structured

Status: implemented
Date: 2026-07-12

`sys.process.run` accepts `[Text; ~]` whose first element is the executable and
whose remaining elements are literal arguments. It returns
`Result<Int, Text>` with a normal exit code or stable `"spawn"`, `"wait"`, and
`"signal"` errors. A single command string was rejected because it conflates
program lookup, shell parsing, quoting, and user data, creating injection and
cross-platform incompatibility.

This follows Rust `Command`, Swift `Process`, and Mojo `Process.run`: configure
the executable and argv separately, then explicitly wait for status. Windows
uses strict UTF-8-to-UTF-16 conversion and complete Microsoft argv quoting,
including quote/backslash runs; Linux uses `posix_spawnp` and `waitpid` with
temporary zero-terminated storage. All temporary argv allocations are freed on
success and failure paths. Browser wasm has a targeted capability diagnostic.

Example 87 launches its own executable and verifies an argument containing a
space, Hangul, exit code zero, and a missing-program spawn error on Windows and
Linux. Two diagnostics cover the wasm boundary and non-Text argv. The complete
suite has 151 passing examples/diagnostics with zero build warnings/errors.

## D103 - Grammar Generation Produces Data, Not Parser Source Logic

Status: first bootstrap slice implemented
Date: 2026-07-12

Sollang will not copy C# source generators or introduce a Rust-style macro
language merely to build its lexer and parser. The canonical lexer and EBNF
files compile into an ordinary `.slg` module containing declarative lexer
descriptors and a compact parser VM instruction stream. One reusable Sollang runtime
will interpret that data and build a lossless CST; ordinary Sollang functions will
lower the CST into the compiler AST.

`sollang grammar build lexer grammar -o generated.slg` now parses grouping,
alternatives, `?`/`*`/`+`, keyword predicates, token lookahead, token/rule
references, and all current lexer pattern kinds. It emits 33 tokens, 75 rules,
lexer descriptors, keyword/literal pools, rule offsets, and a deterministic
1,508-word parser program. A source SHA-256 is recorded in the generated file.

The full runner regenerates the module and requires byte-identical output.
Example 88 compiles the generated module together with a separate root module
and accesses its public metadata, proving that the output is ordinary modular
Sollang source. This is deliberately not counted as a completed lexer/parser gate
until the Sollang VM produces token/CST snapshots equivalent to the bootstrap
compiler.

## D104 - Source Spans Use UTF-8 Byte Offsets

Status: first self-hosting substrate implemented
Date: 2026-07-12

Lexer, CST, diagnostic, and source-map locations share
`SourceSpan { fileId: Int, start: UIntSize, length: UIntSize }`. Stored offsets
count UTF-8 bytes rather than Unicode scalars, grapheme clusters, or rendered
columns. Bytes are stable under tokenization and map directly to source slices;
line, scalar-column, and display-column values can be derived for diagnostics.

`Text -> len` now returns its byte length. `byte(index)` exposes a checked
`UInt8`, while `slice(start, length)` returns a borrowed Text only when both
ends are valid UTF-8 boundaries. This gives the Sollang lexer efficient byte-level
classification without allowing invalid UTF-8 Text values. Example 89 verifies
ASCII byte access, a Hangul slice, byte count, and the reusable span method.
The bootstrap C# diagnostics have not yet migrated to this type, so the broader
source-span gate remains partial.

## D105 - Async Tasks Are Affine Structured Children

Status: first executable slice implemented
Date: 2026-07-13

Async syntax follows the existing left-to-right function and value-flow forms:
`Int -> async Int` declares the effect, an async call produces `Task<Int>`, and
`task -> await` consumes that task to produce its value. A temporary task can
also be consumed in the same flow as `6 -> square -> await => result`.
Parentheses and a second statement-shaped `await task` form are intentionally
unnecessary.

Task lifetime follows Swift and Kotlin structured concurrency: a child cannot
outlive its lexical owner. Scope cleanup therefore joins every unconsumed task
instead of silently detaching it. Task ownership follows Mojo's consume-once
coroutine direction: an explicit await removes the binding, so double-await and
use-after-await fail during semantic analysis. The surface keeps C#'s readable
`async`/`await` vocabulary while rejecting C#'s easy-to-ignore unobserved task
pattern. Rust's cold futures were not selected for ordinary calls because Sollang
uses a task-producing call to make parallel start order visible in straight-line
flow code. Naming two task-producing calls starts concurrent children;
immediately flowing a call into `await` expresses sequential suspension without
a disposable task name.

The initial Windows x64 lowering supports only CPU-pure zero/one-`Int` input and
`Int` result functions. Runtime and standard-library calls are rejected inside
this first slice rather than racing shared output or I/O state. Each call allocates one owned context and starts a native
thread; await or scope cleanup waits, closes the handle, and frees the context
exactly once. The self-hosted AST records async in function flag bit 8. Later
slices will generalize `Task<T>`, add Linux and stackless I/O lowering, then add
cooperative cancellation and task-group combinators without weakening the
structured lifetime rule.

## D106 - Consuming Calls Produce Region-Scoped Move Events

Status: first self-hosted cleanup slice implemented
Date: 2026-07-13

The self-hosted typed IR records a consuming call in a separate `MoveEvent`
side table instead of mutating value or type flags. Each event identifies the
call, the moved binding, and the nearest structured region. Function contracts
remain the source of truth: an argument is consuming only when the resolved
target parameter carries `move`.

LLVM cleanup suppresses an array or dictionary free only when the matching move
is after the binding, before the cleanup edge, and in that same region. A move
inside a nested conditional therefore cannot suppress cleanup on the parent's
sibling path. This follows rustc's separation of move analysis from drop
elaboration and Swift's declaration-level consuming contract without importing
Rust's complete place tree into the first slice. Recursive aggregate drop glue
is completed by D107, and field-level paths are added by D108.

## D107 - Struct Drop Glue Is A Static Obligation Tree

Status: first recursive self-hosted slice implemented
Date: 2026-07-13

An owned struct does not need a monolithic runtime destructor record. Its
self-hosted LLVM drop glue walks declaration metadata and expands only fields
that establish static drop obligations: dynamic arrays, dictionaries, and
nested structs containing either. Scalar fields produce no cleanup work.
Composite field types also participate in emitted LLVM struct layouts, so the
same type information drives ABI and destruction.

The implementation uses an explicit compiler work queue rather than recursive
inline Sollang calls. Each task carries a structural path encoded into deterministic
SSA names. This mirrors rustc's move-path/drop-obligation tree while fitting the
current bootstrap runtime, which intentionally rejects recursive inline local
functions. Normal function exit, early return, and consuming struct parameters
all use the same glue. A whole-owner move suppresses caller cleanup and the
callee recursively releases its fields exactly once.

Example 231 assembles, links, and executes LLVM for `Outer -> Inner -> [Int; ~]`
on normal, moved, and early-return paths. D108 adds the first field-path mask
over this obligation tree.

## D108 - Partial Moves Release One Static Drop Path

Status: first static self-hosted slice implemented
Date: 2026-07-13

A partial move is represented by the binding identity plus the complete member
path, not by invalidating the whole struct. Moving `outer.left.values` releases
only that leaf's drop obligation. Destruction still walks `outer`, expands
`left` and `right`, skips the exact moved leaf, and releases every owned sibling.
The extracted value becomes a separate owner and is dropped exactly once.

Typed IR stores the member node on each `MoveEvent`. Sequential member-type
resolution preserves every intermediate nominal type, and LLVM compares the
resolved field ordinals with its static drop-task ancestry. The ownership
diagnostic is deliberately a separate module from ordinary type checking: a
subsequent use of the whole owner, the moved path, a descendant, or an ancestor
is rejected, while a diverging sibling path remains valid.

Examples 232 and 233 verify nested extraction, sibling cleanup, LLVM assembly,
native execution, and invalid whole-owner reuse. This slice does not yet model
field reinitialization or control-flow joins with different moved-path sets;
those require dataflow state per region before the broader path-sensitive
ownership gate can be complete.

The emitter marks drop requests that can actually intersect a partial move.
D109 then hoists the immutable typed IR, move events, type tables, and module
identities into one emitter analysis context, so drop glue and local helpers no
longer rebuild the same metadata.

## D109 - Emitter Analysis Is An Owned, Borrowed Context

Status: implemented
Date: 2026-07-13

The self-hosted LLVM backend computes typed IR, move events, nominal types,
composite types, and module identities once. An owned `EmitContext` keeps those
arrays alive for the whole emission and local helpers borrow its fields. The
context is consumed once when emission finishes, preserving deterministic
cleanup without global mutable caches.

Constructing a context transfers each owned field from its source binding.
Reading `context.ir` or `context.sources` through a named owner is a borrow, not
an anonymous heap value, while moving such a field still participates in the
existing partial-move rules. Example 235 covers the multi-owned-field transfer
and a nested local helper borrowing the cached typed IR.

Example tests now bound every child process to five minutes, kill the complete
process tree on timeout, default to eight workers, and report per-case timing.
Compiler optimization can be selected with the conventional `-O0`, `-O1`,
`-O2`, and `-O3` flags; tests use `-O1`. The full suite shows that large
self-hosted LLVM cases remain dominated by native optimization, so module-level
object caching is the next performance step rather than further blind worker
growth.

## D110 - Task Result Types Are Generic But Task Ownership Is Affine

Status: first generic-result slice implemented
Date: 2026-07-13

An async declaration still exposes its ordinary result type, while a call
produces `Task<T>` and `await` consumes that task to recover `T`. This follows
Rust `Future::Output` and Kotlin `Deferred<T>` in preserving the result type,
and Swift/Kotlin structured concurrency in keeping child work inside its parent
scope. Sollang deliberately differs from Kotlin's repeatable `Deferred.await`:
the task handle is an affine owner, so its native handle, context, and possibly
owned result have one statically provable cleanup path.

All specializations share `%sollang.task = { ptr, ptr }`. The heap context is
specialized to the LLVM representation and alignment of `T`, avoiding boxed
`Any` values and runtime type tags. Awaiting transfers an owned result to the
caller. Dropping an unawaited task joins it, recursively drops its result, then
releases the context and OS handle. Unit, numeric, Bool, Text, dynamic collections,
and owned structs are executable in example 236. Example 237 records bit 8 on
self-hosted typed-IR call nodes while preserving the declared result metadata,
so later self-hosted async lowering can recover `T` without type erasure.

The current worker input remains absent or `Int`, and Windows native threads
remain the only executor. General inputs and captures require a compile-time
sendability gate; Linux and nonblocking I/O require the planned stackless
scheduler. Cancellation and dynamic task groups follow after those foundations.

References: [Swift structured concurrency](https://docs.swift.org/swift-book/LanguageGuide/Concurrency.html),
[Rust `Future::Output`](https://doc.rust-lang.org/std/future/trait.Future.html),
[Kotlin structured `async`](https://kotlinlang.org/docs/composing-suspending-functions.html).

## D111 - Async Inputs Use Structural Sendability And Ownership Transfer

Status: first general-input slice implemented
Date: 2026-07-13

Async inputs are no longer restricted to `Int`. Sollang infers sendability
structurally instead of requiring a marker on ordinary value types. Numeric
values, `Bool`, immutable `Text`, and structs/enums composed only of sendable
values can be copied into a task context. A value that contains owned heap
storage must instead use a `move` input, such as
`process packet: move Packet -> async Result`. The task then owns the value and
must either move it into its result or drop it after the body. Mutable borrows,
borrowed slices/views, arenas, mappings, and tasks are not sendable in this
slice.

This combines Swift's inferred `Sendable` for value compositions with Rust's
automatically derived `Send`, while making the transfer rule more explicit for
Sollang's affine owners. It also avoids Kotlin's shared-mutable-state hazards:
there is no shared mutable alias to synchronize because an owned value moves to
exactly one concurrency domain. No `unsafe Sendable` escape hatch is introduced;
future shared state must come through an explicit atomic, lock, actor, or isolated
runtime type with a compiler-known contract.

The heap worker context is specialized over both input and result LLVM layouts.
Owned input cleanup is emitted on normal worker completion and on native thread
creation failure. Example 238 covers scalar, Text, value struct, owned struct,
dynamic array, box, and recursive enum inputs, including move-through-result and
unawaited-task cleanup. Diagnostics reject owned default borrows, borrowed views,
and mutable borrows.

References: [Swift `Sendable` types](https://docs.swift.org/swift-book/LanguageGuide/Concurrency.html#ID649),
[Rust `Send`](https://doc.rust-lang.org/std/marker/trait.Send.html),
[Kotlin shared mutable state](https://kotlinlang.org/docs/shared-mutable-state-and-concurrency.html).

## D112 - Task Primitives Are Platform-Neutral Before Stackless Lowering

Status: Windows/Linux native-thread backends implemented
Date: 2026-07-13

User async lowering no longer calls `CreateThread`, `WaitForSingleObject`, or
`CloseHandle` directly. It targets the internal primitives
`sollang_task_start`, `sollang_task_join`, and `sollang_task_release`.
Windows maps these to kernel thread handles. Linux allocates an owned x64
`pthread_t` cell, starts a worker with `pthread_create`, joins it with
`pthread_join`, and frees the cell on release. The public `%sollang.task =
{ ptr, ptr }` representation and generic input/result context remain identical
on both targets.

Worker result ABIs stay platform-correct behind that boundary: Windows x64 uses
an `i32` thread result and Linux x64 uses the POSIX `ptr` result. Linux linking
uses `-pthread` during object generation and final WSL linking. Native execution
was verified for generic sendable inputs/results, nested await, and lexical
scope joining.

This is an executor boundary, not the final scheduler. The next lowering will
replace the blocking native-thread implementation behind the same semantic
surface with coroutine frames and resume continuations. LLVM's async-continuation
model likewise puts argument/result marshalling in an async context and requires
the frontend to describe suspension control flow. Sollang will retain its
owned context and affine task cleanup while adding explicit state/resume/destroy
entries and an event loop.

References: [LLVM coroutines](https://llvm.org/docs/Coroutines.html),
[POSIX `pthread_create`](https://pubs.opengroup.org/onlinepubs/000095399/functions/pthread_create.html),
[POSIX `pthread_join`](https://pubs.opengroup.org/onlinepubs/9799919799/functions/pthread_join.html).

## D113 - Owned Field Replacement Drops The Previous Value And Transfers The New Owner

Status: reference compiler implemented
Date: 2026-07-13

Assigning to an owned mutable struct field is an ownership transition, not a
bitwise overwrite. The compiler first evaluates the replacement, drops the
field's previous value, stores the replacement, and consumes every named owner
transferred into that replacement. A later use of a consumed replacement is a
compile-time error. Fresh container construction remains valid without an
intermediate owner.

Final struct cleanup always loads the current mutable aggregate rather than the
value captured when the binding was introduced. This prevents a replaced field
from being freed twice and ensures the replacement is freed exactly once.
Example 240 covers nested owned structs and dynamic arrays; its diagnostic
covers use after transfer. The self-hosted move-path checker still needs to
model field reinitialization and control-flow joins before this rule closes the
corresponding self-hosting gate.

References: [Rust partial moves](https://doc.rust-lang.org/rust-by-example/scope/move/partial_move.html),
[Rust destructors and assignment](https://doc.rust-lang.org/reference/destructors.html).

## D114 - Async Tasks Use An Owned Cooperative Frame Before True Suspension

Status: cooperative executor and self-host suspension plan implemented
Date: 2026-07-14

Sollang no longer creates one OS thread for every async call. Windows and
Linux share one `%sollang.task_control` layout containing the specialized
context, resume and destroy entries, FIFO ready link, lifecycle status, and a
reserved resume state. Starting a task allocates that control record and queues
it. `await` pumps ready tasks until its affine target completes. Releasing a
completed task invokes its destroy entry, which owns context deallocation, and
then frees the control record. Reverse-order await and lexical cleanup therefore
retain deterministic one-owner destruction without `CreateThread` or pthread.

This follows Swift and Kotlin in keeping scheduling below structured source
syntax, Rust in treating async work as compiler-generated state, and LLVM in
separating ramp/resume/destroy responsibilities. Sollang deliberately keeps
its existing hot child-task surface and explicit `await`; it does not expose
polling, wakers, continuations, or executor objects in ordinary language syntax.
The self-hosted `typedIr.suspensions` pass assigns stable one-based state numbers
to `await` paths inside async functions so the Sollang LLVM emitter can reproduce the
same frame plan.

This is the first stackless boundary, not completed suspension lowering. A
resume entry currently runs its CPU-pure function body once, and a nested await
can still consume native call stack while pumping its child. The next slice must
spill values live across each await into the owned frame, set the numbered
state, return to the scheduler, and resume through a state switch. Nonblocking
timer/file readiness, cooperative cancellation/failure, captures, and task
groups follow that foundation. Example 241 covers FIFO scheduling and reverse
await on Windows and Linux; example 242 covers the self-host state plan.

References: [LLVM coroutines](https://llvm.org/docs/Coroutines.html),
[Swift concurrency](https://docs.swift.org/swift-book/LanguageGuide/Concurrency.html),
[Rust await expressions](https://doc.rust-lang.org/reference/expressions/await-expr.html),
[Kotlin coroutine scopes](https://kotlinlang.org/docs/coroutines-basics.html#coroutine-scope-and-structured-concurrency).

## D115 - Tail Await Is The First True Stackless Suspension Shape

Status: reference compiler implemented
Date: 2026-07-14

An async function whose body starts one child task and returns that task's
awaited result now lowers to a real two-state resume function. State zero starts
the child, stores its task handle and context in the parent's owned async frame,
sets resume state one, and returns `false` (pending) to the cooperative
scheduler. The scheduler appends the parent to the FIFO queue instead of marking
it complete. State one reloads the child task, joins and releases it, transfers
its typed result into the parent result slot, and returns `true` (complete).

The worker ABI is consequently target-neutral: every resume entry accepts its
task-control pointer and returns `i1`, where false means pending and true means
complete. This is the first path where Sollang async execution actually
returns to the scheduler at an `await`; it neither blocks an OS thread nor keeps
the parent Sollang function on the native call stack. Owned array results are
covered so suspension cannot accidentally duplicate or prematurely drop the
child owner. Examples 228 and 243 assert the generated state switch and pending
return; example 243 also executes on Windows and Linux.

This slice deliberately recognizes only the single-binding tail-await shape.
General awaits still need liveness analysis, typed frame slots for every value
live across suspension, one resume block per `typedIr.suspensions` state, and
cleanup edges for partially initialized frames. Cancellation, failure
propagation, task groups, timers, and nonblocking I/O remain later layers rather
than additional surface syntax.

References: [LLVM coroutine lowering](https://llvm.org/docs/Coroutines.html),
[Rust await expressions](https://doc.rust-lang.org/reference/expressions/await-expr.html),
[Swift concurrency](https://docs.swift.org/swift-book/LanguageGuide/Concurrency.html).

## D116 - Async Frames Spill Typed Live Values Across Multiple Await States

Status: reference compiler and self-host plan implemented
Date: 2026-07-14

The semantic compiler now preserves each validated function's binding types for
backend planning. For every direct awaited-task binding in an async body, the
LLVM emitter finds immutable bindings defined before that await and referenced
after it. Only those live values receive frame slots; dead bindings are not
stored. Slots use the exact LLVM type, inline size, and runtime alignment rather
than an `Any` box or a fixed-size buffer. Numeric and Boolean values plus
structs/enums recursively composed from those value types are supported in this
slice.

One resume switch covers every recognized await in source order. State zero
runs the first segment; each suspension stores its live values and child task,
writes the next state, and returns pending. The corresponding resume state
reloads and frees the spill storage, consumes the child result, and continues
with the next segment. This permits ordinary expressions and branches after an
await and permits sequential state 0/1/2/... suspension without retaining a
native Sollang call frame. Examples 244, 245, and 246 cover exact liveness,
multiple awaits, scalar-only struct layout, and post-resume branching on Windows
and Linux.

The self-hosted typed IR exports `CoroutineFrameSlot` and `frameSlots`. It pairs
each stable `CoroutineSuspendPoint` state with every earlier binding symbol
referenced later in the same async function, including its resolved type origin,
module, and symbol. Example 242 now proves two suspension states and the live
slot for each state. This is the metadata required for the self-host LLVM emitter
to reproduce the reference compiler's typed frame layout.

Await inside branch/loop regions and borrowed values still use the older
whole-body path. Those shapes require control-flow liveness and borrow-region
proofs. Cancellation must also destroy a pending spill frame before this
representation can support task cancellation safely.

References: [LLVM coroutine frames](https://llvm.org/docs/Coroutines.html),
[Rust await expressions](https://doc.rust-lang.org/reference/expressions/await-expr.html),
[Swift concurrency](https://docs.swift.org/swift-book/LanguageGuide/Concurrency.html).

## D117 - A Suspended Frame Temporarily Owns Every Spilled Owner

Status: reference compiler and self-host ownership metadata implemented
Date: 2026-07-14

An immutable or mutable heap owner that is live after a direct await can now
cross that suspension. The compiler materializes its exact aggregate into the
spill frame and removes the source local before ordinary scope cleanup runs.
That removal is an affine ownership transfer: while pending, the frame is the
only owner. Resume loads the aggregate, frees only the spill storage, and
reintroduces exactly one local owner. Mutable arrays, dictionaries, scalars, and
structs also receive fresh resume-stack mutation slots so changes after await
are observed by later suspension or final cleanup.

Async functions no longer stack-promote containers. A promoted buffer would
point into the native resume invocation that disappears when the worker returns
pending; forcing heap storage until coroutine-frame allocation elision exists
prevents that use-after-return class. The active resume state is sufficient as
the initialization discriminant for the current straight-line state machine:
all slots for a state are stored before that state becomes observable, and all
are transferred out together on resume. CFG joins and cancellation will require
finer per-slot initialization/drop flags and a destroy path for a still-pending
frame.

Owned drop glue is complete for nested dictionaries as well as dynamic arrays,
boxes, structs, and enums. Dictionary helpers recursively drop owned keys and
values before freeing their table. This fixes a latent undefined helper exposed
by spilling a structure containing a dictionary. Example 247 carries and drops
an owned array/dictionary/box structure, example 248 carries one array across
two states and transfers it as the task result, and example 249 mutates an array
both before and after suspension. Windows and Linux execute the same paths.

Self-host `CoroutineFrameSlot.flags` uses bit 0 for a mutable binding and bit 1
for an obviously heap-owning composite (`[T; ~]`, dictionary, or box). The
self-host plan therefore preserves both type identity and the initial cleanup
contract needed by its future LLVM destroy emitter.

References: [LLVM coroutine cleanup and destroy](https://llvm.org/docs/Coroutines.html),
[Rust destructors](https://doc.rust-lang.org/reference/destructors.html),
[Swift cooperative task cancellation](https://docs.swift.org/swift-book/LanguageGuide/Concurrency.html#Task-Cancellation).

## D118 - Live Child Tasks Are Affine Coroutine-Frame Owners

Status: reference compiler and self-host ownership metadata implemented
Date: 2026-07-14

Starting more than one async function does not implicitly await either child.
If one child remains live while another child is awaited, its `%sollang.task`
pair of handle and context moves into the coroutine spill frame. Ordinary local
cleanup can no longer observe that Task while the parent is pending. Resume
extracts the same pair, reconstructs exactly one `RuntimeTask` owner with its
original input/result type metadata, frees only the spill allocation, and later
`await` consumes and releases the child exactly once.

Consumed affine bindings are absent from the semantic function's final binding
map. Coroutine planning therefore recovers the type of a live Task from its
async declaration expression when necessary; it must not mistake disappearance
from the final map for absence at an earlier suspension point. Mutable Task
slots remain forbidden because Task ownership moves rather than aliases.

Self-host `CoroutineFrameSlot.flags` now uses bit 2 for an affine Task in
addition to bit 0 for mutability and bit 1 for obvious heap ownership. Example
242 proves that metadata for two children started before the first suspension.
Example 250 proves the runtime path with two already-started children, three
resume states, `%sollang.task` store/load, and deterministic result `102` on
Windows and Linux.

This is structured concurrency at the ownership boundary: every started child
is either explicitly awaited or deterministically consumed by scope cleanup.
Cancellation and task groups remain separate future work because they require
a destroy path for pending child and spill-frame owners.

References: [LLVM coroutine frames](https://llvm.org/docs/Coroutines.html),
[Rust destructors](https://doc.rust-lang.org/reference/destructors.html),
[Swift concurrency](https://docs.swift.org/swift-book/LanguageGuide/Concurrency.html).

## D119 - Cancellation Consumes A Task And Destroys Its Active State

Status: reference compiler runtime and self-host destroy plan implemented
Date: 2026-07-14

`task -> cancel` is the explicit cancellation surface. Like `await`, it is an
affine final flow target: it consumes the Task binding, takes no parentheses or
arguments, and cannot be repeated. Lexical cleanup still joins an unconsumed
Task. This keeps ordinary structured scopes completion-oriented while allowing
the owner to state that a result is no longer needed.

The cooperative task control now stores a function-specific cancel entry in
addition to context, resume, normal destroy, ready linkage, status, and resume
state. Cancellation of queued work unlinks it from the FIFO ready queue before
destroying it. Because the executor is single-threaded and cooperative, a
caller cannot concurrently observe another Task in the running state. A
completed Task can also be canceled; its initialized owned result is dropped
instead of transferred to a caller.

Every async declaration emits a matching cancel function. State zero drops an
owned move input that never began execution. A completed state drops an owned
result. Each suspended state recursively cancels the active child, destroys
initialized spill slots in reverse definition order, frees the spill storage,
then frees the async context. Task-valued spill slots recursively use the same
primitive. Invalid states trap rather than guessing which owners were
initialized.

The self-host typed IR exports `destroySlots`. For each suspension it places a
synthetic active-child Task first, followed by owned frame slots in deterministic
LIFO order; scalar-only liveness slots are excluded. Example 242 proves five
destroy entries across its two states. Example 251 executes cancellation before
initial execution, after completion with an owned result, and during suspension
with an active child, owned array, and second live Task. It then runs another
Task to prove the ready queue remains usable.

This combines LLVM's separate resume/destroy entries with Rust's destruction of
suspended state and Swift/Kotlin cooperative cancellation. Sollang deliberately uses
affine flow syntax instead of an exception as its first cancellation surface.
Cancellation observation inside long CPU loops, task groups, and cancellation
propagation from a canceled parent scope remain later slices.

References: [LLVM coroutine destruction](https://llvm.org/docs/Coroutines.html),
[Swift task cancellation](https://docs.swift.org/swift-book/LanguageGuide/Concurrency.html),
[Kotlin cancellation](https://kotlinlang.org/docs/cancellation-and-timeouts.html).

## D120 - Branch-Nested Await Preserves Structured CFG

Status: reference compiler and self-host suspension discovery implemented
Date: 2026-07-14

An `await` binding inside an `if` or `when` arm is a first-class suspension
point. The async worker keeps the source CFG instead of flattening branches into
a linear replay list. Its entry switch targets a stable resume label embedded
in the selected arm. At that label the worker reloads a state-specific frame,
consumes the stored child Task, and continues through the original join.

The first CFG frame is intentionally path-specific. It contains only bindings
that are active at that suspension, so cancellation never guesses whether a
sibling-arm owner was initialized. Straight-line liveness remains minimal;
branch lowering conservatively spills active lexical bindings. At joins,
immutable representations use value phis while mutable scalars, structs, and
container owners use pointer phis over their reconstructed storage slots. This
preserves exactly one owner and makes both the initially selected path and a
resumed path dominate later code correctly.

The self-host grammar previously described braced `when` arms without consuming
their braces or inter-arm newlines. The generated parser VM therefore rejected
valid multi-line arms even though the reference parser accepted them. The
grammar now models `LeftBrace BlockBody RightBrace` and explicit inter-arm
newlines; regenerated tables let self-host typed IR discover nested branch
suspensions and assign states independently per function. Examples 252 and 253
cover runtime resumption/cancellation, mutable and owner joins, and self-host
state planning.

Await in a `while` body remains the next CFG slice. A loop back-edge can revisit
one suspension state many times, so loop-carried mutable slots require a
persistent frame representation or explicit back-edge phis rather than branch
frame reuse.

References: [LLVM coroutines](https://llvm.org/docs/Coroutines.html),
[Swift concurrency](https://docs.swift.org/swift-book/LanguageGuide/Concurrency.html),
[Kotlin coroutine state machines](https://kotlinlang.org/spec/asynchronous-programming-with-coroutines.html).

## D121 - Loop-Nested Await Reuses States Through Back-Edge Phis

Status: reference compiler and self-host suspension discovery implemented
Date: 2026-07-14

An await site is a stable state-machine location, not a dynamically unique
event. A `while` body may therefore suspend at the same site on every iteration.
The async worker entry switch resumes directly inside the structured loop body;
normal completion of that body flows through an explicit continue block and
back to the loop header.

Every binding visible before the loop receives a header phi. Immutable values
carry their materialized LLVM representation. Mutable scalars, structs, and
containers carry storage pointers, with container pointer/length/capacity slots
kept coherent as one mutable owner. The initial predecessor supplies the
pre-loop representation and the back-edge supplies the post-body
representation. This makes both the first iteration and every resumed iteration
dominate the condition, body, and eventual exit without replaying earlier work.

State-specific spill frames remain temporary: each pending child owns the live
values needed at that site, resume reconstructs exactly one local
representation, and cancellation destroys the active frame. A loop may contain
multiple await sites, keep another affine child Task live across one site, own a
dynamic container across the back-edge, or branch around suspension on selected
iterations. Examples 254 and 255 cover runtime repetition, cancellation,
loop-carried mutable/owned state, a skipped-await path, and the self-host state
and frame-slot plan.

Early `break` and `continue` edges are deliberately rejected in a suspending
loop until each edge transports initialized-owner flags. Silently routing those
edges through the ordinary back-edge could otherwise read an uninitialized
state-specific owner or duplicate cleanup.

References: [LLVM coroutines](https://llvm.org/docs/Coroutines.html),
[LLVM phi nodes](https://llvm.org/docs/LangRef.html#phi-instruction),
[Kotlin coroutine state machines](https://kotlinlang.org/spec/asynchronous-programming-with-coroutines.html).

## D122 - Suspending Loop Controls Carry Edge Scopes

Status: reference compiler and self-host loop-target metadata implemented
Date: 2026-07-14

`break`, `continue`, `condition -> if break`, and
`condition -> if continue` are valid after an `await` in a `while` body. A loop
control edge first drops owners created inside that body scope, then records the
surviving local representation and its LLVM predecessor label. It never routes
through ordinary fallthrough cleanup a second time.

All continue edges merge at the explicit continue block. Immutable values use
value phis; mutable scalar, struct, and container state uses storage-pointer
phis. The resulting representation supplies the loop-header back-edge. Break
edges merge with the header's condition-false edge at loop exit, so values
restored after suspension dominate code following either exit form.

The streaming LLVM emitter gives the continue block a statically false
preheader edge. This keeps the header's two-predecessor phi shape valid even
when every source path breaks during its first iteration; LLVM removes the dead
edge. A required outer owner must exist on every incoming edge. If one control
path consumes it while another preserves it, the compiler rejects inconsistent
ownership instead of inventing a runtime initialization flag.

Example 254 covers guarded continue, explicit break, body-local box cleanup,
mutable scalar and dynamic-owner transport, an all-break loop, an all-continue
loop, repeated suspension, and cancellation. Example 255 proves self-host typed
IR still links direct and guarded exits to their structured while. The negative
diagnostic proves path-dependent consumption of a loop-carried owner is
rejected.

References: [LLVM phi nodes](https://llvm.org/docs/LangRef.html#phi-instruction),
[LLVM coroutines](https://llvm.org/docs/Coroutines.html),
[Kotlin coroutine state machines](https://kotlinlang.org/spec/asynchronous-programming-with-coroutines.html).

## D123 - Expensive Tests Run First With Dynamic Load Balancing

Status: implemented
Date: 2026-07-14

The example runner no longer gives a statically partitioned, lexically ordered
array to its eight workers. Self-host LLVM cases take roughly a minute while
ordinary examples take fractions of a second; contiguous range partitioning
therefore left most workers idle near the end of a full run.

Known `selfhost-llvm-` cases are now ordered first and a load-balancing
partitioner hands out the next case whenever a worker becomes free. Artifact
paths remain isolated per example, bootstrap and grammar determinism still run
once, and no overlapping top-level runners are introduced.

On the same machine and adjacent compiler commits, the complete 343-case run
dropped from 793.8 seconds to 382.8 seconds, a 51.8% reduction, with all cases
passing. This fixes scheduling starvation. Reusing fingerprinted self-host
modules or native objects remains a separate future optimization.

## D124 - Bare Yield Is A Cancellation-Aware Scheduler Suspension

Status: reference compiler and self-host suspension metadata implemented
Date: 2026-07-14

Bare `yield` is valid only as a statement inside an async function. It is a real
stackless suspension point with no child Task: the worker spills every active
typed local not already resident in its function context, stores a stable state
number, returns pending, and is appended to the FIFO ready queue. Resume reloads
the exact state frame and continues after the statement.

If the Task owner cancels it while queued, the state-specific cancel entry does
not look for a child handle. It drops affine Task spills and other owned values
in reverse order, frees the frame and context, and removes the task control from
the queue. Long CPU loops are therefore cooperative and cancelable at explicit,
reviewable points rather than through hidden preemption.

The spelling intentionally follows Sollang's zero-input property rule: `yield`, not
`yield()`. It also reuses an existing word contextually. Bare `yield` suspends an
async Task, while `value -> yield` continues to transfer a value out of a block
function. `main` and synchronous functions have no resumable Task frame and
reject the bare form.

This combines Swift's explicit scheduler yield, Kotlin's cancellation-aware
yield, and Tokio's requeue-at-the-back behavior while preserving Sollang's affine
ownership and target-neutral single executor. Example 256 proves an infinite
CPU loop yields so a later gate Task can finish, then is canceled with a live
box and dynamic owner in its frame. It also covers loop, branch, straight-line,
post-await, and repeated yield. Example 257 proves self-host metadata assigns
one shared state sequence while distinguishing await and yield kinds. Example
258 covers a live owned input, normal resumption, and an input transferred to a
child before yielding; each cancellation state retains exactly one cleanup
obligation.

References: [Swift Task.yield](https://developer.apple.com/documentation/swift/task/yield%28%29),
[Kotlin cancellation and yield](https://kotlinlang.org/docs/cancellation-and-timeouts.html),
[Tokio task yield](https://docs.rs/tokio/latest/tokio/task/fn.yield_now.html).

## D125 - Typed Duration Sleeps Park Tasks Outside The Ready Queue

Status: reference compiler, native runtime, and self-host planning implemented
Date: 2026-07-14

Sollang represents elapsed time with the public `sys.time.Duration` value type.
`milliseconds` and `seconds` are ordinary pure constructors, and
`sleep: Duration -> async Unit` returns an affine Task. The intended surface is
therefore `250 -> milliseconds -> sleep -> await`: the unit is visible,
the suspension is explicit, and cancellation uses the same Task ownership rule
as user async functions.

The executor has a deadline-ordered timer wait queue in addition to its FIFO
ready queue. A sleep worker that is not due marks its Task waiting and inserts
it by monotonic deadline instead of returning to the runnable tail. When no
work is ready, the executor waits until the nearest deadline and wakes all due
timers at the ready tail. This performs no busy polling and creates no OS thread
per timer. Non-positive durations are immediately ready. Cancellation can
unlink either a ready or timer-waiting Task and invokes exactly one context
destroy path.

An async parent awaiting a child is likewise removed from the ready queue. The
child stores its unique affine waiter and wakes that parent exactly once on
completion. Canceling the parked parent first detaches this wake edge, then
recursively cancels the stored child. This prevents a slow child timer from
making an unrelated fast Task wait behind a nested blocking join.

This adopts Swift's typed Duration and monotonic Clock separation, Kotlin's
nonblocking cancellable delay behavior, and Rust's rule that pending work must
register how it will become runnable again. Tokio's timer future additionally
confirms that dropping a sleep should require no resource-specific cleanup.
The Sollang timer node lives in the existing affine Task control, so cancellation is
an ordinary ownership operation rather than a separate timer handle protocol.

Example 259 covers ordered 1ms/25ms timers, a canceled 1-second waiter,
zero/negative immediate completion, and elapsed-time behavior. Example 260
proves self-host multi-file module/call resolution for `sys.time` and preserves
the await suspension state.

References: [Swift Task.sleep](https://developer.apple.com/documentation/swift/task/sleep%28for%3Atolerance%3Aclock%3A%29),
[Swift Duration](https://developer.apple.com/documentation/swift/duration),
[Kotlin delay](https://kotlinlang.org/api/kotlinx.coroutines/kotlinx-coroutines-core/kotlinx.coroutines/delay.html),
[Rust Poll](https://doc.rust-lang.org/stable/std/task/enum.Poll.html),
[Tokio sleep](https://docs.rs/tokio/latest/tokio/time/fn.sleep.html).

## D126 - Async Regular Files Use One Shared Operation Worker

Status: portable scalar-read runtime and self-host syntax implemented
Date: 2026-07-14

Regular files do not share the portable readiness semantics of sockets. Tokio
therefore runs ordinary file operations on its blocking pool, while Java's
`AsynchronousFileChannel` associates a file channel with a shared executor.
Windows offers overlapped file operations and Linux offers io_uring, but making
either one the language ABI would make Task semantics platform-dependent.

Sollang exposes the operation, not its backend:

```sollang
file.readAsync<UInt16> => pending
pending -> await => result
```

`readAsync<T>: -> async Result<Option<T>, Text>` is monomorphized for the same
fixed-width scalar set as synchronous `read<T>`. Its EOF and error contract is
identical. The first portable backend has exactly one lazily-created native
file worker per process. Task submission transfers the request to a lock-free
MPSC stack with release/acquire publication; the worker reverses each batch to
preserve FIFO submission order, performs blocking regular-file calls away from
the cooperative executor, and publishes a completion batch. The executor
waits for either the nearest timer or file completion rather than polling.

Windows uses auto-reset Events and `WaitForSingleObject`. Linux uses a pthread,
two eventfds, and `poll`. This is one OS thread for the file subsystem, never
one thread per Task. A later backend may replace the worker with IOCP or
io_uring without changing Sollang syntax, Task semantics, or call-site ownership.

Cancellation marks a worker-owned request and returns after consuming the Task
handle. The completion drain, which owns the final reference, invokes cancel
destruction and frees control storage exactly once instead of re-enqueuing the
Task. Structured shutdown waits for outstanding requests, signals the worker,
joins it, and closes native events. A parent awaiting an async file child uses
the existing unique waiter edge.

The self-host grammar now models `Path<Type>` as
`TypeApplicationExpression`; module-call resolution recognizes it as a real
call, and coroutine discovery retains the following await. The parser's
expression entry point also accepts a complete expression immediately before
the synthetic End token, matching the reference parser.

The current `openReader` compatibility surface still owns one process-wide
cursor. General self-host compiler I/O still requires affine reader/writer
handles, explicit-offset operations like .NET `RandomAccess.ReadAsync` and
Java's position-based file channel, async open/write/flush/close, and native
completion backends where profitable.

Examples 261 and 262 cover Windows/Linux completion, timer coexistence, nested
parent await, EOF, ready-queue cancellation, graceful worker shutdown, imported
generic type application, and self-host suspension discovery.

References: [Tokio fs](https://docs.rs/tokio/latest/tokio/fs/),
[Tokio AsyncRead](https://docs.rs/tokio/latest/tokio/io/),
[tokio-uring ownership-based operations](https://docs.rs/tokio-uring/latest/tokio_uring/),
[Java AsynchronousFileChannel](https://docs.oracle.com/en/java/javase/26/docs/api/java.base/java/nio/channels/AsynchronousFileChannel.html),
[.NET RandomAccess.ReadAsync](https://learn.microsoft.com/dotnet/api/system.io.randomaccess.readasync),
[Windows overlapped I/O](https://learn.microsoft.com/windows/win32/sync/synchronization-and-overlapped-input-and-output),
[Swift FileHandle.AsyncBytes](https://developer.apple.com/documentation/foundation/filehandle/asyncbytes).

## D127 - Files Are Affine Owners And Random Access Is Position-Based

Status: owned read handles and scalar offset reads implemented
Date: 2026-07-14

The process-wide compatibility cursor cannot support independent compiler
modules or concurrent parsing safely. New Sollang code therefore opens an affine
resource:

```sollang
file.openRead(path) => opened
reader -> readAt<UInt16>(offset) => value
reader -> readAtAsync<UInt16>(offset) => pending
```

`File` has one close obligation, cannot be copied, and is closed by ordinary
scope cleanup. A read offset is `UInt64` and does not mutate file position.
This follows Rust `FileExt::read_at` and .NET `RandomAccess.ReadAsync`, both of
which separate position from the file object's sequential cursor.

An async operation must not borrow a File beyond its proven lifetime. Rather
than adding an unsafe implicit escape, the first backend duplicates the native
handle into the affine Task. Completion or cancellation closes that duplicate
exactly once; the original File remains independently usable and closes at its
own scope exit. Windows opens the handle for overlapped I/O and supplies the
64-bit offset through `OVERLAPPED`; Linux uses `pread`. Both still complete via
the shared operation worker, so Task scheduling remains target-neutral.

The reference parser now distinguishes a flow type argument from the existing
compile-time integer argument. The grammar accepts both `value -> fixed<4>` and
`reader -> readAt<UInt16>(offset)`. Self-host call resolution ignores identifiers
inside `<...>` and `(...)` when selecting a flow target, avoiding accidental
resolution to `UInt16` or an argument binding.

Example 263 covers synchronous and concurrent asynchronous offset reads,
automatic File cleanup, duplicated Task handles, and Windows/Linux parity.
Example 264 proves self-host generic flow-target resolution. Diagnostics reject
File copying, unsupported scalar types, and negative/non-UInt64 offsets.

References: [Rust FileExt](https://doc.rust-lang.org/std/os/unix/fs/trait.FileExt.html),
[Rust File](https://doc.rust-lang.org/stable/std/fs/struct.File.html),
[.NET RandomAccess.ReadAsync](https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess.readasync),
[Windows overlapped I/O](https://learn.microsoft.com/en-us/windows/win32/sync/synchronization-and-overlapped-input-and-output),
[Swift concurrency and AsyncSequence](https://docs.swift.org/swift-book/LanguageGuide/Concurrency.html).

## D128 - Random-Access Writers Are Separate Affine Capabilities

Status: owned scalar offset writes implemented
Date: 2026-07-14

Reading and writing are different capabilities in safe Sollang. `openWrite` returns
`Result<FileWriter, Text>` rather than a mode flag on `File`, so attempting a
read through a writer or a write through a reader fails during type checking.
The writer is affine and uses the same deterministic native-handle drop rule.

```sollang
file.openWrite(path) => opened
writer -> writeAt(UInt16(513), 0) => inferred
writer -> writeAt<UInt16>(1027, 3) => contextual
```

`writeAt` infers `T` from the value by default. An explicit type argument is
also accepted so an otherwise default `Int` literal can be contextually encoded
as `UInt16`. The second argument is a `UInt64` byte offset. A scalar write is
all-or-error and returns `Result<Unit, Text>`; exposing short scalar writes
would add recovery complexity without helping compiler workloads.

Rust's `write_at` establishes cursor-independent offset semantics and its
`write_all_at` demonstrates why the high-level contract should retry or fail
instead of silently accepting a partial buffer. .NET `RandomAccess.WriteAsync`
likewise keeps the handle cursor unchanged and treats the offset as an explicit
operation input. Sollang's current synchronous backend uses overlapped `WriteFile`
on Windows and `pwrite` on Linux. Linux writers are deliberately opened without
`O_APPEND`, whose interaction with positional writes is non-portable.

Example 265 writes scalars out of order, closes the writer deterministically,
then reads them back through an affine `File`. Diagnostics reject writer copies,
unsupported `Text`, and negative offsets.

References: [Rust FileExt write_at/write_all_at](https://doc.rust-lang.org/std/os/unix/fs/trait.FileExt.html),
[.NET RandomAccess](https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess),
[.NET RandomAccess.WriteAsync](https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess.writeasync).

## D129 - Async Writes Own Their Bytes And Native Handle

Status: portable scalar async writes implemented
Date: 2026-07-14

`writeAtAsync<T>` follows the same position-based, all-or-error contract as
`writeAt<T>`, but returns `Task<Result<Unit, Text>>`. The Task copies the scalar
bytes at submission and owns a duplicated native writer handle plus the 64-bit
offset. It never borrows the caller's stack or the original `FileWriter`, so
completion order and lexical writer cleanup cannot create dangling storage.

This is the ownership shape demonstrated by `tokio-uring`: an in-flight
operation owns the resource and stable buffer until the kernel returns it. It
also preserves .NET `RandomAccess.WriteAsync` semantics: the explicit offset
does not mutate a shared cursor and cancellation belongs to the operation.

The portable backend reuses one shared file worker for both operation kinds.
Windows writes through overlapped `WriteFile`; Linux uses `pwrite`. Completion
converts a full write to `Ok(Unit)` and every partial or failed scalar write to
`Err("io")`. Cancellation consumes the affine Task and closes its duplicate
exactly once, whether the request is still queued or already worker-owned.

Example 266 covers inferred and explicitly contextual scalar writes, concurrent
submission, cancellation before execution, read-back, and Windows/Linux parity.
Example 267 proves self-host generic flow-call resolution and await suspension
discovery. A diagnostic rejects non-scalar `Text` payloads.

References: [tokio-uring](https://docs.rs/tokio-uring/latest/tokio_uring/),
[tokio-uring write operation](https://docs.rs/tokio-uring/latest/src/tokio_uring/io/write.rs.html),
[Rust FileExt write_all_at](https://doc.rust-lang.org/std/os/unix/fs/trait.FileExt.html),
[.NET RandomAccess.WriteAsync](https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess.writeasync).

## D130 - File Durability Is Async Sync, Not Buffered Flush

Status: portable asynchronous durability barrier implemented
Date: 2026-07-14

Sollang random-access writers issue complete positional scalar writes and have no
hidden language-level output buffer. Calling the durability operation `flush`
would therefore imply state that does not exist. The public flow member is:

```sollang
writer -> syncAsync => pending
pending -> await => result
```

`syncAsync: -> async Result<Unit, Text>` synchronizes file data and metadata to
the filesystem. This follows Tokio's `File.sync_all`, while retaining the
cancellation-aware Task shape of .NET `FileStream.FlushAsync`. Windows calls
`FlushFileBuffers`; Linux calls `fsync`. A platform failure becomes `Err("io")`.

The Task owns a duplicated writer handle and runs on the shared FIFO file
worker. Consequently a sync request submitted after write requests observes
those writes before reporting success. Cancellation consumes the Task and
closes its duplicate exactly once; an operation already owned by the worker may
finish, but its former waiter is never resumed.

Sollang does not add `closeAsync` merely to mirror object-oriented stream APIs.
Pending random-access Tasks never borrow the source handle, and lexical affine
drop closes that source immediately. An explicit asynchronous close becomes
necessary only if a future buffered writer owns unfinished internal work or if
observable close errors enter the language contract.

Example 268 covers two asynchronous writes, the durability barrier, queued
sync cancellation, read-back, and Windows/Linux execution. Example 269 proves
self-host flow-call resolution and await suspension discovery. A diagnostic
rejects arguments and type arguments.

References: [Tokio File sync_all](https://docs.rs/tokio/latest/tokio/fs/struct.File.html#method.sync_all),
[Tokio filesystem implementation](https://docs.rs/tokio/latest/src/tokio/fs/file.rs.html),
[.NET FileStream](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream),
[.NET async dispose pattern](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-disposeasync),
[Windows FlushFileBuffers](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers),
[Linux fsync](https://man7.org/linux/man-pages/man2/fsync.2.html).

## D131 - Async Open Owns Its Path And Transfers One Handle

Status: portable asynchronous read/write open implemented
Date: 2026-07-14

Sollang exposes asynchronous construction on the file module rather than on an
already-existing object:

```sollang
file.openReadAsync(path) => opening
opening -> await => opened
```

`openReadAsync` returns `Task<Result<File, Text>>`; `openWriteAsync` returns
`Task<Result<FileWriter, Text>>`. The Task allocation contains both its result
storage and a byte-for-byte copy of the path, so the worker never borrows a
temporary Text. A successful await clears Task ownership and transfers exactly
one native handle into the affine Result. Failure transfers none. Cancellation
closes a handle if opening completed before the cancellation became visible,
then destroys the context exactly once.

This mirrors Tokio's portable contract: path and options are moved into
blocking filesystem work, while an io_uring backend may later replace the
implementation without changing source syntax. It also avoids pretending that
.NET's asynchronous `FileStream` flag makes constructor-time open asynchronous;
that flag governs I/O on the opened handle.

An owned anonymous enum subject is now dropped after `when` when every arm
returns a non-owning value. This makes `opening -> await -> when { ... }` both
concise and deterministic instead of leaking the matched File/FileWriter.
Move detection also follows an enum-match subject, preventing a consumed Task
from being awaited again at scope cleanup.

The bootstrap grammar recursively parses nested generic annotations, including
`Result<File, Text>` and nested array/dictionary components. Example 270 covers
success, missing-file failure, cancellation, handle transfer, and Windows/Linux
execution. Example 271 proves self-host imported-call and suspension discovery;
a diagnostic rejects non-Text paths.

References: [Tokio OpenOptions source](https://docs.rs/tokio/latest/src/tokio/fs/open_options.rs.html),
[Tokio File](https://docs.rs/tokio/latest/tokio/fs/struct.File.html),
[.NET FileStream constructors](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.-ctor).

## D132 - Projects Name One Root Without Repeating Source Paths

Status: root project manifest and source-free build implemented
Date: 2026-07-14

Small projects previously needed the root `.slg` path on every compiler
invocation, even though import discovery already knew the complete module graph.
The project boundary is now declared once in `sollang.project`:

```sollang
project {
    name: "compiler"
    root: "src/main.slg"
}
```

`sollang build` searches the current directory and its ancestors. An explicit
`--project` accepts a manifest file or directory. The root is relative to the
manifest, must stay inside that directory, and must name an existing `.slg`
file. Unknown or duplicate fields are errors. With no `-o`, the compiler writes
`build/<name>` with the platform suffix. Existing target, optimization, LLVM,
and output flags remain command-line overrides.

Swift demonstrates the value of a source-language-shaped manifest whose root
object names products and targets. Zig demonstrates the eventual expressive
ceiling of an executable build-language DAG. Sollang takes the staged middle path:
the first manifest deliberately has a tiny deterministic data subset that the
self-host compiler can parse without executing arbitrary host code. Its syntax
already looks like Sollang, so a later compile-time `project` value can extend it
without replacing project files or introducing TOML/JSON as a second language.

Example 272 builds a two-file project through its manifest and recursive dotted
import on Windows and Linux. The runner also verifies unknown-field and
root-escape diagnostics. This promotes the explicit-root build gate from
partial to complete; package dependencies and module/interface caching remain.

References: [Swift packages](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/introducingpackages/),
[Zig build system](https://ziglang.org/learn/build-system/).

## D133 - Package Visibility Follows Direct Product Dependencies

Status: deterministic local package graph implemented
Date: 2026-07-14

`sollang.project` now separates selectable products from package dependencies
while keeping both as compact Sollang-shaped maps:

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

This takes Swift Package Manager's useful separation between products, targets,
and dependencies without copying its executable manifest API. It takes Cargo's
exact relative path-dependency rule: local dependency paths point to the actual
package directory rather than searching descendants. It takes Zig's root-module
and build-DAG direction, but keeps bootstrap graph evaluation deterministic and
non-executable until Sollang can host it itself.

A dependency key is deliberately both the project identity and first import
segment. The referenced manifest must have the same `name` and a same-named
product. Renaming and multiple versions are deferred because current semantic
nominal identity is module-qualified rather than package-id-qualified; accepting
aliases now would silently conflate packages. Each package sees only direct
dependencies, while each dependency resolves its own transitive declarations.
The loader sorts dependency names, rejects graph cycles and duplicate names at
different paths, rejects one source file claimed by different packages, and
selects exactly one root product before source discovery.

The next distribution layer needs package-id-qualified nominal identity,
version constraints, content-pinned remote sources, a checked-in lock file, and
workspace resolution. Those features must preserve deterministic inputs before
the build manifest becomes an executable compile-time Sollang value.

References: [Swift packages](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/introducingpackages/),
[Swift products](https://docs.swift.org/swiftpm/documentation/packagedescription/product/),
[Cargo path dependencies](https://doc.rust-lang.org/cargo/reference/specifying-dependencies.html#specifying-path-dependencies),
[Cargo workspaces](https://doc.rust-lang.org/cargo/reference/workspaces.html),
[Zig build system](https://ziglang.org/learn/build-system/).

## D134 - Typed Roles Reuse Result-Producing Block Functions

Status: accepted; common foundation implemented, role libraries in progress
Date: 2026-07-14

Builders, scoped contexts, and handlers use one typed block-function mechanism:

```sollang
source -> build item {
    # normal Sollang statements
} => result
```

`build`, `with`, and `handle` are ordinary resolvable function names rather
than keywords. The block function's final expression produces `result`; its
`yield` operations execute the caller-provided Unit block with a typed item.
This preserves `TypeName { field: value }` exclusively as struct construction,
keeps block contents in the normal AST, and avoids a macro-only sublanguage or
context-sensitive parser.

The implementation and evidence checklist is maintained in
[`ROLE_BLOCKS.md`](ROLE_BLOCKS.md). A role is not reported complete until its
semantic, ownership, LLVM, self-host, and cross-target checklist entries pass.

References: [Kotlin type-safe builders](https://kotlinlang.org/docs/type-safe-builders.html),
[Kotlin context parameters](https://kotlinlang.org/docs/context-parameters.html),
[Swift result builders](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/attributes/#resultBuilder),
[Scala scoped capabilities](https://docs.scala-lang.org/scala3/reference/experimental/capture-checking/scoped-capabilities.html),
[Effekt effect handlers](https://effekt-lang.org/docs/concepts/effect-handlers).

## D135 - Enum Patterns Infer Their Type From The When Subject

Status: implemented
Date: 2026-07-14

An enum subject fixes the enum type for every pattern arm, so payload patterns
omit the redundant type qualification:

```sollang
openedReader -> when {
    Ok(reader) {
        reader -> readAt<UInt16>(0)
    }
    Err(error) => error
}
```

This is contextual pattern lookup, not a global import of variant constructors.
`Ok(reader)` above resolves only against the known subject type, such as
`Result<file.File, Text>`. A missing variant is diagnosed against that enum, so
another enum's identically named variant cannot be selected accidentally.
Fully qualified patterns remain valid when explicitness is useful, and enum
construction remains qualified because a constructor has no subject from which
to infer its type. The same rule applies to user enums and permits forms such
as `Value(value)`, `Some(value)`, `Missing`, and `None`.

## D136 - Self-Hosted Role Calls Remain Ordinary Typed Calls

Status: implemented through self-host typed IR; full role contracts remain partial
Date: 2026-07-14

The self-host compiler assigns result-producing block-function calls a distinct
AST kind only so their caller body and trailing result binding remain
recoverable. Semantic resolution does not introduce a special `build`, `with`,
or `handle` namespace: AST kind 48 resolves its payload token through the same
ordinary function symbol table as direct and fluent calls.

When `source -> role item { ... } => result` has a result binding, the self-host
symbol table projects `result` as a normal lexical binding. Expression
inference gives the call the role function's return type and propagates that
type to later references. Flat typed IR emits an ordinary kind-6 call, a
kind-17 binding, and reconnects typed body operations beneath the call. This
keeps later LLVM lowering independent of surface sugar while retaining enough
structure for scoped cleanup and effect analysis.

Example 279 proves AST payloads, lexical binding, call resolution, result-type
propagation, and typed-IR parentage. This decision does not mark role semantics
complete: capability escape, ownership on every exit edge, generic block-item
specialization, and handled effect sets remain.

## D137 - Role Input Is Selected Before Its Lexical Caller Block

Status: first self-host block-input contract slice implemented
Date: 2026-07-14

For `source -> role item { ... } => result`, the role call itself is a lexical
scope. `item` is a synthetic typed parameter owned by that scope and is visible
only inside the caller block. `result` is an ordinary binding in the enclosing
scope. This prevents the item capability from becoming visible after the role
call before ownership and capture analysis even begins.

The self-host type checker derives the role argument only from inferred
expressions whose source span ends before the role target token. Expressions in
the caller block can therefore never be mistaken for `source`, even when they
are closer descendants in the flat AST. The same boundary is used by generic
call-result inference. A nominal or composite source mismatch uses the existing
call-argument diagnostic code 6. If the resolved target has no declared
`block` input, role syntax emits code 17 over the complete role call instead of
silently treating an ordinary function as a role.

Runtime calls such as `println` have no source-module symbol table and are
excluded from module-backed type checking. This is required for runtime effects
inside role bodies and removes the invalid `sources[-1]` lookup exposed by the
new success example.

Examples 279 and 280 are the executable evidence. This decision deliberately
does not claim full self-host role parity: generic block-item specialization,
capability escape checking, effect-set enforcement, all-exit cleanup, and LLVM
role lowering remain open in [`ROLE_BLOCKS.md`](ROLE_BLOCKS.md). The Release
solution build completed with zero warnings and errors, and the single
coordinated eight-worker runner passed grammar/table determinism plus all 393
examples in 369.3 seconds.

## D138 - An Import Defaults To Its Final Path Segment

Status: reference and self-host implementations aligned
Date: 2026-07-14

An import without `as` uses its last path identifier as the local alias:

```sollang
import sollang.compiler.lexer
```

This is exactly equivalent to `import sollang.compiler.lexer as lexer`.
Explicit aliases remain available when the natural name is unsuitable, such as
`import sollang.compiler.semantic.expression_types as expressionTypes`.
Default and explicit aliases occupy one namespace and therefore share the same
collision rule.

The reference parser already implemented this rule. The self-host module pass
now records the final `Path` identifier as `aliasToken` when no `as` clause is
present, so qualified lookup uses the same token-backed comparison without
allocating a string. Self-host module resolution reports status 3 for the later
of two equal aliases in one source. Examples 110 and 113 execute default alias
capture and qualified lookup; example 282 and the reference diagnostic
`import-default-alias-collision` cover collisions. Explicit `as` remains
continuously exercised by imports whose chosen camel-case name differs from
their snake-case module segment.

## D139 - Generic Role Items Are Fixed Outside-In

Status: first nominal and shallow-composite specialization implemented
Date: 2026-07-14

For a generic role call, Sollang fixes type variables from the source before
checking the caller block. The block body cannot retroactively select a type:

```sollang
visit<T> values: [T; ~] -> Int block item: T { ... }

[1, 2, ~] -> visit item {
    item + 1
}
```

Here `[Int; ~]` fixes `T = Int`; only then is `item + 1` checked. This keeps
inference deterministic and makes imported and local roles identical. It also
avoids introducing Kotlin-style postponed builder inference before Sollang needs it.
Kotlin itself uses builder inference only when regular inference cannot fix a
type and describes lambda types as postponed variables in that fallback. Rust
likewise infers one concrete closure parameter type from its use context.

The self-host fixed-point pass now substitutes a matching nominal generic,
extracts a matching array/box element or dictionary key/value generic from the
source expression, and reuses a source composite when input and item generic
shapes match. It also reconstructs a one-level composite item such as
`T -> [T; ~]`. The specialized item then feeds ordinary operator inference and
typed IR. Example 281 proves scalar, array-element, reconstructed-array,
imported-role, result, and body-operation propagation. Recursive term-arena
integration across every consumer remains explicit follow-up work rather than
being claimed complete.

References: [Kotlin builder inference](https://kotlinlang.org/docs/using-builders-with-builder-inference.html),
[Rust closure type inference](https://doc.rust-lang.org/book/ch13-01-closures.html#inferring-and-annotating-closure-types).

## D140 - Long Test Runs Stream Counted Progress

Status: implemented
Date: 2026-07-14

The example runner must never remain visually silent while expensive self-host
LLVM tests run. Bootstrap build and grammar verification use explicit
`[bootstrap n/total]` phase messages. Test scheduling prints
`[start n/total] name`; completion prints `[n/total] PASS|FAIL name (seconds)`.
Progress records all use stdout and are flushed immediately. Detailed failure
diagnostics remain on stderr; using two streams for counters is forbidden
because a merged terminal can reorder otherwise correctly locked records.

Started and completed counts use separate atomic counters because up to eight
workers run concurrently. A lock keeps each progress record intact, while the
actual compiler/test work remains parallel. Completion reporting is in
`finally`, so every normal return path, including detailed failures, advances
the visible counter exactly once. Diagnostics continue after success examples
with the same total rather than restarting at zero. This preserves the single
coordinated top-level runner rule while making its internal progress observable.

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors. The coordinated eight-worker runner passed
grammar/table determinism and all 396 cases in 390.6 seconds. A focused
parallel check also verified that both emitted start and completion counters
form the exact monotonic sequence `1..total`.

## D141 - Semantic Types Use Recursive Canonical Terms

Status: recursive arena and generic block specialization foundation implemented
Date: 2026-07-14

Syntax types and semantic types are distinct. The AST preserves exact spelling
and source locations, while semantic analysis lowers every nested type into a
flat, index-addressed `TypeTerm` arena. Each term has a kind and up to two child
indexes; structural canonicalization interns equivalent complete trees. This
follows rustc's separation between syntax-level HIR types and canonical
interned semantic `Ty` values, while retaining a representation Sollang can
bootstrap without recursive heap objects.

Substitution is bottom-up over the arena. Replacing `T` in
`Result<[T; ~], {Text: box T}>` rebuilds and interns every affected ancestor,
so no container-specific string replacement or fixed nesting limit is needed.
Example 283 executes this algorithm in Sollang. The older nominal/composite tables
remain temporarily for existing consumers; the arena is the migration target,
not a second permanent type system.

Block-function item declarations now accept a complete `TypeAnnotation` rather
than a nominal-only `TypeName`. The reference compiler retains a generic block
type template, fixes `T` from the ordinary source, recursively materializes the
concrete block type, validates the specialized function and caller, and records
the specialization for LLVM emission. Example 284 executes `T -> [T; ~]`; the
`generic-composite-role-yield-mismatch` diagnostic proves the `yield` body is
checked after specialization. The self-host fixed-point pass performs the same
one-level reconstruction today and will consume the recursive arena as the
remaining shallow expression-type representation is migrated.

This is a partial semantic-infrastructure advance and does not promote a
roadmap gate. Full completion still requires recursive type ids in expression
inference, type checking, typed IR, ownership/effects, and self-host LLVM
lowering.

References: [rustc type representation](https://rustc-dev-guide.rust-lang.org/ty.html),
[Mojo generics](https://mojolang.org/docs/manual/generics/).

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors. The generic/block/grammar/type focused slice passed
75/75, and the single coordinated eight-worker runner passed byte-for-byte
grammar determinism plus all 399 cases in 389.8 seconds.

## D142 - Recursive Type IDs Are Global Expression Currency

Status: first expression boundary implemented
Date: 2026-07-14

Semantic type identity is global to a compilation, not local to a source file
or spelling. `type_ids.slg` lowers each source annotation through the recursive
term arena and interns the result by semantic identity. A locally spelled
`Point` and an imported `model.Point` therefore share the same nominal node and
the same complete `Result<[Point; ~], {Text: box Point}>` root. Local/imported
origin is provenance only and is deliberately excluded from nominal equality
once declaration module and symbol identity agree.

Builtin semantic types are seeded in the existing stable symbol order, so the
canonical ID of `Unit` through `Bool` is identical to the legacy builtin symbol
ID. `expression_type_ids.slg` is the migration bridge: builtin expressions map
directly, while annotation-backed name expressions and resolved call results
use the complete recursive annotation root. It copies the returned semantic
arena into its result because moving an owned array field directly between two
owned aggregate values would create ambiguous ownership at the current
bootstrap boundary.

Examples 285 and 286 prove cross-module canonical identity for recursive
annotations and expressions. This remains a partial migration: exact recursive
equality must next replace shallow comparisons in type checking, then flow
through generic specialization, typed IR, ownership/effects, and LLVM. The
roadmap count therefore remains 48.5/60 (80.8%).

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors. The focused 285/286 slice passed 2/2, and the single
coordinated eight-worker runner passed all 401 cases in 376.7 seconds with
flushed `n/401` progress records.

## D143 - Concrete Recursive IDs Govern Return And Argument Equality

Status: concrete annotation boundary implemented
Date: 2026-07-14

The self-host type checker now compares canonical recursive IDs for concrete
annotation-backed return expressions and call arguments. Equality therefore
includes every nested array, dictionary, box, and nominal-application child;
matching only the outer `Result` and its first component is no longer enough.
Example 287 proves that a two-module call accepts
`Result<[model.Point; ~], {Text: box model.Point}>` and rejects the otherwise
shape-identical type whose deepest nominal is `model.Other`. The same complete
comparison rejects a mismatching function return.

Recursive checking augments rather than blindly replaces the established
checker. A node records whether its tree still contains a generic parameter.
Only fully concrete expected and actual trees use exact ID equality; generic
call results remain on the existing specialization path until call-site
substitution also produces a canonical ID. When the old checker already emits
a mismatch, its stable diagnostic metadata is retained. An exact recursive
match can suppress a shallow cross-module false positive, and an exact
mismatch is added only when no older diagnostic covers that call or function.

`expression_type_ids.slg` maps builtin literals directly from the AST instead
of invoking the complete legacy expression inference pass a second time. This
keeps the migration bridge linear in the input representation and avoids
duplicating the dominant semantic pass inside `type_check`.

This is still a partial migration and does not promote a roadmap gate. Generic
call-site substitution must produce canonical IDs, and typed IR,
ownership/effects, and LLVM must consume them before the shallow representation
can be removed.

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors. The complete self-host type-check slice passed 45/45,
including the two-module recursive case, and the single coordinated
eight-worker runner passed all 402 cases in 378.5 seconds with flushed
`n/402` progress records.

## D144 - Recursive Generic Calls Specialize Canonical Type IDs

Status: call-site specialization and typed IR boundary implemented
Date: 2026-07-14

Generic call specialization now operates on complete canonical type trees.
The input template is structurally unified with the concrete argument, using
the declaring module and symbol as each type parameter's identity. Repeated
parameters must bind consistently, multiple parameters are independent, and
substitution rebuilds and interns every changed ancestor from the leaves to
the result root. A call such as
`Result<[T; ~], {Text: box T}> -> Result<[T; ~], {Text: box T}>` therefore
produces one concrete result ID without container-specific special cases.

The recursive expression pass now canonicalizes local and imported struct
literals plus dynamic-array, dictionary, and box literals. Type checking uses
the specialized input and result IDs, emits one argument diagnostic for an
inconsistent binding, and avoids a secondary return mismatch caused only by
the failed call. Qualified and role-call wrappers retain the same behavior.

`TypedIrNode.typeId` carries the canonical expression ID alongside the legacy
shallow fields during migration. Successful generic calls reach typed IR with
a fully concrete ID; a call whose substitution fails keeps `-1`. Example 288
proves deep repeated substitution, two-parameter swapping, literal-driven
array/dictionary/box specialization, one intentional mismatch, and five
concrete generic call IDs in typed IR across two modules.

This remains a partial migration and does not promote a roadmap gate. Fixed
array length identity, ownership/effect consumers, and self-host LLVM layout
and lowering still need to use canonical IDs before the shallow type fields
can be removed. The count remains 48.5/60 (80.8%).

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors. The representative typed-IR/generic slice passed
9/9, and the coordinated eight-worker runner passed all 403 cases in 411.8
seconds with flushed `n/403` progress records.

## D145 - Ownership Traits Fold Over Canonical Recursive Types

Status: move and coroutine-frame ownership boundary implemented
Date: 2026-07-14

Ownership classification is now derived from the canonical semantic type
arena instead of recognizing only shallow numeric origins. Each type receives
two independent traits: bit 0 means the value carries destruction
responsibility, and bit 1 means that responsibility reaches heap-backed
storage. Dynamic arrays, dictionaries, and boxes set both bits directly.
Fixed arrays and nominal applications such as `Option<T>` and `Result<T, E>`
fold both traits from their canonical child IDs. A local or imported nominal
declaration remains conservatively owned.

`TypedIrNode` carries these traits as `typeFlags` beside `typeId`. Exact
expression matches, binding operands, and resolved member field annotations
propagate the same canonical identity and traits. Typed IR can also materialize
an expression known only to the recursive pass when the temporary legacy
inference table has no entry. The recursive lookup is deliberately skipped
when legacy inference already has the expression; the final canonical mapping
then fills its ID once, avoiding an unnecessary all-expression scan for every
AST node.

Partial-member move discovery now tests canonical ownership rather than the
old array/dictionary/struct origin list. Coroutine frame planning uses heap
reachability, and `CoroutineFrameSlot` retains both canonical `typeId` and
`typeFlags` for later destruction and LLVM lowering. Example 289 proves that
`Result<[Int; ~], Text>` specialized through an imported generic call is
classified as owned and heap-reaching at the call, its binding, and the live
slot that crosses `await`.

This remains a partial migration and does not promote a roadmap gate. A
nominal node does not yet contain its declaration-field type edges, so heap
reachability through arbitrary nested struct fields is still supplied by the
older struct analysis. Capability escape and effect-set enforcement remain
separate work, and self-host LLVM still selects layout and drop glue from
shallow fields. The count remains 48.5/60 (80.8%).

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors. The representative typed-IR/move/async slice passed
13/13. After eliminating redundant recursive lookups, the coordinated
eight-worker runner passed all 404 cases in 429.8 seconds with flushed
`n/404` progress records.

## D146 - Nominal Field Graphs Join Canonical Type Identity

Status: nominal ownership graph and first LLVM consumer implemented
Date: 2026-07-14

The canonical type context now records each nominal declaration field as an
edge from owner type ID to field type ID. An edge also retains declaration
module, owner and field symbols, source-order ordinal, and resolution status.
The field type comes directly from the source's completed `TypeTerm -> typeId`
map, so graph construction does not re-resolve syntax or search the global
reference table.

Ownership classification is a monotone fixed point over both ordinary type
children and nominal field edges. Heap reachability can therefore flow from a
dynamic array through `Result<[Int; ~], Text>` and then through an enclosing
`Envelope` struct. Applications that refer back to a nominal type are updated
on later iterations without depending on declaration order. Example 290
proves the field edge and follows the resulting owned/heap traits through an
imported generic call, its binding, and an `await`-crossing coroutine slot.

`TypedIrNode.typeKind` carries the canonical arena kind beside `typeId` and
`typeFlags`; bindings, member fields, and coroutine slots preserve it. LLVM
type selection now follows canonical kind and ownership whenever `typeId` is
available. Only not-yet-migrated nodes use the explicit shallow compatibility
branch. The central edge-cleanup selector likewise prefers canonical array,
dictionary, nominal, and owned facts. Example 291 deliberately corrupts the
legacy origin and scalar symbol while retaining the canonical fields, then
proves LLVM still selects `%sollang.array.i32` and the correct struct type.

The first field-graph implementation searched all global references once per
field and made the 406-case run take 599.9 seconds. Reusing each source's local
completed term map reduced the final run to 495.9 seconds; the representative
LLVM dynamic-array case took 83.7 seconds alone. A reusable whole-compilation
analysis context is still needed to remove repeated semantic passes across
typed IR and LLVM rather than optimizing individual scans indefinitely.

This remains a partial migration and does not promote a roadmap gate. LLVM
storage size/alignment, aggregate layouts, recursive drop-glue tasks, and
generic nominal-application lowering still use shallow fields in substantial
paths. Those consumers must use the canonical type and field graph before the
compatibility fields can be removed. The count remains 48.5/60 (80.8%).

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors. The recursive-type/ownership/LLVM focused slice
passed 11/11. The optimized coordinated eight-worker runner passed all 406
cases in 495.9 seconds with flushed `n/406` progress records.

## D147 - LLVM Storage Layout Uses Canonical Field Graphs

Status: target-aware storage layout and aggregate type declarations implemented
Date: 2026-07-14

Canonical semantic types now retain a parsed fixed-array `length` in addition
to the spelling hash used for identity. Numeric lengths therefore drive LLVM
storage size while value-generic length identifiers remain unresolved until
specialization. Every struct declaration that has fields also seeds its own
canonical nominal node before later modules are visited. A declaration no
longer loses its owner-to-field edges merely because its first type reference
appears in a later file.

`llvm.text.layoutsFor` computes size, ABI alignment, and status arrays over the
canonical arena. Builtin integers and floats use their declared widths;
`Size` and `UIntSize`, pointers, slices, dynamic arrays, dictionaries, boxes,
and text use the selected target pointer width. Fixed arrays multiply the
canonical element layout by the concrete length. Nominal struct layout is a
monotone fixed point over field edges, applies alignment padding at every
field and tail, and leaves unsupported generic applications or unresolved
recursive inline values explicitly non-layoutable.

The LLVM emitter prepares these facts once for the selected Windows x64,
Linux x64, or wasm32 descriptor. Mutable loads, stores, and allocas obtain
alignment from canonical type IDs. Named struct declarations are emitted from
canonical nominal nodes and field edges rather than rescanning source symbols,
nominal annotations, and composite annotations. The iterative type writer
also handles nested fixed arrays without relying on recursive inline Sollang
functions, which the current runtime intentionally rejects.

Example 292 proves a fixed `[Int16; 3]` layout of size 6/alignment 2 and a
field-graph struct whose dynamic-array descriptor produces different correct
x64 and wasm32 padded sizes. The example constructs the fixed type directly
because self-host AST lowering of fixed-array annotations remains a separate
open parser slice; host value-generic fixed arrays remain covered by examples
50, 51, 57, and 58.

This is still a partial migration and does not promote a roadmap gate. Dynamic
array and dictionary element allocation/index paths still carry shallow scalar
component symbols, recursive drop glue still reconstructs fields from source,
and generic nominal applications do not yet have concrete enum/variant layout.
LLVM preparation also repeats semantic resolution after typed IR lowering;
the next performance slice should return one reusable whole-compilation
analysis context. The count remains 48.5/60 (80.8%).

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors. The LLVM/type-layout focused slices passed 7/7 and
13/13 with counted progress output. The collision-free coordinated runner
passed 405/407 cases in 416.7 seconds; its only two mismatches were the intended
struct and dynamic-array slot alignment changes from 4 to 8. After updating
those exact LLVM baselines, examples 231 and 232 passed 2/2, providing complete
evidence for all 407 current cases without rerunning the unchanged 405 cases.

## D148 - Prepared Semantic Context Removes Repeated LLVM Analysis

Status: canonical type and shallow declaration context reuse implemented
Date: 2026-07-14

Expression type-ID resolution and typed-IR lowering now expose prepared entry
points. `ExpressionTypeIdRequest` accepts an already resolved canonical type,
reference, and field arena. `TypedIrRequest` adds the nominal, composite, and
module tables needed by typed IR. The original `resolve` and `lower` APIs remain
compatible wrappers that prepare these inputs once, so existing clients keep
their surface while orchestration layers can reuse analysis products.

LLVM preparation is the first orchestration consumer. It resolves canonical
types, nominal annotations, composite annotations, and module identities once,
copies their flat relocatable records into the typed-IR request and emitter
context, and calls `lowerPrepared`. It no longer invokes `typeIds.resolve`,
`nominalTypes.resolve`, `compositeTypes.resolve`, or `modules.identities` again
after typed IR has independently performed the same work. The arrays are copied
because the current affine container model cannot yet move one owned field out
of a returned aggregate; semantic passes themselves are not repeated.

Example 293 constructs the prepared request explicitly and proves that its 11
typed-IR nodes have the same kind, canonical type ID, kind, and ownership flags
as the compatible `typedIr.lower` path; eight nodes carry canonical IDs. The
representative LLVM dynamic-array example 188 fell from the prior 83.7-second
single-worker baseline to 63.8 seconds, a 23.8 percent reduction. Sharing the
additional nominal/composite/module tables preserved the result at 64.0 seconds;
those tables are not the remaining dominant cost.

This remains a partial migration and does not promote a roadmap gate. Calls,
qualified resolution, AST/lexer/symbol products, shallow expression inference,
and several ownership/drop consumers still recompute their own tables. A future
whole-compilation context should cache those immutable products per source and
pass one package-qualified context through checking, typed IR, effects, layout,
drop glue, and code emission. The count remains 48.5/60 (80.8%).

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors. The prepared/baseline typed-IR slice passed 13/13,
example 293 passed independently, and example 188 passed in 63.8 seconds. The
single coordinated eight-worker runner then passed all 408 cases in 396.5
seconds with flushed `n/408` progress records. Compared with the preceding
collision-free 416.7-second 407-case run, total wall time fell by 20.2 seconds
despite adding one case.

## D149 - Prepared Resolution Products Flow Through Typed IR

Status: module-call and qualified-resolution reuse implemented
Date: 2026-07-14

The prepared semantic boundary now includes module-qualified names and resolved
calls. `ModuleCallRequest` accepts existing module identities and qualified
results, `ExpressionTypeRequest` accepts nominal, composite, module, qualified,
and call tables, and `ExpressionTypeIdRequest` consumes the same package-level
products. `TypedIrRequest` carries all of them. The LLVM orchestrator computes
qualified resolution and module calls once and supplies those immutable flat
records to shallow inference, recursive expression IDs, and typed IR instead
of allowing every layer to resolve them again. Compatible `resolve`, `infer`,
and `lower` wrappers still construct the complete request for direct clients.

Source-local preparation also gained two lower-level boundaries.
`symbols.collectPrepared` builds a symbol table from an existing AST, and all
semantic passes that already own AST nodes use it instead of parsing the same
source again. `resolution.resolvePrepared` accepts existing AST, token, and
symbol products; its compatible wrapper preserves the old source-only API.
The latter is the contract for the next package cache, while broad consumers
still need to retain or copy those owned arrays before they can use it.

Example 293 now explicitly prepares qualified and call products and still
proves byte-for-byte semantic equivalence at the typed-IR boundary. The
single-worker LLVM example 188 improved from 63.8 to 58.6 seconds, another
8.2 percent reduction and 30.0 percent below the earlier 83.7-second baseline.
The coordinated eight-worker full run remained effectively flat at 397.9
seconds versus 396.5 seconds because eight simultaneous self-host LLVM cases
are dominated by shared CPU and linker contention rather than this serial
semantic slice.

This remains a partial migration and does not promote a roadmap gate. A true
package analysis context must own flattened per-source AST, token, symbol, and
resolved-name ranges so consumers can borrow them without repeated copying.
Type checking, ownership/effects, drop glue, and several LLVM source scans must
then consume that context. The count remains 48.5/60 (80.8%).

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors. The recursive type/ownership/layout prepared slice
passed 7/7, example 188 passed in 58.6 seconds, and the single coordinated
eight-worker runner passed all 408 cases in 397.9 seconds with monotonic
`n/408` progress records.

## D150 - Whole-Compilation Facts Use One Borrowed Flat Context

Status: package context connected through checking, typed IR, and LLVM
Date: 2026-07-14

`sollang.compiler.semantic.analysis` now builds one relocatable package
front end. `PackageAnalysis` owns flat source, AST, token, symbol, and resolved
name arrays. Each `SourceAnalysisRange` maps a source-local index space onto
those arrays. AST parent indexes, symbol indexes, and resolved-name indexes
therefore stay local and stable; consumers add the source range start only at
the package boundary. This avoids nested owned arrays and remains suitable for
serialization, memory mapping, and later incremental invalidation.

`sollang.compiler.semantic.context.CompilationContext` combines those source
products with canonical types/references/fields, nominal and composite facts,
module identities, qualified names, and resolved calls. `inferContext`,
`resolveContext`, and `lowerContext` borrow that one aggregate. Type checking
and LLVM orchestration prepare it once and pass it through shallow expression
inference, recursive expression IDs, and typed IR. These hot paths index the
flat products directly rather than copying per-source AST/token/symbol arrays.
The older source-only and explicit request APIs remain compatibility wrappers.

Module identity, import, qualified-name, and call resolution also expose
`*Analyzed` entry points over the same package products. The context builder
uses them, so these passes no longer re-lex or re-parse merely to inspect module
paths and call names. Example 294 proves two source ranges are contiguous for
all four flat product tables, retain source-local root indexes, and account for
every stored record. Example 293 proves legacy prepared, source-only, and
borrowed-context typed IR remain equivalent.

The first owned-request version copied the package arrays into every semantic
request and raised example 188 to 65.1 seconds. It was rejected and replaced
before this decision was finalized. The borrowed context plus direct flat
indexing measures 63.5-64.1 seconds on the same single-worker example, close to
the earlier stable 63.8-second measurement but above the one-off 58.6-second
low. The two 409-case coordinated runs took 415.5 and 421.2 seconds versus
397.9 seconds for 408 cases. This is not claimed as a performance win: canonical type,
nominal, composite, and type-use resolvers still build their own source-local
products, so the context currently adds one package preparation before those
remaining migrations remove the older work.

The first analyzed module APIs duplicated their compatibility implementations,
which made every self-host manifest compile both paths. The final source keeps
one analyzed implementation and turns the original module/import/qualified
entry points into short wrappers. This removes that source duplication; a
focused post-refactor module/checking/IR/LLVM slice passed, while another full
run was avoided because the preceding two had already established the 409-case
behavior and the wrapper refactor did not alter the analyzed core.

This slice does not promote a formal gate. The next performance step is to make
canonical type terms/IDs, nominal types, composite types, and imported type
resolution consume `PackageAnalysis`; only then should package-context speed be
judged. Ownership/effect products, recursive drop glue, and remaining LLVM
source scans must join the same context afterward. The count remains 48.5/60
(80.8%).

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors. Representative module, type-check, expression-ID,
typed-IR, LLVM, and context slices passed. Two coordinated eight-worker runs
passed all 409 cases in 415.5 and 421.2 seconds with monotonic `n/409` records;
the final compatibility-wrapper deduplication then passed its focused 7/7
slice.

## D151 - Canonical Type Preparation Joins Package Analysis

Status: recursive terms, canonical uses, and type resolvers share package facts
Date: 2026-07-14

`PackageAnalysis` now also owns flat recursive type-term and canonical type-use
arrays. `SourceAnalysisRange` adds contiguous `termStart`/`termCount` and
`typeStart`/`typeCount` pairs while preserving the source-local AST, token,
symbol, child-term, and canonical-use indexes stored in each record. Example
294 verifies both new ranges begin at zero for the first source, join exactly
at the second source, and account for every stored record.

The type-term and type-use lowerers expose prepared syntax entry points. Package
analysis parses and lexes each source once, then supplies the already built AST
and token tables to both lowerers. Canonical type-ID resolution consumes package
module identities, qualified names, AST, tokens, symbols, and term ranges.
Imported-type, nominal-type, and composite-type resolution consume the same
qualified results, nodes, tokens, symbols, and canonical type-use ranges.
Their original source-only entry points now create one package and delegate to
the analyzed implementation, preserving compatibility without retaining a
second resolver body.

The current affine aggregate model still requires one transient copy of each
source's AST and token records into the shared prepared-syntax request while
`PackageAnalysis` is being assembled. This replaces repeated parsing and
lexing, but it is not yet a zero-copy construction path. A future borrowed-view
or builder representation can remove that construction copy without changing
consumer APIs.

This slice does not promote a formal roadmap gate. Ownership/effect products,
recursive drop glue, and remaining LLVM source scans must still consume the
same context; dynamic-array/dictionary child lowering and generic nominal
application layout also remain open. The canonical count stays 42 complete,
13 partial, and 5 missing: 48.5/60 (80.8%).

Regression evidence on 2026-07-14: direct type-term/type-use tests passed 3/3;
the module/imported/nominal/composite/type-ID/context slice passed 8/8; Release
build completed with zero warnings and errors; and the single coordinated
eight-worker full runner passed 409/409 in 417.7 seconds with monotonic flushed
progress records. Example 188 passed alone in 65.08 seconds. These measurements
sit within the preceding context range (415.5-421.2 seconds full and roughly
63.5-64.1 seconds single), so the change is recorded as architectural reuse,
not a demonstrated performance improvement.

## D152 - Diagnostics, Coroutines, and Drop Glue Share Analysis

Status: ownership/type diagnostics and coroutine/drop consumers connected
Date: 2026-07-14

Type diagnostics no longer call nominal and composite source-only resolvers
independently or lower the AST again for spans. `analyzeContext` reads the
already resolved nominal/composite tables and flat AST ranges from one
`CompilationContext`. Partial-move ownership diagnostics similarly call
`typedIr.lowerContext` once and compare member paths against the context's
source-local AST/token ranges rather than parsing and lexing for each move.
Their source-only `analyze` entry points remain thin compatible wrappers.

Coroutine analysis now has one aggregate boundary. `CoroutinePlan` contains
typed IR, suspension points, live frame slots, and deterministic destruction
slots. `coroutinePlanContext` lowers typed IR once and derives all three side
tables from the same flat AST/token products. The legacy `suspensions`,
`frameSlots`, and `destroySlots` functions copy the corresponding table from a
single plan for compatibility. Example 242 uses the aggregate directly, so its
three requested tables no longer trigger six typed-IR lowerings.

LLVM `EmitContext` now retains the package ranges, AST, token, and symbol tables
needed after semantic preparation. Recursive struct drop and partial-member
move filtering use these records directly. They no longer call AST lowering,
lexing, or symbol collection while walking recursive drop tasks. The remaining
emitter still contains other source scans; those are an explicit next migration
target rather than being hidden by this decision.

Example 295 prepares one context and passes it to type diagnostics, ownership
diagnostics, and coroutine planning. Its executable result proves zero type
errors, one overlapping partial-move error, two suspension points, six live
frame slots, and five destruction entries from the shared products. Existing
diagnostic and async compatibility paths remain covered independently.

This migration does not promote a formal language gate. It changes reuse and
orchestration, not the completeness of effect enforcement, all-exit cleanup,
generic nominal layout, or canonical container-child lowering. Progress stays
42 complete, 13 partial, 5 missing: 48.5/60 (80.8%).

Regression evidence on 2026-07-14: type/ownership diagnostic tests passed 6/6,
the async/ownership/context slice passed 12/12, recursive and partial-move LLVM
drop tests passed 6/6, and example 295 passed independently. Release build had
zero warnings and errors. The coordinated eight-worker full suite passed all
410 cases in 388.7 seconds with monotonic flushed `n/410` progress. This is
29.0 seconds, or 6.9 percent, below the preceding 417.7-second 409-case run
despite one added case; it is recorded as an observed full-run improvement,
not as proof of an isolated consumer microbenchmark.

## D153 - LLVM Emission Uses Flat Package Syntax Products

Status: direct emitter AST, token, and symbol rebuilding removed
Date: 2026-07-14

`sollang.compiler.llvm.text` no longer calls `lexer.lex`, `ast.lower`, or
`symbols.collect` while emitting a prepared package. Its 67 direct source
reanalysis sites now translate each typed-IR node's source-local indexes through
`SourceAnalysisRange` and read the flat `EmitContext` AST, token, and symbol
tables. Small helpers cover typed-IR payload tokens and AST starts; member-field
and interpolation-name searches use the declaring source's explicit token and
symbol ranges so imported layouts retain source-local identity.

This migration covers function and `main` scheduling, mutable-read/effect
ordering, region and return cleanup, literal and container operands, nested
conditionals, calls, member projections, interpolation value lookup, and
recursive partial-move comparisons. Source bytes remain intentionally present:
token spans point into the original UTF-8 source and LLVM literal emission must
still read those bytes. At this decision's boundary `ir.interpolation.lower`
still built its own source-level syntax products; D154 removes that remaining
indirect repetition.

No formal language gate is promoted. This is an analysis-reuse boundary, not
completion of capability/effect enforcement, canonical container-child
lowering, generic nominal-application layout, or all-exit cleanup. The canonical
count remains 42 complete, 13 partial, and 5 missing: 48.5/60 (80.8%).

Regression evidence on 2026-07-14: example 188 passed independently in 56.62
seconds. The single coordinated eight-worker suite then passed 410/410 in
395.4 seconds with flushed monotonic `n/410` progress. That is within normal
full-run variation of the preceding 388.7-second run, so no isolated speedup is
claimed. The Release solution build completed with zero warnings and errors,
and source inspection finds zero direct `lexer.lex`, `ast.lower`, or
`symbols.collect` calls in the LLVM text emitter.

## D154 - Interpolation Reuses Prepared Source Syntax

Status: interpolation source analysis prepared once for LLVM emission
Date: 2026-07-14

`sollang.compiler.ir.interpolation` now exposes `lowerPrepared`. It accepts
the source's already prepared AST, token, and symbol products and performs only
the interpolation-specific work. The compatible `lower source` entry point
still builds those inputs for standalone callers such as example 209, then
delegates to the same implementation.

LLVM preparation creates one relocatable interpolation range per source and
stores all `InterpolationNode` records in one flat table. Parent and operand
indexes are shifted to package-global offsets while stored, then a source-local
view is reconstructed only where the existing expression emitter needs local
indexes. Runtime helper detection reads root nodes directly by range. Function
and `main` emission, integer/bool helper selection, and repeated string
references therefore no longer invoke `interpolation.lower` and no longer
re-lex, re-lower, or recollect the enclosing source.

Embedded `$(expression)` fragments still require their own expression-fragment
lexing and parsing because they are text inside a string token and are not part
of the enclosing module AST. That is semantic work unique to interpolation,
not duplicate module analysis. The current preparation makes source-local AST,
token, and symbol copies because affine arrays do not yet expose borrowed
slices; replacing those copies with range views remains an allocation
optimization, not a correctness gap.

This reuse slice does not promote a formal language gate. Progress remains 42
complete, 13 partial, and 5 missing: 48.5/60 (80.8%). Focused compatibility and
LLVM checks passed 2/2, the interpolation/LLVM group passed 9/9, the coordinated
eight-worker suite passed 410/410 in 398.0 seconds with monotonic progress, and
the Release build completed with zero warnings and errors. The full time is
again within the recent 388.7-395.4-second range and is not claimed as an
isolated speedup.


## D155 - Functions Declare Closed Capability Effect Sets

Status: reference compiler propagation implemented; self-host product pending
Date: 2026-07-14

Sollang functions are pure by default and place a closed capability set after
the return type: `-> Unit uses Console` or `-> Result<T, Text> uses File,
Clock`. The initial names are `Console`, `File`, `Clock`, `Random`, `Process`,
and `Environment`. Unknown and duplicate names are rejected. Every ordinary,
local, generic, block, member, zero-input, and imported call checks that the
caller contains the callee's required set. `main` is the unrestricted program
boundary. Memory-map construction and mapped-view `flush` require `File`
directly because they are syntax forms rather than function calls.

`async` remains an execution/suspension property, not an authority token. This
keeps CPU-pure scheduling separate from clock or I/O permission and leaves a
clean future path for `handle` role blocks to discharge handled effects. The
standard library declares its public capability contracts, and the self-host
LLVM emitter declares `Console` through its nested output helpers. Generic
specialization validation now snapshots and restores the caller semantic
context; without that restoration, validating a pure specialization could
incorrectly erase the enclosing caller's effect set.

Examples and diagnostics cover positive transitive propagation, missing direct
and transitive Console effects, unknown and duplicate effects, File-backed map
syntax, async Clock functions, generic-call context restoration, role blocks,
and owned file APIs. This slice does not promote a formal roadmap gate because
the flat self-host effect analysis product and handler discharge are not yet
implemented. The canonical count remains 42 complete, 13 partial, and 5
missing: 48.5/60 (80.8%).

Regression evidence on 2026-07-14: the focused effect set passed 5/5, file and
mapped-I/O migration passed 7/7, grammar/effect snapshot corrections passed
5/5, and the coordinated eight-worker full suite passed 416/416 in 393.2
seconds with flushed monotonic `n/416` progress. The Release solution build
completed with zero warnings and errors.

## D156 - Self-Host Effects Are A Flat Context-Derived Product

Status: declaration and call propagation implemented; handler discharge pending
Date: 2026-07-14

`sollang.compiler.semantic.effects` derives one source-qualified
`FunctionEffect` record per function and structured `EffectDiagnostic` records
from a borrowed `CompilationContext`. Effect masks use stable bits for Console,
File, Clock, Random, Process, and Environment. The pass reads the context's
flat source ranges, AST, tokens, symbols, and resolved calls; it does not lex,
parse, collect symbols, or resolve modules again. Unknown and duplicate
declarations are recorded before missing-caller diagnostics, and `main` remains
the unrestricted root boundary.

The prepared call product now retains unresolved flow calls long enough to
match qualified imports, resolves lexical local functions by owner symbol, and
assigns stable negative symbols to global runtime aliases including console,
file, random, and clock operations. This closes two pre-existing self-host
gaps exposed by effect propagation. The grammar VM also now permits newlines
after local function declarations, matching the reference parser's existing
behavior.

Example 297 prepares one `CompilationContext` and passes it to
`effects.analyzeContext`. It proves pure, multi-effect, local, imported, and
builtin-alias facts and diagnostics across all six initial capability names.
The design follows the subset rule used by Unison abilities and Koka effect
types. Handler subtraction is reserved for separately declared user-defined
effect operations; fixed external capabilities cannot be erased by an
ordinary role block. User-defined effect operations and their handler lowering
remain explicit gaps, so no formal gate is promoted and progress remains
48.5/60 (80.8%).

Regression evidence on 2026-07-14: the call/grammar/effect focused set passed
22/22, including the generic-role `yield` regression and example 297. The
coordinated eight-worker full suite then passed 417/417 in 397.4 seconds with
flushed monotonic `n/417` progress. The Release solution build completed with
zero warnings and errors.

## D157 - Fixed Capabilities Are Not User-Handleable Effects

Status: fixed capability boundary and self-host map/flush parity implemented;
user-defined effect signatures pending
Date: 2026-07-14

Console, File, Clock, Random, Process, and Environment describe authority over
real external resources. Letting a normal `handle` role subtract one of these
bits without replacing its runtime implementation would allow a function to
be typed as pure while it still prints, reads files, or starts processes. Sollang
therefore does not treat the closed capability set as user-handleable algebraic
effects.

This matches the capability interpretation in modern Effekt and the evidence
boundary used by Koka: fixed resources remain explicit requirements, while a
future user-defined effect signature may be installed and discharged by a
matching lexical handler. The accepted ordinary `handle` block-function form
remains the surface mechanism, but it gains no authority merely from its name.

The self-host grammar now recognizes `map read` and `map write` as a distinct
AST kind. `semantic.effects` derives File requirements for map construction
directly from the prepared AST and for mapped-view `flush` through stable
runtime alias -114. A lexically resolved user function named `flush` still
wins over the runtime alias. Example 297 proves missing File diagnostics for
both forms and positive `uses File` coverage without rebuilding syntax.

No formal gate is promoted: effect signatures, typed operations, lexical
handler matching, capability non-escape, nested-handler selection, and LLVM
handler lowering remain. Progress stays 48.5/60 (80.8%).

Regression evidence on 2026-07-14: the focused map/grammar/effect set passed
12/12; the coordinated eight-worker full suite passed 417/417 in 392.1 seconds
with flushed monotonic `n/417` progress. The Release solution build completed
with zero warnings and errors.

## D158 - User Effects Are Typed Module Symbols

Status: self-host declaration and call analysis implemented; execution pending
Date: 2026-07-14

User-defined handleable effects use module-level declarations whose operations
have ordinary static input and return types:

```sollang
public effect Failure {
    fail message: Text -> Int
}

parse text: Text -> Int uses Failure {
    text -> fail
}
```

The generated grammar, self-host AST, and symbol collector represent effects
and their operations directly. `semantic.user_effects` derives flat
`UserEffectSignature`, `UserEffectOperation`, `UserEffectRequirement`,
`UserEffectCall`, and `UserEffectDiagnostic` tables from one borrowed
`CompilationContext`; it does not rebuild source syntax or module resolution.
Qualified requirements such as `uses fx.Failure` obey normal module aliases and
public visibility. Bare operation calls are selected only from the caller's
declared user effects, while an ordinary resolved lexical function wins. An
explicit same-module `Failure.fail` call without `uses Failure` is diagnosed;
qualification never grants authority.

Example 298 proves public and private signatures, typed zero/one-input operation
facts, qualified requirements, ordinary operation calls, duplicate operation
names, unknown and private imported effects, and a missing-`uses` call. The
closed Console/File/Clock/Random/Process/Environment capabilities remain a
separate non-handleable set.

This is deliberately a self-host frontend slice. The reference parser does not
yet accept effect declarations, imported explicit operation calls without
`uses` need a direct diagnostic path, and canonical operation type checking,
lexical handler matching, capability non-escape, nested selection, resumptions,
and LLVM lowering remain. User effect operations are therefore not claimed as
runtime-executable, and the formal roadmap score stays 48.5/60 (80.8%).

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors; the focused grammar/module/call/effect slice passed
26/26, including the long LLVM overlap selected by substring filters; and the
coordinated eight-worker full suite passed 418/418 in 431.5 seconds with
flushed monotonic `n/418` completion records.

## D159 - User Effect Operations Carry Canonical Types

Status: self-host semantic analysis implemented; runtime handling pending
Date: 2026-07-15

User-effect operation input and return annotations resolve to the same
canonical type IDs used by expression analysis. Calls retain their argument and
return IDs, diagnose zero/one-input arity mismatches, and compare argument types
without spelling-based aliases. Explicit imported `Effect.operation` calls use
normal alias, visibility, and `uses` resolution. Example 299 fixes the flat
operation/call/diagnostic contract. Handler matching, resumptions, reference
parser support, and LLVM execution remain open, so the roadmap score is not
promoted.

## D160 - Regression Selection Follows Stable Compiler Layers

Status: implemented
Date: 2026-07-15

The example runner supports exact names, changed-source dependency selection,
and `reference`, `semantic`, `selfhost`, `llvm`, `fast`, and `full` suites.
Development checks can therefore prove the affected compiler layer quickly,
while one unfiltered full run remains the commit gate. All modes retain flushed
`[n/total]` progress and zero-warning Release bootstrap verification.

## D161 - Self-Host LLVM Fixtures Reuse One Native Sollang Compiler

Status: implemented for emitter fixtures
Date: 2026-07-15

Thirty-seven emitter fixtures formerly rebuilt the same multi-file self-host
compiler through the C# reference compiler. One representative split measured
56.05 seconds for that outer build, 0.02 seconds for the generated compiler to
emit LLVM, and 0.01 seconds for `llvm-as`. The runner now bootstraps one native
Sollang driver and reuses it across Windows, Linux, and Wasm cases. The original
bootstrap passed source modules as literal process arguments; D163 replaces
that temporary boundary with mapped source-file paths. Freshness covers
the reference compiler output, driver manifest, listed Sollang sources, and standard
library. The two non-emitter introspection cases keep their original path.

Measured verification: cold driver bootstrap took 56.7 seconds. With the driver
current, all 39 self-host LLVM tests passed in 4.1 seconds; individual emitter
fixtures completed in roughly 0.02-0.26 seconds. This replaces repeated
whole-compiler compilation with the intended bootstrap-once architecture.

## D162 - Owned Array Push Transfers Rather Than Copies

Status: reference semantic and LLVM lowering implemented
Date: 2026-07-15

Pushing a named value whose type owns storage into `[T; ~]` is a move. The
array receives the exact aggregate bits, the source binding becomes unavailable
immediately, and only the array drops the element. This replaces the former
restriction that accepted only freshly constructed owned arguments. It also
makes affine mapped-file owners collectable for a future file-based self-host
compiler without copying mappings or weakening deterministic unmapping.

The LLVM representation of `MappedBytes` and `MutableMappedBytes` is now a
first-class `%sollang.mapped_bytes` aggregate inside generic arrays. Array
growth copies the aggregate, source-owner removal suppresses the old drop, and
element cleanup extracts the base mapping and length before unmapping exactly
once. Example 300 proves a mapped source owner array; example 301 proves a boxed
user value move; the negative diagnostic proves use-after-move rejection.
Owned element extraction by index remains separate work, so the formal gate
count remains 42 complete, 13 partial, 5 missing (48.5/60, 80.8%).

## D163 - Native sollangc Owns Mapped Source Files

Status: file-backed stage-1 emission and reusable test bootstrap implemented;
toolchain invocation and stage-2 comparison pending
Date: 2026-07-15

`sys.file.SourceText` is the affine owner for compiler input. It stores a
bounded UTF-8 view plus the hidden base mapping needed for deterministic
unmapping. Syntax entry points accept `SourceText` directly, and
`semantic.context.prepareFiles` owns all module mappings for the lifetime of a
single immutable `CompilationContext`. Compatibility entry points for borrowed
`Text` remain available.

The native `selfhost-sollangc-driver` accepts a target mode followed by source-file
paths. It maps every module, creates non-escaping `Text` views for the existing
LLVM emitter, and keeps the mapping owners alive until emission completes. The
example runner builds this stage-1 executable only when its compiler, manifest,
Sollang modules, or standard library inputs are newer; all emitter fixtures then
reuse it and pass materialized source paths rather than embedding whole source
programs in process arguments.

This path exposed a latent Windows runtime defect: generated functions larger
than one committed stack page called an empty `__chkstk`. The runtime now probes
each 4096-byte page while preserving the Windows x64 `__chkstk` register
contract. The 234KB self-host `emitCore` frame therefore grows the guarded
stack safely. A Windows stage-1 executable compiled two source files to LLVM,
exited with code 0, and the result passed `llvm-as`; the same pipeline passed
under Linux ASan. A cold focused test took 59.7 seconds including bootstrap,
while the current native `sollangc` warm path took 1.1 seconds total and 0.12 seconds
for self-host compilation.

The formal roadmap score remains 42 complete, 13 partial, 5 missing
(48.5/60, 80.8%): stage 1 emits valid LLVM from files, but it does not yet
invoke the platform linker itself or prove a reproducible stage-2 compiler.

## D164 - Native sollangc Reuses a Bootstrap and Drives the Host Toolchain

Status: stage-1 native build orchestration implemented; reproducible stage 2
pending
Date: 2026-07-15

The reusable native `selfhost-sollangc-driver` now has a `build-windows` mode. It
self-invokes its existing multi-file emitter, redirects the emitted LLVM IR to
a named file through `sys.process.runToFile`, and passes that IR to the pinned
Clang driver. Clang remains responsible for the target backend, assembler, and
linker pipeline; Sollang owns source mapping, module compilation, argv construction,
exit-code checking, and artifact selection. No command shell or command-string
parsing is involved.

`runToFile` accepts a typed `RunToFileRequest { argv, output }`. Windows uses
`CreateProcessW` with explicitly inherited standard handles and correct Windows
argv quoting. Linux redirects the child stdout descriptor around the existing
`posix_spawnp` path. Example 306 proves literal argv preservation, output-file
creation, the child exit code, and captured bytes; example 87 continues to
prove the ordinary inherited-output process path.

One C#-bootstrapped native stage-1 driver compiled a two-module Sollang program,
invoked Clang, produced a Windows executable, and ran it with
`module answer = 42`. The example runner now builds the reusable driver at
`-O0` and includes its bootstrap configuration in freshness inputs. This keeps
the one-time native bootstrap near 2.5 seconds instead of spending roughly a
minute optimizing a compiler that is immediately reused for small fixtures.
The coordinated self-host suite passed 166/166 in 40.5 seconds including a
cold reusable-driver rebuild; individual LLVM emitter fixtures remained around
0.02-0.35 seconds.

LLVM allocas discovered during function lowering are now buffered and emitted
once in the entry block. An `alloca` inside a loop otherwise consumes new stack
space on every iteration until function return, making an unoptimized native
compiler depend on optimizer hoisting. The buffered representation is linear
to render and avoids the quadratic cost of repeatedly inserting text into the
already emitted module. Windows native images retain an 8 MiB reserved stack
and the real `__chkstk` probe from D163.

A full 27-module stage-2 attempt now crosses file mapping, parser, semantic
analysis, and typed-IR lowering and reaches self-host LLVM emission. It does
not yet produce a complete stage-2 module: the self-host emitter still lacks
the complete compiler-sized lowering surface. The formal score therefore
remains 42 complete, 13 partial, 5 missing (48.5/60, 80.8%).

Final coordinated verification passed 428/428 examples and diagnostics with
eight workers in 37.0 seconds. The Release solution build completed with zero
warnings and zero errors.

## D165 - Deterministic Grammar VM with Failure Memoization

Status: implemented and regression-tested
Date: 2026-07-15

Generated optional and repeated productions now commit after a successful
match, matching the existing committed-alternative bytecode instead of leaving
successful choice points available to unrelated caller failures. The grammar
VM also memoizes failed `(rule, token)` pairs in a collision-free table sized
to the current token stream. This bounds repeated negative parsing while
preserving the lossless event rollback used by CST construction.

Deterministic repetition exposed `else` as the only contextual terminator that
could also parse as a general identifier expression. Grammar opcode 8 and the
`notKeyword("else")` predicate now guard subject and expression `when` arms
without consuming input. The contextual enum-pattern and CFG suspension tests
cover both forms.

The reusable native stage-1 compiler remains an `-O0` bootstrap because it
builds in about 2.3 seconds and is cached until compiler inputs change. In the
compiler-sized 27-module probe, the corrected `-O0` executable reached the
known stage-2 emitter trap in 26.3 seconds; an independently cached `-O2`
stage-1 reached the same point in 8.9 seconds, but cost 83.5 seconds to rebuild.
The ordinary test loop therefore keeps the fast cold bootstrap, while a stable
optimized stage-1 is useful for repeated full-compiler work.

## D166 - Fast Regression Reuses Stage 1; Stage 2 Is a Separate Gate

Status: reusable regression path complete; stage-2 LLVM assembly pending
Date: 2026-07-15

The example runner treats the native `selfhost-sollangc-driver` as a freshness-keyed
stage-1 artifact. It is rebuilt only when the driver manifest, compiler DLL,
self-host modules, standard library, or bootstrap configuration changes. All
self-host LLVM fixtures then invoke that executable directly and may run in
parallel. A current emitter-affected selection passed 40/40 in 7.9 seconds,
with most native cases taking 0.02-0.20 seconds. The full regression passed
432/432 in 49.8 seconds while reusing the current driver.

Compiler self-reproduction remains a deliberately separate, expensive gate.
One stage-1 process now owns a flat `CompilationContext`, analyzes all 27
compiler modules once, and emits the complete stage-2 module without rebuilding
the compiler per source or per fixture. The latest completed emission produced
47,574 LLVM lines in 340.1 seconds. This is not part of the ordinary edit-test
loop: bootstrap/release verification regenerates it only when its freshness key
changes, then runs `llvm-as`, the platform linker, and the multi-file smoke
program.

The stage-2 emitter now covers imported composite return inference, transparent
operator operands, mutable initializer scheduling, loop-local member/length/
index recomputation, target-width length conversion, and struct literals inside
control-flow regions. LLVM assembly is still an open checklist item; a completed
text emission alone must not promote the self-hosting gate.

Checklist:

- [x] Reuse one native stage-1 compiler across self-host LLVM fixtures.
- [x] Rebuild the artifact only when a declared freshness input changes.
- [x] Share one borrowed flat `CompilationContext` across the 27-module build.
- [x] Keep monotonic `n/total` output and parallel affected/full regression.
- [x] Complete stage-2 text emission without a native crash.
- [x] Assemble the complete stage-2 IR with `llvm-as`.
- [x] Link and run the stage-2 compiler.
- [x] Compile and execute a multi-file Sollang smoke program with stage 2.

## D167 - Lift Local Functions Before Parallel Native Optimization

Status: reference closure conversion and parallel native build implemented;
stage-2 runtime linkage and parallel frontend analysis pending
Date: 2026-07-17

Local Sollang functions previously remained inline in the reference LLVM emitter.
The compiler-sized `emitCore` therefore became one roughly 830-thousand-line
LLVM function. Splitting the module into 24 partitions did not split that
function: 23 small partitions completed quickly while one Clang process used a
single core for more than 904 seconds and exceeded 9 GB working set.

Semantic analysis now preserves each function's lexical capture types.
Code generation closure-converts ordinary local functions into uniquely named
LLVM functions, passes captures as hidden aggregate parameters, restores the
complete lexical sibling scope, and gives every lifted function its own stack
placement plan. Read-only slices and dictionary views are valid capture ABI
values. User block functions remain inline because their caller block is a
distinct callback-like contract.

After lifting, all 24 `-O1` partitions of the reusable native stage-1 compiler
completed during a cold test bootstrap in seconds instead of hitting the
904-second timeout. The fast regression passed 392/392 with eight workers in
44.46 seconds. The optimized stage-1 emitted the complete 4,651,009-byte
stage-2 LLVM module in 293.32 seconds; `llvm-as` produced a valid 1,345,496-byte
bitcode module, and its 24 native object partitions compiled in 1.17 seconds.

Runtime/product linkage was the remaining stage-2 gate at this decision point.
It was subsequently completed and is tracked by D168. Frontend semantic and
typed-IR work was still sequential here; its 293-second emission time was
separate from the parallel native backend.

## D168 - Preserve Fixed-Point Output While Removing Self-host Emission I/O

Status: capture/index/I/O baseline implemented; superseded by D169 for compute-pool lowering
Date: 2026-07-18

Compiler LLVM output is redirected and must be treated as bulk data rather
than terminal text. The Windows runtime now buffers one MiB, flushes on process
exit, and retains line flushing only for an actual console. The self-host text
emitter produces the same contract; Linux and WebAssembly expose a compatible
no-op flush because their current writers are already direct.

Function capture discovery is computed once per function. Canonical symbol
indexes replace repeated full-IR target lookup, and function boundaries are
cached. These changes preserve deterministic order: complete stage 2 and stage
3 outputs are byte-identical at 6,852,053 bytes, and `llvm-as`, link, execution,
and the 75-case affected LLVM suite pass.

The measured stage-2 verification time moved from about 376 seconds to 260.9
seconds. A generated stage-2 compiler still takes 360.7 seconds and averages
0.99 cores because the self-host emitter lowers its three `parallel` regions as
serial loops. Consequently the next optimization must implement the runtime
parallel ABI in the self-host emitter; adding more test-runner workers or LLVM
partitions cannot accelerate that internal serial path.

## D169 - Lower Only Worker-safe Self-host Parallel Callbacks

Status: implemented and fixed-point verified
Date: 2026-07-18

The self-host LLVM emitter now emits the Windows `%sollang.compute_group`
ABI, persistent workers, stable callback symbols derived from typed-IR indexes,
and ordered output slots. A callback is eligible only when it has at least one
capture and every capture uses the owned/borrowed pointer ABI. Other `parallel`
expressions retain the deterministic serial lowering.

This restriction is evidence-driven. Running large no-capture `SourceText`
analysis on a worker reproducibly caused an access violation, even with one
worker and a 16 MiB thread stack. Keeping that phase serial while enabling the
captured function-lowering regions completed the 28-source compiler in 255.5
seconds instead of 360.7 seconds. The first 15 seconds reached 14.90 effective
cores; the whole run averaged 1.62 cores. A captured-array callback ran through
100 pool generations, the affected LLVM suite passed 75/75, `llvm-as` accepted
the complete compiler, and stage 2 and stage 3 had the same SHA-256
`3A29C41670DAF137B42594A5374A68053D2599EBBFAF287043E1E99B316A7020`.

The next performance decision must address ordered parallel function-body
emission. The compute pool now accelerates the frontend burst, but the long
LLVM text phase still converges to one active core.

## D170 - Bound Self-host Emission Work to the Current Function

Status: implemented and fixed-point verified
Date: 2026-07-18

The apparent ordered-output bottleneck contained a more important algorithmic
defect. Each emitted function allocated Boolean scheduling arrays with one slot
for every node in the complete compiler IR. Nested control regions repeated
the same allocation and scanned the complete IR when ordering local nodes,
finding preceding roots, resolving aggregate wrappers, and locating called
functions. Compiler growth therefore multiplied function and region counts by
the total program size.

Emitter scheduling arrays now use function-relative indexes. Region work is
bounded by its owning function, aggregate and mutable-binding searches stop at
that function boundary, and canonical module-symbol indexes replace repeated
full-IR function lookup. This changes neither LLVM order nor public language
semantics.

On the complete 28-source self-host compiler, LLVM generation fell from the
D169 baseline of 255.5 seconds to 40.27 seconds for stage 2 and 42.72 seconds
for stage 3, an approximately 83% reduction from that baseline. Both outputs
are exactly 6,942,593 bytes and share SHA-256
`3A82A8584A13BBA12A64DBA719A20CE52F2A3787745229C4DFFD8E3B323E5EF3`.
`llvm-as`, the 72/72 emitter-affected LLVM suite, and the complete six-step
stage-2 differential verifier pass. Function-body output is still serialized
to preserve canonical order; a per-function sink can improve the remaining
roughly 40-second path, but it is no longer masking quadratic global scans.

## D171 - Classify No-capture Parallel Callbacks by Value ABI

Status: scalar ABI and entry lowering implemented; owned-analysis and nested-region paths remain
Date: 2026-07-18

The D169 restriction treated every callback without captures as unsafe because
the compiler-sized `SourceText -> SourceAnalysis` callback crashed on a worker.
A focused scalar test showed that capture count itself is not the unsafe
property. Numeric and `Bool` elements are complete values with no borrowed or
affine storage, so no-capture scalar-input/scalar-output callbacks now use the
Windows compute pool and pass a null capture environment. A 100-generation
execution test verifies pool reuse and result storage.

The first direct-entry test also exposed a separate self-host parity gap. A
role call and its `=>` result binding share one AST node, but entry typed IR did
not perform the explicit edge repair already used for ordinary functions.
Entry lowering now repairs that edge and emits top-level `parallel` through
either the compute pool or the deterministic serial fallback. It excludes the
role's child callback call from ordinary call emission.

The emitter-affected LLVM suite passes 74/74 and the complete stage-2
differential verifier passes 6/6. Stage 2 and stage 3 are both 7,011,834 bytes
with SHA-256
`66CFB3F0551D85CDFBDEE7851020F643792FB1AD5CF4FBC8DAE7C2045B19C250`;
stage 3 completed in 44.12 seconds and assembles with `llvm-as`.

This does not declare all no-capture work safe. `SourceText` inputs returning
owned analysis products remain serial until their worker failure is diagnosed.
Likewise, `parallel` inside an `if` or `while` was owned by `emitRegion`, whose
parallel lowering was not implemented in this slice; D172 closes that parity
gap.

## D172 - Reuse Compute Generations Only After Every Worker Departs

Status: implemented and fixed-point verified
Date: 2026-07-18

`emitRegion` now gives `parallel` inside nested `if` and `while` regions the
same compute-pool and deterministic serial lowering as top-level entry and
ordinary function bodies. Generic region-call emission excludes the role and
its child callback call so the callback is invoked exactly once per element.

The first 100-generation region test exposed a reusable-barrier race rather
than an emitter error. The completion event is manual-reset. The last worker
could signal it and the submitting thread could reset it before every waiting
worker had returned from `WaitForSingleObject`; a stranded worker then consumed
a permit from the next generation and eventually deadlocked the pool. Each
non-last worker now increments a departure counter after its event wait, and
the submitter waits for `workerCount - 1` departures before clearing the group
and beginning another generation.

The 100-generation nested-region execution passes in 0.14 seconds, and the
emitter-affected LLVM suite passes 75/75. Complete stage 2 and stage 3 outputs
are byte-identical at 7,069,022 bytes with SHA-256
`B29A0DD2B778BEFF3DF96274C81314DDFE82199F7E5FCD8A462D1135CF4E32F1`.
Stage 3 completed in 46.28 seconds and assembles with `llvm-as`; the complete
six-step stage-2 differential verifier also passes.

## D173 - Capture Parallel Console Output in Owned Memory Sinks

Status: implemented and fixed-point verified
Date: 2026-07-18

Parallel callbacks may perform deterministic Console output without sharing a
mutable stdout buffer. Each input index owns one `%sollang.output_sink`
record containing `data`, `length`, and `capacity`. Appends grow geometrically,
copy initialized bytes, and free the replaced allocation. After all workers
depart the reusable generation barrier, the submitting thread writes every
sink in input order and frees both each payload and the record array. Empty
parallel inputs also release the zero-length sink allocation.

The reference Windows backend passes the sink as a tagged Console capability
pointer. That keeps the sink in the existing effect context across nested calls
without CRT thread-local storage. The self-host-generated runtime uses Win32
`TlsAlloc`, `TlsSetValue`, and `TlsGetValue`, because its compact function ABI
does not yet carry the reference backend's five runtime capability pointers.
Both routes have the same ownership and ordered-merge contract. Programs with
no `parallel` expression retain the previous compact text runtime.

`emitCore` now emits ordinary function bodies through this sink abstraction.
Functions preceding and following the entry root are separate parallel
batches, while `main` remains at its original canonical position. This retains
the exact serial root order even when a module declares its entry before later
functions.

Example 375 proves ordered output through the reference compiler and example
376 proves the same generated by the self-host LLVM emitter. The affected LLVM
suite passes 76/76. Complete stage 2 and stage 3 outputs are byte-identical at
7,119,957 bytes with SHA-256
`1878B6CB90351D4037E1E6319EBDFD17797577F1157DC51F1E0797F6C7890347`.
Stage 3 completes in 26.13 seconds with 213.52 CPU-seconds, averaging 8.17
effective cores. Compared with D172's 46.28 seconds this is a 43.5% reduction;
compared with the original 360.7-second serial baseline it is 92.8% lower.

## D174 - Transfer Owned Source Analysis Results Across Workers

Status: implemented and fixed-point verified
Date: 2026-07-18

No-capture parallel callbacks may accept borrowed `SourceText` elements when
their result is an owned, recursively worker-transferable value graph. The
self-host emitter accepts scalar built-ins, nominal structs, and dynamic or
fixed arrays whose nested fields satisfy the same rule. Text, `SourceText`,
dictionaries, boxes, and unresolved types stay on the serial path. Workers
borrow mapped source bytes for the joined generation and transfer each owned
result into its unique output slot; the submitting thread receives the result
array after every worker has left the generation barrier.

Enabling the real 28-source boundary exposed an existing eager-evaluation bug
in `type_terms`: readiness expressions indexed a child array even when the
child id was `-1`. Explicit guarded branches now ensure that no array access is
formed until the index is valid. The failure was captured in the worker handling
`selfhost/semantic/analysis.slg`, and the corrected stage-2 compiler no longer
faults under heterogeneous parallel analysis.

Example 377 exercises the SourceText/result ABI for 100 generations. The full
stage-2 differential verifier passes, and five consecutive stage-3 generations
produce the same 7,142,042-byte LLVM with SHA-256
`A71D4595F9854C1E7746F5FE1ECFDF2D82D08DB971F6C3837714B2CB07CA11AD`.
Those runs took 33.90-37.75 seconds. An instrumented run recorded 34.81 seconds
wall time, 377.77 CPU-seconds (10.85 effective cores), and an 88.7 MiB peak
working set.

## D175 - Bound and Report Self-host Compiler Workers

Status: implemented and fixed-point verified
Date: 2026-07-18

The native compiler accepts `--jobs N` immediately after its target and calls
the typed `limitParallelWorkers(Int) -> Int` runtime intrinsic before compiler
work begins. The default remains the host's available logical processor count;
explicit values are positive and bounded to 64 native workers. The intrinsic
returns the number of workers actually created, and the driver reports that
effective value as an LLVM comment so stdout remains a valid module. Invalid or
missing values emit one diagnostic comment and do not compile input files.

This surface follows established build-tool convention: Cargo exposes
`--jobs N` and otherwise uses logical CPUs, while Zig exposes `-j<N>` and uses
all cores by default. Sollang uses Cargo's readable long option because the
self-host driver is also a user-facing compiler command.

- [Cargo build options](https://doc.rust-lang.org/cargo/commands/cargo-build.html)
- [Zig build system](https://ziglang.org/learn/build-system/)

The same intrinsic is resolved and emitted by both the C# reference compiler
and the Sollang compiler. Compute-runtime discovery also covers an isolated limiter
call, including its output-sink and allocator dependencies. Function-local
typed IR already lowers `FunctionLowerRequest` values through indexed
`parallel`; the complete generated LLVM now has an automated assertion for its
worker callback into `lowerFunction`.

Example 378 assembles, links, and executes the limiter together with native
parallel work. The six-step differential verifier passes with `--jobs 2` for
both stage-1 and stage-2. A complete 28-source stage-3 generation is byte-equal
to stage-2 at 7,184,456 bytes with SHA-256
`DDC1D4C7DD1B363972A64EE546B12007DA5630550C0EC3A99A4AA3CB08E98740`,
and `llvm-as` accepts the result. Parallel-compilation progress is now 22/28
(78.6%); the canonical roadmap remains 48.5/60 (80.8%).

## D176 - Freeze Global Semantic Facts Before Parallel Lowering

Status: implemented and fixed-point verified
Date: 2026-07-18

Whole-compilation package, type, nominal/composite, module, import, qualified
resolution, and call facts cross one named ownership boundary:
`SemanticSnapshot`. Construction temporaries remain inside `semantic.context`;
`freeze` consumes the completed aggregate, and all semantic diagnostics,
effect/ownership analysis, typed-IR lowering, and LLVM preparation borrow the
snapshot. The old construction-oriented `CompilationContext` type no longer
exists, so worker-facing APIs cannot accidentally advertise a partially built
context. Flat owned arrays move into the snapshot once; the barrier introduces
no deep copy or reference-counted graph.

This follows two compatible primary designs. Swift treats immutable data as
isolated and allows read-only `Sendable` state to cross concurrency domains.
rustc models compiler queries as pure functions and loads the prior dependency
graph as immutable data before current-session work. Sollang adopts the
shared invariant without importing actors or a query runtime: complete global
facts first, immutable snapshot next, disjoint indexed worker products last.

- [Swift concurrency and Sendable](https://docs.swift.org/swift-book/LanguageGuide/Concurrency.html)
- [rustc incremental query evaluation](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation-in-detail.html)

Example 379 executes a two-module package through the snapshot and proves its
package/module/import/resolved-import views. The fixed-point verifier rejects a
return to public `CompilationContext`, then performs the existing callback,
`--jobs`, assembly, link, execution, and C#/Sollang differential checks. A complete
28-source stage-3 generation is byte-equal to stage-2 at 7,185,332 bytes with
SHA-256
`2E2AEFB4830A45A0C7E890AD22D7D55C0EF181C9CB0A4AEB87DCB97F9CB2776A`,
and `llvm-as` accepts it. Self-host integration is now 6/6 and the parallel
subproject is 23/28 (82.1%); the canonical roadmap remains 48.5/60 (80.8%).

## D177 - Reject Unsafe Parallel Callback Captures

Status: implemented and fixed-point verified
Date: 2026-07-18

Every `parallel` callback now has a compile-time capture boundary. A mutable
binding is rejected even when the callback only reads it, because the binding
still permits another lexical access to the same storage. Immutable values are
accepted only when their type is structurally sendable. Numeric values, Bool,
Text, arrays, dictionaries, boxes, and nominal values recursively composed from
those types are valid read-only captures. Arena, Arguments, mapped/source views,
tasks, slices, unresolved types, and nominal values containing them are not.

The reference compiler walks the inline block and follows called local
functions transitively, preventing a mutable capture from hiding behind an
outlined helper. The self-host ownership pass uses the shared
`SemanticSnapshot` and typed IR to report direct mutable and non-sendable
captures as diagnostic codes 18 and 19. Its emitter also snapshots the mutable
function-end and capture indexes after construction; parallel LLVM-body workers
therefore capture immutable arrays rather than construction-time `!` bindings.

This follows Swift's rule that concurrently executed `@Sendable` closures may
not capture mutable variables, together with Rust's structural distinction
between values that can cross a thread boundary and shared references whose
referent must be safe for concurrent access.

- [Swift sendable closure captures](https://docs.swift.org/compiler/documentation/diagnostics/sendable-closure-captures/)
- [Rust `Send`](https://doc.rust-lang.org/std/marker/trait.Send.html)
- [Rust `Sync`](https://doc.rust-lang.org/std/marker/trait.Sync.html)

Three reference diagnostics cover direct mutable, transitive mutable, and
non-sendable captures. Example 331 retains immutable captured-state execution,
and example 380 executes the self-host diagnostic boundary for mutable,
non-sendable, and immutable cases. The Release build has zero warnings/errors,
the complete Windows suite passes 505/505, and the six-step stage-2 differential
verifier passes. Stage 2 and stage 3 are byte-identical at 7,195,817 bytes with
SHA-256 `B57FB15B373CB0348EB16EAA7B1727D56D3B382F5FA5E01C1FF0280F3BCA7410`;
`llvm-as` accepts stage 3. The measured stage-3 run took 38.50 seconds wall,
400.38 CPU-seconds (10.40 effective cores), and 77.5 MiB peak memory.

Typed-role coverage is now 5/5 and the parallel subproject is 24/28 (85.7%).
The canonical self-host roadmap remains 48.5/60 equivalent gates (80.8%).

## D178 - Let the Submitting Parent Help Before Structured Join

Status: implemented and fixed-point verified
Date: 2026-07-18

Submitting a compute group no longer releases the native workers and then
immediately blocks. The parent claims unassigned indices from the same atomic
queue and executes callbacks until no claim remains. It then waits for native
worker completion and barrier departure before ordered sink flush, group
destruction, and return. Native worker count remains an explicit pool-size
metric, while peak active callbacks includes the helping parent.

The C# reference backend and self-host LLVM runtime implement the same contract.
The self-host parent binds each claimed index's memory output sink in TLS before
calling the callback and clears it afterward, so parent and worker output still
merge deterministically by source index. The parent is not included in the
native-worker completion counter; this preserves the existing worker barrier
and makes the join boundary unambiguous. Reentrant nested compute groups remain
outside this decision because the runtime still publishes one current group.

This design follows the established helping-wait principle: Java
`ForkJoinPool.awaitQuiescence` may assist task execution while waiting, and a
oneTBB thread waiting for a task group may participate in scheduled work.

- [Java `ForkJoinPool.awaitQuiescence`](https://docs.oracle.com/en/java/javase/25/docs/api/java.base/java/util/concurrent/ForkJoinPool.html#awaitQuiescence(long,java.util.concurrent.TimeUnit))
- [oneTBB `task_group`](https://uxlfoundation.github.io/oneTBB/main/specification/source/task_scheduler/task_group/task_group_cls.html)

Example 381 limits the pool to one native worker and observes two active
callbacks, directly proving parent participation. The Release build has zero
warnings/errors, the complete Windows suite passes 506/506, and the six-step
stage-2 differential verifier passes. Stage 2 and stage 3 are byte-identical at
7,198,336 bytes with SHA-256
`CBCED4918D9AF37C71AF792D99016A27C2F4CC9D4407CD123CD866BF32DB555F`;
`llvm-as` accepts stage 3. The measured stage-3 run took 34.56 seconds wall and
376.91 CPU-seconds (10.91 effective cores).

Native compute-task-group coverage is now 5/7 and the parallel subproject is
25/28 (89.3%). The canonical self-host roadmap remains 48.5/60 equivalent gates
(80.8%).

## D179 - Use a Bounded Reusable Linux Compute Pool

Status: implemented and fixed-point verified
Date: 2026-07-18

The Linux x86-64 backend now implements the same bounded reusable compute-pool
contract as Windows. Native workers are pthreads; an `eventfd` semaphore wakes
one worker per token, a second `eventfd` reports group completion, and an
x86-64 futex generation barrier prevents a fast worker from consuming more than
one token in a generation. The submitting parent drains the same atomic index
queue before waiting. Shutdown wakes and joins every worker, deletes TLS, and
closes both event descriptors.

The self-host Linux emitter implements the same pool and pthread TLS output
binding. Example 383 reuses the pool for 100 generations. The focused Linux
verifier passes 5/5 in WSL2 Ubuntu, covering ordered output sinks, one-worker
parent help, generation reuse, and execution of Linux LLVM emitted by the
native self-host compiler.

- [POSIX `pthread_create`](https://pubs.opengroup.org/onlinepubs/000095399/functions/pthread_create.html)
- [POSIX `pthread_join`](https://pubs.opengroup.org/onlinepubs/009695399/functions/pthread_join.html)
- [Linux `eventfd`](https://man7.org/linux/man-pages/man2/eventfd.2.html)

## D180 - Share One Memory Output Sink Contract Across Platforms

Status: implemented and fixed-point verified
Date: 2026-07-18

Memory output sinks now have one runtime abstraction in both the C# reference
emitter and the self-host emitter. The common layer owns geometric growth,
append, canonical array flush, and disposal. Windows and Linux retain only a
small final-writer adapter and their platform-specific TLS binding. Compute
pools no longer inspect the sink's `{data, length, capacity}` fields or duplicate
the flush/free loop.

This boundary makes sink ownership explicit: `array_flush` consumes every
payload and the sink array, while `array_dispose` handles an unflushed or empty
array. A writer callback receives opaque platform context, so future sinks can
target files, diagnostics, or in-memory compiler products without copying the
buffer-management algorithm into each runtime.

The self-host common layer also exposes `array_flush_prefix`. It writes only
indices below the canonical prefix, but still destroys every per-index buffer
and finally consumes the sink array. Full flush is a thin call with
`prefix == count`. This keeps fallible parallel execution from duplicating
buffer ownership logic when it commits output preceding the earliest failure.

The Release build has zero warnings/errors, the Windows suite passes 508/508,
the focused Linux verifier passes 5/5, and stage-2 differential verification
passes 6/6. Stage 2 and stage 3 are byte-identical at 7,217,656 bytes with
SHA-256 `1C026529C832C88AA54ACCC55B05FE0A7358BBFA4F2A31F6F6F1F1ECEF0FD0DD`;
`llvm-as` accepts stage 3. The measured stage-3 run took 36.86 seconds wall and
407.42 CPU-seconds (11.05 effective cores), with a 100.7 MiB peak working set.

Native compute-task-group coverage is now 6/7 and the parallel subproject is
26/28 (92.9%). Cancellation/partial-result destruction and full Windows/Linux
suite parity remain open. The canonical roadmap remains 48.5/60 equivalent
gates (80.8%).

## D181 - Admit Three Named Generic Parameters for Fallible Roles

Status: implemented and focused verified
Date: 2026-07-18

Fallible result-producing roles need three independently inferred types: the
source element `T`, successful result `R`, and error `E`. Function declarations
therefore accept a third named generic parameter. The reference compiler binds
and specializes that parameter through nested `Option`, `Result`, growable
array, and dictionary type templates; specialization identities include all
three type IDs. The self-host parser and symbol collector preserve the same
three declarations.

This is a bounded prerequisite for `tryParallel<T, R, E>`, not a claim that Sollang
now supports arbitrary generic arity. Examples 384 and 385 prove self-host
symbol collection and reference declaration binding. The Release build has
zero warnings/errors and both focused examples pass with deterministic grammar
generation. Cancellation, partial-result destruction, and runtime lowering
remain open, so parallel progress stays 26/28 (92.9%) and the canonical roadmap
stays 48.5/60 (80.8%).

## D182 - Preserve Typed Failure Through `tryParallel`

Status: reference runtime implemented; self-host scalar runtime executable
Date: 2026-07-18

`tryParallel<T, R, E>` is the fallible counterpart of `parallel`. Its callback
returns `Result<R, E>` and the role returns `Result<[R; ~], E>`; failure is not
translated into an exception or a hidden runtime flag. The reference semantic
compiler infers all three types from the source and callback, rejects callbacks
that do not return `Result`, and requires callers to bind owned result arrays.

The native compute group keeps an atomic earliest-failure source index. Workers
stop claiming indices at or beyond that limit, while callbacks that already
started are structurally joined. The lowest failing source index wins even when
a later failure finishes first. After the join, successful payloads are moved
into canonical input order or the selected error is moved out; every other
initialized success/error payload is destroyed exactly once. Per-index memory
output sinks flush only the successful prefix before the selected failure and
dispose all remaining buffers.

Examples 386-388 cover empty and successful arrays, deterministic competing
errors with prefix-only output, and owned partial results. The two diagnostics
cover a non-`Result` callback and an ignored owned return. The self-host semantic
and typed-IR passes recognize opcode `-209`, infer `Result<[R; ~], E>`, and apply
the same capture-safety boundary; examples 380 and 389 prove that scope.

The self-host LLVM emitter now executes scalar `tryParallel` through the same
13-field compute-group ABI in entry, ordinary-function, and nested-region
positions. Nested owned callback payloads still need an executable exactly-once
gate. Because that proof and Linux full-suite parity are not yet complete,
parallel progress remains 26/28 (92.9%) and the canonical roadmap remains
48.5/60 equivalent gates (80.8%).

The Release build has zero warnings/errors and the complete Windows regression
passes 516/516. All three reference runtime examples also pass on Linux x86-64.
Stage-2 differential verification passes 6/6. Stage 2 and stage 3 are
byte-identical at 7,247,585 bytes with SHA-256
`C1D43534CFC873CC3BB18BA9DDE3CAF1F515FB8D9FEBA57ABDFE063F648F0723`;
`llvm-as` accepts stage 3, whose emission took 35.19 seconds wall time. The
stage-2 verifier now identifies the typed-IR worker by ABI shape instead of a
fragile function symbol ordinal.

## D183 - Give Self-Host `Option` and `Result` a Stable LLVM Value ABI

Status: constructor, contextual matching, and owned array payload drop executable
Date: 2026-07-19

The self-host compiler now preserves qualified generic enum constructors such
as `Result<Int, Text>.Ok(value)` through grammar, AST, semantic type IDs, and
typed IR. `Option<T>` and `Result<T, E>` use the same value representation as
the reference compiler: an `i32` tag followed by an eight-byte-aligned payload
area sized for the largest variant. `Task<T>` remains a distinct two-pointer
value and is not accidentally classified as an enum merely because all three
types share the nominal-application semantic kind.

Function and return nodes now retain their canonical recursive result type ID.
The ordinary input parameter is selected before a block-role parameter, which
prevents a function's `Result` return annotation from replacing an `Int` input
in the emitted ABI. Constructor lowering supports function bodies, structured
regions, and `main`; literal, local, parameter, text, and aggregate payloads
are stored into the common tagged layout.

Example 390 constructs both `Ok(Int)` and `Err(Text)`, calls a Result-returning
function, assembles the generated LLVM with `llvm-as`, links it, and executes
the native program. Contextual `when` arms now compare the runtime tag, execute
only the selected region, and bind both `Ok(item)` integer payloads and
`Err(error)` text payloads from the shared storage area. The focused
grammar, enum-parser, ordered-output, tryParallel-IR, and executable Result
regression set passes 5/5. The initial constructor/match checkpoint preceded
owned-payload destruction and self-host opcode `-209` runtime lowering.

The follow-up owned payload gate normalizes nested generic type syntax before
typed-IR scheduling, so `Result<[Int; ~], Text>.Ok(...)` no longer leaves a
one-operand index node around its constructor. Entry points now run the same
final owner cleanup as ordinary functions. Generic enum drop glue reads the
runtime tag and destroys only the active `Option`/`Result` payload; dynamic
arrays, dictionaries, boxes, `SourceText`, and owned nominal structs have
direct payload paths. Example 391 assembles, links, and executes both an owned
`Ok` and an inactive `Err`, and its LLVM contains one array-buffer `free` only
on the `Ok` branch. Nested owned boxes/fixed arrays still require the remaining
recursive cleanup proof, so progress does not move yet.

## D184 - Execute Self-Host `tryParallel` Through the Native Pool

Status: scalar entry/function/region paths executable; owned nested proof pending
Date: 2026-07-19

Self-host opcode `-209` now lowers to the native Windows/Linux compute-pool ABI.
The compute group carries atomic `failure_limit` and per-index initialization
bytes. Both workers and the submitting thread stop claiming indices at that
limit, and the common output sink commits only the canonical prefix. Callbacks
store complete `Result<R, E>` values before publishing initialization and use
an atomic minimum to select the earliest failing source index.

Collection moves successful payloads into a growable output array in input
order or moves the selected error into the outer Result. The lowering is shared
by top-level entry expressions, ordinary function bodies, and nested control
regions. Examples 392-394 assemble, link, and execute all three positions;
example 392 covers both successful array reconstruction and failure selection.

This work also removed two independent self-host traps exposed by the new
test. Symbol collection now sizes its AST-to-symbol map before following valid
forward-parent edges, and fluent runtime calls consume the final transformed
value (for example, a `when` result) instead of the original subject.

The generated cleanup loop consults each initialization byte and has
tag-directed owned-payload destruction hooks, but nested owned callback Results
still need an executable leak/double-free proof. Therefore cancellation and
partial-result destruction remain unchecked, parallel progress stays 26/28
(92.9%), and the canonical roadmap stays 48.5/60 (80.8%).

## D185 - Prove Deterministic Failure and Owned Partial Cleanup

Status: implemented and executable
Date: 2026-07-19

Self-host `tryParallel` now has executable gates for the two failure properties
left open by D184. Example 395 runs callbacks with failures at source indices 2
and 5. The generated executable consistently selects `three`, commits only the
outputs from indices 0 and 1, and produced the same result in 50 repeated runs.

Example 396 returns an owned dynamic array from every successful callback before
the first error. Its error path scans initialized callback Results, skips the
selected error, dispatches by the runtime enum tag, and frees each active owned
`Ok` payload. A missing function-body canonicalization for the one-operand
constructor wrapper was fixed so named owned payloads no longer reach LLVM
lowering as false index operations.

LeakSanitizer initially exposed a separate 32-byte leak: the directly supplied
eight-element `Int` input literal survived the joined operation. Entry,
ordinary-function, and nested-region lowering now release direct temporary array
inputs immediately after structured join. The durable Linux verifier compiles
example 396 with AddressSanitizer and leak detection and requires clean exit and
the exact expected error text.

Cancellation and partial-result destruction are therefore proven exactly once.
Parallel progress advances to 27/28 (96.4%). The remaining check is full
Windows/Linux suite parity, and the canonical roadmap remains 48.5/60 (80.8%)
because this is a feature-local checklist checkpoint.

## D186 - Stabilize the Complete Windows Parallel Regression Gate

Status: Windows full suite proven; Linux full suite pending
Date: 2026-07-19

The first complete post-D185 Windows run exposed two implementation defects
among otherwise stale generated-LLVM fixtures. The self-host emitter's
`aggregateValueIndex` helper treated every kind-9 node as a one-operand wrapper.
A real slice node therefore resolved through its final explicit `UIntSize`
operand, producing an invalid aggregate value at a later call. Wrapper
unwrapping is now restricted to opcode `-1`, and two-operand calls resolve both
candidates before selecting the later canonical IR value. Examples 358 and 360
now assemble and execute with the intended slice and nested-region results.

Example 381 also showed that parent-assisted waiting was observable but not
deterministic: native workers could claim the entire short queue before the
submitting thread entered its first claim. Windows and Linux runtimes now
reserve the first source index atomically before releasing worker tokens. The
parent executes that reserved callback and then joins the shared atomic queue;
30 repeated Windows executions all report `parent-helped=true`, while the Linux
focused verifier still passes its parent-help case.

After validating generated LLVM with `llvm-as`, the full fixture set was
refreshed and a read-only rerun passed all 523 Windows examples. The Release
solution build reports zero warnings and zero errors, and the focused Linux
parallel verifier passes all six steps including AddressSanitizer. This proves
the Windows half of the final parallel checklist item. It does not claim Linux
full-suite parity, so progress remains 27/28 (96.4%) and the canonical roadmap
remains 48.5/60 (80.8%).

## D187 - Make Linux a Complete 523-Case Test Target

Status: implemented and verified
Date: 2026-07-19

The example runner now accepts `--target windows-x64|linux-x64`. Linux ordinary
examples compile to native ELF binaries and execute in the selected WSL
distribution with stdin, arguments, environment, and repository working
directory preserved. Diagnostics compile against the Linux backend, while
Wasm-specific diagnostics retain their explicit Wasm target. Target-specific
LLVM assertion files override Windows API assertions without weakening shared
target-neutral checks.

Reusable self-host cases keep one cached Windows-hosted Sollang compiler driver but
request Linux emission. Their raw LLVM is target-specific and therefore is not
compared to the Windows text snapshot; every Linux module is assembled, and
all cases with an observable execution expectation are compiled to Linux
objects, linked by WSL `gcc -pthread`, and executed. `scripts/verify-linux-full.ps1`
provides the durable two-step Release-build and 523-case gate with four bounded
workers.

The first reference run found missing Linux `sollang_compute_workers` support
and five Windows-only LLVM assertions; after correction, 281/281 reference and
diagnostic cases passed. The first complete run then found an invalid self-host
control-result module that the old raw snapshot had not assembled. A binding
that directly consumed an `if` result and that `if` incorrectly waited on each
other because their source offsets formed a false scheduling barrier. Function
and entry schedulers now exclude the direct result binding from that barrier,
and example 311 has mandatory assemble/link/execute gates.

The final read-only Linux run passes 523/523 in 112.2 seconds. The corresponding
Windows inventory remains 523 cases, and the Release solution builds with zero
warnings and zero errors. Parallel compilation therefore reaches 28/28 checks
(100%). This feature-local completion does not promote the canonical self-host
roadmap, which remains 48.5/60 equivalent gates (80.8%).

## D188 - Specialize Generic Containers From Canonical Component Types

Status: contextual array/dictionary layout and lookup implemented
Date: 2026-07-19

Sollang uses a statically specialized value-witness model for generic
containers. Once a collection is concrete, its canonical component type IDs
are the authority for size, alignment, LLVM representation, and recursive
ownership traits. The compiler does not add runtime metadata to every value.
This matches Sollang's existing monomorphization model while preserving the useful
part of Swift value witnesses and the sound generic-drop constraints described
by Rust and Mojo.

The self-host typed IR now applies a function's declared collection return
context to its literal in the final canonicalization pass. Dynamic arrays
therefore allocate and address by
their canonical element stride. Dictionaries independently select canonical
key and value size, alignment, LLVM type, store, comparison, and load rules.
Recursive ownership classification remains carried on typed-IR values; the
container component lowering in this decision consumes canonical layout facts.

Example 397 exposed the previous ABI mismatch: a function declared to return
`{UInt16: Int64}` stored both sides as default `Int32`, while its consumer loaded
`i16` and `i64`, producing `21474836489` instead of `9`. It now allocates 4 key
bytes and 16 value bytes for two entries and executes with result `9`. Example
398 proves a `[UInt16; ~]` producer and consumer use a 2-byte element stride.
Both examples assemble, link, and execute on Windows and Linux.

Recursive owned-element destruction, indexed move extraction, and fixed-array
generic function contracts remain. The focused generic-container migration is
5/8 checks (62.5%), and the canonical score remains 48.5/60 (80.8%).

- [Rust drop check](https://doc.rust-lang.org/nightly/nomicon/dropck.html)
- [Swift generics implementation model](https://download.swift.org/docs/assets/generics.pdf)
- [Mojo generic traits and containers](https://mojolang.org/docs/manual/traits/)

## D189 - Lower Shell-Free Process Intrinsics in the Self-Host Backend

Status: Windows stage-2 implemented and verified; Linux full compiler runtime pending
Date: 2026-07-19

The complete self-host compiler imports `stdlib/sys/process.slg`, so its process
intrinsics are public module API rather than declarations visible only inside
that source. Calls are canonicalized after module IR merging by the
`sys.process` module and resolved symbol identity. Stable internal opcodes
distinguish `run`, `runToFile`, and the contextual `arguments` conversion; the
backend therefore does not depend on a surface token that an imported call does
not own.

Both process operations lower through `%sollang.process_result { i32, i32 }`.
The first field is the exit code and the second is a portable spawn, wait, or
signal error kind. The emitter converts that pair into the canonical
`Result<Int, Text>` enum layout. Windows builds a null-terminated argv and uses
`_spawnvp`; `runToFile` temporarily redirects stdout through `_open`, `_dup`,
and `_dup2`. Linux emits the corresponding `fork`, `execvp`, `waitpid`, `open`,
and `dup2` implementation without introducing a shell.

The native Sollang driver normalizes the process result before its control-flow
branch, then invokes its own LLVM emission through `runToFile` and Clang through
`run`. The durable stage-2 verifier now checks that actual `build-windows` path,
requires both output artifacts, and executes the resulting program. The
7,997,972-byte Windows stage-2 compiler passes all six differential phases;
single-file, grouped-Boolean, and imported multi-file LLVM are identical across
stage 1 and stage 2, and the self-built executable prints `stage2-single-ok`.
This restores the native stage-2 checklist to 8/8 while leaving the canonical
language roadmap at 48.5/60 (80.8%).

The Linux process ABI itself is emitted, but a complete Linux-hosted self-host
compiler currently reaches an independent missing `sollang_runtime_map_text`
SourceText runtime symbol. Linux stage-2 parity must not be claimed until that
runtime gate is implemented and verified.

## D190 - Make SourceText Ownership Native on Linux Stage 2

Status: implemented and cross-target verified
Date: 2026-07-19

The self-host Linux emitter now provides the same owned SourceText contract as
Windows without copying file contents into an Sollang heap array. It copies only the
path into a temporary null-terminated buffer, opens the file read-only, obtains
its length through `lseek`, maps the file with `mmap`, and stores the mapping
base and length in `%sollang.source_text`. Empty files produce the zero value. The
owned drop path calls `munmap` exactly once when the base is non-null; every
failure after open closes the descriptor before trapping.

The runtime declaration boundary accounts for the compute and process
runtimes, which may already declare `close` or `open`. SourceText therefore
adds only the declarations that are not already owned by those feature
runtimes, avoiding the duplicate declarations that originally obscured the
next missing symbol.

`scripts/verify-selfhost-stage2-linux.ps1` is the durable five-phase gate. It
bootstraps or reuses stage 1, emits the complete 29-source Linux compiler,
assembles it, cross-compiles its object, links it through WSL, then runs that
Linux stage 2 on single and imported multi-file inputs. Stage-1 and stage-2
normalized LLVM hashes are respectively
`A522FB076919297BDD7A78FAEDF099DA65217F23BE7079A922DFFF3AD4B1FCE5`
and `017126FCF6494AFC28175F5D8C555E775D1157A56C022C0EFD9C59270FFDA46E`.
Both products assemble, link, and execute with the expected output.

The complete Linux stage-2 module is 8,002,648 bytes and its verifier passes
5/5. The corresponding Windows module is 8,002,786 bytes and its six-phase
verifier still passes, including the shell-free native build path. The Release
solution has zero warnings and errors, and the full Linux suite passes 526/526.
This closes the cross-target Stage2 runtime gate but does not replace the
remaining canonical ownership, generic-container, package, tooling, and
library work, so the formal roadmap remains 48.5/60 (80.8%).

## D191 - Generate Recursive Drop Witnesses From Canonical Type IDs

Status: implemented and cross-target verified
Date: 2026-07-19

The self-host LLVM emitter derives destruction from canonical semantic type IDs
instead of the shallow spelling carried by an individual IR value. Before
emission it computes the dependency closure of active owned types, then emits a
specialized `sollang_drop_t<ID>` witness for every reachable dynamic array,
fixed array, dictionary, box, nominal struct, `Option`, `Result`, and
`SourceText` type. This keeps values metadata-free while giving each concrete
generic instantiation the exact recursive ownership behavior it requires.

Dynamic-array witnesses destroy owned elements before freeing the backing
allocation. Dictionary witnesses independently destroy owned keys and values,
then free both backing arrays. Aggregate witnesses recurse through their owned
fields or active payload. Ordinary bindings, parameters, early returns, loop
edges, and region cleanup invoke the same witnesses. A nominal struct with a
partial move deliberately retains field-path cleanup; calling the whole-value
witness there would destroy a field that has already transferred ownership.

Example 400 covers a dynamic array of owned structs and a dictionary whose
values are owned dynamic arrays. Its generated LLVM assembles and executes on
Windows and Linux. `scripts/verify-recursive-container-drop.ps1` additionally
runs the Linux product under AddressSanitizer and LeakSanitizer, detecting both
leaks and double frees. The Windows and Linux full suites pass 527/527. The
complete Stage2 modules remain differential-hash identical to Stage1 and pass
their 6/6 and 5/5 scripts at 8,185,153 and 8,185,015 LLVM bytes respectively.

The focused generic-container migration advances from 5/8 to 6/8 checks (75%).
Owned indexed extraction and fixed-array generic function contracts remain, so
the canonical roadmap stays 48.5/60 equivalent gates (80.8%).

## D192 - Stream Reference LLVM Through Composable Memory Output Sinks

Status: implemented and regression verified
Date: 2026-07-19

The C# reference emitter no longer retains one managed string per emitted LLVM
line and then creates another complete concatenated LLVM string before writing
the temporary module. `MemoryOutputSink` coalesces sequential output in
`StringBuilder` buffers and models delayed function-entry `alloca` emission as
an explicit child insertion sink. This removes the marker-string dictionary and
keeps ordering structural instead of recovering it during final concatenation.

`ITextOutputSink` also has a `TextWriter` adapter. The CLI now copies buffered
sections directly into its UTF-8 LLVM file through `ReadOnlySpan<char>` chunks,
so it does not materialize the complete module as an additional managed string.
The string-returning generator remains available for differential tests and
other in-memory consumers; both paths use the same emitter and insertion
semantics.

The Release solution build reports zero warnings and errors. The fast Windows
suite passes 428/428, the owned-container extraction smoke test passes, and a
direct CLI build emits a 45,065-byte LLVM module whose executable prints the
expected `Hello, dimohy. square = 49`. This is a compiler memory-efficiency
checkpoint, not a new language gate, so the canonical roadmap remains
48.5/60 (80.8%).

## D193 - Make Owned Indexed Extraction Explicit and Destructive

Status: implemented and cross-target verified
Date: 2026-07-19

Ordinary `owner![index]` remains a checked read and never silently transfers an
owned element. Destructive extraction is explicit and fluent:

```sollang
values! -> take(index) => value!
dictionary! -> take(key) => value!
```

`take` requires a named mutable owner and exactly one correctly typed index or
key. Missing array positions and dictionary keys trap consistently with checked
indexing. Discarding an owned result is rejected by the existing value-flow
rule, so every extracted value receives a new drop owner.

Dynamic arrays load the selected value, raw-relocate later elements toward the
gap, and decrement the stored length without destroying either the extracted
value or relocated duplicates beyond the new logical end. Dictionaries remove
the stored entry, destroy an owned stored key, transfer the value, and rebuild
or compact the remaining lookup layout without recursively dropping moved
entries. The C# reference emitter and self-host LLVM emitter use the same
ownership contract for scalar and recursively owned components.

Self-host typed IR assigns stable opcode `-213` and derives the result from the
canonical array element or dictionary value type ID. Scheduling treats `take`
as both a dependency-bearing mutation and an ordered effect in entry,
function, and nested-region emission. The implementation also fixed an older
entry ownership bug: consuming calls now record kind-11 entry points as their
move region, preventing main cleanup from dropping a moved value twice.

During verification, a multiline dictionary containing owned array values
exposed that `DictionaryExpression` did not accept newline tokens after `{` or
between entries. The grammar now gives dictionary literals the same newline
layout freedom as arrays and struct literals, and the checked-in self-host
grammar bytecode was regenerated deterministically.

Examples 401-404 cover reference execution, canonical owned-result typed IR,
self-host owned-array extraction, and self-host owned-dictionary extraction.
Three negative cases preserve mutable-owner, key-type, and owned-result binding
rules. `scripts/verify-owned-indexed-take.ps1` assembles and executes all three
reference/self-host ownership cases under Linux AddressSanitizer and
LeakSanitizer with leak and double-free detection enabled.

The Release build has zero warnings and errors. The complete Windows and Linux
suites pass 534/534. Windows Stage2 passes 6/6 at 8,336,587 LLVM bytes; Linux
Stage2 passes 5/5 at 8,336,449 bytes. Existing single-file and imported-file
differential hashes remain unchanged. The focused generic-container checklist
advances to 7/8 (87.5%); only fixed-array generic function contracts remain, so
the canonical roadmap stays 48.5/60 (80.8%).

Research basis:

- [Rust `Vec::remove`](https://doc.rust-lang.org/std/vec/struct.Vec.html#method.remove)
- [Rust `HashMap::remove`](https://doc.rust-lang.org/std/collections/struct.HashMap.html#method.remove)
- [Mojo `List.pop`](https://docs.modular.com/mojo/stdlib/collections/list/List/#pop)

## D194 - Infer Fixed-Array Element Types While Checking Compile-Time Lengths

Fixed-array generic functions use a compile-time value parameter and an inferred
element parameter in one structural contract:

```sl
fixedLength<N: Int, T> values: [T; N] -> Int {
    values -> len
}

["lexer", "parser", "llvm"] => stages
stages -> fixedLength<3>
```

The caller writes only the value argument that cannot be inferred. `T` is
inferred from the fixed-array value, while `N` is checked against its concrete
length. A dynamic array cannot satisfy `[T; N]`, and a call such as
`stages -> fixedLength<4>` is rejected. This follows Rust's separation of type
and const parameters and Mojo's compile-time parameter inference while keeping
Sollang's existing fluent call syntax.

The C# compiler specializes a function by both the compile-time length and the
canonical element type. Its LLVM ABI passes fixed arrays as a borrowed
pointer/length pair. The callee reconstructs the concrete fixed-array view, so
`len`, indexing, and element typing remain precise without copying the array or
transferring ownership. `Int`, `Text`, inline user structs, and structs that own
boxes all use the same contract.

The Sollang semantic compiler now interns inferred fixed-array literals as recursive
`kind 4` types with a concrete length. Binding propagation therefore retains
the whole array type rather than collapsing to its element type. Structural
specialization treats an identifier length in `[T; N]` as a value-parameter
wildcard, then the call checker compares the explicit numeric argument with the
actual array length.

Example 51 executes the host-generated specializations on Windows and Linux.
Example 405 verifies self-host type inference and length rejection. Dedicated
diagnostics cover dynamic-array input and length mismatch. The owned-element
case also executes under Linux AddressSanitizer and LeakSanitizer without leaks
or double frees. The Release build has zero warnings and errors, and the full
Windows/Linux suites pass 536/536. Windows Stage2 passes 6/6 at 8,343,036 LLVM
bytes; Linux Stage2 passes 5/5 at 8,342,898 bytes. This completes the focused
generic-container checklist at 8/8 and promotes the canonical roadmap to 43
complete, 12 partial, and 5 missing gates: 49/60 (81.7%).

Research basis:

- [Rust generic parameters](https://doc.rust-lang.org/stable/reference/items/generics.html)
- [Mojo parameters](https://docs.modular.com/mojo/manual/parameters/)
- [Mojo generics](https://docs.modular.com/mojo/manual/generics/)

## D195 - Keep the Fluent Subject While Adding Positional Runtime Arguments

Status: implemented and cross-target verified
Date: 2026-07-19

The first declared function input remains Sollang's fluent subject. A
function declares additional runtime inputs after commas, and a fluent call
supplies only those additional values in parentheses:

```sl
weighted value: Int, scale: Int, offset: Int -> Int {
    value * scale + offset
}

7 -> weighted(3, 2) => flowed
weighted(7, 3, 2) => direct
```

This keeps existing one-input pipeline syntax unchanged and gives direct calls
an unsurprising positional form. Compile-time generic parameters stay in angle
brackets, so runtime arguments and compile-time specialization remain visibly
distinct. Parameter labels improve declarations and diagnostics without adding
a second named-argument call syntax.

The C# compiler carries additional parameters through parsing, binding,
specialization, ownership analysis, and LLVM lowering. Direct, fluent, method,
generic, inline-standard-library, Arena, and structured-async calls share the
same ABI. Every parameter independently supports readonly, `mut`, and `move`;
moved additional owners participate in transfer, failure cleanup, cancellation,
and returned-owner analysis.

The Sollang compiler represents each additional declaration explicitly in its AST,
resolves it as a lexical parameter symbol, preserves its type in typed IR, and
emits `%arg1`, `%arg2`, and later LLVM parameters after the fluent `%arg`.
Nested calls and calls inside control regions use the same ordered argument
walker. Self-host type checking reports both arity and per-position type
mismatches before LLVM generation.

Examples 406-409 cover reference execution, methods, generics, `mut`, `move`,
async ownership, self-host LLVM execution, and self-host arity/type diagnostics.
Owned aggregate values in additional self-host LLVM arguments remain part of
the separate ownership-and-storage completeness gate; this decision closes the
general syntax, binding, scalar/copyable ABI, and diagnostic path without
claiming that broader storage gate.

The final Release solution build has zero warnings and errors. The fast suite
passes 439/439 and the complete Windows and Linux suites each pass 543/543.
Windows Stage2 passes 6/6 with 8,392,752 LLVM bytes; Linux Stage2 passes 5/5
with 8,392,614 bytes. Single-file, grouped-not, and imported multi-file
differential hashes agree between the C# stage-1 compiler and the Sollang stage-2
compiler, and both native products assemble, link, and execute.

Research basis:

- [Gleam pipelines](https://tour.gleam.run/functions/pipelines/)
- [Swift functions](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/functions/)
- [Mojo functions](https://docs.modular.com/mojo/manual/functions/)

## D196 - Batch Stage3 Fixed-Point Verification After Stage2 Checkpoints

Status: implemented and fixed-point verified
Date: 2026-07-19

Stage2 remains the normal self-host verification gate. Stage3 is intentionally
separate because regenerating the complete compiler on every feature checkpoint
adds substantial latency while providing little extra signal between compiler
bootstrap changes.

Run Stage3 after ten Stage2-verified feature checkpoints. Run it earlier for a
bootstrap, intrinsic, ABI, or compiler-emitter change that can affect
self-reproduction, or when explicitly requested. A feature checkpoint, rather
than each script invocation, advances the cadence so retries and target reruns
do not consume the ten-check budget.

The dedicated `scripts/verify-selfhost-stage3.ps1` rejects stale Stage2
artifacts, can rebuild Stage2 explicitly, generates Stage3, compares normalized
complete-module SHA-256 values, and assembles the result with `llvm-as`.
Windows and Linux Stage2 verifiers do not generate Stage3.

The cadence began after correcting wide integer literal conversion in the
self-host LLVM emitter. Integer literals now materialize directly at the
destination width rather than first becoming `i32`, which had truncated a
64-bit module hash and made the current Stage3 diverge. The repaired Stage2 and
Stage3 outputs are both 8,397,917 LLVM bytes with normalized SHA-256
`143EF69D5213B05C992D43643E71318525436A60FDFF2DF315C534D00D856832`.
That verification resets the tracked cadence to 0/10.

## D197 - Rename The Language To Sollang And Use `.slg` Sources

Status: implemented and cross-target verified
Date: 2026-07-19

The language is named **Sollang** and its source extension is **`.slg`**. This
is a full brand migration rather than an alias layered over the former brand: solution,
project, namespace, compiler, self-host modules, standard library imports,
examples, fixtures, manifests, scripts, generated grammar, documentation, and
editor tooling all use the new name. The former source extension is not accepted
as the canonical source form.

Sollang deliberately preserves four equal meanings of `Sol`:

1. the sun: light, warmth, and transparent code;
2. the solfège note: rhythm, harmony, and pleasure in reading and writing;
3. the beginning of *solution*: turning complicated problems into clear ones;
4. the creator principle **S·O·L — Simple, Original, Logical**.

`README.md` presents all four commitments and `docs/PHILOSOPHY.md` gives each
one an independent design test. They are not collapsed into one generic claim
of simplicity.

The GitHub repository is `dimohy/Sollang`. Its About description is “A bright,
harmonious native language for clear solutions and Simple, Original, Logical
creation.” The canonical SVG and derived VS Code PNG display `SLG`. The VS Code
language id and TextMate scope are `sollang` and `source.sollang`; version 0.3.0
recognizes `.slg` and is packaged as `sollang-language-support-0.3.0.vsix`.

Generated LLVM uses `%sollang.*` types and `@sollang_*` symbols, so generated
artifacts do not retain the old abbreviation. The `sollang` CLI, `sollang.project`,
`SOLLANG_LLVM_HOME`, and native `sollangc` bootstrap name complete the external
and internal naming boundary.

The Release solution builds with zero warnings and errors. The complete Windows
and Linux suites each pass 544/544. Windows Stage2 passes 6/6 at 8,781,929 LLVM
bytes; Linux Stage2 passes 5/5 at 8,781,688 bytes. This brand migration is the
first checkpoint after the periodic Stage3 baseline, so the cadence is 1/10 and
Stage3 is intentionally not regenerated for this checkpoint. The canonical
language-capability score remains 49.5/60 (82.5%) because a rename does not
complete a capability gate.

## D198 - Explicit Return In Local Functions

Status: implemented and Stage2 verified
Date: 2026-07-19

Local functions are emitted as independent LLVM functions with their own
parameters, captured bindings, stack frame, and ownership scope. They therefore
use the same explicit `value -> return` and Unit `return` semantics as module
functions. The former semantic restriction treated them like inline block
functions even though code generation already gave them an independent return
boundary; that restriction is removed. Result-producing block functions remain
restricted because they are genuinely lowered inside the caller's control-flow
region.

Every local owner other than the transferred return value is dropped in reverse
declaration order before an early return. Example 411 covers both the early and
tail paths with a local dynamic array. Release builds with zero warnings and
errors, and the complete Windows suite passes 545/545. Windows Stage2 passes
6/6 at 8,781,929 LLVM bytes. This is checkpoint 2/10 after the periodic Stage3
baseline, so Stage3 is intentionally not regenerated. This slice does not yet
promote the structured early-exit roadmap gate: moved-field reinitialization and
branch joins remain.

Research basis:

- [Rust destructor scopes](https://doc.rust-lang.org/reference/destructors.html)
- [rustc move paths](https://rustc-dev-guide.rust-lang.org/borrow-check/moves-and-initialization/move-paths.html)
- [Swift deferred actions](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/controlflow/)

## D199 - Context-Inferred Private Function Signatures

Status: implemented and Stage2 verified
Date: 2026-07-19

Sollang keeps the colon and arrow as visible function boundaries while allowing
the type names between them to be absent. `helper value: -> { ... }` infers the
primary input and return, `helper value: Int -> { ... }` infers only the return,
and `helper value: -> Int { ... }` infers only the input. This avoids adding an
underscore placeholder and keeps explicit and inferred declarations visually
aligned.

Inference is intentionally visibility-bounded. Local functions always have a
narrow lexical consumer context. A non-public top-level helper is eligible only
when exactly one function or `main` scope consumes it. Public, standard-library,
generic, and impl signatures remain explicit; multiple consumer scopes,
conflicting call arguments or returns, and underconstrained signatures are
compile-time errors. A fixed-point constraint pass combines call argument types,
tail expressions, explicit returns, bindings, and nested calls before ordinary
semantic binding.

Example 412 covers both-sides, input-only, return-only, and zero-input return
inference. Three diagnostics cover public ABI leakage, multiple consumers, and
conflicting inputs. The generated grammar gives explicit dictionary and array
return types priority over the omitted-return alternative.

The self-host AST records omitted input and return positions as declaration
flags instead of fabricating type AST nodes. Symbol collection creates an
untyped synthetic parameter, and the canonical expression-type-ID fixed point
propagates call arguments into that parameter and the final owned body
expression back to call results. The legacy expression projection follows the
same constraints. Example 413 assembles, links, and executes LLVM containing
both a private top-level helper and a local helper; examples 414 and 415 pin the
flat AST and symbol representation.

The Release build has zero warnings and errors and the full Windows suite passes
552/552. Native self-host regeneration passes Stage2 6/6 at 8,881,548 LLVM
bytes with Stage1/Stage2 hashes preserved. This is checkpoint 4/10 after the
periodic Stage3 baseline, so Stage3 is intentionally not regenerated.

Research basis:

- [Rust closure type inference](https://doc.rust-lang.org/stable/book/ch13-01-closures.html)
- [Swift inferred closure signatures](https://docs.swift.org/swift-book/ReferenceManual/Expressions.html#ID544)

## D200 - Deterministic Standard-Library Source-Set Discovery

Status: implemented and Stage2 verified
Date: 2026-07-19

The standard library is a confined module source set rather than six filenames
compiled into the C# driver. `sollang build` recursively discovers every
`.slg` file below the repository `stdlib` root, sorts by ordinal relative path,
and parses the complete deterministic set. A module's path is its structural
identity: `stdlib/sys/text.slg` must declare `namespace sys.text`.

Discovery rejects an empty standard library, path/namespace mismatches,
duplicate namespace declarations, and executable top-level statements. This
keeps adding a library module data-driven without letting a misplaced source
silently change module identity or introduce a second program entry point.
`sys.text` is the first module added without changing compiler code; its
`byteCount` and `isEmpty` functions keep UTF-8 byte operations explicit.
Example 416 imports the automatically discovered module and executes both
functions.

The Release build has zero warnings and errors and the complete Windows suite
passes 553/553. Native self-host regeneration remains byte-stable and passes
Stage2 6/6 at 8,881,548 LLVM bytes with all three differential hashes
preserved. This is checkpoint 5/10 after the periodic Stage3 baseline, so
Stage3 is intentionally not regenerated.

The design follows Swift Package Manager's confined target source sets and
Rust's deterministic module-path/file-path relationship while preserving
Sollang's one namespace per file model. It completes the standard-library
source-set gate. The formal roadmap becomes 45 complete, 10 partial, and 5
missing: **50/60 (83.3%)**.

Research basis:

- [Swift Package Manager targets](https://docs.swift.org/swiftpm/documentation/packagedescription/target/)
- [Rust module files](https://doc.rust-lang.org/reference/items/modules.html)

## D201 - Static Move-Path Reinitialization and Safe Joins

Status: implemented and Stage2 verified
Date: 2026-07-19

A mutable struct field assignment now distinguishes replacement from
reinitialization. Replacing an initialized owned field drops its previous value
before the store. Assigning the exact direct field after that path was moved
does not read or drop the uninitialized slot; it restores the path's drop
obligation. The owned replacement binding is itself consumed by the assignment,
so exactly one owner remains.

The self-host ownership pass permits later whole-owner and exact-field use only
after the repair. Structured `if` and `while` regions use a deliberately static
join rule: every partial move must be repaired before leaving that region.
Otherwise diagnostic 20 rejects the join. This is stricter than a runtime drop
flag scheme, but it keeps cleanup deterministic and proves that every joined
path has the same initialized move-path set.

Examples 417-420 cover native LLVM replacement/reinitialization cleanup,
post-repair use, an unrepaired branch diagnostic, and the terminal branch case
where an explicit return needs no join repair. This closes the
structured early-exit roadmap gate and moves the formal roadmap to 46 complete,
9 partial, and 5 missing: **50.5/60 (84.2%)**.

The Release build has zero warnings and errors and the complete Windows suite
passes 557/557. Native self-host regeneration passes Stage2 6/6 at 8,919,060
LLVM bytes with all three
differential hashes preserved. This is checkpoint 6/10 after the periodic
Stage3 baseline, so Stage3 is intentionally not regenerated.

Research basis:

- [rustc move paths](https://rustc-dev-guide.rust-lang.org/borrow-check/moves-and-initialization/move-paths.html)
- [Rust assignment and destruction](https://doc.rust-lang.org/reference/destructors.html)

## D202 - Allocation-Free Compiler Text Search

Status: implemented and Stage2 verified
Date: 2026-07-19

`sys.text` now provides `startsWith`, `endsWith`, `indexOf`, `lastIndexOf`,
`contains`, `compareOrdinal`, and `equalsAsciiIgnoreCase`. Search results use
`Option<UIntSize>` and therefore make absence distinct from byte offset zero.
All offsets are explicitly UTF-8 byte offsets, matching lexer spans, mapped
source views, and the existing `Text.byte`/`Text.slice` substrate. Empty
needles return `None`; empty prefixes and suffixes match.

The operations borrow their Text inputs and allocate no temporary strings or
arrays. Ordinal comparison is deterministic across targets, while the
case-insensitive operation is deliberately ASCII-only so it does not pretend
to implement Unicode case folding. Example 421 exercises the actual discovered
standard-library module through the C# host. Example 422 assembles, links, and
executes the same multi-argument loop shape through the self-host LLVM compiler.

This completes the broader string-processing compiler primitive and provides
the search substrate for the following portable Path/filesystem work. The
formal roadmap becomes 47 complete, 8 partial, and 5 missing:
**51/60 (85.0%)**.

The Release build has zero warnings and errors and the complete Windows suite
passes 559/559. Native self-host regeneration passes Stage2 6/6 at 8,919,060
LLVM bytes with all three differential hashes preserved. This is checkpoint
7/10 after the periodic Stage3 baseline, so Stage3 is intentionally not
regenerated.

Research basis:

- [Rust cross-platform path and component model](https://doc.rust-lang.org/std/path/index.html)
- [Swift System FilePath](https://developer.apple.com/documentation/system/filepath)

## D203 - Owned Target-Explicit Portable Paths

Status: implemented and Stage2 verified
Date: 2026-07-19

`sys.path.Path` owns a canonical growable UInt8 buffer and carries an explicit
`Style.Posix` or `Style.Windows`. The style is data, not ambient host state, so
a Windows-hosted compiler can normalize a Posix target path and vice versa.
`normalizeConfined` is lexical: it collapses repeated separators and `.`
components, resolves `..`, preserves Posix, drive, and UNC roots, and rejects
parent traversal beyond the starting root. `join` rejects absolute children
instead of silently replacing the base. No operation performs filesystem I/O or
claims to resolve symlinks.

The reference LLVM backend exposed a latent module-boundary ownership defect.
Standard-library user functions had always been inlined, so an imported `move`
function containing a field move or early return inherited the caller's
function context. That could drop both the moved input field and the returned
Path payload. Standard-library functions that contain an early return or a
stack-promotable container are now emitted as ordinary LLVM functions, but only
when reachable from the program's call graph. Pure scalar wrappers remain
inline. Storage-placement analysis uses the same classification, preventing
unused library functions from reserving caller frame slots.

`Path`, `Style`, and `[UInt8; ~]` receive reserved standard-library type IDs.
Adding the discovered module therefore does not renumber unrelated user types,
alter legacy LLVM witnesses, or make unused heap containers appear on wasm.
Example 423 executes Posix, drive, UNC, escape rejection, relative join, and
absolute-child rejection through the reference compiler. Example 424 has the
self-host compiler emit, assemble, link, and execute a two-module program that
creates, transfers, returns, and consumes an owned Path-shaped value.

The Release build has zero warnings and errors and the complete Windows suite
passes 561/561. Native self-host regeneration passes Stage2 6/6 at 8,919,060
LLVM bytes with all three differential hashes preserved. This is checkpoint
8/10 after the periodic Stage3 baseline, so Stage3 is intentionally not
regenerated. The portable path/filesystem gate advances from missing to partial:
directory handles, metadata, canonical filesystem queries, and deterministic
directory traversal remain. The formal roadmap becomes 47 complete, 9 partial,
and 4 missing: **51.5/60 (85.8%)**.

Research basis:

- [Rust Path and PathBuf](https://doc.rust-lang.org/std/path/index.html)
- [Swift System FilePath](https://developer.apple.com/documentation/system/filepath)

## D204 - Directory Reads Are Sorted Owned Snapshots

Status: implemented and Stage2 verified
Date: 2026-07-19

`sys.directory.read(Path)` is a snapshot operation rather than a lazy iterator
or an exposed native handle. Native Windows and Linux implementations enumerate
once, exclude `.` and `..`, classify each entry as file, directory, symlink, or
other, and insert it into raw UTF-8 byte lexical order before serialization.
The operating-system handle and temporary nodes are released before control
returns to Sollang. The stdlib decoder then allocates independent basename
buffers, preserving the caller's explicit Posix/Windows `Path.Style`.

This choice makes compiler module discovery reproducible. Native directory APIs
do not guarantee enumeration order, so source ordering must be explicit rather
than inherited from a filesystem. A compact `kind:u8 + length:u32-le + bytes`
snapshot also avoids retaining `WIN32_FIND_DATA`, `dirent`, or a long-lived
directory handle across the language boundary. Browser wasm rejects traversal
until its host provides a filesystem capability.

Directory `Raw`, `Entry`, `Kind`, dynamic-entry-array, and result identities are
reserved. Their LLVM definitions, drop helpers, declarations, and platform
runtime are emitted only when traversal is reachable, so adding the module does
not change unrelated LLVM witnesses. Dedicated directory result enums avoid
eager generic `Result` specializations from consuming user parametric IDs.

The implementation exposed three ownership/code-generation defects. Moving an
owned struct literal into a container now transfers nested owned field sources;
an enum match drops or transfers its owned payload exactly once on each branch;
and enum constructor emission now accepts the same multi-segment type paths as
semantic analysis. Example 425 passes on Windows and Linux. Release builds with
zero warnings, Windows passes 562/562, and Stage2 passes 6/6 at 8,919,060 bytes
with hashes `8C4E94FCB4EBAC81D62C2AE4FB1CC97833045D829C20F84622870278C6EE5DE2`,
`BAF325B5C8013346E5242C8011C2B26B11F4F5BDC40BFBFC2B1BF9338DB2860D`, and
`F33D650B170E3E4383E41208E00634F1B9978C6EA4C922908D95E3AA1ABA2FD0`.
This is Stage2 checkpoint 9/10; Stage3 is deferred. The filesystem gate remains
partial and the formal score remains **51.5/60 (85.8%)**.

Research basis:

- [Rust `std::fs::read_dir`](https://doc.rust-lang.org/std/fs/fn.read_dir.html)
- [Microsoft `FindFirstFile`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-findfirstfilea)

## D205 - Target-Native Path Source Mapping

Status: implemented and Stage3 fixed-point verified
Date: 2026-07-19

`sys.file.mapPath(Path)` is the ownership-safe boundary between the D204
directory snapshot and affine compiler source mappings. It borrows the Path's
owned UInt8 storage, validates that its explicit style matches the compilation
target, and maps the file without allocating a second Text path. The Path and
the returned `SourceText` remain independent owners: dropping the mapping
unmaps the file, while dropping the Path releases only its byte buffer.

`sys.path.nativeStyle` is selected from the output target rather than the host
process. A Windows-hosted Linux build therefore receives `Style.Posix`. This
keeps cross-compilation deterministic and prevents accidental host-path
interpretation. The reference compiler supports both APIs. The self-host LLVM
compiler lowers the Path mapping boundary with the same style check and native
mapping runtime; its compiler driver will select the style when D206 combines
directory discovery with source-root loading.

The self-host typed IR now preserves `mapPath` as a SourceText-producing
intrinsic, and the LLVM scheduler excludes it from ordinary user-function call
emission. Example 426 executes the real stdlib API on Windows and Linux.
Example 427 has the self-host compiler emit, assemble, link, and execute a
Path-shaped mapped-source program. The filesystem gate remains partial because
canonical filesystem queries, richer metadata, and source-root discovery still
remain. The Release build has zero warnings and errors, the complete Windows
suite passes 564/564, and the Linux path-mapping regression passes. Stage2
passes 6/6 at 8,958,755 LLVM bytes with the three differential hashes preserved.
The checkpoint-10 Stage3 gate reaches the same 8,958,755-byte fixed point with
hash `A8FF3B396E03DD487017C3EC04521CE605501A61860B87849DF55714DE48CA39`.
The cadence therefore resets to 0/10. The formal roadmap remains **51.5/60
(85.8%)**.

Research basis:

- [Rust modules](https://doc.rust-lang.org/reference/items/modules.html)
- [Zig modules](https://ziglang.org/documentation/master/)
- [Swift Package Manager target paths](https://docs.swift.org/swiftpm/documentation/packagedescription/target/path/)

## D206 - Deterministic Source-Root Discovery

Status: implemented and Windows/Linux Stage2 verified
Date: 2026-07-19

`sollang.compiler.source_root.discover(Path)` performs breadth-first traversal
over the sorted, owned D204 directory snapshots. It returns only regular
`.slg` files, carries target-explicit `Path` values throughout traversal, and
therefore produces the same relative source order independently of host
directory enumeration order. An empty root returns an explicit error rather
than silently compiling an empty product.

The self-host LLVM boundary maps every discovered Path directly with the D205
`file.mapPath` intrinsic. Its small local `mapSource` helper intentionally
omits the predictable return type while retaining `uses File`, proving that
private/local signature inference survives source-root compilation without
weakening public ABI rules. Fully qualified payload-enum constructors are now
resolved before call arguments, so argument identifiers cannot be mistaken for
namespace members. The Linux directory failure path also frees the current SSA
list head, matching the reference runtime and LLVM dominance rules.

Completion checklist:

- [x] Deterministic breadth-first discovery and empty-root failure
- [x] Owned target-explicit paths with direct mapped-source loading
- [x] Windows examples 428-430 and valid self-host LLVM assembly
- [x] Complete Windows Stage2 6/6 at 9,361,816 LLVM bytes
- [x] Complete Linux Stage2 5/5 at 9,360,301 LLVM bytes
- [x] Stage1/Stage2 differential hashes preserved on both targets
- [x] Complete Windows example suite passes 567/567

This is checkpoint 1/10 after the D205 periodic Stage3 reset, so Stage3 is
intentionally deferred. Canonical filesystem queries and richer metadata keep
the filesystem gate partial; the formal roadmap remains **51.5/60 (85.8%)**.

## D207A - Stable Module Interface Fingerprints

Status: fingerprint foundation implemented; persistent reuse pending
Date: 2026-07-19

Sollang module caching uses three separate content identities rather than file
timestamps. `interfaceHash` contains only exported declarations and their
signature-bearing members, `implementationHash` contains the normalized module
token stream, and `importHash` contains the ordered direct imports. Whitespace,
comments, and repeated blank lines do not affect these identities. A private or
body-only edit changes the implementation identity without invalidating
consumers; a public signature or direct import edit changes the corresponding
consumer invalidation input.

The design follows Rust's stable cross-session dependency fingerprints and
Clang's strict module-context consistency, while avoiding Clang PCM's
compiler-version-sensitive binary AST as Sollang's first cache format. A cache
entry will additionally carry its schema/compiler/target identity and the full
canonical interface bytes, so the 64-bit lookup hash is never trusted as the
sole correctness check. Direct dependency interface hashes, not transitive
source timestamps, form the invalidation frontier.

Implementation checklist:

- [x] Stable public-interface fingerprint
- [x] Separate normalized implementation and direct-import fingerprints
- [x] Trivia stability, body isolation, private isolation, and ABI invalidation tests
- [x] Native Stage1 `fingerprint` mode
- [x] Stage1/Stage2 fingerprint equality gate
- [x] Versioned canonical interface serialization
- [x] Atomic cache publication
- [x] Cache corruption rejection
- [x] Direct-dependency cache hit/miss integration
- [x] Body-only edit proves consumer reuse
- [x] Self-host owned File lowering for persistent cache I/O
- [x] Stage2 persistent cache read/write integration

D207A is an independently Stage2-verified foundation checkpoint, advancing the
periodic Stage3 cadence to 2/10. It does not complete the module/interface-cache
gate, so the formal roadmap remains **47 complete, 9 partial, 4 missing:
51.5/60 (85.8%)**.

The complete Windows suite passes 568/568. Windows Stage2 passes 6/6 at
9,401,740 LLVM bytes, and Linux Stage2 passes 5/5 at 9,400,225 bytes with the
existing target differential hashes preserved.

Research basis:

- [Rust incremental compilation fingerprints](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation-in-detail.html)
- [Clang modules and cache invalidation](https://clang.llvm.org/docs/Modules.html)
- [Clang standard module consistency](https://clang.llvm.org/docs/StandardCPlusPlusModules.html)

## D207B1 - Canonical Interfaces and Atomic Publication

Status: implemented and Stage2 verified; persistent cache reuse pending
Date: 2026-07-19

The interface hash is now derived from a schema-1 canonical UInt64 word stream.
The stream contains its magic, schema, module identity, exported-symbol count,
public ordinal, symbol kind and flags, token kind and length, and every original
signature byte. Whitespace, comments, bodies, source-local AST indices, and
private-symbol positions are absent. A lookup hash is only an accelerator:
`sameInterface` compares the full flattened canonical stream before reuse. This
also fixes the D207A edge case where inserting a private declaration before a
public declaration could change a source-local symbol index and spuriously
invalidate consumers.

`sys.file.FileWriter` now exposes a synchronous durability barrier, and
`AtomicReplaceRequest -> atomicReplace` publishes a closed staged file with
Windows `MoveFileExA(REPLACE_EXISTING | WRITE_THROUGH)` or Linux `rename`.
Example 432 writes, syncs, closes by affine scope exit, atomically replaces an
existing file, and reads back the new value on both Windows and Linux. Example
431 now proves full canonical equality across private insertion and full
canonical inequality after a public signature change.

The complete Windows suite passes 569/569. Windows Stage2 passes 6/6 at
9,464,194 LLVM bytes; Linux Stage2 passes 5/5 at 9,462,679 bytes. Stage1 and
Stage2 canonical fingerprint output and all target differential hashes remain
equal. This advances the periodic Stage3 cadence to 3/10. Corruption rejection,
persistent read/write integration, direct-dependency hit/miss planning, and the
body-only consumer-reuse proof remain D207B2, so the formal roadmap remains
**47 complete, 9 partial, 4 missing: 51.5/60 (85.8%)**.

Research basis:

- [Rust incremental compilation fingerprints](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation-in-detail.html)
- [Clang modules and cache invalidation](https://clang.llvm.org/docs/Modules.html)
- [Clang strict module consistency](https://clang.llvm.org/docs/StandardCPlusPlusModules.html)

## D207B2 - Validated Cache Codec and Dependency Reuse Planner

Status: planner implemented and Stage2 verified; self-host persistence pending
Date: 2026-07-19

The schema-1 cache now has a bounded decoder and validator for magic, schema,
compiler context, target, declared word count, checksum, record structure, and
dependency lists. Reuse compares the full canonical public-interface stream in
addition to lookup hashes, then checks each ordered direct dependency's module
identity and interface hash. An implementation-only dependency edit misses the
edited module while retaining the consumer; a public-signature edit invalidates
both. Example 433 covers warm reuse, body-only consumer reuse, signature
invalidation, each corruption guard, and staged atomic persistence through the
reference backend on Windows and Linux.

The pure codec, validator, and planner are part of the reusable self-host
compiler. Its `interface-cache` verification mode analyzes real files, encodes
their cache records, and immediately revalidates/reuses all three modules in
both Stage1 and Stage2. Persistent I/O is deliberately isolated in
`module_cache_io.slg`: the C# backend already lowers owned File open/read/write,
sync, and atomic replace, while the self-host LLVM backend does not yet lower
those owned File operations. Keeping the adapter separate prevents an
unsupported effect backend from contaminating the pure planner and leaves that
parity gap explicit rather than masking it with a fallback.

The complete Windows suite passes 570/570. Windows Stage2 passes 6/6 at
9,545,859 LLVM bytes, and Linux Stage2 passes 5/5 at 9,544,344 bytes with all
existing differential hashes preserved. This is Stage2 checkpoint 4/10 after
the D205 reset. Because Stage2 persistence still depends on the missing
self-host owned File lowering, the module/interface-cache gate remains partial
and the formal roadmap remains **47 complete, 9 partial, 4 missing: 51.5/60
(85.8%)**.

## D207B3 - Self-Host Persistent Cache I/O and D207C Boundary

Status: Windows Stage2 verified; ordinary-build reuse remains D207C
Date: 2026-07-19

The self-host backend now emits the complete affine cache-file path:
`openRead`, `openWrite`, positioned `readAt<UInt64>` and `writeAt<UInt64>`,
`sync`, scope-close, and `atomicReplace`. Stage1 and Stage2 persist and reload
independent cache files, then produce the same validated planner result
`0,3,0,0,1`. The implementation also closes matched owned file payloads at the
`when` merge whenever the result does not transfer that same owner.

During this work, a direct no-argument call used as a payloadless enum arm result
was present in typed IR but absent from emitted LLVM. The match emitter now
materializes that call before storing the arm result. Example 434 isolates the
case and its expected LLVM assembles with `llvm-as`.

This checkpoint does not claim the module-cache gate complete. The current
`interface-cache` mode still performs complete analysis before planning reuse,
and ordinary `sollang build` does not load cached semantic or LLVM artifacts.
D207C will use an immutable old manifest plus an atomically published new
generation. Reuse requires strict compiler/target/configuration identity, full
canonical dependency interfaces, validated artifact bytes, and clean-vs-cached
output equality. Cache data is always disposable; deletion must restore a cold
but equivalent build.

Research basis:

- [rustc incremental compilation in detail](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation-in-detail.html)
- [Clang module cache compilation model](https://clang.llvm.org/docs/Modules.html#compilation-model)
- [Clang module option consistency](https://clang.llvm.org/docs/StandardCPlusPlusModules.html#options-consistency)
- [Zig 0.16 incremental compilation](https://ziglang.org/download/0.16.0/release-notes.html#Incremental-Compilation)
- [Zig disposable local build cache](https://ziglang.org/learn/build-system/)

D207B3 advances the periodic Stage3 cadence to 5/10. The formal score remains
**47 complete, 9 partial, 4 missing: 51.5/60 (85.8%)** until ordinary builds
actually skip validated module/codegen work.

## D207C1 - Raw-Source Identity and Schema-2 Preflight

Status: Windows and Linux Stage2 verified; D207C 1/5 complete
Date: 2026-07-19

Persistent cache schema 2 adds `sourceHash` to every module record. The hash is
computed from the exact source length and bytes, providing a cheap pre-analysis
identity for files discovered by deterministic module paths. `preflight`
accepts only a fully validated cache whose target, module identity, and raw
source identity all match.

Raw-source identity is intentionally separate from semantic reuse identity.
Whitespace or comments must miss the preflight fast path, while the normalized
implementation hash and full canonical public interface can still prove that
dependent modules remain reusable. This preserves correctness without making
the cache sensitive to session-local indexes or pretending that trivia changes
alter module semantics.

Example 431 proves that trivia changes raw identity but not semantic identity.
Example 433 proves schema-2 persistence and warm/miss preflight behavior.
Example 434 independently locks down the payloadless-match call-emission bug
found while making the cache path self-hosted. Block-arm payload bindings keep
their loaded payload when their synthetic region result is not emitted. Linux
runtime composition also assigns shared `open`/`close` declarations to one
runtime owner when process, source-text, and owned-file support coexist.
Windows Stage2 passes all six phases and Linux Stage2 all five, including
matching single-file and multi-file LLVM plus native execution.

D207C is now **1/5 (20%)**, and this advances the periodic Stage3 cadence to
6/10. The formal roadmap remains **47 complete, 9 partial, 4 missing: 51.5/60
(85.8%)** because normal builds still do not load reusable semantic or LLVM
artifacts.

## D207C2A - Canonical Typed-IR Artifact Envelope

Status: Windows and Linux Stage2 verified; D207C 1.5/5 complete
Date: 2026-07-19

The first half of typed-IR artifact serialization is a canonical word envelope,
not a dump of process-local arrays. Modules are emitted in ascending stable path
hash order. Every IR reference is represented by a module hash and a one-based
module-local ordinal. Module references use path hashes, signed IR metadata uses
zig-zag encoding, and session-local `typeId` is deliberately excluded.

The envelope carries compiler schema, module/node counts, declared word length,
per-module payload lengths, and a checksum. A zero module hash remains legal;
the `(hash, ordinal)` pair distinguishes it from an absent `(0, 0)` reference.
Example 435 proves that reversing source input order produces identical words
and that payload corruption is rejected. The Stage2 verifier executes the same
three-module artifact operation through both Stage1 and Stage2 and requires the
shared result `module artifacts = 0,3,1`.

Linux Stage2 also assembles, links, and executes the complete compiler with the
serializer included; Windows additionally executes and compares the artifact
mode directly between Stage1 and Stage2.

This does not yet make the artifact reusable. D207C2B must serialize canonical
structural types and decode the stable references into a fresh compilation
session. D207C is **1.5/5 (30%)**, the Stage3 cadence is 7/10, and the formal
roadmap remains **47 complete, 9 partial, 4 missing: 51.5/60 (85.8%)**.

## D207C2B - Canonical Structural Types and Artifact Rehydration

Status: Windows and Linux Stage2 verified; D207C 2/5 complete
Date: 2026-07-19

Schema 2 now embeds a canonical structural type table before the module IR.
Each record contains stable module and child-type hashes, explicit presence
words for optional references, scalar metadata, and a structural checksum.
Equivalent records are interned, unequal records with the same identity are
rejected, and records are emitted in an explicit unsigned hash order. The
unsigned comparator is expressed in language-level arithmetic so Stage1 and
Stage2 cannot disagree at the signed `i64` boundary.

Decoding no longer restores process-local indexes. It resolves module hashes
against the new session, rebuilds child types in dependency-ready order, maps
stable type hashes to fresh canonical IDs, and then reconstructs IR references
from module hash plus one-based local ordinal. Dynamic indexed words are bound
before signed conversion so the self-host LLVM backend does not hoist a value
outside its defining control-flow region.

Example 435 proves canonical input-order independence, corruption rejection,
and encode-decode-encode byte equality through the C# compiler. The native
Stage2 driver validates the same type table and rehydrates all 13 IR nodes in
both Windows and Linux products. Windows passes 572/572 examples and the full
six-phase Stage2 differential check (10,406,201 LLVM bytes). Linux passes the
five-phase Stage2 check (10,402,804 LLVM bytes).

This follows rustc's stable dependency identities and Clang's stable-ID
remapping principle: persisted identity is separate from the fresh in-memory
arena index. Strict schema, target, and configuration matching remains a
precondition for reuse.

Research basis:

- [rustc incremental compilation](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation.html)
- [Clang modules](https://clang.llvm.org/docs/Modules.html)
- [Clang PCH stable ID remapping](https://clang.llvm.org/docs/PCHInternals.html)

D207C is now **2/5 (40%)**, and the periodic Stage3 cadence advances to 8/10.
The formal roadmap remains **47 complete, 9 partial, 4 missing: 51.5/60
(85.8%)** until ordinary builds load and merge reusable codegen units.

## D207C3A - Persistent Codegen-Unit Contract and Canonical Merge

Status: Windows and Linux Stage2 verified; D207C 2.5/5 complete
Date: 2026-07-20

Sollang now has a self-hosted persistent format for deterministic LLVM output
fragments. An artifact contains exactly one shared prefix, zero or more module
units, and exactly one shared suffix. Module records are ordered by stable path
hash using an explicit unsigned `UInt64` comparison. The complete canonical
namespace bytes are stored beside that hash and the decoder recomputes it; a
lookup-hash collision or duplicate module is rejected rather than reused.

Compatibility is explicit rather than inferred from filenames or timestamps.
The envelope records compiler-schema, target, and build-configuration hashes;
each module records interface and implementation hashes. LLVM bytes are packed
little-endian at eight bytes per `UInt64`, with an exact byte length so fragments
can be concatenated across non-word-aligned boundaries without expanding each
byte to a 64-bit element. Validation checks declared lengths, canonical unit
order, prefix/suffix cardinality, namespace bytes, recomputed module hashes,
zero tail padding, per-fragment checksums, and a whole-envelope checksum before
decode or merge.

Example 436 constructs the same four units in different input and storage
orders and requires byte-identical artifacts. It also verifies exact merged
bytes, decode, target mismatch rejection, module-hash collision rejection, and
payload corruption rejection. The Stage1 and Stage2 compiler drivers expose an
`llvm-codegen-units` verification mode; Windows and Linux both require the
shared result `codegen units = 0,2,6`.

The partition follows rustc's model that a codegen unit is an independently
reusable LLVM module boundary, while keeping Sollang's initial granularity at
one stable unit per source module. The canonical merge follows `llvm-link`'s
ordered-input model. Packed fragments and strict context identities prepare for
a ThinLTO-style content-addressed cache without claiming ThinLTO itself. The
next half, D207C3B, must route the actual emitter into prefix/module/suffix
sinks, select validated old-generation fragments, and prove clean-vs-cached
LLVM byte equality in ordinary builds.

Primary references:

- [rustc codegen unit partitioning](https://doc.rust-lang.org/nightly/nightly-rustc/rustc_monomorphize/partitioning/index.html)
- [rustc codegen-units trade-offs](https://doc.rust-lang.org/rustc/codegen-options/index.html#codegen-units)
- [LLVM llvm-link](https://llvm.org/docs/CommandGuide/llvm-link.html)
- [LLVM ThinLTO](https://clang.llvm.org/docs/ThinLTO.html)
- [Clang modules](https://clang.llvm.org/docs/Modules.html)
- [Swift 5.2 incremental compilation architecture](https://www.swift.org/blog/swift-5.2-released/)

D207C is now **2.5/5 (50%)**, and the periodic Stage3 cadence advances to
**9/10**. The formal roadmap remains **47 complete, 9 partial, 4 missing:
51.5/60 (85.8%)** because the ordinary emitter and build pipeline do not yet
consume cached codegen units.

## D207C3B - Production LLVM Units and Ordinary-Build Reuse

Status: Windows/Linux full suites, Stage2, and Stage3 fixed point verified
Date: 2026-07-20

The production C# emitter no longer owns one monolithic globals/functions pair.
It emits a shared prefix, canonical stable-hash-ordered module units, and a
shared suffix. Global strings include a stable unit token, while SSA temporary
and label numbering restarts for each function. A module fragment is therefore
independent of prior modules and can be selected without replaying their
emission. When every key is warm, the emitter returns the old fragments before
generating any LLVM; on a partial hit it emits only the invalid modules and
merges both generations in canonical order.

`sollang build` retains source bytes and syntax per discovered source module.
Its codegen key covers compiler MVID/schema, target, optimization configuration,
exact module implementation bytes, canonical public declaration shape,
transitive imported interfaces, the ambient standard-library interface, and
the concrete bound-function specialization inventory. This is deliberately
stricter than timestamps and remains correct when generic calls change the set
of emitted functions.

The disposable `.sollang-cache` generation uses the exact D207C3A schema-1
`UInt64` envelope: the same magic, record order, little-endian fragment packing,
and checksum functions. It carries
exact UTF-8 identities, one prefix and suffix, unique canonically ordered
module records, and the same per-fragment and envelope checksums defined by the
self-host D207C3A schema. Loading rejects
invalid magic, schema, kind, cardinality, ordering, lengths, UTF-8, duplicates,
and checksums. A rejected cache is named in the build output and rebuilt rather
than silently trusted. The new generation is written through a same-directory
temporary file and atomically replaces the old generation only after the LLVM
link succeeds.

`scripts/verify-codegen-cache.ps1` executes the same generation matrix for
Windows x64 and Linux x64. Each target proves cold `0/5`, warm `5/5`, exact LLVM
byte equality, native output, body-only provider edits retaining the consumer
and root as `2/5`, public-interface edits invalidating the transitive consumers
as `0/5`, corruption rejection and repair. The Linux cold generation after the
Windows run also proves target isolation. The Windows and Linux full suites
each pass 573/573.

The checkpoint-10 Stage3 run initially exposed drift between duplicated source
lists: Stage2 included `selfhost/runtime/file.slg`, while Stage3 did not. The
missing `sys.file` module left `openWrite` and `openRead` unresolved, so their
following enum matches had no subject IR. Both Stage2 verifiers and the Stage3
verifier now read `selfhost-compiler-runtime.sources.txt`; the manifest itself
also participates in freshness checks. This makes the compiler source set one
shared contract instead of three manually synchronized arrays.

This completes the third D207C slice (**3/5, 60%**). Windows Stage2 passes 6/6
at 10,553,582 LLVM bytes, Linux Stage2 passes 5/5 at 10,550,185 bytes, and
Stage3 reaches the byte-identical 10,553,582-byte fixed point with normalized
SHA-256 `21A504DB039BE52029D594580D3EA4B9002AB17C5C45B7C36EDD52BD7BF349E6`.
The checkpoint cadence therefore resets from **10/10** to **0/10**. The
module/interface cache is now a partial rather than missing gate:
**47 complete, 10 partial, 3 missing: 52.0/60 (86.7%)**. Ordinary-build reuse of
the already-defined raw-source and typed-IR artifacts remains required before
the gate can be complete.

## D207C4A - Exact-Input Pre-Semantic Fast Path

Status: Windows/Linux full suites and Stage2 verified; D207C 3.5/5 complete
Date: 2026-07-20

Normal `sollang build` now probes an exact-input source snapshot before loading
or parsing the program. The snapshot records compiler, target, and optimization
identity; ordered root and project-manifest records; every standard-library and
discovered user-source path; and the exact bytes of manifests and sources.
Lengths and record counts are bounded, paths are unique and canonically ordered,
UTF-8 is strict, source comparison is streamed in 64 KiB blocks, and the full
envelope has a SHA-256 checksum.

The snapshot contains the SHA-256 digest of the corresponding `.cgu` file. An
exact hit therefore requires both artifacts to be individually valid and to
belong to the same published generation. This closes the crash window in which
atomic replacement of two separate files could otherwise leave a new codegen
generation beside an old source snapshot. Publication remains write-through,
same-directory, and atomic, and happens only after a successful link.

An exact hit skips source discovery, lexing, parsing, semantic analysis,
specialization discovery, and LLVM emission; the compiler decodes, merges, and
links the validated codegen units directly. A miss or rejected snapshot runs the
complete frontend and retains D207C3B's module-level LLVM reuse. This is a
whole-compilation exact fast path, not yet partial typed-IR rehydration.

The focused matrix now covers seven states on both Windows and Linux: cold,
exact warm, body-only change, public-interface change, source-snapshot
corruption, codegen corruption/generation mismatch, and repaired exact warm.
It proves native output and clean/cached LLVM byte identity. Windows and Linux
full suites each pass 573/573; Windows Stage2 passes 6/6 at 10,553,582 LLVM
bytes; Linux Stage2 passes 5/5 at 10,550,185 LLVM bytes. This is checkpoint 1/10
after the Stage3 reset, so Stage3 is deferred. D207C is **3.5/5 (70%)** while
the formal roadmap remains **47 complete, 10 partial, 3 missing: 52.0/60
(86.7%)** until changed modules can reuse pre-semantic typed IR.

## D207C4B - Generation-Bound Final Product Reuse

Status: Windows/Linux full suites and Stage2 verified; D207C 4/5 complete
Date: 2026-07-20

An exact source and codegen hit previously still paid the target linker cost.
The build now atomically publishes a fixed-size `.product` generation after the
source snapshot and LLVM-unit generation. Its checksummed payload binds compiler,
target, and optimization identity to SHA-256 digests of `.sources`, `.cgu`, and
the final executable or target artifact.

An exact match skips linking as well as all frontend and LLVM work. A missing or
changed output relinks from the already validated LLVM units and republishes only
the product marker; source or codegen changes continue through their stricter
fallback paths. The focused matrix now proves eight states on Windows and Linux,
including output corruption, frontend-free relinking, and the repaired exact
no-op path. The 13-source exact warm case measures 54.7 ms on the verification
machine.

The Windows and Linux full suites each pass 573/573. Windows Stage2 passes 6/6
at 10,553,582 LLVM bytes and Linux Stage2 passes 5/5 at 10,550,185 LLVM bytes.

This completes the fourth integration slice: D207C is **4/5 (80%)**, and the
periodic Stage3 cadence is **2/10**. Module-granular typed-IR rehydration after a
partial source miss remains the fifth slice, so the formal roadmap remains
**47 complete, 10 partial, 3 missing: 52.0/60 (86.7%)**.

## D207C5A - Stable Semantic Session Bridge

Status: Windows/Linux full suites and Stage2 verified; D207C 4.25/5 complete
Date: 2026-07-20

Persistent typed IR cannot safely contain the reference compiler's numeric
`TypeId` assignments or object-keyed resolved-call table because both identities
are reconstructed in each process. Sollang now derives a structural type name
for every builtin, nominal, option, result, task, array, dictionary, and box
shape. A stable function identity combines that type structure with module and
function name, kind, ownership, async state, generic templates and concrete
specializations, value-generic arguments, additional parameters, associated
types, effects, and the enclosing identity of a local function. Resolved call
sites receive deterministic ordinals within that stable owner while traversing
the source tree in semantic order.

This follows rustc's requirement that incremental dependency identities and
fingerprints remain stable across compiler sessions, while adopting Salsa's
separation between durable input identity and tracked derived values. Salsa's
backdating also reinforces an important later optimization: if a rebuilt
function body produces the same typed result, dependents should remain green
rather than being invalidated merely because work ran.

The ordinary build writes the identities into a schema-1 `.semantic` generation.
Records are unique and ordinally sorted, all lengths and counts are bounded,
UTF-8 decoding is strict, compiler/target/configuration identities are checked,
and a SHA-256 checksum covers the complete payload. Publication uses a
same-directory write-through temporary file and atomic replacement after the
link succeeds. A later changed-source build validates the old generation and
maps old functions and call-site targets onto the new semantic objects.

This checkpoint does not deserialize typed expressions and does not skip
semantic analysis. Output deliberately says `mapped`, never `reused`. The next
half must add module-scoped typed-IR body payloads, resolve stable references
against the declaration universe, and load green modules before body analysis.

The production emission fingerprint now uses the same stable function identity.
The public-interface fingerprint also filters private structs and enums, fixing
unnecessary transitive invalidation. The nine-state Windows/Linux cache matrix
proves private-declaration `2/5` unit reuse, public-interface `0/5` invalidation,
semantic corruption rejection/recovery, exact frontend/product hits, native
execution, and LLVM byte equality. Both full suites pass 573/573; Windows
Stage2 passes 6/6 at 10,553,582 LLVM bytes and Linux Stage2 passes 5/5 at
10,550,185 bytes.

This is D207C **4.25/5 (85%)** and periodic Stage3 checkpoint **3/10**. The
formal roadmap remains **47 complete, 10 partial, 3 missing: 52.0/60 (86.7%)**.

References:

- [rustc incremental compilation](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation.html)
- [rustc incremental compilation in detail](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation-in-detail.html)
- [Salsa incremental algorithm](https://salsa-rs.github.io/salsa/reference/algorithm.html)
- [Salsa tracked IR](https://salsa-rs.github.io/salsa/tutorial/ir.html)

## D207C5B - Dependency-Safe Function Semantic Reuse

Status: Windows/Linux full suites and Stage2 verified; D207C 4.5/5 complete
Date: 2026-07-20

D207C5A could map stable semantic identities after a complete analysis but did
not avoid any body work. D207C5B splits the reference compiler's function phase
into declaration construction and body validation. This is the request boundary
used by Swift's compiler architecture: immutable declarations are established
first, and later requests compute and cache body-derived information lazily.
It also matches Salsa's tracked-function rule that the reuse key and every input
read by the computation must be explicit.

The previous `.semantic` generation is now opened after parsing but before the
semantic compiler starts. Schema 2 adds:

- one SHA-256 fingerprint over canonically ordered visible struct, enum, trait,
  and function declarations;
- an exact SHA-256 digest for every module's ordered paths and source bytes; and
- canonical per-function binding and captured-binding maps whose type values use
  stable structural identities rather than session-local `TypeId` values.

After private-signature inference and declaration construction, the compiler
recomputes the visible declaration fingerprint. If it differs, no function is
reused. If it matches, an unchanged module may restore a cached function's type
maps into the fresh `TypeDefinitionTable` and bypass `ValidateUserFunction`.
Module source equality protects private declarations and bodies inside the same
module; the visible declaration fingerprint protects every cross-module input.
Consequently a provider body or private declaration change does not invalidate
an unchanged consumer, while a public signature change invalidates all affected
reuse conservatively.

This checkpoint deliberately excludes functions with local functions or
resolved generic/specialized call sites. Those require stable current-AST node
reconnection, not merely function and type reconnection. Main-scope bindings are
also recomputed. The compiler reports `reused N/M functions`, and the verifier
requires positive reuse for body-only/private changes and exactly zero for a
public-interface change. This is actual semantic work avoidance, but not yet the
complete module typed-IR query system.

The ten-state cache matrix passes on Windows and Linux, including semantic
corruption rejection and atomic recovery. Both full suites pass 573/573.
Windows Stage2 passes 6/6 with 10,553,582 LLVM bytes and unchanged differential
hashes; Linux Stage2 passes 5/5 with 10,550,185 bytes and unchanged hashes.

This advances D207C to **4.5/5 (90%)** and the periodic Stage3 cadence to
**4/10**. The formal roadmap stays **47 complete, 10 partial, 3 missing:
52.0/60 (86.7%)** until local functions, generic call sites, and main-scope
semantic state can be rehydrated.

References:

- [rustc incremental compilation and try-mark-green](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation.html)
- [rustc stable cross-session query fingerprints](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation-in-detail.html)
- [Salsa tracked functions and backdating](https://salsa-rs.github.io/salsa/reference/algorithm.html)
- [Swift incremental request architecture](https://www.swift.org/blog/swift-5.2-released/)

## D207C5C - Atomic Local Trees and Main-Scope Reuse

Status: Windows/Linux full suites and Stage2 verified; D207C 4.75/5 complete
Date: 2026-07-20

D207C5B excluded every function containing local declarations and always bound
main from scratch. Schema 3 removes both coarse exclusions without weakening
the dependency proof. Stable local-function identity already includes the
complete enclosing identity, so the artifact now serializes local binding and
captured-binding maps alongside top-level records. Before skipping a parent,
the compiler recursively verifies that every local descendant has a matching
record from the same exact module. It applies no state until the complete tree
is proven, then restores all maps together and skips the parent's recursive
`ValidateUserFunction` operation.

The schema also has an explicit optional main record: executable module identity
plus canonical binding types. Probe exposes it only when the current executable
module digest matches. The compiler additionally requires the same visible
declaration fingerprint used for functions. Main records with resolved generic
or specialized calls are not published yet, so restoring main cannot omit a
required specialization object or object-keyed call mapping.

This follows rustc's cache-promotion principle: a green parent carries the
green nested results that were not independently demanded in the current
session. It also follows Salsa's tracked-output ownership rule by making the
parent query responsible for its local results instead of allowing unrelated
partial restoration.

The focused consumer now contains a local identity function. A provider
body-only edit proves restoration of that parent/local tree and main. The Linux
measurement reports `reused 43/45 functions; main reused` plus `2/5` LLVM
units. A public-interface edit reports zero function reuse and a rebuilt main.
Corruption, private changes, exact hits, native execution, and LLVM byte
equality remain covered by the ten-state Windows/Linux matrix.

Both full suites pass 573/573. Windows Stage2 passes 6/6 at 10,553,582 bytes;
Linux Stage2 passes 5/5 at 10,550,185 bytes. D207C reaches **4.75/5 (95%)**,
the periodic Stage3 cadence is **5/10**, and the formal roadmap remains
**47 complete, 10 partial, 3 missing: 52.0/60 (86.7%)**. Stable syntax-node and
specialization reconstruction is the remaining final slice.

References:

- [rustc incremental cache promotion](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation-in-detail.html)
- [Salsa tracked functions as reuse units](https://salsa-rs.github.io/salsa/tutorial/parser.html)

## D207C5D - Stable Syntax Calls and Specialization Rehydration

Status: Windows/Linux full suites and Stage2 verified; D207C 5/5 complete
Date: 2026-07-20

The final D207C boundary cannot use an ordinal over only already-resolved calls:
that set does not exist until after the work the cache is intended to skip.
Schema 4 instead indexes every potential call node before body validation.
Ordinary `CallExpression`, `TypeApplicationExpression`, fluent `FlowTarget`,
and `BlockFunctionCallStatement` nodes receive deterministic owner-local source
ordinals. The persisted resolved subset can therefore locate the corresponding
objects in a fresh AST without consulting session-local object addresses.

Each non-declared target has a canonical recipe. User and runtime generic
specializations reference the stable identity of their current declaration and
record primary, secondary, tertiary, input, and compile-time value arguments as
structural types. Synthesized file operations record their concrete signature
and runtime kind. Loading re-interns those types, recreates the specialization
without revalidating its body, verifies the resulting complete function
identity, restores cached user-specialization bindings, and follows nested
template-owned resolved edges. Direct declared async targets resolve through
the current declaration identity and need no recipe.

Restoration is transactional. A function first materializes its complete local
tree. Call reconstruction snapshots the existing specialization dictionary and
resolved-call map; a missing syntax node, stale template, unavailable exact-
module specialization payload, malformed structural type, or identity mismatch
removes newly created targets and restores the prior map. Main uses the same
path. Storage placement still runs over the current AST after semantic reuse.

The focused matrix exercises nested type specialization, compile-time value
specialization, a local function, main-scope specialization, and synthesized
`readAt<UInt16>` state. A body-only dependency edit restores 5/5 call sites;
Windows reuses 44/45 functions plus main and Linux reuses 44/46 plus main. Both
ten-state target matrices, both 573/573 full suites, Windows Stage2 6/6 at
10,553,582 bytes, and Linux Stage2 5/5 at 10,550,185 bytes pass. D207C is
**5/5 (100%)**, periodic Stage3 is **6/10**, and the formal roadmap becomes
**48 complete, 9 partial, 3 missing: 52.5/60 (87.5%)**.

The reference backend intentionally continues to lower the current AST with
rehydrated semantic side tables. This checkpoint claims dependency-safe body
validation reuse, not a separate serialized monolithic typed-AST format.

References:

- [rustc stable cross-session query fingerprints](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation-in-detail.html)
- [Salsa tracked functions and backdating](https://salsa-rs.github.io/salsa/reference/algorithm.html)
- [Swift incremental request architecture](https://www.swift.org/blog/swift-5.2-released/)

## D208A - Confined Path-Only Local Workspaces

Status: implemented; Windows/Linux full suites and Stage2 verified
Date: 2026-07-20

Sollang workspaces use a language-shaped `sollang.workspace` manifest with one
explicit, non-empty `members` array. The manifest does not repeat package names
or products: each member's `sollang.project` is the single authority for that
metadata. This prevents a second name map from drifting away from the project
graph while keeping the workspace concise.

Member paths are normalized relative to the workspace root and may not escape
it. Resolved paths and package names must be unique. Building a workspace with
more than one member requires `--package`; building from inside a member uses
ancestor discovery to select that member. Every dependency reachable from the
selected project must be declared in the workspace. Outputs are isolated by
target, package, and product, and the workspace manifest participates in the
incremental input identity.

The self-host boundary is `selfhost/workspace.slg`. It parses the same token
shape and reports explicit status codes without depending on the C# manifest
reader. Examples 437 and 438 plus six negative diagnostics cover the complete
local contract. The test runner additionally separates native and wasm LLVM
temporary stems; this removes a cache-sensitive false failure where a wasm
`.ll` file could overwrite a native `.ll` file with the same base name.

Validation: Release build has zero warnings and errors; Windows and Linux each
pass 581/581; Windows Stage2 passes 6/6 at 10,590,477 bytes; Linux Stage2 passes
5/5 at 10,587,080 bytes. This is Stage3 cadence checkpoint 7/10. Versioned
resolution, registries, Git sources, and lock-file reproducibility remain
separate D208 work.

References:

- [Cargo workspaces](https://doc.rust-lang.org/cargo/reference/workspaces.html)
- [SwiftPM package dependencies](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/addingdependencies/)
- [Zig build system](https://ziglang.org/learn/build-system/)

## D208B - Versioned Local Identity And Deterministic Workspace Lock

Status: implemented; Windows/Linux full suites and Stage2 verified
Date: 2026-07-20

Every `sollang.project` declares a canonical SemVer 2.0.0 `version`, making the
local package identity `name@version` instead of a path or an unversioned name.
Dependencies may use the concise legacy path string for an unconstrained local
edge or an explicit `{ path, version }` record. Requirements support `*`, exact
and `=` versions, compatible `^` and `~` ranges, and whitespace-separated
`>`, `>=`, `<`, and `<=` intersections. Resolution verifies the declared
dependency version before source discovery.

One workspace owns one checked-in `sollang.lock`. `sollang resolve` renders the
complete member graph in canonical package-identity order with normalized
`path:` sources and exact dependency identities, then replaces the file
atomically only when its bytes differ. Workspace builds keep that shared lock
current; `--locked` changes mismatch or absence into an error. A standalone
project writes a lock only when explicitly resolved, avoiding incidental files
for one-off project builds and diagnostic fixtures. The lock joins the
incremental frontend identity whenever it exists.

The self-host boundary is split into `selfhost/package_versions.slg` and
`selfhost/package_lock.slg`. The former parses SemVer and evaluates the same
requirement families, including prerelease precedence. The latter tokenizes the
canonical lock shape and validates package/dependency identities, local source
tags, and duplicate package IDs. Examples 440 and 441 exercise these contracts
without delegating their parsing to the C# manifest reader.

The lock intentionally does not hash mutable local source trees: source control
is the authority for local workspace bytes. Registry archives, Git revisions,
content hashes, signing, and multi-version remote resolution remain later D208
layers. This follows Cargo's one-lock-per-workspace reproducibility, SwiftPM's
resolved exact versions after constraint solving, Go's explicit module-version
identity, and Zig's content-addressed direction without pretending that local
paths are immutable packages.

References:

- [Cargo workspaces](https://doc.rust-lang.org/stable/cargo/reference/workspaces.html)
- [SwiftPM resolving package versions](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/resolvingpackageversions/)
- [SwiftPM adding dependencies](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/addingdependencies/)
- [Go modules reference](https://go.dev/ref/mod)
- [Zig 0.11 package management notes](https://ziglang.org/download/0.11.0/release-notes.html)

Validation: Release build has zero warnings and errors; Windows and Linux each
pass 590/590; Windows Stage2 passes 6/6 at 10,752,017 LLVM text bytes; Linux
Stage2 passes 5/5 at 10,748,620 LLVM text bytes. The Windows native Stage2
executable is approximately 1.4 MiB; the 10.8 MB measurement is generated LLVM
text, not executable size. This is Stage3 cadence checkpoint 8/10.

## D208C - Full-Revision Git Dependencies With Source-Tree Digests

Status: implemented; Windows/Linux full suites and Stage2 verified
Date: 2026-07-20

Git dependencies use `{ git, rev, version }`. `rev` is mandatory and must be a
full 40- or 64-hex-digit commit ID; mutable branch/tag names and abbreviated
IDs are rejected. The declared version remains an independent compatibility
check against the fetched package manifest.

The compiler keeps bare repositories and immutable materializations under the
project or workspace `.sollang/dependencies` directory. It fetches the exact
revision, verifies the resolved commit identity, checks out source bytes without
embedding `.git`, rejects symbolic links, and computes SHA-256 over sorted
relative paths, encoded path lengths, file lengths, and file bytes. Lock format
2 records `git:<location>#<revision>` plus `sha256:<digest>`; a mutated cache is
an error instead of an implicit repair. Relative path dependencies inside the
Git tree are confined to that materialized root and inherit the same revision
and checksum identity.

This combines Cargo's explicit Git revision and lock semantics with Go's
content verification and SwiftPM's revision fingerprint principle. Sollang is
stricter at the manifest boundary: only immutable full commit identities are
accepted. Registries, signed indexes, and archive distribution remain D208D.

References:

- [Cargo Git dependencies](https://doc.rust-lang.org/cargo/reference/specifying-dependencies.html#specifying-dependencies-from-git-repositories)
- [Go module authentication](https://go.dev/ref/mod#authenticating)
- [Swift package security](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/packagesecurity/)

Validation: Release build has zero warnings and errors; Windows and Linux each
pass 594/594. Windows Stage2 passes 6/6 at 10,772,923 LLVM text bytes; Linux
Stage2 passes 5/5 at 10,769,526 bytes. This advances the periodic Stage3 cadence
to 9/10, so Stage3 remains deferred until the next Stage2 checkpoint.

## D208D - Sparse Static Registry With Lock-Preserved Resolution

Status: implemented; Windows/Linux full suites, Stage2, and Stage3 verified
Date: 2026-07-20

Sollang uses an explicit `{ registry, version }` source rather than a global
ambient default. Version discovery reads only `v1/<package>/index.slg`; package
bytes come from `v1/<package>/<version>.zip`. This keeps the server implementable
as static HTTPS storage and lets the Sollang-written compiler parse the same
language-shaped index without adding a second JSON grammar.

The resolver selects the highest compatible non-yanked version, excluding
prereleases unless the requirement names one. A normal build reuses a compatible
lock pin and can work from its verified cache without querying the index.
`sollang resolve` is the sole update operation and intentionally ignores old
registry pins. `--locked` requires an exact compatible registry source, version,
and checksum.

The index-provided SHA-256 authenticates exact ZIP bytes before extraction.
Extraction rejects traversal, absolute/backslash paths, symbolic links,
case-insensitive collisions, excessive entries, and archive/expanded byte-limit
violations. Materialized trees are independently hashed so cache mutation is an
error. Nested path packages remain inside the archive and inherit its registry
identity.

This combines Cargo's sparse per-package index and yanked/checksum fields with
Go's static proxy/cache and deterministic content verification. SwiftPM's
fingerprint model reinforces the rule that a previously resolved package cannot
silently change. The read protocol is normative in `docs/PACKAGE_REGISTRY.md`;
publishing, credentials, signing, and transparency are explicitly separate
server/tooling work.

References:

- [Cargo registry index](https://doc.rust-lang.org/cargo/reference/registry-index.html)
- [Cargo registries](https://doc.rust-lang.org/cargo/reference/registries.html)
- [Go module proxy and authentication](https://go.dev/ref/mod)
- [Swift package security](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/packagesecurity/)

Validation: Release build has zero warnings and errors; Windows and Linux each
pass 600/600. Windows Stage2 passes 6/6 at 10,851,049 LLVM text bytes; Linux
Stage2 passes 5/5 at 10,847,652 bytes. The scheduled Stage3 gate regenerates
10,851,049 bytes and matches Stage2 at normalized SHA-256
`0A8E471CCCC2A97895537FB6279DC84579D052AD4AFECBAA03BDFBA4794FE0DD`.
The ten-checkpoint cadence resets to 0/10.

## D209A - Treat Owned Array Index Assignment As Place Replacement

Status: implemented and cross-target verified
Date: 2026-07-20

An indexed assignment into an owned dynamic array is a place replacement, not a
bitwise overwrite. The replacement is established as an owned temporary, the
index is bounds checked, the previous element is loaded and destroyed through
its canonical recursive drop witness, and the new value is stored. A named
source transfers exactly once and becomes unavailable; a fresh aggregate can be
stored directly. The containing array cannot be used as its own replacement.

This follows Rust's specified assignment order at the ownership boundary: the
old initialized place is dropped before the new value occupies it. It also
matches Mojo's unique-owner rule and deterministic destructor model. Sollang
keeps the operation fully static: concrete array specialization selects the
layout and `sollang_drop_t<ID>` function, so values gain no runtime type tag or
per-element witness pointer.

The reference semantic pass now transfers owned sources for index assignment,
and the LLVM backend loads/drops the previous concrete element before storing.
The self-host expression passes assign `Unit` to both index and member
assignment, typed IR records kind-24 owned replacement as a move event, and the
self-host LLVM emitter invokes the same recursive witness. The test also exposed
that main-entry emission scheduled struct constructors but did not lower them;
main now has the same canonical field construction and numeric conversion path
as ordinary functions.

Examples 446 and 447 cover reference and self-host replacement. The generated
self-host LLVM contains the previous-element load, recursive drop call, and new
store, then assembles, links, and executes on both targets.
`scripts/verify-owned-indexed-replacement.ps1` instruments reference and
self-host Linux modules with ASan/UBSan and verifies leak, double-free, and UB
freedom. Release builds report zero warnings and errors; Windows/Linux full
suites pass 602/602. Windows Stage2 passes 6/6 at 10,904,470 LLVM text bytes;
Linux Stage2 passes 5/5 at 10,901,073 bytes. Formal progress remains 53/60
(88.3%) because generic dictionary replacement and wider container/borrow work
remain. Stage3 cadence advances to 1/10.

References:

- [Rust assignment expressions](https://doc.rust-lang.org/stable/reference/expressions/operator-expr.html#assignment-expressions)
- [Rust `mem::replace`](https://doc.rust-lang.org/std/mem/fn.replace.html)
- [Mojo ownership](https://docs.modular.com/mojo/manual/values/ownership/)

## D209B - Preserve Dictionary Keys And Replace Owned Values Exactly Once

Status: implemented and cross-target verified
Date: 2026-07-20

Mutable generic-dictionary indexing is an occupied-entry replacement. The
right-hand value is established first, the key is looked up without taking its
ownership, a missing key traps, the previous value is recursively destroyed,
and the replacement is stored in the existing value slot. The stored key is
never rewritten. Named replacement owners transfer once and become
unavailable.

Generic `put` now shares the same occupied-entry primitive. On an existing key,
the canonical stored key is retained, the old value is destroyed, and the new
value is installed. The backend also destroys an incoming equal key whenever
its admitted concrete key type owns storage; admitting fully owned key types is
a later gate. On a vacant entry, incoming key/value storage transfers into the
dictionary. This repairs the previous raw-overwrite leak and makes insertion
and replacement ownership explicit.

The reference semantic pass accepts concrete `Dictionary<K, V>` index
assignment with contextual struct keys/values and removes transferred owners.
The reference LLVM backend uses the SwissTable slot search and replaces only
the value field. The self-host LLVM backend performs the equivalent operation
over its current canonical parallel key/value representation and emits the
specialized `sollang_drop_t<ID>` witness before the store.

Examples 448 and 449 cover reference and self-host indexed replacement; example
450 covers owned `put` update and insertion. Two diagnostics prove use-after-move
rejection. `scripts/verify-owned-dictionary-replacement.ps1` instruments all
three Linux products with ASan/UBSan and passes leak, double-free, and UB
detection. Release builds have zero warnings and errors; Windows/Linux full
suites pass 607/607. Windows Stage2 passes 6/6 at 10,962,922 LLVM text bytes;
Linux Stage2 passes 5/5 at 10,959,525 bytes. Formal progress remains 53/60
(88.3%) because owned-key generality and wider path-sensitive container borrows
are not yet complete. Stage3 cadence advances to 2/10.

References:

- [Rust `HashMap::insert`](https://doc.rust-lang.org/std/collections/hash_map/struct.HashMap.html#method.insert)
- [Rust `HashMap::Entry`](https://doc.rust-lang.org/stable/std/collections/hash_map/enum.Entry.html)
- [Swift `Dictionary.updateValue`](https://developer.apple.com/documentation/swift/dictionary/updatevalue(_:forkey:))

## D210A - Borrow Owned Dictionary Keys Through Hash And Eq

Status: implemented and cross-target verified for local nominal keys
Date: 2026-07-20

A dictionary key may now own recursively managed storage. Admission still
requires the concrete key type to implement both `Hash` and `Eq`; ownership no
longer rejects the type before those semantic witnesses are considered. A
dictionary literal or vacant `put` transfers the key exactly once. Lookup,
indexed replacement, and `take` pass stored and query keys to readonly `self`
methods without consuming either key. Removing an entry destroys only the
stored key, while the independent query owner remains valid.

The reference semantic pass recursively discovers transfers inside struct,
array, and dictionary literals, rejects duplicate aggregate ownership, and the
LLVM backend removes those source owners from cleanup state. The self-host
type-ID pass derives a method's synthetic `self` from its impl target, typed IR
lowers impl methods as ordinary functions, and LLVM lookup/replacement/take
uses the canonical key type and the resolved `Eq.eq` witness in function,
main-entry, and nested-region emission.

This work exposed two independent parser/module fidelity defects. Impl bodies
now permit newlines between methods and the closing brace. Qualified lookup no
longer lets a top-level `impl SourceSpan` shadow the public `SourceSpan` type
whose name it repeats. Example 453 permanently covers an imported struct with
an inherent impl. Explicit imported dictionary-key composite types remain a
separate module-type inference gate and are not claimed here.

Examples 451 and 452 cover reference and self-host owned keys, including
literal transfer, readonly lookup, indexed update, `take`, post-borrow member
access, nested-region lookup, and recursive key destruction. Two diagnostics
prove use-after-move rejection. `scripts/verify-owned-dictionary-keys.ps1`
runs both Linux LLVM products under ASan/UBSan with leak detection and passes.
Release builds have zero warnings and errors; Windows/Linux full suites pass
612/612. Windows Stage2 passes 6/6 at 11,249,244 LLVM text bytes and Linux
Stage2 passes 5/5 at 11,245,847 bytes. Formal progress remains 53/60 (88.3%)
because imported composite-key inference and wider path-sensitive container
borrows remain. Stage3 cadence advances to 3/10.

The design follows Rust's rule that a map owns its stored key while lookup may
borrow a compatible key, and Swift's rule that custom dictionary keys satisfy
hashing and equality together. Sollang keeps these witnesses statically
specialized and adds no runtime type tag or per-key witness pointer.

References:

- [Rust `Borrow`](https://doc.rust-lang.org/std/borrow/trait.Borrow.html)
- [Swift `Hashable`](https://developer.apple.com/documentation/swift/hashable)
- [Swift `Dictionary`](https://developer.apple.com/documentation/swift/dictionary)

## D210B - Canonical Imported Dictionary Key Types Across Modules

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

Imported nominal dictionary keys now retain declaration identity through both
typed literals and function signatures. The explicit forms
`{keys.OwnedKey: Int; key: value}` and `{keys.OwnedKey: Int}` parse the key and
value as recursive type annotations rather than assuming one-token type names.
The generated grammar accepts typed nonempty entries after the semicolon, and
the C# parser uses speculative recursive type parsing to preserve its ordinary
dictionary-entry ambiguity boundary.

The self-host expression type-ID pass constructs the literal's dictionary ID
directly from its two `TypeAnnotation` children. Composite projection consumes
the already prepared canonical type arena, so a module alias spelling maps to
the imported declaration's module/symbol identity without repeating semantic
analysis. Reference admission and LLVM dispatch recognize the defining
module's `Hash` and `Eq` witnesses while still allowing the root traits used by
existing programs. No runtime type tag, witness table, or erased key ABI is
introduced.

Examples 454 and 455 cover self-host and reference compilation of an imported
owned key in a typed literal and `{keys.OwnedKey: Int}` function contract. They
exercise readonly lookup, occupied replacement, `take`, static `Eq.eq` calls,
and recursive destruction. The owned-key sanitizer gate now instruments all
four local/imported reference/self-host LLVM products with ASan/UBSan and leak
detection. Release builds report zero warnings and errors; Windows and Linux
full suites pass 614/614. Windows Stage2 passes 6/6 at 11,278,354 LLVM text
bytes and Linux Stage2 passes 5/5 at 11,274,957 bytes. Formal progress remains
49 complete, 8 partial, and 3 missing: 53/60 (88.3%), because this closes a
documented sub-gate rather than one of the 60 top-level capability gates.
Stage3 cadence advances to 4/10.

The design follows Rust's distinction between source aliases and canonical
item paths, while keeping qualified paths valid in type positions. Swift's
dictionary type likewise composes arbitrary types and requires every key to
conform to `Hashable`; Swift module access control makes imported public types
part of the consumer's usable API. Sollang keeps its own dot-qualified import
syntax and statically specializes the resolved witnesses.

References:

- [Rust paths and canonical paths](https://doc.rust-lang.org/reference/paths.html)
- [Rust trait implementation coherence](https://doc.rust-lang.org/stable/reference/items/implementations.html)
- [Swift dictionary types](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/types/)
- [Swift module access control](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/accesscontrol/)

## D211A - Call-Scoped Indexed Place Borrows

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

An indexed array element or dictionary value that recursively owns storage is
not an ordinary copied expression. It is a place owned by its container.
Sollang now permits that place only as the direct argument of a default
readonly function input. The borrow ends with the call expression, so the
container remains the sole owner and may subsequently replace or extract the
element. Binding, returning, storing, or mutating through the indexed result is
rejected. `take` is the separate operation that moves the value out and clears
the source slot.

The C# semantic compiler applies one call-input context to both array and
dictionary indexing. Outside that context it reports that the owned element may
only be borrowed directly for a readonly call. The self-host LLVM path passes
the loaded aggregate under the same readonly ABI and does not schedule a drop
for the temporary argument. Consequently the call has no hidden copy and no
second owner.

The owned dictionary test also found an LLVM declaration-order defect that was
previously latent. A nominal struct containing `Text` can cause its drop helper
to use the struct in a sized GEP before `%sollang.text` has been completed. The
self-host emitter now detects only an active nominal drop type with a direct
owned `Text` field and emits the text type before that closure. All other
programs keep the previous ordering, avoiding broad snapshot churn.

Examples 456 and 457 exercise repeated readonly borrows, replacement, `take`,
output, and recursive destruction in the reference and self-host LLVM paths.
The dictionary escape diagnostic proves that a borrow cannot be bound beyond
the call. `scripts/verify-call-scoped-container-borrows.ps1` compiles and runs
both products under ASan/UBSan with leak detection. Release builds report zero
warnings and errors; Windows and Linux full suites pass 617/617. Windows
Stage2 passes 6/6 at 11,285,200 LLVM text bytes and Linux Stage2 passes 5/5 at
11,281,803 bytes. Stage3 cadence advances to 5/10. Formal progress remains 49
complete, 8 partial, and 3 missing: 53/60 (88.3%); stored and returned
references and full path-sensitive conflict analysis remain open.

The design follows Rust's place-expression and borrow model but intentionally
ships a smaller non-escaping lifetime first. Rust's borrow splitting explains
the additional analysis required for simultaneous disjoint paths. Mojo's
origin model similarly ties borrowed references to the lifetime of the owned
storage. Sollang preserves its pipeline syntax while making the ownership
boundary statically visible.

References:

- [Rust place expressions](https://doc.rust-lang.org/stable/reference/expressions.html#place-expressions-and-value-expressions)
- [Rust borrow expressions](https://doc.rust-lang.org/reference/expressions/operator-expr.html#borrow-operators)
- [Rust field borrowing](https://doc.rust-lang.org/reference/expressions/field-expr.html#borrowing)
- [Rust borrow splitting](https://doc.rust-lang.org/nomicon/borrow-splitting.html)
- [Mojo lifetimes, origins, and references](https://docs.modular.com/mojo/manual/values/lifetimes/)

## D211B - Projected Indexed Place Borrows

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

The call-scoped place introduced by D211A now survives field and nested-index
projection. `symbols![key].payload -> inspect`, `symbols![key].name -> len`, and
`(symbols![key].payload)[index] -> inspect` borrow only the selected place for
the direct readonly call. No projected aggregate becomes a second owner. A
binding, return, store, or mutation through that projection is still rejected,
and `take` remains the operation that transfers ownership.

The C# semantic compiler threads the readonly call-input context through field
and index inference. The reference LLVM helper-discovery rule now examines
dictionary key/value ownership directly. This emits the nominal or nested
container drop closure required by a dictionary of owned values without adding
heap cleanup to scalar-only stack-promoted containers.

The self-host main-entry emitter had a narrower index implementation than its
ordinary-function emitter: it handled argument views and dictionaries but not
dynamic arrays. Consequently a nested projected array index was present in
typed IR but had no LLVM definition. Main emission now uses the same dynamic
array data extraction, index widening, GEP, load, alignment, and dependency
ordering as the function path.

Examples 458 and 459 cover projected `Text`, array, and array-element borrows;
the projection-escape diagnostic fixes the lifetime boundary. The expanded
call-borrow verifier runs all four D211 examples on Linux and checks all emitted
LLVM products with ASan/UBSan. Release builds report zero warnings and errors;
Windows and Linux full suites pass 620/620. Windows Stage2 passes 6/6 at
11,297,708 LLVM text bytes and Linux Stage2 passes 5/5 at 11,294,311 bytes.
Stage3 cadence advances to 6/10. Formal progress remains 49 complete, 8 partial,
and 3 missing: 53/60 (88.3%), because stored/returned references and general
path-sensitive conflict analysis remain open.

The design follows Rust's field-place borrowing and structural borrow splitting
while retaining Sollang's conservative non-escaping call lifetime. Mojo's
origin model supports carrying the source lifetime through derived projections.

References:

- [Rust field borrowing](https://doc.rust-lang.org/reference/expressions/field-expr.html#borrowing)
- [Rust place expressions](https://doc.rust-lang.org/stable/reference/expressions.html#place-expressions-and-value-expressions)
- [Rust borrow splitting](https://doc.rust-lang.org/nomicon/borrow-splitting.html)
- [Mojo lifetimes, origins, and references](https://docs.modular.com/mojo/manual/values/lifetimes/)

## D211C - Mixed Postfix Chain Parity

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

Sollang treats field and index access as suffixes of one left-associated postfix
expression. The reference parser therefore consumes `.field`, `[index]`, and
`![index]` in arbitrary order rather than in separate grouped passes. The
self-host frontend keeps pure member paths compact for qualified-name
compatibility, but normalizes any mixed chain containing an index into explicit
prefix AST nodes. Thus `symbols![1].payload![0]` has the same shape and meaning
as `((symbols![1]).payload)![0]` without requiring punctuation that obscures the
place path.

Examples 460 and 461 establish reference/self-host parity for two- and
three-projection chains. The focused six-example D211 verifier passes on Linux,
and all six emitted products are covered by ASan/UBSan leak, double-free,
use-after-free, and undefined-behavior checks.

Stage2 found and constrained a separate scheduling edge in the bootstrapped
compiler: a computed final array index used as a call input could be emitted
after the consuming operation. The normalizer records the last prefix node as
it is created, eliminating that nested recovery expression. Windows/Linux full
suites pass 622/622. Windows Stage2 passes 6/6 at 11,313,892 LLVM text bytes and
a 1,570,304-byte executable; Linux Stage2 passes 5/5 at 11,310,495 LLVM text
bytes and a 3,116,392-byte executable. Stage3 cadence advances to 7/10. Formal
progress remains 49 complete, 8 partial, and 3 missing: 53/60 (88.3%).

The design follows Kotlin's repeated postfix-suffix grammar and Rust's shared
high-precedence, left-to-right field/index expression model while preserving
Sollang's mutable-place `!` and propagation `?` spellings.

References:

- [Kotlin expressions specification](https://kotlinlang.org/spec/expressions.html)
- [Kotlin grammar](https://kotlinlang.org/grammar/)
- [Rust expression precedence and order](https://doc.rust-lang.org/stable/reference/expressions.html)
- [Rust field expressions](https://doc.rust-lang.org/reference/expressions/field-expr.html)
- [Rust array and slice indexing](https://doc.rust-lang.org/reference/expressions/array-expr.html)

## D212A - Concrete Index Operand Dependencies

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

An index expression must depend on the node that actually computes its source
and index values. A transparent flow wrapper is useful for syntax and type
grouping but has no LLVM result of its own. Pointing an index operand at such a
wrapper can therefore produce an undefined SSA value even when the underlying
binary expression is present later in typed IR.

Self-host typed-IR finalization now unwraps both operands of every index node to
their concrete non-Unit value child, including recursively nested wrappers.
This is done before global index type recovery and LLVM scheduling, making the
dependency graph authoritative. The emitter and scheduler remain generic: the
real subtraction is ready and emitted before the consuming GEP because the IR
edge says so, not because text emission recognizes one source spelling.

Examples 462-464 fix the failure-first contract at three levels: reference
execution returns the last element, self-host LLVM assembles and executes, and
the typed-IR snapshot proves that the index operand names the binary node rather
than its wrapper. Windows/Linux full suites pass 625/625. Windows Stage2 passes
6/6 at 11,325,985 LLVM text bytes and a 1,570,816-byte executable; Linux Stage2
passes 5/5 at 11,322,588 LLVM text bytes and a 3,120,488-byte executable. Stage3
cadence advances to 8/10. Formal progress remains 49 complete, 8 partial, and 3
missing: 53/60 (88.3%).

The invariant follows LLVM's SSA dominance requirement, Rust's recursive and
left-to-right operand evaluation rule, and Kotlin's receiver-then-left-to-right
call argument order.

References:

- [LLVM language reference: well-formed SSA](https://llvm.org/docs/LangRef.html#well-formedness)
- [Rust operand evaluation order](https://doc.rust-lang.org/stable/reference/expressions.html#evaluation-order-of-operands)
- [Kotlin function-call evaluation](https://kotlinlang.org/spec/expressions.html#function-calls-and-property-access)

## D213A - Inferred Borrowed Text Return Origins

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

Sollang now permits the first stored and returned borrowed view without adding
lifetime punctuation to the common signature. A function with one default
`SourceText` input and a direct `Text` result derived through `slice` assigns
the input's symbolic origin to the result. A caller can bind that returned
view, and the runtime value remains the existing pointer-plus-length Text with
no allocation, reference count, or hidden owner.

The reference semantic compiler discovers these contracts as a fixed point, so
one borrowed-return helper may delegate to another. Each bound result records
the caller's named owner. Moving, transferring into an aggregate, replacing,
or mutating that owner while the view remains active is rejected. This first
slice uses a deliberately conservative lexical region: the owner stays frozen
until the enclosing scope ends rather than ending the borrow at last use.
Aggregate returns containing borrowed Text remain rejected.

The self-host ownership analyzer derives the same contract from flat typed IR:
a Text-returning function's return subtree must contain the SourceText `slice`
intrinsic rooted at its default parameter. A binding of that call records the
concrete owner-binding IR index, and diagnostic 21 rejects a later move event
against it. The self-host LLVM backend already carries the two-word view
unchanged and executes the positive program. Wiring every ownership diagnostic
into the standalone Stage2 driver remains part of the broader self-host
diagnostic gate; example 468 executes this analyzer directly so the rule is not
merely documented.

Examples 465, 466, and 468 cover reference execution, self-host LLVM
assembly/execution, and self-host origin-conflict analysis. The reference
diagnostic `borrowed-text-origin-move` fixes the owner-freeze boundary. Release
builds have zero warnings and errors; Windows and Linux full suites pass
628/628. Windows Stage2 passes 6/6 at 11,325,985 LLVM text bytes with a
1,570,816-byte executable, and Linux Stage2 passes 5/5 at 11,322,588 LLVM text
bytes with a 3,120,488-byte executable. Stage3 cadence advances to 9/10.
Formal progress remains 49 complete, 8 partial, and 3 missing: 53/60 (88.3%),
because origin unions, multiple borrowed inputs, path-sensitive simultaneous
borrows, last-use regions, and production-driver diagnostic integration remain
inside the incomplete ownership/storage gate.

The design combines Rust's ergonomic lifetime elision rule with Mojo's
symbolic origin model. Rust assigns an elided output lifetime to the single
input lifetime and rejects an ambiguous multi-input result. Mojo explicitly
tracks which variable owns referenced storage and allows return references to
carry an inferred or declared origin. Swift's location, duration, and access
kind model informs the later conflict phase; D213A implements only the
single-origin readonly case.

References:

- [Rust lifetime elision](https://doc.rust-lang.org/reference/lifetime-elision.html)
- [Mojo lifetimes, origins, and references](https://mojolang.org/docs/manual/values/lifetimes/)
- [Swift memory safety and overlapping access](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/memorysafety/)

## D213L - Transitive Mutable Parallel Captures Become Production E18

Status: implemented and fixed-point verified
Date: 2026-07-20

The checked self-host compiler now treats mutable parallel-capture diagnostic
E18 as fatal before LLVM emission. Capture analysis starts from calls inside a
`parallel` or `tryParallel` body, resolves their local function targets in the
canonical typed IR, and walks the local call graph transitively. Name uses in
each reachable helper resolve back to their stable outer binding. A mutable
binding is rejected even for a read-only use because its lexical owner still
permits access to shared mutable storage while workers execute. Recursive call
graphs use a visited set, and repeated uses produce one diagnostic per binding.

This follows Swift's rule that concurrently executing sendable closures cannot
capture mutable variables and Rust's structural rule that closure sendability
is derived from the exact capture modes and captured types. Sollang keeps its
surface concise: the standard `parallel` roles establish this boundary without
adding an annotation to every callback. Immutable structurally sendable values
remain ordinary read-only captures.

The first production Stage2 run rejected six construction-time typed-IR tables
captured by its own per-function parallelizer. Copying those compiler-sized
arrays would have weakened the memory objective. The final implementation moves
each completed mutable builder through a typed freeze helper into an immutable
owner, a zero-copy ownership transition. Worker callbacks borrow only those
frozen names, and the old mutable bindings are unavailable.

Examples 497 and 498 lock direct, transitive, immutable, and checked-driver
behavior with English scenario comments. A dedicated Stage2 fixture proves
that Stage1 and Stage2 both reject E18 before printing an LLVM target header.
Release builds have zero warnings and errors. Windows/Linux full suites pass
**666/666**. Windows Stage2 passes **7/7** with **11,840,360 LLVM bytes**,
**3,496,608 bitcode bytes**, and a **1,650,176-byte executable**. Linux Stage2
passes **6/6** with **11,836,939 LLVM bytes**, **3,494,820 bitcode bytes**, and
a **3,331,320-byte executable**.

The required periodic Stage3 is byte-for-byte equal to Stage2 at **11,840,360
LLVM bytes**, assembles successfully, and has SHA-256
`E0B91E9140B90D04F3417926C80C3B2BE38CF5B35EC975D119757B8C75C2BBF9`.
The cadence resets to **0/10**. Formal progress remains **49 complete, 8
partial, 3 missing: 53/60 (88.3%)** because E19-E20 production precision still
keeps the broader ownership/storage gate partial.

References:

- [Swift sendable closure captures](https://docs.swift.org/compiler/documentation/diagnostics/sendable-closure-captures/)
- [Swift sending closure data-race diagnostics](https://docs.swift.org/compiler/documentation/diagnostics/sending-closure-risks-data-race/)
- [Rust closure capture precision and `Send`/`Sync`](https://doc.rust-lang.org/reference/types/closure.html)

## D213M - Transitive Non-Sendable Parallel Captures Become Production E19

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

D213M promotes non-sendable parallel-capture diagnostic E19 into the checked
self-host compiler. The ownership pass applies one recursive structural
classifier to direct captures and to every local helper reachable from a
`parallel` or `tryParallel` callback. Repeated uses of the same unsafe binding
produce one diagnostic per callback. Arena, Arguments, mapped byte views, and
values containing them are rejected before LLVM emission.

Structured parallel sharing is deliberately distinct from async transfer.
Async owns or transfers a value across a task boundary and keeps the existing
Send-like classifier. A structured parallel callback only borrows immutable
captures and joins before its parent resumes, so `SourceText` is allowed as a
read-only Sync-like view. User types containing `SourceText` become shareable
through the same structural rule; there is no compiler-internal nominal-name
exception that user code could imitate. Mutable captures remain E18 regardless
of their field types.

The first Stage2 proof also exposed a canonical builtin-type gap: a local
`Arena(8)` binding retained its type origin but lost its builtin symbol in the
self-host typed IR. Constructor typing now recognizes Arena and binding/read
propagation preserves builtin owner markers even before a recursive type ID is
available. Examples 499-502 cover direct, transitive, nested, deduplicated, and
checked E19 behavior, canonical Arena typing, and valid immutable SourceText
sharing. Every new source includes English `#` comments explaining its purpose.

The Release build has zero warnings and errors. Windows and Linux full suites
pass **671/671**. Windows Stage2 passes **7/7** at **11,858,370 LLVM bytes**,
**3,501,240 bitcode bytes**, and a **1,652,736-byte executable**. Linux Stage2
passes **6/6** at **11,854,949 LLVM bytes**, **3,499,448 bitcode bytes**, and a
**3,339,560-byte executable**. Both Stage1 and Stage2 reject E17, E18, E19, and
E21 fixtures before emitting a target header.

The required D213L Stage3 reset the periodic cadence to 0/10; D213M advances it
to **1/10**, so no new Stage3 run is due. Formal progress remains **49 complete,
8 partial, 3 missing: 53/60 (88.3%)** because E20 and the remaining ownership
and storage precision still keep the broader gate partial.

Research basis:

- [Swift sendable closure captures](https://docs.swift.org/compiler/documentation/diagnostics/sendable-closure-captures/)
- [Swift sending closure data-race diagnostics](https://docs.swift.org/compiler/documentation/diagnostics/sending-closure-risks-data-race/)
- [Rust closure capture precision and `Send`/`Sync`](https://doc.rust-lang.org/reference/types/closure.html)

## D213N - Branch and Loop Partial-Move Joins Become Production E20

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

D213N promotes partial-move join diagnostic E20 into the checked self-host
compiler. Every normal `if`, `when`, and loop join or back-edge must preserve a
definitely initialized move-path state. Moving an owned field on only one path
therefore blocks LLVM emission. Reinitializing the exact path before the join
repairs the state, while a moving branch that returns does not contribute a
normal successor and needs no artificial repair.

The first production run found eleven false positives in the compiler itself.
They were read-only field projections nested in call-scoped request literals,
not ownership extractions. Kind-13 request projections remain in the move table
for drop planning, but E20 now deinitializes only the same explicit kind-17
extraction sites already recognized by E17. This keeps the drop planner
conservative without making a read-only request construction mutate its owner.

Examples 503 and 504 cover the checked diagnostic, request-literal precision,
branch joins, loop back-edges, and exact-path reinitialization. A dedicated
Stage2 fixture requires both Stage1 and Stage2 to emit E20 with a nonzero exit
before any LLVM target header. The Release build has zero warnings and errors,
and Windows/Linux full suites pass **673/673**. Windows Stage2 passes **7/7**
at **11,860,813 LLVM bytes**, **3,502,048 bitcode bytes**, and a
**1,653,248-byte executable**.
Linux Stage2 passes **6/6** at **11,857,392 LLVM bytes**, **3,500,252 bitcode
bytes**, and a **3,339,560-byte executable**. Both checked drivers now enforce
the complete E17-E21 production diagnostic band.

The Stage3 cadence advances from **1/10** to **2/10**, so no new fixed-point run
is due. Formal progress remains **49 complete, 8 partial, 3 missing: 53/60
(88.3%)**. E20 closes the remaining production-diagnostic sub-boundary, but the
broader ownership/storage gate still lacks a full path-sensitive checker for
stored and returned references and fully generic container ownership.

Research basis:

- [rustc moves and initialization](https://rustc-dev-guide.rust-lang.org/borrow-check/moves-and-initialization.html)
- [rustc move paths](https://rustc-dev-guide.rust-lang.org/borrow-check/moves-and-initialization/move-paths.html)
- [Rust variable initialization](https://doc.rust-lang.org/stable/reference/variables.html)
- [Rust move deinitialization](https://doc.rust-lang.org/stable/reference/expressions.html#move-and-copy-semantics)
- [Rust partial initialization and destructors](https://doc.rust-lang.org/reference/destructors.html)
- [Swift definite initialization](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/declarations/)

## D213K - Reachable Partial Moves Become Production E17

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

The checked self-host compiler now treats diagnostic E17 as fatal before LLVM
emission. An explicit extraction, consuming call, or assignment that moves a
heap-reaching field deinitializes its canonical place. A later whole-owner,
equal-path, or descendant use is rejected; a disjoint sibling remains legal.
Reinitialization restores the path, and a direct return after the move ends
that control-flow path, so a use reached only through the non-moving branch is
not a false positive.

D213K reuses D213J's resolved field identity rather than comparing incidental
AST punctuation tokens. It also distinguishes scalar-only nominal fields,
which are copyable values, from fields whose unique drop responsibility reaches
heap storage. Owned projections nested in readonly request literals remain in
the move table for drop suppression but are not promoted to explicit E17 move
sites. This distinction was required for the self-host compiler's own request
structs and `SourceSpan` values to pass the production gate without weakening
real dynamic-array field moves.

The first Stage2 attempt exposed a fixed-point-only regression: factoring the
return test into a new local helper made the Stage2-generated analyzer suppress
both E17 and E21. The final form preserves the already-proven shared
reachability helper for E21 and performs return-path termination locally in the
E17 scan. Stage1 and Stage2 now reject both diagnostics identically.

Examples 495 and 496 cover invalid whole-owner reuse, legal sibling access,
returning-branch reachability, and checked output/exit behavior. Their English
`#` comments state each invariant. A dedicated Stage2 fixture proves that E17
terminates both production compiler generations before any target header.

Release builds have zero warnings and errors; Windows/Linux full suites pass
**664/664**. Windows Stage2 passes **7/7** with **11,793,906 LLVM bytes**,
**3,483,864 bitcode bytes**, and a **1,647,104-byte executable**. Linux Stage2
passes **6/6** with **11,790,485 LLVM bytes**, **3,482,072 bitcode bytes**, and
a **3,323,008-byte executable**.

The periodic Stage3 cadence advances to **9/10**; no Stage3 run is due at this
checkpoint. Formal progress remains **49 complete, 8 partial, 3 missing:
53/60 (88.3%)** because E18-E20 production precision still leaves the broader
ownership/storage gate partial.

References:

- [rustc move paths](https://rustc-dev-guide.rust-lang.org/borrow-check/moves-and-initialization/move-paths.html)
- [Rust moved place expressions](https://doc.rust-lang.org/stable/reference/expressions.html#move-and-copy-semantics)
- [Rust partial moves](https://doc.rust-lang.org/nightly/rust-by-example/scope/move/partial_move.html)
- [Rust destructors and partial initialization](https://doc.rust-lang.org/reference/destructors.html)
- [Swift borrowing and consuming parameters](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/declarations/)

## D213J - Borrow Conflicts Use Canonical Projected Places

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

Sollang now distinguishes borrowed origins by canonical place path rather than
collapsing every projection to its root owner. Whole-owner access, equal paths,
and prefix-related paths overlap. Different stored struct fields and unequal
compile-time numeric array indices are disjoint at their first difference.
Runtime indices and other unproven projection pairs remain conservatively
overlapping. This permits useful code such as retaining a view from
`sources![0]` while extracting `sources![1]`, without weakening rejection of an
equal or dynamic index.

The C# semantic compiler recursively constructs places from name, field, and
index expressions and invalidates the exact assigned projection. The self-host
analyzer reconstructs the same path from typed IR, retains it across borrowed
aliases, and recognizes `take(index)` from the binding AST because that move is
flattened by the current typed IR. The comparison is allocation-free and adds
no source syntax, runtime metadata, or ABI field.

Examples 491-494 cover disjoint stored fields, unequal constant indices,
self-host E21 analysis, and self-host LLVM execution. The two diagnostics cover
an equal field and a dynamic index, and the Stage2 fixture covers a nested
index-and-field place. These examples include English `#` comments that state
the invariant each source verifies; new examples follow that executable-
documentation convention by default.

Release builds have zero warnings and errors; Windows/Linux full suites pass
**662/662**. Windows Stage2 passes **7/7** with **11,795,808 LLVM bytes**,
**3,483,932 bitcode bytes**, and a **1,645,056-byte executable**. Linux Stage2
passes **6/6** with **11,792,387 LLVM bytes**, **3,482,144 bitcode bytes**, and
a **3,318,912-byte executable**. Stage1 and Stage2 both reject single, union,
transferred, aggregate, and projected-origin E21 before LLVM emission.

The periodic Stage3 cadence advances to **8/10**; no Stage3 run is due at this
checkpoint. Formal progress remains **49 complete, 8 partial, 3 missing:
53/60 (88.3%)** because production precision for E17-E20 still leaves the
broader ownership/storage gate partial.

References:

- [Rust field expressions](https://doc.rust-lang.org/reference/expressions/field-expr.html)
- [Rust borrow splitting](https://doc.rust-lang.org/nomicon/borrow-splitting.html)
- [rustc place-conflict analysis](https://doc.rust-lang.org/stable/nightly-rustc/rustc_borrowck/places_conflict/index.html)
- [Mojo lifetimes and origins](https://docs.modular.com/mojo/manual/values/lifetimes/)
- [Swift memory safety](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/memorysafety/)

## D214 - Explicit Readonly `ref T` with Inferred Origins

Status: first C# reference-compiler vertical slice implemented; lifetime and
self-host completion tracked below
Date: 2026-07-20

Sollang uses `ref T` for a long-lived readonly reference. The `ref` marker is
kept because returning or storing an alias is materially different from copying
or moving `T`; ordinary callers do not write lifetime parameters. A function
such as `first pair: ref Pair -> ref Int` borrows an addressable `Pair`, returns
the address of `pair.first`, and lets the caller read the result as an `Int`
through transparent readonly dereference.

The first vertical slice is deliberately real rather than syntactic sugar.
`ref T` has its own parametric semantic type, pointer-sized layout, stable type
identity, LLVM `ptr` parameter and result ABI, reference runtime value, field
place GEP, and load-on-read behavior. A returned place must be rooted in a
reference input; literals, temporaries, and locals owned only by the callee are
not valid return origins. Until the CFG owner-lock is implemented, this slice
accepts only immutable owners whose `T` contains no owned storage; mutable and
heap-owning owners are rejected instead of receiving an unsound partial rule.
Example 505 executes this ABI end to end.

Single-input origins will be inferred from the referenced parameter. Multiple
possible input origins will form a conservative union. Mutation, move, drop,
and replacement of an owner must remain blocked until every reachable
reference has passed its CFG last use. References stored in user structs need
the same origin metadata without a runtime lifetime object. Async and parallel
escape are rejected until Send/Sync and suspension lifetime proofs exist.

Completion checklist:

- [x] `ref T` grammar, parser, semantic type, formatting, visibility, and stable identity
- [x] pointer-sized C# reference-compiler layout and LLVM parameter/result ABI
- [x] named owner borrowing, returned struct-field place, and transparent readonly load
- [x] conservative rejection of mutable and owned-storage owners in the first slice
- [ ] indexed and nested aggregate places
- [ ] explicit early-return, branch, loop, and union-origin contracts
- [ ] owner mutation/move/drop conflicts through CFG last use
- [ ] references stored in user values
- [ ] generic substitution and trait interactions verified by examples
- [x] Sollang self-host type arena, typed-IR reference projection, pointer ABI,
  projected field address, and transparent return load
- [x] Sollang self-host caller-side address formation, executable
  returned-reference path, and production temporary-origin rejection
- [ ] complete Sollang self-host CFG origin/liveness conflict enforcement
- [x] Windows/Linux regression suites and the required Stage2 checkpoint

The design combines Mojo's explicit `ref` surface and inferred origins with
Rust's return-lifetime relationships and Swift's exclusive-access rule. The
formal roadmap remains 53/60 (88.3%) until the general returned/stored-reference
gate, including self-host parity, is complete.

Checkpoint validation is a zero-warning Release build, Windows/Linux full
suites at 677/677, Windows Stage2 at 7/7 with 11,862,180 LLVM bytes, and Linux
Stage2 at 6/6 with 11,858,759 LLVM bytes. Stage3 cadence remains 2/10 because
this checkpoint does not claim self-host `ref T` implementation parity.

The D215 self-host vertical slice adds reference kind 8 to the recursive type
arena, preserves it through expression typing and typed IR, and emits pointer
parameters/results, projected field GEPs, and transparent loads when a reference
is consumed as a value. Example 506 verifies canonical interning, typed member
identity, non-owning classification, and 64/32-bit pointer layouts. Example 507
assembles the generated LLVM for `ref Pair -> ref Int` field projection and
`ref Int -> Int` transparent read. The remaining self-host work is caller-side
automatic address formation plus the same origin and owner-lock rules enforced
by the C# compiler; therefore the general gate and formal score do not advance.

D215 validation is a zero-warning Release build, Windows/Linux full suites at
679/679, Windows Stage2 at 7/7 with 11,910,020 LLVM bytes, and Linux Stage2 at
6/6 with 11,906,599 LLVM bytes. The periodic Stage3 cadence advances to 3/10,
so Stage3 is not due.

References:

- [Mojo lifetimes, origins, and references](https://docs.modular.com/mojo/manual/values/lifetimes/)
- [Rust lifetime elision](https://doc.rust-lang.org/stable/reference/lifetime-elision.html)
- [Swift memory safety](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/memorysafety/)

## D213I - Aggregate Values Carry Inferred Borrow-Origin Unions

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

Sollang infers borrowed origins recursively through returned structs, fixed and
growable arrays, and dictionaries. No lifetime syntax or runtime field is
added. A returned aggregate receives the union of every reachable borrowed
`Text`, and a field or index projection keeps that union live until its final
CFG-reachable use. Explicit early returns contribute to the same function
contract.

SourceText parameters introduce concrete caller-owner origins. By contrast, a
parameter whose type merely contains `Text` propagates only origins already
carried by its argument. This distinction is required so moving an array of
static string literals through `forwardValues` remains legal, while moving an
array of slices preserves the underlying SourceText owner set. Aggregate
forwarding therefore transfers metadata without confusing the container owner
with the storage referenced by its elements.

The self-host analyzer classifies canonical types bottom-up because semantic
type children precede their owners. Contract discovery is restricted to the
actual canonical or explicit return operand, preventing ordinary parameter
uses elsewhere in a compiler function from becoming false return origins.
Borrow rows are propagated transitively when a moved aggregate is returned by
another function. This stays allocation-free and keeps Windows/Linux behavior
identical.

Examples 483-490 and the aggregate diagnostic cover reference execution,
self-host analysis, self-host LLVM, aggregate forwarding, and explicit early
returns. Stage2 adds the aggregate fixture to its production E21 gate. Release
builds have zero warnings and errors; Windows/Linux full suites pass
**656/656**. Windows Stage2 passes **7/7** with **11,727,474 LLVM bytes**,
**3,465,668 bitcode bytes**, and a **1,638,400-byte executable**. Linux Stage2
passes **6/6** with **11,724,053 LLVM bytes**, **3,463,876 bitcode bytes**, and
a **3,298,104-byte executable**.

The periodic Stage3 cadence is **7/10**. Formal progress remains **49 complete,
8 partial, 3 missing: 53/60 (88.3%)**; disjoint projected-borrow precision and
production E17-E20 precision remain open within the broader ownership gate.

References:

- [Rust lifetime elision](https://doc.rust-lang.org/stable/reference/lifetime-elision.html)
- [Rust references stored in structs](https://doc.rust-lang.org/book/ch10-03-lifetime-syntax.html)
- [Mojo lifetimes, origins, and references](https://docs.modular.com/mojo/manual/values/lifetimes/)
- [Swift memory safety](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/memorysafety/)

## D213H - Collection-Argument ABI Integrity at the Stage3 Fixed Point

Status: implemented, cross-target Stage2 verified, and Stage3 fixed point verified
Date: 2026-07-20

The voluntary Stage3 probe after D213G exposed two independent self-host LLVM
ABI defects. First, a growable array literal made only from binding reads could
disappear from typed IR when shallow expression inference had not assigned the
literal itself a type. Its element reads then became separate call arguments.
LLVM opaque pointers allowed that direct-call arity/type mismatch to assemble,
and the callee interpreted source text bytes as an array header. Typed lowering
now preserves every array-literal node and infers a non-empty growable array's
canonical type from its first typed element.

Second, short-circuit `while`-style boolean emission scheduled and printed only
the first operand of an ordinary function call. A call nested under `and`,
`or`, or `not` could therefore target a multi-parameter function with missing
arguments. The while-value scheduler now visits the complete canonical
`operand0`/`nextOperand` chain, and its call writer emits every argument with
the correct while-local value spelling. Text literals in that chain are
written as inline two-word Text constants instead of undefined while-local SSA
names.

Examples 481 and 482 lock both boundaries. Example 481 passes two bound Text
values as one growable array value and executes `count=2`. Example 482 emits a
three-argument call, including a Text literal, from a nested short-circuit
boolean expression. Both self-host LLVM products assemble, execute, and agree
with the C# reference compiler. The original example 480 is also compiled by
the rebuilt Stage2 executable and again reports
`reassigned cfg conflicts=0,1,1,1,1,1`.

The Release build has zero warnings and errors. Windows and Linux full suites
pass **648/648**. Fresh Windows Stage2 passes **7/7** at **11,698,851 LLVM
bytes**, **3,457,924 bitcode bytes**, and a **1,637,376-byte executable**.
Fresh Linux Stage2 passes **6/6** at **11,695,430 LLVM bytes**, **3,456,132
bitcode bytes**, and a **3,294,008-byte executable**. A voluntary Stage3 run
regenerates **11,698,851 identical LLVM bytes** and passes fixed-point hash
`07281B4A9C220FC5C49A474705F9108EA64FE2E8F0C159CF2EF7A1DA55E8A75D`.

This is Stage2 checkpoint **6/10**. The early Stage3 proof does not reset the
periodic cadence. Formal progress remains **49 complete, 8 partial, 3 missing:
53/60 (88.3%)** because D213H repairs fixed-point correctness without closing
aggregate borrowed returns, disjoint projected conflicts, or production
precision for E17-E20.

## D213G - Inferred Origin Transfer Across Reference Reassignment

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

A mutable `Text` binding now carries a compile-time origin set that changes
with reassignment. Rebinding from another borrowed view installs the right-hand
origin set, assigning an owned or static `Text` kills the previous loan, and
binding-to-binding assignment transfers every possible origin without adding a
runtime tag. This follows the reference-binding model used by Mojo while
retaining Sollang's punctuation-free inferred common case.

Control-flow joins operate on possible exit states. An `if` or `when` unions
the origin sets from its alternatives; when every reachable alternative
overwrites the binding with the same new origin, the old loan is absent after
the join. Loops conservatively union the entry and body-exit states because the
body may execute zero times. This is the structured equivalent of Polonius'
point-sensitive loan-kill and loan-liveness relations.

The self-host typed IR intentionally gives branch-local reassignments distinct
definition identities. Its ownership analysis therefore keeps the exact
definition edge as the fast path and compares source-token spelling only when
control-flow lowering creates another definition for the same binding. A
fixed-point alias pass transfers union-origin edges, while the reachability
solver treats all-alternative overwrites as kills and mixed branches or loop
exits as unions. Keeping the exact edge also prevents optimized Stage2 builds
from weakening the established single-origin E21 gate.

Examples 477-480 cover straight-line replacement, owned/static loan kills,
alias retention, all-branch overwrite, mixed `if` and `when` joins, and loop
zero-iteration behavior. Four diagnostics cover alias transfer and unsafe
`if`, `when`, and loop joins. The production Stage2 gate now requires Stage1
and Stage2 to reject single, union, and transferred-origin moves before LLVM
emission.

Validation is a zero-warning, zero-error Release build. Focused ownership
verification covers 23 tests, and Windows/Linux full suites pass **646/646**
in **56.5 seconds** and **59.5 seconds** respectively.
Fresh Windows Stage2 passes **7/7 in 70.5 seconds** at **11,663,233 LLVM text
bytes**, **3,448,648 bitcode bytes**, and a **1,635,328-byte executable**.
Fresh Linux Stage2 passes **6/6 in 146.0 seconds** at **11,659,812 LLVM text
bytes**, **3,446,856 bitcode bytes**, and a **3,285,776-byte executable**. The
periodic Stage3 cadence
advances to **5/10**, so Stage3 is intentionally deferred.

Formal progress remains 49 complete, 8 partial, and 3 missing: **53/60
(88.3%)**. Reference reassignment closes another ownership sub-gate, while
aggregate borrowed returns, disjoint projected conflicts, and production
enforcement precision for E17-E20 remain.

References:

- [Mojo lifetimes, origins, and reference bindings](https://mojolang.org/docs/manual/values/lifetimes/)
- [Mojo variables and reference bindings](https://mojolang.org/docs/manual/variables/)
- [Mojo ownership](https://mojolang.org/docs/manual/values/ownership/)
- [Polonius loan-kill and loan-liveness rules](https://rust-lang.github.io/polonius/rules/loans.html)

## D213F - Inferred Union Origins Across Multiple Borrowed Inputs

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

Sollang infers a set of possible origins when a returned `Text` view can select
between multiple default-borrowed `SourceText` parameters. The programmer does
not name lifetimes or origins. `if` and `when` result paths contribute their
origin sets, function contracts are discovered to a fixed point, and direct or
fluent call sites substitute concrete caller owners by parameter ordinal. A
live union view blocks moving or mutating every possible owner until CFG
last-use analysis ends that view.

The reference semantic compiler replaces scalar `function -> input` and `view
-> owner` maps with immutable origin sets. The self-host ownership analyzer
records one return-contract row per contributing parameter and follows the
typed-IR call argument chain to create distinct binding-to-owner edges. These
sets are erased after semantic analysis, so there is no runtime tag, allocation,
or ABI change.

Examples 475 and 476 cover reference execution and self-host left/right conflict
classification. The new diagnostic proves the reference compiler freezes the
non-selected possible owner too. Windows and Linux Stage2 now run a union-origin
failure fixture through both Stage1 and Stage2 and require fatal E21 before LLVM
emission.

Release builds remain at zero warnings and errors; focused ownership checks pass
14/14 and Windows/Linux full suites pass 638/638. Windows Stage2 passes 7/7 in
68.5 seconds with 11,612,260 LLVM bytes, 3,435,204 bitcode bytes, and a
1,628,672-byte executable. Linux Stage2 passes 6/6 in 149.3 seconds with
11,608,839 LLVM bytes, 3,433,412 bitcode bytes, and a 3,265,096-byte executable.
This advances the periodic Stage3 cadence to 4/10, so Stage3 is deferred.

Formal progress remains 49 complete, 8 partial, and 3 missing: 53/60 (88.3%).
The broader ownership gate still contains reference reassignment, borrowed
values inside aggregate returns, disjoint projected-borrow conflicts, and
production precision for E17-E20.

Mojo directly models a reference that may originate from `a` or `b` as the
union `origin_of(a, b)` and extends both owners' lifetimes. Polonius likewise
models origins as sets containing loans, propagates them through subset
relations, and combines that relation with CFG-point liveness. Sollang adopts
the set semantics while inferring the contract from its expression-first body.

References:

- [Mojo lifetimes, origins, and union origins](https://mojolang.org/docs/manual/values/lifetimes/)
- [Polonius origin and subset relations](https://rust-lang.github.io/polonius/rules/relations.html)
- [Polonius loan propagation and liveness](https://rust-lang.github.io/polonius/rules/loans.html)
- [Rust lifetime elision](https://doc.rust-lang.org/reference/lifetime-elision.html)

## D213E - CFG-Reachable Borrow Liveness Across Branches and Loops

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

Borrowed Text regions now follow structured control-flow reachability rather
than global source order. The reference semantic compiler carries the set of
borrow bindings used by the continuation into nested `if` and `when` blocks,
analyzes every alternative from the same entry state, and expires a view before
a block result when neither that result nor the outer continuation reads it.
An owned SourceText may therefore be consumed after the selected arm's final
view use when every arm consumes it. Consumption on only some outgoing paths is
rejected because the join would otherwise contain a conditionally initialized
owner.

Loop bodies use a conservative back-edge rule. If a view is read anywhere in a
repeating body, its origin remains borrowed at every invalidation in that body,
including a move textually after the read: the next iteration can reach the
earlier read. A dead view that is not used in the loop or continuation may still
expire before the loop-local invalidation.

The self-host ownership analyzer applies the same reachability rule directly to
flat typed IR. It recognizes sibling regions of structured `if` and enum
control, the linked arm/alternative representation used by `when`, enclosing
kind-20 loop regions, and ordinary forward reachability. Mutually exclusive
arms do not create E21; a use after the join and a use reachable through a loop
back-edge do. Example 473 proves the three outcomes independently as `0,1,1`.
Examples 472 and 474 execute all-branch SourceText consumption after branch-
local last uses, while `borrowed-text-branch-mixed-consumption` proves the join
diagnostic.

Validation passes a zero-warning Release build, focused checks 8/8, and the
Windows and Linux full suites at 635/635. Windows Stage2 passes 7/7 in 70.5
seconds at 11,606,935 LLVM bytes, 3,433,820 bitcode bytes, and a 1,628,160-byte
executable. Linux Stage2 passes 6/6 in 141.2 seconds at 11,603,514 LLVM bytes,
3,432,024 bitcode bytes, and a 3,265,096-byte executable. The Stage3 cadence is
3/10, so Stage3 is intentionally deferred.

Formal progress remains 49 complete, 8 partial, and 3 missing: 53/60 (88.3%).
D213E closes structured branch alternatives and conservative loop back-edges
for the current single-origin Text view, but multiple/union origins, reference
reassignment, aggregate borrowed returns, disjoint projected conflicts, and
production enforcement of E17-E20 remain.

Rust NLL defines non-lexical lifetimes over the CFG rather than lexical scopes.
Polonius makes the operational rule explicit: a loan is illegal to invalidate
only when it is live at that CFG point, and its location-sensitive model follows
CFG reachability. Sollang adopts that principle with a compact structured-CFG
solver suited to its current typed IR, leaving general origin propagation for a
later gate.

References:

- [Rust RFC 2094: non-lexical lifetimes](https://rust-lang.github.io/rfcs/2094-nll.html)
- [Rust MIR dataflow](https://rustc-dev-guide.rust-lang.org/mir/dataflow.html)
- [Polonius loan analysis](https://rust-lang.github.io/polonius/rules/loans.html)
- [Polonius CFG input relations](https://rust-lang.github.io/polonius/rules/relations.html)
- [2026 Polonius alpha project goal](https://rust-lang.github.io/rust-project-goals/2026/polonius.html)

## D213D - Shared Canonical Typed IR and Observable Stage2 Phases

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

Ownership validation and LLVM emission now consume one canonical typed-IR
array. `llvm.text.prepare` lowers the semantic snapshot once, then lends that
array read-only to `ownership_check.analyzeLoweredContext` before retaining it
for emission. The public `analyzeContext` convenience API still lowers its own
IR for standalone ownership tools, while the production driver avoids the
duplicate work introduced by D213C.

The attempted reuse exposed a self-host closure ABI defect. When a local
function captured an owned additional parameter, `emitCaptureValue` always
selected the primary `%arg`, even when the captured binding was `%arg1` or
later. Capture lowering now derives the ordinal from the enclosing function's
primary and linked additional parameter nodes and emits the matching LLVM
argument. Complete Stage2 assembly is the regression gate because the former
output stored a semantic-snapshot struct where a typed-IR array was required.

Stage2 verification no longer repeats a misleading `LLVM 0 bytes (0.0%)` while
front-end work is active. It reports `phase 1/2`, the exact source-file and
source-line totals, and a ten-second elapsed heartbeat. Once LLVM output starts,
`phase 2/2` reports bytes and a bounded percentage ending at 100.0%. This is an
honest externally observable boundary; finer per-pass progress should later use
a dedicated compiler instrumentation channel rather than estimated percentages.

Fresh Windows Stage2 verification passes 7/7 in 68.17 seconds, down from
101.8 seconds in D213C (33.0% faster), at 11,585,512 LLVM bytes with a
1,626,624-byte executable. Fresh Linux Stage2 passes 6/6 in 145.13 seconds,
down from 253.9 seconds (42.8% faster), at 11,582,091 LLVM bytes with a
3,260,840-byte executable. Focused self-host/capture/ownership checks pass 6/6,
Windows and Linux full suites pass 631/631, and the Release build has zero
warnings and errors. This advances the periodic Stage3 cadence to 2/10, so
Stage3 is intentionally deferred.

Formal progress remains 49 complete, 8 partial, and 3 missing: 53/60 (88.3%).
D213D removes a production performance regression and repairs canonical IR
sharing, but it does not close the remaining CFG-sensitive lifetime, origin
union, aggregate-reference, projected-conflict, or diagnostic-completeness
work.

Rust's compiler query model reuses tracked intermediate results instead of
running redundant whole-program passes. Its LLVM backend integration keeps
expensive codegen-unit artifacts under explicit lifetime control. LLVM and
MLIR expose separate read-only pass/analysis instrumentation hooks. Sollang
adopts the same separation at its current scale: one immutable typed IR,
multiple consumers, and progress observation outside the IR mutation path.

References:

- [Rust compiler overview and query model](https://rustc-dev-guide.rust-lang.org/overview.html)
- [Rust incremental compilation and backend integration](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation-in-detail.html)
- [LLVM pass instrumentation](https://llvm.org/doxygen/PassInstrumentation_8h.html)
- [MLIR pass instrumentation](https://mlir.llvm.org/docs/PassManagement/#pass-instrumentation)

## D213C - Failure-First Ownership Diagnostics in the Production Driver

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

The native Stage2 compiler now validates the inferred borrowed-Text lifetime
contract before it prints a target data layout, target triple, or LLVM body.
Diagnostic E21 is a fatal compiler result: both explicit file lists and
discovered source roots print the source/byte/length diagnostic and terminate
with a nonzero process exit code. Successful compilation still emits pure LLVM
text. The checked API deliberately blocks only E21 for now; E17-E20 remain
available to focused ownership tests but are not production-fatal until their
remaining false positives are removed.

`sys.process.exit(Int)` is the runtime boundary used by the Sollang-written
driver. Windows lowers it to `ExitProcess` and Linux/Wasm-native hosting lowers
it to `exit`; buffered compiler output is flushed before termination. The C#
reference compiler and self-host emitter resolve the same intrinsic identity.
The failure gate runs the unsafe two-file fixture through both Stage1 and
Stage2, requires a nonzero result and E21, and rejects any output that already
contains a target header.

The first attempt to share one lowered IR exposed two existing self-host
backend limits: a borrowed array captured by a local function can select the
wrong outer `%arg`, and an owned array cannot yet be extracted from a returned
aggregate into a new binding. Until those are repaired, checked preparation
lowers typed IR once for ownership analysis and once for emission. This is
correct but measurable: fresh Windows Stage2 verification took 101.8 seconds,
and fresh Linux Stage2 verification took 253.9 seconds. Removing the duplicate
lowering and replacing the pre-output 0-byte heuristic are explicit follow-up
performance work rather than hidden as successful optimization.

Validation is a zero-warning Release build, Windows/Linux full suites at
631/631, Windows Stage2 7/7 at 11,581,500 LLVM bytes with a 1,625,088-byte
executable, and Linux Stage2 6/6 at 11,578,079 LLVM bytes. This is checkpoint
1/10 after the D213B Stage3 reset, so Stage3 is intentionally deferred. Formal
progress remains 49 complete, 8 partial, and 3 missing: 53/60 (88.3%), because
CFG-sensitive regions, origin unions, aggregate borrowed returns, projection
conflicts, and the remaining production ownership diagnostics are still open.

Rust's diagnostic architecture treats an emitted fatal error as proof that
compilation cannot continue. Clang likewise performs parsing and semantic
analysis before LLVM generation and regression-tests location-specific
diagnostics. Sollang adopts that failure boundary while keeping its concise
pipeline syntax and deterministic source spans.

References:

- [Rust compiler diagnostics](https://rustc-dev-guide.rust-lang.org/diagnostics.html)
- [Rust `ErrorGuaranteed`](https://rustc-dev-guide.rust-lang.org/diagnostics/error-guaranteed.html)
- [Clang command stages](https://clang.llvm.org/docs/CommandGuide/clang.html)
- [Clang diagnostics internals](https://clang.llvm.org/docs/InternalsManual.html)

## D213B - Straight-Line Last-Use Borrow Regions

Status: implemented, cross-target Stage2 verified, and Stage3 fixed point verified
Date: 2026-07-20

An inferred borrowed Text result no longer freezes its SourceText origin until
lexical scope exit when the caller has finished using the view. Before each
statement in a function or main's straight-line statement sequence, the
reference semantic compiler checks the remaining statements and final result
expression. Borrow bindings with no remaining use are removed from the active
origin set. A later owner move, replacement, or mutation is therefore legal;
the same operation remains an error when any later statement can read the view.

The self-host ownership analyzer applies the equivalent rule to flat typed IR.
For each move event against a borrowed origin it searches for a later name-read
whose definition is the borrowed binding. Diagnostic 21 is emitted only when
that later use exists. The analysis is intentionally source-order conservative:
full control-flow-graph liveness for branch joins, loops, reassignment, and
path-specific regions remains open.

The self-host LLVM execution example exposed and fixed an adjacent compiler
defect rather than weakening the test. A function-local `println` previously
treated non-Int32 numeric results such as SourceText `len` (`UIntSize`) as
Text. Function emission now selects Bool or signed/unsigned 8/16/32/64-bit
integer formatting, widens narrow values when required, and the runtime-feature
scan includes the matching formatter helpers.

Examples 469-471 cover reference execution, direct self-host ownership
analysis, LLVM assembly, and Linux execution. The existing
`borrowed-text-origin-move` diagnostic and example 468 prove that a move before
the final view use is still rejected. Release builds have zero warnings and
errors; Windows and Linux full suites pass 631/631. Windows Stage2 passes 6/6
at 11,348,275 LLVM text bytes with a 1,573,376-byte executable, and Linux
Stage2 passes 5/5 at 11,344,878 LLVM text bytes with a 3,128,680-byte
executable. Stage3 regenerates 11,348,275 identical LLVM bytes and passes the
fixed-point hash
`390F7C0482933D3C2918421B9CE1994762712C4FA459F240407A1C5A302D0976`.
This completes cadence 10/10 and resets it to 0/10.

Formal progress remains 49 complete, 8 partial, and 3 missing: 53/60 (88.3%).
D213B narrows the ownership/storage gate but does not yet close CFG-sensitive
regions, multiple or union origins, aggregate borrowed returns, disjoint
projection conflicts, or production-driver ownership-diagnostic integration.

The design follows Rust NLL's minimal region containing every possible future
use, Mojo's compiler-inferred symbolic origins, and Swift's rule that accesses
conflict only when their locations and durations overlap. Sollang deliberately
keeps lifetime punctuation out of the single-origin common case.

References:

- [Rust RFC 2094: non-lexical lifetimes](https://rust-lang.github.io/rfcs/2094-nll.html)
- [Mojo lifetimes, origins, and references](https://mojolang.org/docs/manual/values/lifetimes/)
- [Swift memory safety and overlapping access](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/memorysafety/)

## D216 - Self-Host Readonly-Reference Call Boundary

Status: implemented and cross-target Stage2 verified
Date: 2026-07-20

The self-host LLVM backend now derives each call parameter from the canonical
target function. When a parameter is `ref T` and the argument is a stable
immutable value, the caller creates an aligned stack slot, stores the value
once, and passes the slot as `ptr`. An argument which is already `ref T` is
forwarded directly. This preserves the non-owning pointer ABI established in
D215 without adding lifetime punctuation to ordinary Sollang calls.

Readonly references may escape through a return, so not every expression may
be placed in an anonymous slot. New production diagnostic E22 rejects literals,
constructor and call temporaries, and mutable bindings as reference origins.
The checked compiler reports the source span and exits before any target header
or LLVM body. Stable immutable bindings are the intentionally conservative
first accepted place class; general CFG liveness and stored references remain
future work.

Example 507 executes the complete path and prints `42`. Its LLVM contains the
caller `alloca` and store, a projected-field `getelementptr`, direct pointer
forwarding into a reference consumer, and one final scalar load. Example 508
and the Stage2 fixture prove E22 in both compiler generations. Linux passes
680/680 in one full run; Windows covers 680/680 after the known timing-sensitive
example 381 is rerun alone. Windows Stage2 passes 7/7 at 11,963,482 LLVM bytes;
Linux Stage2 passes 6/6 at 11,960,061 LLVM bytes. Formal progress remains
53/60 (88.3%), and the periodic Stage3 cadence advances to 4/10.

## D217 - Inferred Readonly-Reference Last Use

Status: implemented first mutable-owner vertical and cross-target Stage2 verified
Date: 2026-07-21

Sollang permits a mutable owner to be passed to `ref T` when the compiler can
keep the reference tied to stable owner storage. The language adds no explicit
lifetime syntax. The compiler infers the symbolic owner root and treats a
mutation as conflicting only while a returned readonly reference has a later
reachable use. This combines Polonius-style live-loan invalidation, Mojo-style
inferred origins, and Swift's duration/location overlap rule.

The C# compiler records reference-call origins in the existing last-use state,
rejects overlapping mutable-field writes while the loan is live, and expires
the loan before a safe mutation after the final use. The self-host LLVM backend
passes the existing mutable struct `%slot` pointer instead of copying the
aggregate into a temporary. Its ownership pass emits blocking E23 before LLVM
for a live-reference mutation; E22 continues to reject literal, constructor,
and call-result temporaries.

Examples 509-511 prove the positive C# path, checked self-host E23, and native
self-host LLVM assembly/link/execution. The Stage2 fixtures enforce E17-E23 in
both compiler generations. Windows passes 683/683 and Stage2 7/7 at 11,990,618
LLVM bytes. Linux covers 683/683 and Stage2 6/6 at 11,987,197 LLVM bytes. Formal
progress remains 53/60 (88.3%) because branch-sensitive reference uses, origin
unions, indexed/nested places, owner moves/rebinds, and stored references remain
open. This is Stage3 cadence 5/10, so the periodic Stage3 run is not due.

## D218 - Inferred Readonly-Reference Origin Unions

Status: implemented first return-parameter union vertical and cross-target Stage2 verified
Date: 2026-07-21

A reference-returning function now has an inferred symbolic contract containing
only the `ref T` parameters that can reach a return. An ordinary function that
always returns `left` does not lock an unrelated `right` argument. When control
flow can return either parameter, the contract is their union and both owners
remain protected until the returned reference's last reachable use. Sollang
keeps this contract internal and adds no explicit lifetime punctuation.

The C# compiler discovers contracts to a fixed point, maps them through direct
and flow calls, and emits an explicit reference return as the selected pointer
rather than loading the pointee. The self-host ownership pass derives the same
contracts from implicit and explicit return nodes and applies E23 to every
possible call-site origin. Its parameter walker now follows the distinct first
and additional-parameter chains, so LLVM calls pass later `ref` arguments as
`ptr` and early returns correctly emit `%arg1`, `%arg2`, and subsequent pointer
parameters.

Example 512 proves call-site precision and safe mutation after union last use.
The `ref-origin-union-owner-mutation` diagnostic proves that either possible
owner is protected. Examples 513-514 prove self-host E23 plus native LLVM
assembly, link, and execution. The Stage2 E23 fixture now uses the same
branch-selected union in both compiler generations. Release builds with zero
warnings and errors; Windows and Linux full suites pass 687/687. Windows
Stage2 passes 7/7 at 12,021,178 LLVM bytes, and Linux Stage2 passes 6/6 at
12,017,757 LLVM bytes. Formal progress remains 53/60 (88.3%) because projected
place precision, owner move/rebind/drop conflicts, branch-local loan liveness,
and references stored in user aggregates remain open. Stage3 cadence advances
to 6/10, so the periodic Stage3 run is not due.

The rule follows Mojo's symbolic union origins, Polonius's origins containing
live loans, and Swift's overlapping-access duration/location model.

References:

- [Mojo lifetimes, origins, and references](https://docs.modular.com/mojo/manual/values/lifetimes)
- [Polonius relation rules](https://rust-lang.github.io/polonius/rules/relations.html)
- [Swift memory safety](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/memorysafety/)

## D219 - Projected Readonly-Reference Places

Status: implemented nested stored-field vertical and cross-target Stage2 verified
Date: 2026-07-21

A readonly-reference origin is now an internal place consisting of a root
binding plus its field-projection path. Equal places, a whole owner and any
descendant, and prefix-related paths overlap. Different stored fields are
provably disjoint, so a loan of `outer.inner.first` does not prevent mutation
of `outer.tail`; replacing `outer.inner` still conflicts while the loan is
live. This adds no lifetime or place-path punctuation to Sollang source.

The C# compiler forms reference arguments from addressable name and nested
member places, emits the corresponding recursive `getelementptr` chain, and
uses the existing projected origin in last-use conflict checks. The self-host
LLVM backend reconstructs the nested member path from source-backed typed IR,
passes the deepest projected pointer to `ref T`, and applies the same E23
overlap rule before LLVM emission. Constant and dynamic array-index projection
precision is intentionally not claimed by this checkpoint; dynamic indices
remain conservative.

Example 515 proves the C# execution path. The projected-place diagnostic proves
that replacing a live borrowed prefix is rejected. Examples 516 prove direct
self-host E23 classification plus native LLVM assembly, link, and execution.
The Stage2 E23 fixture combines the D218 branch-selected origin union with the
D219 nested field place. Release builds have zero warnings and errors; Windows
and Linux full suites pass 691/691. Windows Stage2 passes 7/7 at 12,072,227
LLVM bytes, and Linux Stage2 passes 6/6 at 12,068,806 LLVM bytes. Formal
progress remains 53/60 (88.3%) because indexed reference places, owner
move/rebind/drop conflicts, branch-local loan liveness, and references stored
in user aggregates remain open. Stage3 cadence advances to 7/10, so the
periodic Stage3 run is not due.

The design follows Rust's knowledge of disjoint struct fields and its
projection-based capture paths, Swift's stored-property overlap rule, Mojo's
derived origins, and Polonius's live-loan invalidation model.

References:

- [Rust borrow splitting](https://doc.rust-lang.org/nomicon/borrow-splitting.html)
- [Rust closure capture place projections](https://doc.rust-lang.org/stable/reference/types/closure.html)
- [Swift memory safety](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/memorysafety/)
- [Mojo lifetimes, origins, and references](https://docs.modular.com/mojo/manual/values/lifetimes)
- [Polonius loan rules](https://rust-lang.github.io/polonius/rules/loans.html)

## D220 - Indexed Readonly-Reference Places

Status: implemented constant, dynamic, and nested indexed-place vertical with cross-target Stage2 verification
Date: 2026-07-21

A readonly-reference place may now include an array index. An integer literal is
recorded as a precise projection such as `[0]`; two different literal indices
are disjoint. Every non-literal index is recorded as `[*]` and conservatively
overlaps every element. Field projections continue after the index, so the
compiler treats `items![0].value` as one composed place rather than losing the
owner at the array boundary. Dictionary keys are not covered by this rule.

The C# LLVM backend emits addressable reference arguments for fixed and growable
Int, Text, and inline aggregate arrays plus `IntSlice`. It converts the index to
the target size, emits an unsigned bounds check, and forms the element GEP before
any member GEP. The self-host backend performs the equivalent integer widening,
trap edge, element GEP, and nested field GEP while preserving the existing
pointer-only `ref T` ABI. Production E23 compares literal indices precisely,
treats runtime indices as wildcards, unwraps a member chain to its indexed base,
and releases the loan at the returned reference's last use.

The Stage2 fixed-point gate exposed two self-host-only emission defects that
focused examples could not reveal: a direct parameter-to-mutable-local initializer
could name an unemitted SSA value, and interpolating later parameters of a
five-argument local function could emit an empty operand. D220 avoids both by
materializing explicit arithmetic SSA aliases before mutable traversal and text
interpolation. The rebuilt compiler then assembles and reaches the same normalized
LLVM fixed point on Windows and Linux.

Examples 517-525 cover constant and dynamic element references, disjoint and
conflicting E23 cases, bounds checks, index widening, and nested index-to-field
GEP execution. `ref-indexed-place-mutation` and
`ref-dynamic-index-mutation` cover reference-compiler diagnostics. The Stage2
E23 fixture combines a D218 branch-selected origin union with
`items![0].value`, and both Stage1 and Stage2 reject replacement of the live
borrowed element before LLVM emission.

The Release build has zero warnings and errors. Windows and Linux full suites
pass 702/702. Windows Stage2 passes 7/7 at 12,155,615 LLVM bytes; Linux Stage2
passes 6/6 at 12,152,194 LLVM bytes. Formal progress remains 53/60 (88.3%)
until the general returned/stored-reference boundary closes. The periodic Stage3
cadence advances to 8/10, so Stage3 is not due.

The design follows Rust's rule that fixed array indices are not generally split
by the borrow checker unless proved separately, Mojo's inferred origin model,
and Polonius's loan invalidation relations. Sollang deliberately adds the safe
compile-time-literal distinction while keeping runtime indices conservative.

References:

- [Rust borrow splitting](https://doc.rust-lang.org/nomicon/borrow-splitting.html)
- [Mojo lifetimes, origins, and references](https://docs.modular.com/mojo/manual/values/lifetimes)
- [Polonius loan rules](https://rust-lang.github.io/polonius/rules/loans.html)

## D221 - Readonly-Reference Owner Invalidation

Status: implemented owner move/rebind vertical with cross-target Stage2 verification
Date: 2026-07-21

Sollang defines a readonly-reference conflict in terms of overlapping places and
storage identity, not only assignment syntax. Passing an owner to a `move`
parameter, transferring an owned value into another aggregate, or rebinding a
mutable owner invalidates the old place. A whole-owner invalidation overlaps all
of its projections. A partial move keeps its field/index path, so moving
`bundle.right` does not invalidate a reference to `bundle.left`.

There is no source-level `drop()` statement in this checkpoint. Destruction is
an implicit compiler action and remains valid after the reference's final use.
The source-level equivalent of an early drop is an explicit consuming transfer;
it is rejected with E23 when the reference is still live. Rebinding is governed
by the same rule because it replaces the addressable storage identity even for a
scalar-only struct.

The C# semantic pass now sends the precise owned-field place into invalidation
checking and retains root removal only for the moved-owner state. Its LLVM
backend stores a mutable struct replacement into the original slot. The
self-host pass consumes canonical `MoveEvent` places, recognizes whole-owner
mutable rebindings by source binding name, and applies the same last-use test.
Examples 526-528 prove safe post-last-use replacement/consumption, disjoint
partial movement, self-host E23 classification, and native LLVM execution. The
`ref-owner-move` and `ref-owner-rebind` diagnostics plus the Stage2 owner-move
fixture prove that both compiler generations stop before LLVM emission.

Windows and Linux full suites pass 707/707. Windows Stage2 passes 7/7 at
12,170,216 LLVM bytes; Linux Stage2 passes 6/6 at 12,166,795 LLVM bytes. Formal
progress remains 53/60 (88.3%), and Stage3 cadence is 9/10.

The rule follows Rust's prohibition on moving or assigning an owner while it is
borrowed, Mojo's inferred origin model, and Polonius loan invalidation. Sollang
retains its existing syntax and exposes none of those systems' lifetime notation.

References:

- [Rust E0505: move while borrowed](https://doc.rust-lang.org/error_codes/E0505.html)
- [Rust E0506: assignment while borrowed](https://doc.rust-lang.org/error_codes/E0506.html)
- [Mojo lifetimes, origins, and references](https://docs.modular.com/mojo/manual/values/lifetimes)
- [Polonius loan rules](https://rust-lang.github.io/polonius/rules/loans.html)

## D222 - Control-Flow-Sensitive Readonly-Reference Liveness

Status: implemented branch-local and loop-sensitive vertical with Stage3 verification
Date: 2026-07-21

Sollang now determines a readonly reference's remaining lifetime from reachable
control flow rather than source order alone. An early-returning alternative does
not inherit reference uses after the join, and sibling `if`, `when`, and enum
alternatives do not keep one another's loans live. Within a loop, an
unconditional `break` removes the back edge, while `continue` preserves it; a
reference used in the next iteration therefore still protects its owner. The
language keeps lifetime notation implicit.

The C# semantic pass snapshots the origin map and active readonly-reference
bindings together, summarizes branch and loop control exits, and merges only
states that can reach the continuation. The self-host ownership pass applies
the same reachability rule to source-backed typed IR. Its helper takes the
semantic snapshot and typed statements explicitly instead of capturing them;
this avoids an invalid captured-aggregate ABI path that was exposed by the
Linux native self-host executable.

Examples 529-531 prove early return, mutually exclusive alternatives,
post-last-use break, loop-carried continue, checked self-host classification,
and native LLVM execution. A dedicated diagnostic and Stage2 fixture preserve
E23 for the loop-carried loan. Release builds have zero warnings and errors.
Windows and Linux full suites pass 711/711. Windows Stage2 passes 7/7 at
12,229,189 LLVM bytes, and Linux Stage2 passes 6/6 at 12,225,768 LLVM bytes.
The required Stage3 run passes 3/3 and reproduces normalized SHA-256
`73B84663339F8C691EFAD3D291C92E97E2B54CA4FEA0C8DE2818A1410C5BF5FB`.
The cadence resets to 0/10. Formal progress remains 53/60 (88.3%) because
references stored in user aggregates remain open.

The rule follows Rust NLL's CFG-based non-lexical regions and Polonius's
location-sensitive live-loan and invalidation relations. Mojo's inferred
origins confirm that Sollang can retain this precision without exposing
explicit lifetime parameters.

References:

- [Rust non-lexical lifetimes RFC](https://rust-lang.github.io/rfcs/2094-nll.html)
- [Polonius loan analysis](https://rust-lang.github.io/polonius/rules/loans.html)
- [Polonius relations](https://rust-lang.github.io/polonius/rules/relations.html)
- [Polonius implementation](https://github.com/rust-lang/polonius)
- [Mojo lifetimes and origins](https://docs.modular.com/mojo/manual/values/lifetimes)
- [Mojo value lifecycle](https://docs.modular.com/mojo/manual/lifecycle/life)

## D223 - Inferred Origins for References Stored in User Structs

Status: implemented struct vertical with cross-target Stage2 verification
Date: 2026-07-21

Sollang permits `ref T` fields in user structs without adding lifetime syntax.
The runtime representation is the ordinary pointer field. At compile time, the
containing struct carries the symbolic origin union of its reference-bearing
fields. A reference-bearing return is valid only when that union can be inferred
from reference-bearing inputs; hiding a reference to callee-owned storage in a
returned struct is E22. Mutating, moving, or rebinding an overlapping owner while
a stored reference has a reachable later use is E23.

The analysis is field-sensitive. `stored.value` keeps the corresponding origin
live, while later use of scalar sibling `stored.tag` does not. Origins are found
both through helper calls returning a struct and through reference-returning
calls nested directly in a struct literal. C# type definition construction now
admits reference fields, recursively discovers reference-bearing return/input
types, and emits the stored pointer without an accidental pointer-to-pointer.
The self-host ownership pass mirrors aggregate origin inference, local escape
rejection, nested-call discovery, and field-sensitive last use. Its LLVM entry
path also now lowers member expressions embedded in string interpolation.

Implementation checklist:

- [x] Parse and resolve `ref T` user-struct fields in the reference compiler.
- [x] Infer origins through reference-bearing parameters and struct returns.
- [x] Reject callee-local aggregate escapes with E22.
- [x] Preserve E23 across helper-returned and direct-literal struct storage.
- [x] End a stored reference at its final reference-field use, not a scalar
  sibling use.
- [x] Emit and execute the struct's pointer field in C# and self-host LLVM.
- [x] Verify stage-1/stage-2 parity on Windows and Linux.
- [x] Extend the same rule to enum payloads.
- [ ] Extend the same rule to array/dictionary element storage before closing
  the general user-aggregate gate.

Examples 532-534 cover reference-compiler execution, checked self-host
classification, direct-literal tracking, local escape rejection, and native
self-host LLVM execution. Release builds have zero warnings and errors. Windows
and Linux full suites pass 716/716. Windows Stage2 passes 7/7 at 12,292,062 LLVM
bytes, and Linux Stage2 passes 6/6 at 12,288,641 LLVM bytes. The periodic Stage3
cadence advances to 1/10, so Stage3 is not due. Formal progress remains 53/60
(88.3%) until enum and container storage complete the broader aggregate gate.

This combines Mojo's inferred-origin model with Swift's non-escapable aggregate
and lifetime-dependency design. Rust's lifetime-bearing structs provide the
safety invariant, but Sollang deliberately keeps those parameters inferred.

References:

- [Mojo lifetimes and origins](https://docs.modular.com/mojo/manual/values/lifetimes)
- [Mojo ownership](https://docs.modular.com/mojo/manual/values/ownership)
- [Swift non-escapable types](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0446-non-escapable.md)
- [Swift safe C++ interop](https://www.swift.org/documentation/cxx-interop/safe-interop/)
- [Swift 6.2 Span](https://www.swift.org/blog/swift-6.2-released/)
- [Rust lifetime syntax for structs](https://doc.rust-lang.org/book/ch10-03-lifetime-syntax.html)

## D224 - Inferred Origins for References Stored in Enum Payloads

Status: implemented enum vertical with cross-target Stage2 verification
Date: 2026-07-21

Sollang extends its implicit origin model from struct fields to nominal enum
payloads. Constructing `Variant(refValue)` transfers the reference's symbolic
origin into the enum carrier, returning a reference-bearing enum is legal only
when that origin comes from a reference-bearing input, and extracting the
payload in `when` keeps the owner protected until the payload alias's final
reachable use. No lifetime parameter is added to source syntax.

The C# compiler recursively collects enum payload leaf paths such as
`stored[Ref]`, maps them to pattern bindings while an arm is checked, and
applies E22/E23 to direct constructors, returned carriers, owner rebinding, and
field mutation inside the selected arm. The self-host type and ownership passes
recover nominal variant payload types from the declaration AST, carry the same
reference-bearing classification into typed IR, connect pattern uses to their
enum carrier, and emit the payload pointer unchanged. Mutable scalar slots are
used at nested type-query call boundaries so the compiler's own LLVM IR keeps
every `extractvalue` definition ahead of its use.

Examples 535-537 and three diagnostics cover native execution, self-host
classification, pattern-arm liveness, local escape rejection, and LLVM
assembly. The Windows and Linux Stage2 fixtures enforce the new E22/E23 cases
in both compiler generations. Release builds have zero warnings and errors;
Windows and Linux full suites pass **722/722**. Windows Stage2 passes **7/7** at
**12,369,268 LLVM bytes**, and Linux Stage2 passes **6/6** at **12,365,847 LLVM
bytes**. The periodic Stage3 cadence advances to **2/10**, so Stage3 is not due.

Formal progress remains **49 complete, 8 partial, 3 missing: 53/60 (88.3%)**.
Enum storage closes the second user-aggregate vertical, but fixed/growable array
and dictionary element storage still keep the general stored-reference gate
partial.

This follows Mojo's inferred symbolic origins and Swift's lifetime-dependent,
non-escapable aggregate direction while preserving Rust's owner-outlives-borrow
safety invariant without exposing lifetime punctuation.

References:

- [Mojo lifetimes and origins](https://docs.modular.com/mojo/manual/values/lifetimes)
- [Swift non-escapable types](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0446-non-escapable.md)
- [Swift safe C++ interop](https://www.swift.org/documentation/cxx-interop/safe-interop/)
- [Rust lifetime syntax for structs](https://doc.rust-lang.org/book/ch10-03-lifetime-syntax.html)

## D225 - Inferred Origins for References Stored in Array Elements

Status: implemented fixed/growable array vertical with self-host semantic verification
Date: 2026-07-21

Sollang now carries readonly-reference origins through fixed and growable array
elements. An enum or aggregate stored in an array retains the symbolic union of
the references it contains. Index extraction and contextual enum matching keep
the corresponding owner live, while replacing the overlapping array element is
rejected until the extracted reference has no reachable later use. Returning an
array containing a callee-local reference remains E22.

The reference compiler matches a precise indexed place against the stored
`[*]` carrier path; constant indexes may be distinguished when the access is
known, while dynamic indexes remain conservative. The self-host ownership pass
mirrors the same rule, including a fallback scan for collection literals whose
recursive collection type id is repaired after ownership analysis. No lifetime
syntax is exposed.

Examples 538-539, two diagnostics, and Windows/Linux Stage2 fixtures cover
native array execution, self-host E22/E23 classification, indexed pattern
extraction, and Stage1/Stage2 parity. Windows full tests pass **726/726**;
Linux array-focused tests pass **4/4**. Windows Stage2 passes **7/7** at
**12,371,921 LLVM bytes**, and Linux Stage2 passes **6/6** at
**12,368,500 LLVM bytes**. The formal score remains 53/60 (88.3%) because
dictionary element storage is still open. Dictionary values and keys will use
a conservative wildcard path until the Swiss-table address and mutation rules
are complete.

This keeps Rust's owner-outlives-borrow invariant and Mojo's inferred origins,
while retaining Sollang's compact array syntax and implicit lifetimes.

References:

- [Mojo lifetimes and origins](https://docs.modular.com/mojo/manual/values/lifetimes)
- [Rust borrow splitting](https://doc.rust-lang.org/nomicon/borrow-splitting.html)
- [Swift Collection](https://developer.apple.com/documentation/swift/collection)

## D226 - Conservative Origins for Swiss-Table Dictionary Values

Status: reference-compiler storage slice implemented; self-host parity remains open
Date: 2026-07-21

Dictionary values that contain `ref T` now participate in the C# compiler's
origin model. A value lookup is represented as the wildcard entry path `[*]`,
because Swiss-table probing, insertion, growth, and rehashing can move a value
even when the source key is constant. The value slot pointer is recovered from
the existing generic dictionary entry layout, so `key -> same` can return a
reference without copying the value. E22 rejects a dictionary returned with a
callee-local reference, and E23 protects an owner referenced by a stored value.

Dictionary keys remain outside this first safe surface: lookup keys are not
addressable reference places and key types must satisfy the existing hash and
equality contracts. A later slice may add explicit key views if the runtime can
preserve their address through rehash. The self-host typed-IR and LLVM paths
still need dictionary element type recovery and entry-pointer lowering before
this gate can be promoted from partial.

Example 540 and two diagnostics pass on Windows and Linux. The formal roadmap
score remains 53/60 (88.3%). This conservative wildcard follows Rust's warning
that collection disjointness is not generally proven for mutable indexing,
Mojo's symbolic origin sets, Swift's collection mutation invalidation, and the
Swiss-table implementation's relocation behavior.

References:

- [Rust borrow splitting](https://doc.rust-lang.org/nomicon/borrow-splitting.html)
- [Mojo lifetimes and origins](https://docs.modular.com/mojo/manual/values/lifetimes)
- [Swift Dictionary](https://developer.apple.com/documentation/swift/dictionary)
- [Rust HashMap](https://doc.rust-lang.org/std/collections/struct.HashMap.html)

## D227 - Self-host Dictionary Recursive Type Recovery

The self-host recursive type arena now performs a deferred dictionary pass
after binding and path inference. This repairs untyped dictionary literals
whose value is an enum constructor carrying a readonly reference, and then
propagates the dictionary value type into an indexed access. Example 541
verifies both the dictionary and index expression type IDs (`1/1`) on the
self-host path.

This is intentionally a type-recovery checkpoint, not completion of the
dictionary stored-reference gate: ownership diagnostics and self-host LLVM
entry-pointer lowering still need to consume the recovered value type. The
formal roadmap remains 53/60 (88.3%) until that end-to-end behavior is proven.

## D228 - Preserve Dictionary Enum Pattern Reference Payloads

The self-host typed-IR lowering path now has a legacy-inference fallback when
selecting an enum pattern subject. If a dictionary index already has a shallow
enum identity but its recursive AST map is late, the fallback reconstructs the
canonical enum type and emits the pattern payload binding with the real
readonly-reference type (`typeKind == 8`). Example 546 verifies this directly;
the Windows and Linux checks both pass.

This closes type preservation only. The ownership analyzer still needs to
recognize the dictionary carrier as the owner of that payload before the
formal stored-reference gate can advance beyond 53/60 (88.3%).

## D229 - Self-host Dictionary Carrier Ownership Recognition

The self-host ownership pass now recognizes a dictionary constructor carrying
an enum payload reference even when the recursive dictionary type ID is not yet
present in the semantic type arena. Its fallback follows the typed-IR shape
(`dictionary -> enum constructor -> reference call`) and recognizes the
reference call's `typeKind == 8` directly. Example 545 verifies E23: replacing
the owner after storing the reference and before matching the dictionary
payload reports exactly one conflict. Examples 545, 546, 536, and 547 pass on
Windows. LLVM entry-pointer lowering and Linux dictionary ownership coverage
remain open, so the formal score stays **53/60 (88.3%)**.

## D230 - Self-host LLVM Reference Dictionary Slots

The self-host LLVM text emitter now preserves direct readonly-reference values
in inferred dictionary literals (`{1: owner! -> same}`). When recursive type
recovery leaves the value projection shallow, the emitter still
allocates pointer-width storage, emits `ptr` element GEPs, and stores the
reference pointer with pointer alignment. Example 548 compiles a dictionary
reference lookup through a self-hosted LLVM module; LLVM assembly, Windows/Linux
linking, and execution all pass with `observed=40`. The reference C# emitter
also unwraps a stored `ref T` slot before returning its pointer, and the
Windows/Linux differential checks pass. Enum payload projection,
Swiss-table entry addressing, and complete dictionary ownership remain open, so
the formal score remains **53/60
(88.3%)**.

## D231 - Self-host LLVM Dictionary Enum Reference Payloads

Example 550 proves the next dictionary slice end to end: an inferred
`{1: IntView.Ref(owner! -> same)}` value is indexed, matched as `Ref(value)`,
and read through the stored reference payload. The self-host LLVM output
assembles, links, and prints `observed=40` on Windows and Linux, and the C# /
self-host differential checks agree on both targets. The formal score remains
**53/60 (88.3%)** because Swiss-table entry addressing and the complete
dictionary ownership gate still require broader coverage.

## D232 - Direct Dictionary Call Type Recovery

The self-host expression-type resolver now treats a dictionary's direct child
expression as a valid nearest candidate. Previously, a value such as
`{1: owner -> same}` skipped the call node itself and selected the nested
`value` parameter, flattening the dictionary value from `ref Int` to `Int`.
The initial and deferred dictionary passes now cover both key and value sides.
Example 551 asserts the recovered value type is kind 8 (`ref`) and passes with
the reference compiler and self-host compiler. This repairs type identity but
does not advance the broader Swiss-table entry-addressing or ownership gates;
formal progress remains **53/60 (88.3%)**.

## D233 - Direct Dictionary Reference Ownership Coverage

Example 552 adds the missing direct-value ownership check for
`{1: owner! -> same}`: replacing the owner while the stored reference is live
must produce conflict 23. The self-host ownership analyzer reports exactly
one conflict and passes on both Windows and Linux. This confirms the direct
carrier path alongside the enum-payload path; full dictionary mutation/drop
coverage and Swiss-table entry addressing remain open, so formal progress is
still **53/60 (88.3%)**.

## D234 - Self-host Dictionary Control-Byte Storage

The self-host LLVM emitter now reserves a control-byte tail after each
dictionary key allocation, marks occupied literal slots, and checks the byte
before loading a key during indexed lookup. The existing four-field dictionary
ABI remains unchanged, so values and drop/free ownership continue to work.
Examples 548 and 550 produce, assemble, link, and execute on Windows and
Linux with the new layout. This is the first self-host Swiss-table storage
slice, not the complete algorithm: grouped probing, insertion, growth, and
rehashing remain open; formal progress stays
**53/60 (88.3%)**.

## D235 - Self-host Integer H2 Probing

Self-host dictionary literals and indexed lookup now use the key storage width
to emit matching H2 fingerprints and hash-based start slots for 16-, 32-, and
64-bit integer keys. One-byte keys remain on the conservative marker-1 path
because the current ABI does not distinguish `Bool` from `UInt8` by storage
width alone. Probing is bounded and wraps scalarly; grouped probing,
insertion, growth, and rehashing remain open.
This advances the formal score to **54/60 (90.0%)**; the remaining gates are
group-wide probing, one-byte/non-integer hashing, insertion, growth, and
rehashing.

## D236 - Self-host Group Control Scans

The self-host LLVM indexed lookup paths now inspect an eight-slot control-byte
group before falling back to the scalar probe. The normal expression path,
region path, and entry path all use the same wrapped group window and bounded
probe accounting. Existing key equality and value lowering remain unchanged,
so the group scan is an acceleration and candidate-preservation layer rather
than a new dictionary ABI. The complete Swiss-table mutation algorithm is
still open: matching candidates must be selected directly, and insertion,
growth, rehashing, and one-byte/non-integer hashing remain pending.
Examples 548 and 550 pass self-host LLVM verification on Windows and Linux;
the full self-host suite passes **341/341**. The formal score remains
**54/60 (90.0%)** until the mutation and remaining key-family gates are proven.

## D237 - Self-host Integer Dictionary Put

The self-host typed IR now recognizes mutable dictionary `put` as intrinsic
opcode `-223`, and all three LLVM emission paths lower the operation for
`Int` keys and values. The first mutation vertical allocates a larger table,
rehashes each occupied entry with the existing H2 layout, inserts a missing
key or replaces an existing value, releases the old buffers, and stores the
new dictionary aggregate back into its mutable slot.

Sequential mutation must observe the latest aggregate rather than a binding
value loaded before an earlier effect. `put` and mutable dictionary indexing
therefore load the dictionary from its canonical slot at their execution
point. This avoids both lost updates and use-after-free when two `put`
operations appear in one expression region, without making `put` a global
scheduler barrier.

Example 553 inserts `3: 30`, updates `2` to `222`, and prints both values.
LLVM assembly, linking, and execution pass on Windows and Linux. The mutable
dictionary lookup snapshots in examples 452, 454, 457, 459, and 461 were
updated for the explicit slot load, and the complete Windows self-host suite
passes **342/342**. This remains a first vertical: it currently specializes
`Int`/`Int`, grows on every mutation, and uses scalar insertion/rehashing.
Threshold-based growth, direct grouped-candidate selection, and the remaining
one-byte/non-integer key families stay open, so formal progress remains
**54/60 (90.0%)**.

## D238 - Swiss-table Load-factor Growth

Sollang adopts the production Swiss-table resize boundary documented by
Abseil: a maximum load factor of 87.5%, followed by capacity doubling. The
self-host `Int` dictionary `put` path now probes the current table before any
allocation. A matching key updates its value in place. A missing key inserts
into the discovered empty slot while `(length + 1) * 8 <= capacity * 7`; only
an insertion beyond that boundary allocates a table with doubled capacity and
rehashes occupied entries. Capacities below four grow to four so the first
allocation also establishes useful probe space.

This ordering is semantically important as well as faster: replacement keeps
key/value addresses and the dictionary aggregate stable, while insertion
changes only the length until growth is actually required. Example 554 drives
the 2-to-4 and 4-to-8 growth paths, then proves an in-place insertion and an
in-place replacement in the same dictionary. It prints `30`, `40`, `50`, and
`222` after LLVM assembly, linking, and execution on Windows and Linux. The
complete Windows self-host suite passes **343/343**.

Direct matching-candidate selection from the grouped control scan, generic
key/value lowering, tombstones, and one-byte/non-integer hashing remain open,
so formal progress stays **54/60 (90.0%)**.

Research basis:

- [Abseil container guide](https://abseil.io/docs/cpp/guides/container)
- [Abseil Performance Tip #90](https://abseil.io/fast/90)
- [hashbrown `HashTable::reserve`](https://docs.rs/hashbrown/latest/hashbrown/struct.HashTable.html#method.reserve)

## D239 - Direct Group Candidate Selection

The self-host dictionary lookup paths now carry an eight-control H2 match all
the way to the candidate entry. Each normal, region, and entry lookup builds a
lowest-match offset from the eight comparison bits, wraps `probe + offset` by
the dictionary capacity, and uses that candidate slot for both key equality
and value access. A false key equality returns to the existing scalar advance,
so a later entry with the same H2 fingerprint remains reachable.

This replaces the D236 transitional behavior that detected a match somewhere
in the group but still tested only the group's first slot. Existing example
190 exercises a wrapped nonzero candidate offset, while examples 548 and 550
prove pointer and enum-reference values through the same lookup. Examples 190,
548, 550, 553, and 554 assemble, link, and execute on Windows and Linux, and
the complete Windows self-host suite passes **343/343**.

Sollang currently materializes the eight comparison bits and candidate
selection as scalar LLVM operations; later target optimization may combine
them, while a future wider control-group ABI may expose a native mask directly.
Tombstones, generic key/value mutation, and one-byte/non-integer hashing remain
open, so formal progress stays **54/60 (90.0%)**.

Research basis:

- [Abseil Swiss Tables Design Notes](https://abseil.io/about/design/swisstables)

## D240 - Signed and Unsigned Byte-Key Hashing

The reference semantic compiler now accepts every fixed-width integer family
as a dictionary key, while the self-host semantic conversion tables use the
canonical builtin order (`Int8` type 3 and `UInt8` type 8). Dictionary literal
and indexed-lookup lowering derive one-byte hash width from the semantic integer
symbol rather than treating every one-byte key as a non-hashable fallback.

Signed keys sign-extend and unsigned keys zero-extend before the shared 64-bit
mix. The normal, region, and entry emitter paths apply the same rule. Example
555 proves `Int8(-7)`, `UInt8(250)`, and a region-local `Int8(-9)` dictionary;
its LLVM assembles, links, and executes on Windows and Linux, and differential
verification against the C# compiler passes on both targets. The complete
self-host suite passes **345/345** on Windows and Linux. Wider integer families
already use their storage widths, while non-integer hashing and generic mutation
remain open. Formal progress stays **54/60 (90.0%)**.

## D241 - Dictionary Tombstones Preserve Probe Chains

The self-host LLVM dictionary now uses three control-byte states without
changing the four-field `%sollang.dict` ABI: `0` is empty, `1..127` is an H2
fingerprint, and `255` is deleted. Dictionary `take` hashes and probes the key,
returns the value, drops an owned key when required, writes the deleted marker,
and decrements the live length without shifting unrelated slots. Lookup passes
over deleted controls and still terminates at an empty control.

The `put` probe remembers the first deleted slot, continues searching for an
existing equal key, and reuses that slot only when insertion is required. A
full probe also reuses a remembered tombstone instead of growing. Rehash copies
only positive H2 controls, so deleted storage is compacted naturally. Mutable
dictionary `len` now reloads its source slot at the extraction point, preventing
a pre-mutation SSA snapshot from hiding the updated length.

Example 556 uses colliding integer keys `1`, `5`, and `9`: after taking `1`, it
still finds `5`, reuses the tombstone for `9`, and reports length two. The
generated LLVM assembles, links, and executes on Windows and Linux. Generic
key/value mutation and non-integer hashing remain open, so formal progress stays
**54/60 (90.0%)**. The combined self-host suite passes **345/345** on Windows
and Linux.

Research basis:

- [Abseil Swiss Tables Design Notes](https://abseil.io/about/design/swisstables)
- [`hashbrown::HashTable`](https://docs.rs/hashbrown/latest/hashbrown/struct.HashTable.html)

## D242 - Cross-process Example Artifact Locks

The example runner now serializes only colliding work across runner processes.
A repository-scoped named mutex protects the reusable self-host compiler
bootstrap, and a repository/target/test-scoped mutex protects each expected or
diagnostic artifact path. Different tests remain parallel inside and across
processes; only two invocations of the same case wait for one another.

This fixes `.slg-tmp` deletion and temporary object rename failures observed
when two sessions ran the same self-host case concurrently. Two simultaneous
Release-runner invocations of example 410 both pass. The Release solution build
completes with zero warnings and zero errors, and the mutex-enabled Windows
self-host suite passes **345/345**. This reliability checkpoint does not change
the formal **54/60 (90.0%)** language-capability score.

## D243 - Deterministic Bootstrap Hashing Behind a Runtime Boundary

Sollang separates the meaning of a hashable key from the table's concrete hash
policy. `Hash` and `Eq` remain the key contract, including the invariant that
equal keys must produce equal hashes. The self-host bootstrap now lowers `Text`
keys through shared LLVM helpers: a byte-wise 64-bit FNV-1a hash matching the
C# reference emitter and a length-plus-byte equality comparison. Dictionary
literals and indexed lookups use the same helpers in normal function, region,
and entry paths. The helpers are emitted only when a canonical dictionary has a
`Text` key, so unrelated dictionary modules and snapshots do not grow.

The deterministic algorithm is a bootstrap and reproducible-code-generation
policy, not a claim of collision-attack resistance. Rust and Swift use random
seeds for their general-purpose runtime maps, while Zig makes the hash/equality
context explicit and currently gives its string context a deterministic
Wyhash seed. Sollang therefore keeps the generated call boundary suitable for
a future table-owned runtime seed without making compiler output depend on
process entropy today.

Example 557 proves Text-key literals and lookups in function, entry, and region
paths. LLVM validation, link, execution, and C# versus self-host differential
verification pass on Windows and Linux. The complete self-host suite passes
**347/347** on both targets. Other non-integer key families and generic mutation
remain open, so formal progress stays **54/60 (90.0%)**.

Research basis:

- [Rust `HashMap`](https://doc.rust-lang.org/std/collections/hash_map/struct.HashMap.html)
- [Swift `Hasher.init()`](https://developer.apple.com/documentation/swift/hasher/init%28%29)
- [Zig `hash_map.zig`](https://github.com/ziglang/zig/blob/master/lib/std/hash_map.zig)

## D244 - Fixed-width Integer Dictionary Mutation Uses Canonical Types

The self-host `put` lowering no longer assumes four-byte signed keys and
values. It resolves the canonical dictionary key and value type IDs once, then
uses their LLVM type, storage size, alignment, integer width, and signedness for
lookup, in-place replacement, insertion, allocation, and rehashing. Signed keys
are sign-extended and unsigned keys are zero-extended into the common 64-bit
hash input; 64-bit keys remain unchanged. This keeps the control-byte layout
and four-field dictionary ABI independent of primitive element width.

Nested conversion calls exposed a separate typed-IR defect. The intrinsic name
scanner used a Boolean `inside arguments` flag, so the first inner `)` in
`put(UInt64(9), Int16(90))` ended the outer argument list early and selected
`Int16` as the flow target. It now tracks parenthesis and type-argument depth,
preserving the `put` opcode through nested arguments.

Example 558 covers `Int8: UInt64`, `UInt64: Int16`, and `UInt16: Int8`
dictionaries across normal function, entry, and region emitters. Insertion,
replacement, load-factor growth, rehashing, lookup, LLVM validation, linking,
execution, and C# versus self-host differential verification pass on Windows
and Linux. The complete self-host suite passes **347/347** on both targets, and
the Release solution build has zero warnings and zero errors. Owned/composite
key and value mutation still requires transfer,
replacement-drop, equality, and hashing integration, so generic mutation is not
closed and formal progress remains **54/60 (90.0%)**.
