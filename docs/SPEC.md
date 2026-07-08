# SmallLang Language Specification Draft

Status: draft
Date: 2026-07-07

This document is the living specification for SmallLang. It records the language
shape before implementation so design decisions do not get lost.

## Current Boundary

SmallLang implementation has started for the smallest approved language slice.

The implementation boundary is intentionally narrow:

- one `main` block
- zero-argument and one-input expression functions
- local string bindings with `name = value`
- integer bindings with decimal integer literals
- left-associative integer `+` and `*`
- simple string interpolation with `{name}`
- value-flow calls and result bindings with `value -> function -> name`
- parenthesized calls with `function(value)`
- integer input with `readInt`
- line output with `println`
- closed integer range loops with `each item in start..end { ... }`
- Windows x64 native executable output through LLVM

Anything beyond that remains specification work until explicitly approved.

## Core Goals

- Provide a beautiful, simple, and powerful source language.
- Parse and tokenize source code very effectively.
- Compile through LLVM into highly optimized native executables.
- Keep the compiler pipeline efficient from source text to final executable.
- Support cross-platform native output from the beginning.
- Prefer clear compile-time errors over silent fallback behavior.
- Use the latest .NET and latest C# Preview features for the compiler
  implementation unless a later constraint requires otherwise.

## Non-Goals For The Current Phase

- No implementation beyond the explicitly approved current slice.
- No final type system.
- No final memory model.
- No final module/package system.
- No finalized LLVM binding choice.
- No runtime design beyond the minimum needed to specify initial output.

## Compilation Model

The intended compilation pipeline is:

```text
source text
-> lexer
-> parser
-> AST
-> semantic model
-> typed IR
-> LLVM IR
-> optimized object code
-> linked native executable
```

The pipeline must avoid unnecessary intermediate forms. Each stage should exist
because it makes diagnostics, semantic analysis, optimization, or LLVM lowering
meaningfully better.

## First Program

The first valid SmallLang program is:

```smalllang
main {
    name = "dimohy"
    print("Hello, {name}")
}
```

Expected stdout bytes:

```text
Hello, dimohy
```

`print` does not append a newline. `println` is the current newline-producing
convenience.

The current extended example is:

```smalllang
getName: -> Text {
    "dimohy"
}

square: Int -> Int {
    it * it
}

main {
    getName -> name
    7 -> square -> num
    "Hello, {name}. square = {num}" -> print
}
```

Expected stdout bytes:

```text
Hello, dimohy. square = 49
```

The current cumulative input and loop example is:

```smalllang
main {
    "n = ? " -> readInt -> n

    each i in 1..9 {
        value = n * i
        "{n} x {i} = {value}" -> println
    }
}
```

With stdin `9`, the expected stdout bytes are:

```text
n = ? 9 x 1 = 9
9 x 2 = 18
9 x 3 = 27
9 x 4 = 36
9 x 5 = 45
9 x 6 = 54
9 x 7 = 63
9 x 8 = 72
9 x 9 = 81
```

## Initial Syntax Direction

SmallLang starts with an explicit `main` block instead of a fully general function
declaration. Local bindings do not use `let`, `var`, or a declaration keyword:

```smalllang
getName: -> Text {
    "dimohy"
}

square: Int -> Int {
    it * it
}

main {
    getName -> name
    7 -> square -> num
    "Hello, {name}. square = {num}" -> print
}
```

Rationale:

- `main { ... }` is shorter than `fn main() { ... }`.
- `name = value` is the smallest readable binding form.
- `"Hello, {name}"` keeps string interpolation direct and familiar.
- `it * it` introduces the smallest one-input numeric function without deciding the
  final numeric tower.
- `getName: -> Text { ... }` and `square: Int -> Int { ... }` introduce the
  smallest zero-input and one-input function declaration shapes.
- `getName -> name` and `7 -> square -> num` make returned values bindable
  without hiding the flow behind assignment syntax.
- `"..." -> print` makes the primary data flow visible at the call site.
- The executable entry point is still explicit.
- The parser can recognize the first complete program with a tiny grammar.
- The syntax leaves room for full functions, modules, and effects later.
- Braces avoid indentation-sensitive block parsing.

## Initial Grammar

The initial grammar is deliberately small:

```ebnf
source_file  := trivia* function_declaration* main_block trivia* eof
function_declaration := identifier ":" function_signature "{" expression "}"
function_signature := "->" type_name | type_name "->" type_name
main_block   := "main" block
block        := "{" statement* "}"
statement    := each_statement | binding_statement | expression_statement
each_statement := "each" identifier "in" range_expression block
binding_statement := identifier "=" expression statement_end
expression_statement := expression statement_end
statement_end := newline+ | "}" lookahead
range_expression := expression ".." expression
expression   := flow_expression
flow_expression := additive_expression ("->" path)*
additive_expression := multiplicative_expression ("+" multiplicative_expression)*
multiplicative_expression := primary ("*" primary)*
call         := path "(" argument_list? ")"
argument_list := expression ("," expression)*
path         := identifier ("." identifier)*
type_name    := identifier
primary      := call | string_literal | number_literal | identifier
number_literal := decimal_digit+
string_literal := "\"" string_part* "\""
string_part  := string_text | interpolation
interpolation := "{" path "}"
```

Notes:

- Newline is a statement separator, not an indentation rule.
- Semicolons are not part of the initial surface syntax.
- Braces are the only block delimiters.
- `identifier = expression` introduces a local binding in the current block.
- `+` is initially defined only for integer addition.
- `*` is initially defined only for integer multiplication.
- `value -> function` is parsed as a flow expression with `value` as the source.
- A final unknown single identifier in a statement-level flow binds the result:
  `7 -> square -> num`.
- `each i in 1..9 { ... }` iterates an inclusive integer range and introduces
  `i` only inside the loop body.
- Function declarations are currently expression bodies with either no input or
  one implicit input binding named `it`.

## Bindings

The initial binding syntax is:

```smalllang
name = "dimohy"
```

There is no `let`, `var`, or declaration keyword.

Initial binding rules:

- The first `name = expression` in a block introduces `name`.
- A binding is visible after its declaration statement.
- Referencing a binding before declaration is a compile-time error.
- Reusing the same name in the same scope is a compile-time error for now.
- Reassignment and mutability are not specified yet.
- Type inference determines the binding's type from the initializer.

This keeps the smallest program easy to read while avoiding hidden mutation
semantics before the memory and value model are decided.

## Numeric Expressions

The first numeric expression support is intentionally narrow:

```smalllang
sum = 20 + 22
```

Initial numeric rules:

- Decimal integer literals are supported.
- Integer values are represented as signed 64-bit values in the current
  semantic evaluator.
- `+` performs checked integer addition.
- `*` performs checked integer multiplication.
- `*` binds tighter than `+`; both operators are left-associative.
- Mixing strings and integers with `+` is not part of the current language.
- Integer bindings can be interpolated into strings using their invariant
  decimal display form.

This adds arithmetic without deciding floating point, arbitrary precision,
numeric suffixes, overflow policy syntax, or implicit string concatenation.

## Lexical Design

The lexer must be single-pass and allocation-conscious.

Initial token categories:

- keywords represented by identifier text in the current lexer: `main`, `each`,
  `in`
- identifiers
- string literals, including interpolation markers inside string mode
- decimal integer literals
- punctuation: `{`, `}`, `(`, `)`, `..`, `.`, `,`, `+`, `*`, `->`, `:`, `=`
- newlines
- trivia: spaces, tabs, comments when comments are specified
- end of file

Lexing principles:

- Source text is UTF-8.
- Tokenization should be deterministic and mostly context-free.
- String literal contents should be represented as source slices where possible.
- String interpolation should be tokenized in string mode without allocating
  concatenated strings.
- Diagnostics must preserve byte offset, line, and column information.
- The compiler must not normalize source text before tokenization.

The exact string escape set is not finalized. The first required string form is
a double-quoted UTF-8 literal with optional identifier/path interpolation:

```smalllang
"Hello World"
"Hello, {name}"
```

Interpolation rules:

- `{name}` inserts the current value of the binding named `name`.
- Interpolating an integer binding uses its invariant decimal display form.
- `{module.name}` style paths are reserved by the grammar but module semantics
  are not finalized.
- Arbitrary expressions inside interpolation are not part of the initial
  language.
- An unmatched `{` or `}` inside a string is a compile-time lexical error unless
  a later escape rule defines a literal brace form.

## Output Surface Semantics

`print` and `println` are available in the initial prelude. The preferred source
form is a value-flow call:

```smalllang
"Hello, {name}. square = {num}" -> print
"Hello, {name}. square = {num}" -> println
```

The parenthesized forms remain valid and equivalent:

```smalllang
print("Hello, {name}. square = {num}")
println("Hello, {name}. square = {num}")
```

Semantically, it resolves to:

```text
core.io.print(utf8_output_expression)
core.io.println(utf8_output_expression)
```

`print` emits exactly the requested bytes. `println` emits the requested bytes
followed by a single line-feed byte in the current runtime slice.

## Input Surface Semantics

`readInt` is available as the first input primitive. The preferred form mirrors
output value flow:

```smalllang
"n = ? " -> readInt -> n
```

The parenthesized form is also valid:

```smalllang
n = readInt("n = ? ")
```

Semantically, it resolves to:

```text
core.io.readInt(prompt_text) -> Int
```

The current runtime accepts a decimal integer line from standard input. Input
failure or a non-integer input must affect the process exit code; it must not
silently fall back to an arbitrary value.

## Range Loops

The first loop form is an inclusive integer range loop:

```smalllang
each i in 1..9 {
    value = n * i
    "{n} x {i} = {value}" -> println
}
```

The loop variable is immutable for the iteration and scoped to the loop body.
Bindings introduced inside the loop body are also scoped to that body. The
current range direction is ascending only; if the start is greater than the end,
the loop executes zero times.

## Value-Flow Calls

SmallLang accepts `->` as the preferred direction for function calls where the
input value should be visually explicit:

```smalllang
main {
    getName -> name
    7 -> square -> num
    "Hello, {name}. square = {num}" -> print
}
```

The expression on the left flows into the function or callable path on the
right. The example above is semantically equivalent to:

```smalllang
print("Hello, {name}. square = {num}")
```

This makes argument flow and return flow visible without discarding the familiar
parenthesized call form. Parenthesized calls remain valid as a compatibility and
escape-hatch syntax, but the value-flow form is the preferred SmallLang style for
single-primary-input operations.

Return values can be bound at the end of a statement-level flow:

```smalllang
getName -> name
7 -> square -> num
name -> greeting -> message
```

The assignment form remains valid when it is clearer for a non-flowing
expression:

```smalllang
num = square(7)
```

The corresponding function type notation follows the same direction:

```smalllang
greeting: Text -> Text
print: Text -> Io<Unit>
stdout.write: Bytes -> Io<Int>
```

The current parser preserves:

```smalllang
value -> function
```

as a `FlowExpression`. Semantic analysis resolves each target as either a
callable path, `print`, or a final flow binding. The executable lowering remains
equivalent to:

```smalllang
function(value)
```

for unary calls. Chained value-flow calls are parsed left-to-right. Extended
forms such as additional named arguments remain future syntax work.

Initial constraints:

- The first supported argument is a displayable scalar expression.
- Plain and interpolated string literals are valid displayable expressions.
- Integer expressions are displayable through invariant decimal formatting.
- The output target is standard output.
- The emitted data is exactly the evaluated string content, with no implicit
  newline.
- Output failure must not be silently ignored.
- The exact user-facing error-handling syntax is still open.

## Cross-Platform Output Binding

Although `print` is simple at the surface, it is a platform-bound I/O primitive
inside the compiler and core library.

The compiler chooses the output backend from the target triple at compile time.
There must be no generic runtime dispatch layer for selecting the OS backend.
Small OS-required runtime checks are allowed inside a selected backend, such as
distinguishing a Windows console handle from redirected stdout.

### POSIX-like Targets

For Linux, macOS, and other POSIX-like targets, initial lowering should target a
minimal stdout write path equivalent to:

```text
write(stdout_fd, ptr, len)
```

Requirements:

- Handle short writes correctly.
- Handle interruptible writes correctly where required by the platform.
- Avoid heap allocation for static string literals.
- Keep the ABI boundary explicit.

### Windows Targets

For Windows native targets, stdout must be Unicode-correct and efficient.

Requirements:

- If stdout is redirected to a pipe or file, write UTF-8 bytes directly.
- If stdout is a console, use a console-correct path such as UTF-16 output.
- For string literals, compile-time generation of UTF-16 companion data is
  preferred over runtime heap conversion.
- Handle partial writes and API failures explicitly.

### WASI Targets

For WASI-style targets, initial lowering should use the target ABI's stdout
write primitive, such as `fd_write`.

Requirements:

- Preserve exact UTF-8 bytes.
- Avoid assuming a host OS console.
- Report unsupported capabilities at compile time where possible.

### Unsupported Targets

Unsupported output targets must fail at compile time with a clear diagnostic.
They must not silently fall back to another backend.

## LLVM Lowering Direction

For the current runtime sample:

```smalllang
getName: -> Text {
    "dimohy"
}

square: Int -> Int {
    it * it
}

main {
    getName -> name
    7 -> square -> num
    "Hello, {name}. square = {num}" -> print
}
```

The intended lowering shape is:

```text
static global utf8 bytes: "dimohy"
function getName -> returns text slice
function square -> accepts i64 %it, evaluates %it * %it at runtime, returns i64
runtime decimal conversion helper for integer output
native entry function
-> call getName through flow source and bind name to returned text slice
-> pass 7 to square through flow and bind num to returned integer
-> write string literal segments directly
-> write name as a text slice
-> convert num to decimal bytes at runtime and write them
-> lower value-flow print call to selected core.io.print backend with output bytes
-> return process exit code
```

Optimization requirements:

- String literals are emitted as immutable globals.
- `(ptr, len)` should be passed without copying.
- Interpolated strings should avoid heap allocation when all parts are known
  static strings.
- Runtime function calls are emitted even when the current implementation could
  theoretically constant-fold the sample.
- Printing segmented string parts directly is preferred over building a
  temporary heap string.
- Platform output calls should be direct and inlinable when practical.
- The final executable should be produced through LLVM's native target pipeline.

## Current Implementation Slice

The current compiler supports:

```smalllang
getName: -> Text {
    "dimohy"
}

square: Int -> Int {
    it * it
}

main {
    getName -> name
    7 -> square -> num
    "Hello, {name}. square = {num}" -> print
}
```

and the cumulative input and loop sample shown above.

Current backend:

- target: Windows x64
- LLVM toolchain: LLVM 22.1.8, downloaded under `.tools` by `scripts/smalllang.ps1`
- lexer: generated from `syntax/smalllang.lexer` by a Roslyn incremental source
  generator
- parser: generated from `syntax/smalllang.grammar` by a Roslyn incremental source
  generator
- semantics: zero-argument and one-input function declarations, string and
  integer bindings, checked integer `+` and `*`, scalar interpolation, and
  statement-level value-flow binding are type-checked for the current slice
- value-flow calls: `value -> function` is parsed as a flow AST and lowered by
  semantic/codegen stages according to target position
- input: `readInt` lowers to a selected stdin backend primitive and returns an
  integer value
- loops: `each i in start..end { ... }` lowers to LLVM basic blocks with an SSA
  phi value for the loop variable
- IR output: immutable UTF-8 literal segments, runtime function calls, runtime
  i64 addition/multiplication, and runtime integer decimal output
- entry point: `smalllang_start`
- imports: `GetStdHandle`, `ReadFile`, `WriteFile`
- linker: `lld-link`
- CRT: none
- current verified executable sizes: 1,104 bytes for `hello.sl`, 1,584
  bytes for `gugudan.sl`

The current runtime backend emits direct `WriteFile` calls for text segments,
uses `ReadFile` for integer input on the selected Windows backend, calls
generated SmallLang functions, converts integer output to decimal bytes at
runtime, and returns `0` or `1` from the native entry point based on API success
and input parse success.

## Current Module Layout

The compiler implementation is organized by responsibility:

- `Cli`: command line parsing and build orchestration
- `Lexing`: token model and generated lexer
- `Parsing`: parser helpers; the token-to-AST parser is generated
- `Syntax`: AST node definitions
- `Semantics`: current binding/interpolation/I/O/loop lowering
- `CodeGen`: LLVM IR generation
- `Tooling`: LLVM and Windows linker integration

Lexer rules are expressed in the compact `syntax/smalllang.lexer` file. The source
generator reads that file as an MSBuild `AdditionalFiles` input and emits
`TokenKind` plus the deterministic lexer during C# compilation.

Parser rules are expressed in the compact `syntax/smalllang.grammar` file. The
source generator validates the first approved grammar slice and emits the
recursive descent parser during C# compilation. This keeps the grammar visible
without introducing a separate external parser generation toolchain at this
stage.

## Open Questions

- Should the language include additional output conveniences beyond `println`?
- What is the exact error model for I/O failure?
- Does `main` return an explicit exit code later?
- What is the final string type: owned string, slice, UTF-8 view, or multiple
  forms?
- What escape sequences are allowed in string literals?
- Should string interpolation later allow full expressions?
- How should literal `{` and `}` be written inside strings?
- What is the mutability/reassignment model for `name = value`?
- What numeric types beyond the initial signed 64-bit integer should exist?
- What comment syntax should be adopted?
- What is the first official target matrix?
- Which LLVM integration strategy will the .NET compiler use?
- How much core library is required before the first executable?
