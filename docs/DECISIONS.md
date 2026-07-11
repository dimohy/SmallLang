# SmallLang Decision Log

This file records accepted or working decisions so the language design can
evolve without losing context.

## D001 - Specification Before Implementation

Status: accepted
Date: 2026-07-07

SmallLang remains in specification mode until the user explicitly asks for
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

SmallLang compiles through LLVM and ultimately produces highly optimized native
executables. The language and compiler pipeline should be designed around
efficient LLVM lowering rather than treating LLVM as an afterthought.

## D004 - First Program Syntax

Status: working decision
Date: 2026-07-07

The first complete SmallLang program is:

```smalllang
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

```smalllang
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

```smalllang
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

## D011 - Lexer Source Generation From SmallLang Rules

Status: accepted
Date: 2026-07-07

SmallLang lexer rules are expressed in `syntax/smalllang.lexer`. A Roslyn incremental
source generator in `src/SmallLang.Compiler.Generators` reads that rules file and
generates `TokenKind` and `Lexer` during compiler build. This keeps the language
surface concise and regular while producing deterministic C# tokenization code.

## D012 - Parser Source Generation From SmallLang Grammar

Status: accepted
Date: 2026-07-07

SmallLang parser rules are expressed in `syntax/smalllang.grammar`. A Roslyn incremental
source generator reads that grammar file as an MSBuild `AdditionalFiles` input
and emits the current token-to-AST parser during compiler build.

ANTLR, parser combinators, and C# embedded parser generators remain valid future
options, but they are not the best fit for the first SmallLang slice. ANTLR adds a
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

SmallLang adopts `value -> function()` as the preferred call style when a primary
input value flows into a function:

```smalllang
"Hello, $name" -> print()
```

This form makes data flow visually explicit. The expression on the left is the
first input to the callable path on the right. For the initial unary case, it is
semantically equivalent to:

```smalllang
print("Hello, $name")
```

Parenthesized calls remain valid as a conventional compatibility syntax and for
cases where the value-flow form is not expressive enough. The preferred SmallLang
style is value-flow first:

```smalllang
result = value -> transform
```

Function type notation should use the same left-to-right direction:

```smalllang
print: Text -> Io<Unit>
```

The first implementation lowered unary value-flow calls to the same call AST
shape used by the existing parenthesized call. D016 keeps value-flow as its own
AST node so a final target can bind the result.

## D014 - Initial Integer Addition And Scalar Interpolation

Status: accepted
Date: 2026-07-07

SmallLang supports decimal integer literals and left-associative integer `+` in the
current compiler slice:

```smalllang
sum = 20 + 22
```

The first numeric model is intentionally narrow. Integer literals evaluate to
signed 64-bit values in the semantic evaluator, and `+` performs checked integer
addition. Mixed string/integer `+`, floating point values, arbitrary precision
numbers, suffixes, and final overflow policy syntax are not decided yet.

String interpolation can display integer bindings:

```smalllang
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

```smalllang
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
backend emits `@smalllang_fn_getName`, `@smalllang_fn_getNum`, runtime `i64` addition,
segmented `WriteFile` output, and a runtime integer decimal conversion helper
instead of one full static output string. D016 is the current sample and
supersedes the `getNum` naming.

## D016 - Flow Binding And One-Input Square Function

Status: implemented, superseded by D036
Date: 2026-07-07

SmallLang adopts statement-level value-flow binding for the current sample:

```smalllang
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
`square: Int -> Int { it * it }`, emits `@smalllang_fn_square(i64 %it)`, lowers the
body to `mul nsw i64 %it, %it`, and calls it as `@smalllang_fn_square(i64 7)`.
The verified output at D016 time was `Hello, dimohy. square = 49`; the Windows
x64 executable size at D016 time remained 1,088 bytes.

## D017 - Input Primitive And Inclusive Range Loop

Status: implemented, loop spelling superseded by D019
Date: 2026-07-08

SmallLang samples are cumulative. New samples should be added alongside earlier
samples instead of replacing `examples/01-function-basic-hello.sl`.

The next implemented sample reads an integer and prints that multiplication
table:

```smalllang
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

```smalllang
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
bytes for `examples/01-function-basic-hello.sl` and 1,584 bytes for
`examples/07-block-each-explicit-item.sl`.

## D018 - Arrow Binding As Preferred Assignment Direction

Status: accepted
Date: 2026-07-08

SmallLang should prefer arrow-oriented binding even for local assignment-like
introductions. New samples should use:

```smalllang
expression -> name
n * i -> value
```

instead of leading with:

```smalllang
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

SmallLang's preferred range loop syntax is now flow-oriented:

```smalllang
1..9 -> each i {
    n * i -> value
    "$n x $i = $value" -> println()
}
```

The range expression flows into `each`, and the optional identifier after `each`
names the current item. When the identifier is omitted, the loop item is bound as
`it`:

```smalllang
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

SmallLang one-input functions now follow the same naming shape as `each`.

When the input name is omitted, the function body receives the value as `it`:

```smalllang
square: Int -> Int {
    it * it
}
```

When the input name is supplied after the function name, the body receives the
value through that binding:

```smalllang
square n: Int -> Int {
    n * n
}
```

This mirrors the loop forms:

```smalllang
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

```smalllang
1..9 -> each i {
    n * i -> value
    "$n x $i = $value" -> println()
}
```

Semantically, the range value flows into the block function `each`, `i` names the
block invocation input, and the brace body is the executable block argument.
The default item form follows the same rule:

```smalllang
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

## D022 - `sys.io` Is A SmallLang Standard Library Module

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

The actual `sys.io` module is implemented in SmallLang:

```smalllang
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

```smalllang
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

The compiler loads `stdlib/sys/runtime.sl` and `stdlib/sys/io.sl` before user
source, then adds only alias entries for `print`, `println`, and `readInt`.
The semantic model resolves `sys.io` through the same function table as user
functions. The Windows LLVM backend inlines standard library wrappers and lowers
only the `sys.runtime` intrinsic boundary to direct `ReadFile`/`WriteFile`
runtime code, so verified IR does not emit `sys.io` function calls.

## D023 - Local Functions Inside Functions

Status: implemented
Date: 2026-07-08

Function declarations may appear at the start of another function body:

```smalllang
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
paths on every function declaration and runtime call. SmallLang now accepts an
optional file-level namespace declaration followed by import declarations:

```smalllang
namespace sys.io

import sys.runtime as rt
```

Within a namespaced file, top-level single-segment function declarations are
qualified by that namespace:

```smalllang
print value: Text -> Unit {
    value -> rt.print()
}
```

This defines `sys.io.print`. The import alias rewrites `rt.print` to
`sys.runtime.print` before semantic analysis, so the semantic model and LLVM
lowering still work with fully qualified canonical names.

The lower runtime boundary uses the same namespace rule:

```smalllang
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

SmallLang now supports a `linux-x64` compiler target in addition to the default
`windows-x64` target:

```powershell
.\scripts\smalllang.ps1 -Source examples\01-function-basic-hello.sl -Output artifacts\01-function-basic-hello-linux -Target linux-x64 -KeepTemps
```

The compiler selects the LLVM target triple from the requested target. Windows
continues to emit `x86_64-pc-windows-msvc`, import `GetStdHandle`/`ReadFile`/
`WriteFile`, expose the native entry point as `smalllang_start`, and link with
`lld-link` without the C runtime.

Linux emits `x86_64-unknown-linux-gnu`, imports libc `read` and `write`, exposes
the native entry point as `main`, and links through WSL. The link path uses the
downloaded Windows LLVM `clang` to produce a Linux ELF object, then invokes WSL
`cc` to produce the final executable. This keeps one Windows-hosted compiler
bootstrap while still validating the final binary inside Linux.

The Linux runtime backend lowers `sys.runtime.print`, `sys.runtime.println`, and
`sys.runtime.readInt` to direct `write`/`read` calls. Standard library `sys.io`
wrappers are still ordinary SmallLang functions and are inlined before the
intrinsic runtime boundary is lowered.

Verification on WSL produced ELF x86-64 executables for `01-function-basic-hello.sl`,
`09-namespace-sys-io.sl`, and `05-function-local.sl`. The `01-function-basic-hello` sample printed
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
- byte-level `smalllang_write`
- byte-level `smalllang_read_stdin`

`WindowsLlvmRuntimePlatform` supplies `GetStdHandle`, `ReadFile`, and
`WriteFile`. `LinuxLlvmRuntimePlatform` supplies libc `read` and `write`.
The linker layer remains target-specific: `WindowsLinker` uses `lld-link`, and
`WslLinuxLinker` uses Windows LLVM `clang` for the ELF object followed by WSL
`cc` for the final Linux executable.

## D027 - Flow-Oriented Conditionals And Bool Expressions

Status: implemented
Date: 2026-07-08

SmallLang adopts conditionals that match the existing value-flow style instead
of adding a separate parenthesized statement form:

```smalllang
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

For ordered multi-branch value selection, SmallLang uses `when`:

```smalllang
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
condition is unnecessary noise. SmallLang now supports a subject-value form that
keeps the existing flow direction:

```smalllang
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

SmallLang now supports the expression basics needed before broader library and
control-flow work:

- parenthesized expressions
- integer `+`, `-`, `*`, `/`, `%`
- unary integer `-`
- line comments beginning with `#`

The parser keeps the usual precedence shape: parentheses, unary operators,
multiplicative operators, additive operators, comparison, logical `and`, and
logical `or`. The current arithmetic slice is integer-only. LLVM lowering emits
`add`, `sub`, `mul`, `sdiv`, and `srem` for the new operations.

`examples/06-expression-arithmetic-comments.sl` verifies parentheses, comments, division,
modulo, and `not (...)` grouping.

## D030 - Integer Fold Block Function

Status: implemented
Date: 2026-07-08

`fold` is the second built-in block function and returns a value:

```smalllang
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

`examples/13-block-fold-sum.sl` verifies `1..100 -> fold 0 sum, i { sum + i }` and prints
`sum = 5050`.

## D031 - Subject When Range Arms

Status: implemented
Date: 2026-07-08

Subject-value `when` can now use inclusive integer range arms:

```smalllang
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

`examples/17-condition-when-range.sl` verifies this form.

## D032 - Expected Stdout Example Tests

Status: implemented
Date: 2026-07-08

Executable samples now have a lightweight expected-output test runner. Expected
fixtures live under `examples/expected` as:

- `{sample}.stdout.txt`
- optional `{sample}.stdin.txt`

The runner is a no-dependency .NET console project at
`tests/SmallLang.ExampleTests`. It compiles each listed sample through
`scripts/smalllang.ps1`, executes the generated Windows binary sequentially, and
compares normalized stdout exactly against the fixture.

Current verified fixtures cover arithmetic/comments, subject `when`, range-arm
`when`, `fold`, `08-block-each-default-it` input/default loop item behavior, and local
functions. The runner is included in `SmallLang.slnx` and is invoked with:

```powershell
dotnet run --project tests\SmallLang.ExampleTests\SmallLang.ExampleTests.csproj --no-build
```

## D033 - Compact Function And When Expression Bodies

Status: implemented
Date: 2026-07-08

To avoid nested braces for single-expression functions whose body is a `when`,
SmallLang now allows expression-bodied function declarations:

```smalllang
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

```smalllang
condition -> value
else -> fallback
```

Block arms remain valid when the arm needs statements before the final value.

Subject-style `when` arms can also omit an explicit subject inside a one-input
function that uses the default input binding `it`. Explicitly named inputs keep
the subject visible:

```smalllang
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

`examples/18-condition-when-compact.sl` verifies both compact forms.

## D034 - Purpose-Oriented Sorted Int File Workflow

Status: implemented
Date: 2026-07-08

SmallLang now supports the first large-data workflow requested by the user:
generate 100,000,000 pseudo-random numbers from `1..1,000,000,000`, store them
sorted in a file, and query the value closest to `500,000,000`.

The implementation deliberately avoids opening a general array/sort feature
first. The generator uses a sorted bucket strategy:

```smalllang
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

- `examples/19-stdlib-random-file-demo-generate.sl` produced
  `artifacts/random-sorted-demo.i64` with 1,000 records / 8,000 bytes.
- `examples/20-stdlib-file-demo-query.sl` printed `closest = 4995`; independent
  PowerShell binary-search verification matched.
- `examples/21-stdlib-random-file-100m-generate.sl` produced
  `artifacts/random-sorted-100m.i64` with 100,000,000 records / 800,000,000
  bytes.
- `examples/22-stdlib-file-100m-query.sl` printed `closest = 500000006`;
  independent verification found candidates `499999991` and `500000006`, so
  the closest difference is `6`.
- The demo generator/query pair also compiled and ran on `linux-x64` through WSL,
  with query output `closest = 4995`.
- `dotnet build SmallLang.slnx` passed with 0 warnings and 0 errors.
- `tests/SmallLang.ExampleTests` still passed all 7 existing expected-stdout
  tests.

## D035 - Empty Parentheses On Value-Flow Function Targets

Status: implemented
Date: 2026-07-08

SmallLang accepted empty call syntax on value-flow function targets:

```smalllang
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

`examples/03-flow-call-parens.sl` verifies the accepted syntax. Verification:
`dotnet build SmallLang.slnx` passed, `tests/SmallLang.ExampleTests` passed all
8 expected-stdout samples, `7 -> square(7)` failed in parsing with an empty
parentheses-only diagnostic, and `7 -> value()` failed semantically as an
unknown function instead of becoming a binding.

## D036 - Function Calls Require Parentheses In Flow Syntax

Status: implemented
Date: 2026-07-08

D035 allowed `-> func()` but still accepted the older `-> func` function-call
target. That left the function/binding distinction visually ambiguous. SmallLang
now requires functions to be visibly called:

```smalllang
getName() -> name
7 -> square() -> num
"Hello, $name. square = $num" -> print()
```

The old flow-call spelling is now rejected:

```smalllang
7 -> square -> num
```

Semantic analysis resolves a target path that names a function only when
`UsesCallSyntax=true`; otherwise it reports that the function target must use
`func()`. A final bare single identifier without `()` remains a binding target,
so `value -> name` still introduces a binding. Bare zero-input function names in
expression/source position are no longer treated as implicit calls; use
`getName()`.

All `.sl` examples and standard-library wrappers were updated to use `func()` for
function targets. Verification: all 19 example `.sl` files compiled, the 8
expected-stdout samples passed, `7 -> square -> num` failed with the required
`square()` diagnostic, and `getName -> name` failed as a missing function-call
parentheses case.

## D037 - Block Arguments Omit Empty Function Parentheses

Status: implemented
Date: 2026-07-08

D036 requires ordinary function targets to use `func()` in value-flow syntax, but
code block arguments are intentionally an exception. When the target is followed
by a brace block, the block itself marks the target as a function-like call:

```smalllang
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

To make code-block arguments visible outside range iteration, SmallLang now
supports a second built-in block function:

```smalllang
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
01-function-basic-hello.sl
02-function-named-input.sl
03-flow-call-parens.sl
04-main-omitted-top-level.sl
...
22-stdlib-file-100m-query.sl
```

Expected stdout/stdin fixtures under `examples/expected` use the same basename as
their source example. `scripts/smalllang.ps1` defaults to
`examples/01-function-basic-hello.sl`. New cumulative examples should continue
this naming style instead of appending unnumbered names.

## D041 - User-Defined Block Functions

Status: implemented
Date: 2026-07-08

Block functions are no longer limited to built-ins. A user-defined block function
declares a normal input, a `Unit` return, and a block input:

```smalllang
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

SmallLang now includes a local declarative VS Code language support extension
under `tools/vscode-smalllang`. It registers `.sl` as `smalllang`, contributes a
TextMate grammar for comments, strings, interpolation, function declarations,
flow calls, block-function calls, keywords, types, constants, numbers, and
operators, and provides snippets for `main`, functions, flow calls, `each`,
`repeat`, and `when`.

Install locally with:

```powershell
Push-Location tools\vscode-smalllang
npx --yes @vscode/vsce package --no-dependencies --allow-missing-repository
code --install-extension .\smalllang-language-support-0.1.2.vsix
Pop-Location
```

## D042 - Browser WebAssembly Target

Status: implemented
Date: 2026-07-09

SmallLang now supports a browser WebAssembly target in addition to the native
Windows and Linux targets:

```powershell
.\scripts\smalllang.ps1 -Source examples\23-webassembly-browser.sl -Output artifacts\23-webassembly-browser.wasm -Target wasm32-browser -KeepTemps
python -m http.server 5080
```

The compiler emits LLVM IR with the `wasm32-unknown-unknown-wasm` target triple,
uses the existing common LLVM lowering for functions, flow bindings,
interpolation, conditionals, `fold`, and output calls, compiles the IR with
Windows LLVM `clang`, and links the final `.wasm` with `wasm-ld`.

The generated module exports `smalllang_start` and `memory`. It imports a single
browser-hosted output boundary:

```text
env.smalllang_browser_write(ptr, len) -> i32
```

The static runner under `examples/browser` implements this import by reading
UTF-8 bytes from exported linear memory and appending them to the page output.
The current browser target intentionally supports stdout-style text output
first. `readInt` and sorted-int file runtime primitives are present as explicit
failure stubs for this target rather than silently mapping to browser prompts or
storage.

`examples/23-webassembly-browser.sl` is cumulative and also runs as a native
example. Its expected output fixture verifies:

```text
Hello from SmallLang WebAssembly
8 squared = 64
1..5 sum = 15
```

## D043 - Arrays Without A Garbage Collector

Status: working decision
Date: 2026-07-09

SmallLang's static and dynamic array design should follow Rust's ownership
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

Dynamic arrays use a Rust `Vec<T>`-like internal model, but the SmallLang source
surface is `[T; ~]`. The value owns payload storage, length, and capacity. Heap
storage is the normal placement; D063 later permits proven local readonly
literals to use stack storage. Moving a dynamic array moves ownership of the
buffer. Dropping a dynamic array deallocates heap-placed storage only. A dynamic
array is not implicitly copied, reference-counted, or garbage-collected.

Dynamic array literals use an open tail marker:

```smalllang
[1, 2, 3, ~] => values!
```

SmallLang should not use `{ ... }` for dynamic arrays. Braces already delimit
blocks and remain the better future fit for dictionaries or maps. Dynamic arrays
stay in the `[]` syntax family and use `..` to mean open/growable.

Memory leak prevention is a compile-time guarantee for safe SmallLang, not a
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

SmallLang should introduce explicit mutable bindings with the existing
flow-first binding direction:

```smalllang
[Int; ~] => values!
values! -> push(10)
99 => values![1]
```

Array support should also extend value-flow target calls to allow additional
arguments. The value on the left remains the primary first argument, and
parentheses on the target may contain extra arguments:

```smalllang
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
SmallLang's safe surface must be stricter than Rust because Rust explicitly
allows leak-safe constructs such as `mem::forget` and can leak through
reference-count cycles. SmallLang should therefore keep safe `forget`, safe
`leak`, raw owning allocation, implicit shared ownership, and unproven cyclic
ownership out of the safe language surface.

Zig is a useful allocator reference because it makes allocation explicit and
keeps memory-management responsibility visible, but that is not enough for
SmallLang's goal: safe SmallLang must statically prove owner/drop coverage
rather than leaving leak prevention to programmer discipline or test-time leak
detection. Austral's linear-resource checking is a better reference for the
strict part of the design: values that own resources must be consumed exactly
once, and a function cannot return while owned linear resources remain
unconsumed.

The same research argues against using `func!` as the ordinary function-call
marker. Rust already uses `name!(...)` for macros, Elixir uses trailing bang for
raising function variants, and Julia uses trailing bang for mutating functions.
SmallLang should avoid assigning ordinary function-call meaning to `!`.

The preferred next syntax direction is to remove empty parentheses from
value-flow calls when the left value is the only explicit input:

```smalllang
getName() => name
7 -> square => num
values -> len => count
```

Parentheses remain useful when additional arguments are present:

```smalllang
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

SmallLang now gives the two arrows separate jobs:

```text
->   flow/apply/transform
=>   bind/define/resolve
```

The parser accepts receiver-only value-flow calls without empty parentheses:

```smalllang
7 -> square => num
"Hello, $num" -> println
```

The previous empty-parentheses form remains accepted as compatibility syntax,
but it is no longer the preferred style:

```smalllang
7 -> square() => num
```

Statement-level bindings now use `=>`:

```smalllang
getName() => name
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

```smalllang
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

```smalllang
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
used by normal SmallLang source. Semantic analysis checks that interpolated
expressions are displayable (`Text` or `Int` in the current slice), and LLVM
emission writes each interpolated value with the normal runtime value output
path.

## D047 - First Int Containers With Deterministic Native Drop

Status: implemented
Date: 2026-07-09

SmallLang now has the first `Int` container slice:

```smalllang
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
not mean SmallLang objects are mutable by default. Immutable bindings remain the
default:

```smalllang
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
  SmallLang because there is no garbage collector.
- Add benchmarks before replacing the lowering: repeated append, random update,
  iteration/fold throughput, and dictionary update/lookup.

General coding rule: a feature may be implemented functionally first, but if
speed or memory work is intentionally deferred, the implementation must leave a
durable optimization note in the repo that names the tradeoff and the intended
follow-up direction.

## D050 - Dictionaries Use Scalar Swiss-Style Hash Tables

Status: implemented
Date: 2026-07-09

SmallLang's `{Int: Int}` dictionary lowering no longer uses a contiguous
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
SmallLang implementation scans scalar control bytes instead of SIMD groups.

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

```smalllang
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
but applies it to SmallLang's mutable owner name instead of ordinary function
names. This avoids spending `!` on normal calls and keeps `-> push` readable as
a receiver operation while the receiver itself carries the mutation signal.

Indexed assignment is now implemented for current `Int` containers:

```smalllang
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

SmallLang now supports typed empty literals for the current `Int` container
slice:

```smalllang
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

```smalllang
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

```smalllang
1..3 -> each i {
    [Int; 2~] => row!
    row! -> push(i)
}
```

When the block's final expression returns a block-local owner, the block does
not drop that owner. Ownership and the drop obligation move to the surrounding
binding:

```smalllang
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

```smalllang
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

```smalllang
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

```smalllang
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

```smalllang
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

```smalllang
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

```smalllang
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

```smalllang
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

The ABI representation remains `%smalllang.int_dictionary = { ptr, i64, i64 }`.
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

```smalllang
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
scope cleanup emits `smalllang_free` only for heap storage. Example 38 verifies
both the program output and LLVM requirements: the stack allocation must be
present, while calls to `smalllang_alloc` and `smalllang_free` must be absent.
Because this path needs no allocator, the same example also compiles for the
browser WebAssembly target.

## D064 - Automatic Stack Promotion For Readonly Int Dictionaries

Status: implemented
Date: 2026-07-10

Small nonempty `{Int: Int}` literals now join dynamic arrays in automatic
storage placement analysis:

```smalllang
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
storage, and deterministic drop emits `smalllang_free` only for heap storage.

D063's 4096-byte function-frame budget is now shared by promoted dynamic-array
and dictionary payloads. The dictionary's full table allocation, rather than
only its live entry count, is charged to that budget. Example 39 checks the
expected 72-byte and 136-byte stack blocks and rejects generated calls to
`smalllang_alloc` or `smalllang_free`. It also compiles for browser WebAssembly
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

Local functions and standard-library SmallLang wrappers are emitted inline and
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

```smalllang
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
`%smalllang.struct.1024 = type { i64, i64 }`. Literals use `insertvalue`, field
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

SmallLang avoids empty call parentheses. A readonly method with no additional
arguments is a computed member and uses uniform member access:

```smalllang
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
properties, and SmallLang's existing flow calls. It keeps query expressions
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

```smalllang
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

```smalllang
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

```smalllang
identity[T] value: T -> T { value }
measureOf[T: Measure] value: T -> Int { value -> Measure.measure }
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
`smalllang_free`. These helpers are statically selected by concrete type and do
not use metadata or vtables. Examples 48 and 49 verify single-owner box transfer,
readonly repeated access, recursive enum destruction, copy rejection, and
use-after-move rejection.

## D071 - Explicit Compile-Time Int Value Generics

Status: implemented
Date: 2026-07-11

A global function may declare one compile-time `Int` value parameter:

```smalllang
sumFilled[N: Int] value: Int -> Int {
    [value; N] => values
    values -> fold 0 total, item { total + item }
}
```

The value argument is explicit at the fluent call boundary, such as
`7 -> sumFilled[3]`. It is not passed at runtime. Every used value produces a
separately checked and emitted specialization, and symbolic fixed-repeat counts
become LLVM constants in that specialization. Omitting the value argument is a
compile error. Example 50 verifies distinct `3` and `5` specializations and
their fixed LLVM array shapes.

Value parameters also participate in fixed-array input types:

```smalllang
fixedLength[N: Int] values: [Int; N] -> Int {
    values -> len
}

[10, 20, 30] -> fixedLength[3]
```

`[Int; 3]` and `[Int; 5]` are distinct compile-time size contracts at this
boundary even though the readonly native ABI remains the compact `{ pointer,
length }` slice pair. The compiler proves that the fixed source length equals
the explicit specialization value before emitting a call. A dynamic array or a
fixed array with another length is rejected. Example 51 verifies the two valid
specializations and a `3` versus `4` size-mismatch diagnostic.


