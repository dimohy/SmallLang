# SmallLang Language Specification Draft

Status: draft
Date: 2026-07-09

This document is the living specification for SmallLang. It records the language
shape before implementation so design decisions do not get lost.

## Current Boundary

SmallLang implementation has started for the smallest approved language slice.

The implementation boundary is intentionally narrow:

- explicit `main` block or omitted `main` with top-level executable statements
- zero-argument and one-input expression functions with default `it` or an
  explicit input name
- single-expression function bodies with `name: Input -> Output => expression`
- nested local function declarations scoped to their containing function
- local bindings with `value => name`
- fixed-width signed `Int8`, `Int16`, `Int32`, `Int64` and unsigned
  `UInt8`, `UInt16`, `UInt32`, `UInt64` values; `Int` aliases `Int32` and
  `Long` aliases `Int64`
- IEEE-754 `Float32` and `Float64` values; `Float` aliases `Float32` and
  `Double` aliases `Float64`
- decimal integer and floating-point literals
- same-type numeric `+`, `-`, `*`, `/`, comparisons, integer `%`, unary `-`,
  and parenthesized expressions
- line comments with `#`
- `Bool` values from `true`/`false` literals and integer comparison expressions
- logical `and`, `or`, and `not`
- simple string interpolation with `$name`
- fluent value-flow calls and result bindings with `value -> function => name`
- receiver-only value-flow target syntax with `value -> function`
- flow-oriented conditionals with `condition -> if { ... } else { ... }`
- multi-branch `when { condition { ... } else { ... } }` expressions
- subject-value `when` with `value -> when { >= limit { ... } else { ... } }`
- subject-value `when` range arms with `value -> when { start..end { ... } }`
- compact `when` arms with `condition => value`, including implicit `it` subject
  inside one-input functions
- parenthesized calls with `function(value)`
- SmallLang standard library functions `sys.io.print`, `sys.io.println`, and
  `sys.io.readInt` through global import aliases `print`, `println`, and
  `readInt`
- optional source-file `namespace` declaration and `import ... as ...` aliases
- integer input with `readInt` or `sys.io.readInt`
- line output with `println` or `sys.io.println`
- block-function calls, with `each` and `repeat` as the first built-ins
- closed integer range loops with `start..end -> each item { ... }`
- default loop item binding with `start..end -> each { ... }`, exposed as `it`
- integer folds with `start..end -> fold initial acc, item { nextAcc }`
- owned `Int` static arrays with `[1, 2, 3]` and `[0; 8]`
- owned growable `Int` arrays with `[1, 2, ~]`, typed empty `[Int; ~]`, and
  capacity hint `[Int; 1024~]`
- owned `{Int: Int}` dictionaries with `{ 1: 100, 2: 200 }` and typed empty
  `{Int: Int}` or capacity hint `{Int: Int; 1024~}`
- checked array and dictionary indexing with `container[index]`
- mutable owner bindings with `value => name!`
- mutable indexing assignment with `value => owner![index]`
- receiver-flow container operations: `len`, `capacity`, `push(value)`, and
  `put(key, value)`
- move-consuming owner-returning container operations: `append(value)` and
  `updated(keyOrIndex, value)`
- deterministic drop emission for heap-owning dynamic arrays and dictionaries
  on native targets
- automatic stack promotion for small dynamic-array and dictionary literals
  whose owners are proven not to grow or escape their function frame
- function-entry stack slots, last-use lifetime markers, and non-overlapping
  payload reuse in nested branches and loops
- block-local drop emission for heap-owning dynamic arrays and dictionaries
- readonly `[Int]` function parameters that accept static and growable `Int`
  arrays without taking ownership
- readonly `{Int: Int}` function parameters that inspect a dictionary without
  taking ownership or copying its heap allocation
- `mut [Int; ~]` and `mut {Int: Int}` function parameters that can mutate a
  caller-owned growable container without taking ownership
- explicit `move` growable array and dictionary parameters that may return the
  consumed input owner
- purpose-oriented pseudo-random integer generation with `seedRandom` and
  `randomBelow`
- binary sorted `Int` file writing with `openIntWriter`, `writeInt`, and
  `closeIntWriter`
- nearest-value lookup over a sorted binary `Int` file with `openIntReader`,
  `closestInt`, and `closeIntReader`
- Windows x64, Linux x64, and browser WebAssembly output through LLVM

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

Structured control flow is retained through typed IR. An `if` owns ordered
`then` and optional `else` regions plus its Bool condition. LLVM lowering then
creates explicit basic blocks and branches; it creates a merge `phi` only when
the conditional is value-producing. This keeps branch-local type, ownership,
and lifetime analysis independent of target-specific CFG text emission.
Independent effectful calls and control regions retain source order even when
their value dependencies would otherwise allow topological reordering.
Nested value conditionals compose by using the innermost result-producing merge
block as the predecessor of an enclosing `phi` input.

Self-hosted mutable scalar loops use an explicit memory form before LLVM
optimization: one non-escaping entry-block `alloca` represents each mutable
binding chain, rebinds are ordered stores, and reads are ordered loads. A
`while` comparison is regenerated in the header so every iteration observes
the latest stored value. LLVM may then promote the slot to SSA and synthesize
the loop-carried `phi`; the language semantics do not require a heap allocation
or expose this intermediate representation.

## First Program

The first valid SmallLang program is:

```smalllang
main {
    "dimohy" => name
    print("Hello, $name")
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
    getName() => name
    7 -> square => num
    "Hello, $name. square = $num" -> print
}
```

Expected stdout bytes:

```text
Hello, dimohy. square = 49
```

The one-input function can also name its input explicitly:

```smalllang
square n: Int -> Int {
    n * n
}
```

The current cumulative input and loop example is:

```smalllang
main {
    "n = ? " -> readInt => n

    1..9 -> each i {
        n * i => value
        "$n x $i = $value" -> println
    }
}
```

With stdin `9`, the expected stdout bytes are:

```text
n = ? 9
9 x 1 = 9
9 x 2 = 18
9 x 3 = 27
9 x 4 = 36
9 x 5 = 45
9 x 6 = 54
9 x 7 = 63
9 x 8 = 72
9 x 9 = 81
```

The same loop can omit the item name and use the default binding `it`:

```smalllang
main {
    "n = ? " -> readInt => n

    1..9 -> each {
        n * it => value
        "$n x $it = $value" -> println
    }
}
```

The executable `main` wrapper can be omitted. These top-level statements are
compiled as the main body:

```smalllang
getName() => name
7 -> square => num
"Hello, $name. square = $num" -> sys.io.print
```

The input and output functions can also be addressed by their canonical module
path:

```smalllang
"n = ? " -> sys.io.readInt => n

1..9 -> each i {
    n * i => value
    "$n x $i = $value" -> sys.io.println
}
```

The current conditional form keeps the condition as the value flowing into a
block function-like control expression:

```smalllang
"n = ? " -> readInt => n

n < 1 or n > 9 -> if {
    "n must be 1..9" -> println
} else {
    1..9 -> each i {
        n * i => value
        "$n x $i = $value" -> println
    }
}
```

For multi-branch value selection, `when` is an expression:

```smalllang
grade score: Int -> Text {
    when {
        score >= 90 { "A" }
        score >= 80 { "B" }
        score >= 70 { "C" }
        else { "F" }
    }
}
```

When every arm compares the same value, the value can flow into `when` once:

```smalllang
grade score: Int -> Text {
    score -> when {
        >= 90 { "A" }
        >= 80 { "B" }
        >= 70 { "C" }
        else { "F" }
    }
}
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
    getName() => name
    7 -> square => num
    "Hello, $name. square = $num" -> print
}
```

For short executable scripts, the `main` wrapper may be omitted:

```smalllang
getName() => name
7 -> square => num
"Hello, $name. square = $num" -> print
```

Rationale:

- `main { ... }` is shorter than `fn main() { ... }`.
- `value => name` keeps local binding aligned with the language's expression-first
  direction.
- `"Hello, $name"` keeps string interpolation direct and familiar.
- `it * it` introduces the smallest one-input numeric function without deciding the
  final numeric tower.
- `getName: -> Text { ... }`, `square: Int -> Int { ... }`, and
  `square n: Int -> Int { ... }` introduce the smallest zero-input and one-input
  function declaration shapes.
- `getName() => name` and `7 -> square => num` make returned values bindable
  without hiding the flow behind assignment syntax.
- `"..." -> print` makes the primary data flow visible at the call site.
- The executable entry point can be explicit with `main { ... }` or implicit
  when top-level executable statements are present.
- The parser can recognize the first complete program with a tiny grammar.
- The syntax leaves room for full functions, modules, and effects later.
- Braces avoid indentation-sensitive block parsing.

## Initial Grammar

The initial grammar is deliberately small:

```ebnf
source_file  := trivia* namespace_declaration? import_declaration* function_declaration* (main_block | statement*) trivia* eof
namespace_declaration := "namespace" path statement_end
import_declaration := "import" path ("as" identifier)? statement_end
function_declaration := path identifier? ":" function_signature (block_function_body | function_body)
function_signature := "->" type_annotation | type_annotation "->" type_annotation
block_function_body := "block" identifier ":" type_name "{" statement* "}"
function_body := "{" function_declaration* statement* expression "}" | "=>" expression | "->" expression | "=" "intrinsic"
main_block   := "main" block
block        := "{" statement* "}"
statement    := each_statement | binding_statement | index_assignment_statement | field_assignment_statement | expression_statement | block_function_call
block_function_call := range_or_logical_expression "->" path identifier? block
each_statement := "each" identifier "in" range_expression block
binding_statement := identifier "=" expression statement_end | expression "=>" identifier "!"? statement_end
index_assignment_statement := expression "=>" identifier "!"? "[" expression "]" statement_end
field_assignment_statement := expression "=>" identifier "!"? "." identifier statement_end
expression_statement := expression statement_end
statement_end := newline+ | "}" lookahead
range_expression := logical_or_expression ".." logical_or_expression
expression   := flow_expression
flow_expression := range_or_logical_expression ("->" (if_flow_target | while_flow_target | when_flow_target | fold_flow_target | path flow_target_call?))*
flow_target_call := "(" argument_list? ")"
range_or_logical_expression := range_expression | logical_or_expression
if_flow_target := "if" block_body ("else" block_body)?
while_flow_target := "while" block_body
when_flow_target := "when" "{" subject_when_arm+ "else" when_arm_body "}"
fold_flow_target := "fold" expression identifier "," identifier block_body
when_expression := "when" "{" when_arm+ "else" when_arm_body "}"
when_arm := when_condition when_arm_body
when_condition := subject_when_condition | logical_or_expression
subject_when_arm := subject_when_condition when_arm_body
subject_when_condition := subject_comparison expression | range_expression
subject_comparison := "==" | "!=" | "<" | "<=" | ">" | ">="
when_arm_body := "=>" expression | "->" expression | block_body
block_body := "{" statement* expression? "}"
logical_or_expression := logical_and_expression ("or" logical_and_expression)*
logical_and_expression := equality_expression ("and" equality_expression)*
equality_expression := comparison_expression (("==" | "!=") comparison_expression)*
comparison_expression := additive_expression (("<" | "<=" | ">" | ">=") additive_expression)*
additive_expression := multiplicative_expression (("+" | "-") multiplicative_expression)*
multiplicative_expression := unary_expression (("*" | "/" | "%") unary_expression)*
unary_expression := "not" unary_expression | "-" unary_expression | primary
call         := path "(" argument_list? ")"
argument_list := expression ("," expression)*
path         := identifier ("." identifier)*
type_name    := identifier
type_annotation := type_name | "[" type_name ";" "~" "]" | "{" type_name ":" type_name "}"
primary      := atom ("[" expression "]")*
atom         := when_expression | call | array_literal | dictionary_literal | "(" expression ")" | bool_literal | string_literal | number_literal | identifier
array_literal := "[" type_name ";" ("~" | number_literal "~") "]" | "[" expression ("," expression)* ("," "~")? "]" | "[" expression ";" number_literal "]"
dictionary_literal := "{" type_name ":" type_name (";" number_literal "~")? "}" | "{" dictionary_entry ("," dictionary_entry)* ","? "}"
dictionary_entry := expression ":" expression
bool_literal := "true" | "false"
number_literal := decimal_digit+
string_literal := "\"" string_part* "\"" | "\"\"\"" raw_string_text* "\"\"\""
string_part  := string_text | interpolation
interpolation := "$" identifier | "$(" expression ")"
```

Notes:

- Newline is a statement separator, not an indentation rule.
- Array literals may place their opening item, comma-separated items, trailing
  `~`, and closing bracket on separate lines. This keeps raw multiline strings
  and other structured values readable without changing their element order.
- Semicolons are not statement separators. The current surface uses `;` only in
  repeated fixed-array literals such as `[0; 8]`.
- Braces are the only block delimiters.
- If `main { ... }` is omitted, remaining top-level statements after function
  declarations are treated as the executable main body.
- A function whose body is a single expression should use `=> expression`
  instead of an outer body block. `-> expression` remains accepted as
  compatibility syntax.
- A statement-level expression followed by `=> name` introduces a local binding
  in the current block.
- `source -> path item? { ... }` introduces a block-function call. The supported
  block-function targets in the current slice are `each` and `repeat`. This is
  the only function-like call form that intentionally omits `()` because the
  following brace block is the code block argument.
- `condition -> if { ... } else { ... }` introduces the current conditional
  expression form. The value flowing into `if` must be `Bool`.
- `when { condition { ... } else { ... } }` is the current multi-branch
  conditional expression form.
- `value -> when { >= limit { ... } else { ... } }` is the subject-value
  shorthand for ordered comparisons against one value. The subject value is
  evaluated once.
- `value -> when { start..end { ... } else { ... } }` checks inclusive integer
  ranges against the subject value.
- `when { condition => value else => fallback }` is shorthand for single-value
  arms. Block arms remain valid for multi-statement arm bodies.
- In a one-input function using the default input binding `it`, a subject-style
  `when` without an explicit subject uses `it` as the subject. Explicitly named
  inputs should still use `input -> when { ... }`.
- `range -> fold initial acc, item { nextAcc }` returns the final integer
  accumulator value after direct loop lowering.
- `+`, `-`, `*`, `/`, and `%` are initially defined only for integers.
- Unary `-` is initially defined only for integers.
- Parentheses group expressions.
- `#` starts a line comment outside string literals.
- comparison operators initially compare integers and return `Bool`.
- `and` and `or` short-circuit and require `Bool` operands.
- `not` requires a `Bool` operand.
- `value -> function` is parsed as a fluent flow expression with `value` as the
  source. The value on the left remains the function input.
- `value -> function()` remains accepted as compatibility syntax, but empty
  parentheses are no longer preferred for receiver-only flow calls.
- `->` never creates a binding. Bind results with `=>`: `7 -> square => num`.
- `1..9 -> each i { ... }` iterates an inclusive integer range and introduces
  `i` only inside the loop body.
- `1..9 -> each { ... }` uses `it` as the default loop item binding.
- Function declarations are currently expression bodies with either no input or
  one input. A one-input function uses `it` when no input name is supplied, and
  uses the supplied name in `square n: Int -> Int { ... }`.
- Path-qualified function declarations remain valid, such as
  `sys.io.print value: Text -> Unit { ... }`, but standard library source now
  prefers file-level `namespace` declarations.
- `= intrinsic` declarations are reserved for the standard library's lower
  runtime boundary.
- Function declarations may appear at the start of another function body. These
  local functions are scoped to the containing function and nested functions
  below it; they are not visible from `main` or unrelated functions.
- A source file may declare one namespace before imports and function
  declarations. Top-level single-segment function declarations in that file are
  qualified by the namespace.
- Imports may alias a path, such as `import sys.runtime as rt`. A path beginning
  with the alias is resolved to the imported path before semantic analysis.

## Bindings

The preferred binding syntax is:

```smalllang
"dimohy" => name
n * i => value
```

There is no `let`, `var`, or declaration keyword.

Initial binding rules:

- `expression => name` introduces an immutable binding.
- `expression => name!` introduces a mutable owner binding. The `!` suffix is
  part of the local name, so later reads and mutating calls also show mutation
  capability at the use site.
- The older `name = expression` form remains accepted as a compatibility syntax,
  but new samples should prefer `expression => name`.
- A binding is visible after its declaration statement.
- Referencing a binding before declaration is a compile-time error.
- Reusing the same name in the same scope is a compile-time error for now.
- Mutating container operations require a mutable owner binding with a `!`
  suffix.
- Type inference determines the binding's type from the initializer.

This keeps the smallest program easy to read while avoiding hidden mutation
semantics before the memory and value model are decided.

## Function Inputs

One-input functions use the same naming idea as range loops:

```smalllang
square: Int -> Int {
    it * it
}

square n: Int -> Int {
    n * n
}
```

When the input name is omitted, the function body receives the value as `it`.
When the input name is supplied after the function name, the body receives the
value through that binding. This mirrors `start..end -> each { ... }` and
`start..end -> each item { ... }`.

## Local Functions

Functions may declare local helper functions before their final body expression:

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

Local functions use the same input naming rule as top-level functions. Their
names are visible only inside the containing function and functions nested below
it. They can read bindings from the containing function, such as `n` above. In
the current backend, local functions are lowered by inlining at the call site;
no global LLVM symbol is emitted for `double` or `addBase`.

## Block Functions

SmallLang models executable blocks as values passed to block functions at the
semantic layer:

```smalllang
1..9 -> each i {
    n * i => value
    "$n x $i = $value" -> println
}
```

In the current slice, `each` and `repeat` are the supported block-function
targets. For `each`, the range expression flows into `each`, the optional
identifier names the block input for each invocation, and the brace body is the
executable block argument. For `repeat`, an integer count flows into `repeat` and
the block receives repeat numbers from `1` through that count:

```smalllang
3 -> repeat turn {
    "repeat turn $turn" -> println
}
```

Because the code block is the argument, these forms are written
`-> each i { ... }` and `-> repeat turn { ... }`, not `-> each() { ... }` or
`-> repeat() { ... }`.

Users can define block functions with a `block` parameter. The block-function
body calls the passed executable block with `value -> yield()`:

```smalllang
runTimes count: Int -> Unit block turn: Int {
    1..count -> each turn {
        turn -> yield()
    }
}

main {
    3 -> runTimes step {
        "custom block step $step" -> println
    }
}
```

The compiler is not required to lower this as a runtime closure. For built-in
block functions such as `each`, the backend may specialize the call at compile
time. The current LLVM backends lower `each` directly to basic blocks with an
SSA phi value for the item binding, with no heap allocation, function pointer,
closure object, or dynamic block dispatch.

## Standard Library Imports And Aliases

The current standard library implements the `sys.io` module in SmallLang:

```smalllang
namespace sys.io

import sys.runtime as rt

print value: Text -> Unit {
    value -> rt.print
}

println value: Text -> Unit {
    value -> rt.println
}

readInt prompt: Text -> Int {
    prompt -> rt.readInt
}
```

The lower `sys.runtime` functions are intrinsic declarations owned by the
standard library:

```smalllang
namespace sys.runtime

print value: Text -> Unit = intrinsic
println value: Text -> Unit = intrinsic
readInt prompt: Text -> Int = intrinsic
```

The compiler loads the standard library before user code and globally imports
the public `sys.io` functions:

```text
sys.io.print
sys.io.println
sys.io.readInt
```

The short names are aliases:

```text
print   -> sys.io.print
println -> sys.io.println
readInt -> sys.io.readInt
```

Source code can use either spelling:

```smalllang
"Hello" -> print
"Hello" -> sys.io.print
"n = ? " -> readInt => n
"n = ? " -> sys.io.readInt => n
```

These functions are resolved through the same function table as user functions.
They are not parsed as keywords or statement-specific built-ins. Their only
current privilege is the global alias layer. The backend inlines the SmallLang
`sys.io` wrappers and lowers the `sys.runtime` intrinsic boundary to the
selected platform I/O implementation.

The current purpose-oriented file and random libraries follow the same wrapper
pattern:

```smalllang
seedRandom value: Int -> Unit
randomBelow maxExclusive: Int -> Int

openIntWriter path: Text -> Unit
writeInt value: Int -> Unit
closeIntWriter: -> Unit

openIntReader path: Text -> Unit
closestInt target: Int -> Int
closeIntReader: -> Unit
```

The current `Int` file format is binary, little-endian, signed 64-bit records.
`writeInt` appends to the current writer through an internal buffer. `closestInt`
expects the current reader file to be sorted ascending and performs a binary
search over fixed-width records. These APIs are intentionally current-file
intrinsics for the first large-data workflow; general file handles and arbitrary
binary/text formats remain future language work.

The 100,000,000-record generator avoids a separate sort by dividing
`1..1,000,000,000` into 100,000,000 10-wide buckets and choosing one
pseudo-random value from each bucket in increasing bucket order:

```smalllang
main {
    "artifacts/random-sorted-100m.i64" -> openIntWriter
    20260708 -> seedRandom

    1..100000000 -> each bucket {
        bucket - 1 => zeroBased
        zeroBased * 10 => base
        10 -> randomBelow => offset
        base + offset + 1 => value
        value -> writeInt
    }

    closeIntWriter()
}
```

This produces sorted unique values with one pseudo-random choice per bucket. It
is not a uniform sample over all possible 100,000,000-element subsets of
`1..1,000,000,000`.

## Numeric Expressions

Numeric expressions use stable, fixed-width primitives:

```smalllang
Int8(20) + Int8(22) => small
Float32(1.5) * Float32(2.0) => scaled
```

Numeric rules:

- Decimal integer literals default to `Int`, which is always `Int32`.
- Fractional or exponent literals default to `Float`, which is always
  IEEE-754 `Float32`.
- Explicit widths are `Int8/16/32/64`, `UInt8/16/32/64`, and `Float32/64`.
- Constructor-like conversions such as `Int8(value)` and `Float32(value)` are
  explicit. Literal range failures are compile errors and runtime integer
  narrowing performs a bounds check.
- Binary arithmetic requires equal operand types; SL does not silently widen,
  narrow, or change signedness. `%` is integer-only and unary `-` rejects
  unsigned values.
- `*`, `/`, and `%` bind tighter than `+` and `-`; operators are
  left-associative.
- Integer bindings can be interpolated using invariant decimal display.
- `Long` aliases `Int64` and `Double` aliases `Float64`; the exact-width names
  remain available when representation should be explicit.
- `Size` and `UIntSize` use the target pointer width: 64 bits on the current
  Windows/Linux x64 targets and 32 bits on wasm32. `Size` is signed for offsets
  and differences; `UIntSize` is unsigned for byte counts and capacities.
  Literal range checks, explicit conversions, aggregate layout, and LLVM
  function ABI all use that target width. Ordinary `Int` remains `Int32` on
  every target.

## Nested Structs

Struct declarations may contain helper struct declarations:

```smalllang
struct Parser {
    struct Cursor {
        offset: Int
    }

    cursor: Cursor
}
```

The nested type is nominally `Parser.Cursor` but `Parser` fields and
`impl Parser` bodies resolve the short name `Cursor`. A nested struct is private
to its declaring struct by default. Prefixing the nested declaration with
`public` exposes the qualified name to other code. Layout, initialization,
ownership, recursive drop, and value-cycle checks are identical to top-level
structs.

## Generic Delimiters And Result Propagation

Generic type and compile-time value parameters use `<...>`:

```smalllang
Option<Int>
Result<Int, Text>
identity<T> value: T -> T => value
values -> fixedLength<3>
```

`[]` is reserved for arrays, indexing, and collection expansion. The former
generic square-bracket spelling is not accepted.

Postfix `?` applies only to `Result<T, E>`. On `Ok`, its expression value is the
success payload. On `Err`, it returns `Result<U, E>.Err(error)` from the nearest
enclosing Result-returning function after deterministic local cleanup. Error
types must match exactly. An owned Result may be propagated when the operand is
a fresh temporary or the function's explicit `move Result<T, E>` input. A named
non-move owned Result is rejected instead of being copied. Result constructors
consume named owned payloads and transfer their single drop obligation into the
new enum value.

## Containers

Constant ranges and compile-time `each` expressions can construct collections:

```smalllang
[1..10]
[1..10 -> each { it + 1 }]
{1..3 -> each { it: it * 10 }}
```

Ranges are inclusive. When their bounds and selector arithmetic are constant
integers, the compiler expands these forms into ordinary array elements or
dictionary entries before semantic analysis. An explicit item name may replace
`it`, as in `[1..3 -> each item { item * item }]`. Nonconstant expressions are
diagnosed; compile-time expansion currently has a 100,000-element limit.

The first container implementation is intentionally `Int`-only. It proves the
syntax, checked access, mutation surface, and deterministic native cleanup
before generic containers and borrowing are added.

Static arrays:

```smalllang
[1, 2, 3] => numbers
[0; 8] => zeros
numbers[0] => first
numbers -> len => count
```

Dynamic arrays:

```smalllang
[10, 20, ~] => values!
values! -> push(30)
values![2] => third
values! -> capacity => capacity

99 => values![1]

[10, 20, ~] => values
values -> append(30) => values
values -> updated(0, 99) => values
```

Dictionaries:

```smalllang
{ 1: 100, 2: 200 } => scores!
scores! -> put(3, 300)
scores![3] => score
scores! -> len => count

{ 1: 100, 2: 200 } => frozenScores
frozenScores -> updated(2, 250) => frozenScores
```

Container rules in the current slice:

- Static arrays are owned fixed-size `Int` values stored inline in the owner.
- Dynamic arrays own `ptr`, `len`, and `capacity` metadata plus their payload
  storage. The normal payload placement is heap storage.
- A nonempty dynamic-array literal bound to an immutable local may instead use
  inline stack payload storage when the compiler proves every remaining use is
  readonly and the owner does not escape. This optimization does not change the
  source type or syntax.
- Dictionaries infer homogeneous key/value types and own Swiss-style control
  bytes plus type-aligned key-value entries. `Int` and `Text` currently provide
  the required built-in hash/equality operations. Values may be scalar, `Text`,
  or inline user values; owned values receive recursive entry destruction.
  `{Key: Value; N~}` creates an empty typed dictionary with a capacity hint.
  The legacy `{Int: Int}` layout also supports readonly stack promotion; other
  specializations currently use heap payload storage.
- Typed empty arrays and dictionaries without capacity hints begin with a null
  pointer and zero capacity. Their first mutation allocates initial storage;
  readonly use of the empty value performs no heap allocation.
- Indexing is checked. Out-of-bounds array access and missing dictionary keys
  trap in the current runtime slice.
- `push`, `put`, and indexed assignment require a named mutable owner binding
  created with `=> name!`.
- `array -> each item { ... }` binds `item` to the concrete element type for
  fixed and dynamic arrays. Owned elements are readonly borrows for one block
  invocation and are never dropped separately from their array owner.
- `dictionary -> eachKey key { ... }` and `dictionary -> eachValue value {
  ... }` scan occupied Swiss-table slots and bind the concrete K or V type.
  Iteration order is unspecified. Owned items are readonly per-slot borrows.
- `Int` and `Text` have built-in dictionary hash/equality. A copyable nominal
  key must implement `Hash.hash: self -> Int` and `Eq.eq: self -> Int`.
  `Eq.eq` returns the canonical equality-class integer, and equal keys must
  return the same hash. Dispatch is statically specialized with no vtable.
- When dictionary K is a struct, `dictionary[{ field: value, ... }]` is a
  contextual K literal equivalent to `dictionary[K { field: value, ... }]`.
  All fields remain required and type checked; elsewhere braces retain their
  dictionary-literal meaning.
- `append` and `updated` consume a named source owner and return the moved owner.
  After the transform, the source binding is no longer live. The target may
  reuse the same name because the old owner is consumed before the new owner is
  bound.
- `append` reuses the existing dynamic-array buffer when capacity remains and
  grows/free-replaces only when capacity is full. Dynamic-array `updated` writes
  into the moved buffer after a bounds check. Dictionary `updated` reuses the
  `put` path, which performs expected O(1) hash-table lookup/update/insert and
  grows/rehashes at the load threshold.
- Move-consuming owner-returning operations must be final flow targets and must
  be bound directly with `=>`, so the compiler has a drop point for the moved
  heap owner.
- Heap-owning container creation must happen directly at a binding site so the
  compiler can insert deterministic cleanup.
- Heap-owning containers may be created inside nested blocks. The compiler drops
  block-local owners at the end of that block unless the final block expression
  moves the owner out as the block result.
- A block result may move a block-local growable array or dictionary owner out
  to the surrounding binding. Moving an outer owner out through an inner block
  result is rejected, except when every return branch transfers the current
  function's own `move` input to the function result.
- User functions may return concrete `[T; ~]` and `{K: V}` owners. The returned
  owner must be bound directly by the caller so the caller owns the drop point.
  Calling such a function as an anonymous flow source is rejected.
- User functions may accept `[Int]` readonly views. A static `Int` array or
  growable `Int` array can be passed to `[Int]` without transferring ownership.
  The callee can read with indexing, `len`, `each`, and `fold`, but cannot
  mutate or store the view beyond the call.
- User functions may accept any supported concrete `{K: V}` as a readonly
  dictionary view. The
  callee receives `ptr`, `len`, and `capacity` metadata by value and may use
  indexing, `len`, and `capacity`. `put`, indexed assignment, `updated`, return,
  and storage beyond the call are rejected. The caller remains the owner.
- User functions may accept `mut [T; ~]` and `mut {K: V}` mutable
  borrows. The caller must pass a named mutable owner such as `values!` or
  `scores!`. The callee can use existing mutable operations such as `push`,
  `put`, and indexed assignment, and the caller keeps ownership after the call.
- User functions may accept `move [T; ~]` and `move {K: V}` owners.
  Passing such a value moves ownership into the callee. The caller binding is
  no longer live after the call. The callee drops the parameter at function
  exit or returns it directly or after move-consuming transforms. A returned
  owner must be bound directly by the caller, which receives the drop duty.
  Conditional returns must transfer the input on every branch or on none.
- Native Windows and Linux targets emit platform allocation/free primitives and
  drop dynamic arrays and dictionaries at scope exit.
- Browser WebAssembly rejects heap-placed containers until the target has a
  linear-memory allocator. Stack-promoted readonly dynamic arrays and
  dictionaries require no allocator and are accepted.
- Fixed-array generic function contracts, collection iterators, and user-defined
  dictionary `Hash`/`Eq` key dispatch remain future work. Compile-time `Int`
  value parameters specialize fixed
  repeat counts and fixed `Int` array input contracts. `[Int; N]` accepts only a
  fixed array whose compile-time length equals the explicit call specialization.

## Lexical Design

The lexer must be single-pass and allocation-conscious.

Initial token categories:

- keywords represented by identifier text in the current lexer: `main`, `each`,
  `in`
- identifiers
- string literals, including interpolation markers inside string mode
- decimal integer literals
- punctuation: `{`, `}`, `[`, `]`, `(`, `)`, `..`, `~`, `.`, `,`, `;`, `+`,
  `-`, `*`, `/`, `%`, `->`, `=>`, `:`, `!`, `=`
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

`Text` stores validated UTF-8 bytes and `text -> each scalar { ... }` decodes
them into `CodePoint` values. `CodePoint` is a distinct unsigned 32-bit Unicode
scalar type: its valid values are `U+0000..U+D7FF` and `U+E000..U+10FFFF`.
Iteration advances by one UTF-8 sequence, not one byte or one user-perceived
grapheme cluster. Malformed, truncated, overlong, surrogate, and out-of-range
sequences trap at the safe runtime boundary. Explicit `CodePoint(integer)`
conversion performs the same range and surrogate checks. Arithmetic is not
defined directly on `CodePoint`; convert to `UInt32` first when numeric work is
intentional. Equality and ordering comparisons remain available for lexer
classification.

`Text` also exposes explicit UTF-8 byte operations for lexer and source-map
work. `text -> len` returns `UIntSize` byte length, `text -> byte(index)` returns
a bounds-checked `UInt8`, and `text -> slice(start, length)` returns a borrowed
`Text` view. Slice offsets are byte offsets and both ends must lie on UTF-8
scalar boundaries; splitting a continuation sequence traps. Thus byte scanning
is explicit while every value that retains the `Text` type remains valid UTF-8.

The self-hosting syntax substrate defines `SourceSpan { fileId, start, length }`
with `UIntSize` byte offsets and `SyntaxToken { kind, span }`. Byte offsets are
shared across tokens, CST nodes, diagnostics, and source maps so Unicode column
rendering can be derived without destabilizing stored spans.

## Arena Storage

`Arena(initialCapacity)` creates a unique owned byte arena. It is a three-word
handle containing a backing pointer, used byte count, and capacity. `box T`
remains different and always means an individually owned heap allocation;
ordinary structs remain inline values.

```smalllang
Arena(4096) => syntax!
syntax! -> alloc(24, 8) => nodeOffset
syntax! -> store(nodeOffset, UInt8(1))
syntax! -> load(nodeOffset) => tag
syntax! -> used => bytesUsed
syntax! -> reset
```

- `alloc(bytes, alignment)` requires a mutable arena, accepts `Int` or
  `UIntSize`, validates non-negative sizes and a nonzero power-of-two alignment,
  and returns an aligned `UIntSize` offset.
- Offsets stay stable when the arena grows because they are relative to the
  backing block rather than raw addresses.
- Growth selects at least `max(capacity * 2, requiredEnd)`, copies only used
  bytes, and immediately frees the previous block.
- `store(UIntSize, UInt8)` and `load(UIntSize)` perform bounds checks against
  used bytes. Raw pointers are not exposed in safe SL.
- `reset` retains capacity and sets used bytes to zero. Existing offsets become
  logically invalid; subsequent checked access is relative to the new contents.
- `Arena` is affine: readonly borrowing, `mut Arena`, and `move Arena` follow the
  ordinary ownership rules. Final drop frees the current backing block exactly
  once. Individual arena allocations are never freed separately.
- The native targets currently support arenas. Browser wasm remains blocked by
  its existing no-heap runtime boundary.

## Memory-Mapped Bytes

Native SL programs can map a file directly as an owned byte view:

```smalllang
map read "huge.dat" at 4_000_000_000 for 64_000_000 => data
map write "index.dat" size 10_000_000 => output!
output![0] = UInt8(42)
output! -> flush
```

- `map read` produces immutable `MappedBytes`; `map write` produces
  `MutableMappedBytes` and therefore requires a mutable owner binding.
- The `at` and `size` contexts infer integer literals as `UInt64`; `for` and
  mapped indices infer literals as target-sized `UIntSize`. Explicit constructors
  remain valid, and `_` may separate decimal digits.
- `at ... for ...` maps a view without loading the whole file. The runtime
  aligns the operating-system view down to its required granularity while the
  language-visible view begins at the exact requested byte offset.
- Indexing and assignment are bounds checked and yield/accept `UInt8`.
  `len` returns `UIntSize`, `each` iterates bytes, and mutable `flush` requests
  synchronous writeback.
- A mapped view is affine. Leaving its owning scope unmaps the underlying view
  exactly once; copying a mapped owner is not allowed.
- Windows x64 and Linux x64 use their native mapping APIs. Browser wasm rejects
  `map` because it has no corresponding host-file mapping primitive.

## Process Arguments

Native entry points expose their launch arguments through the standard-library
property `sys.process.arguments`:

```smalllang
import sys.process as process

process.arguments => args
args -> len => count
args[1] => sourcePath
args -> each argument {
    "argument = $argument" -> println
}
```

`Arguments` is a copyable, process-lifetime, read-only view rather than an owned
`[Text; ~]`. Its `len` and index use `UIntSize`; indexing returns borrowed
`Text`; `each` binds `Text`. The first item is the executable name supplied by
the host and must not be treated as a canonical or security-checked path.

On Windows, SL uses the operating system's Unicode command-line parser and
converts each UTF-16 item to validated UTF-8 storage retained until program
exit. That storage is released exactly once by the runtime. On Linux, the
native `argc`/`argv` entry ABI supplies stable byte spans directly. Browser wasm
does not currently define host process arguments and rejects the property.

Argument setup and its allocation helpers are emitted only when the program
actually references `sys.process.arguments`, preserving allocation-free LLVM
for programs that do not use this host capability.

Environment lookup uses the same module and returns an option so a present
empty value is distinct from a missing name:

```smalllang
process.environment("LLVM_ROOT") -> when {
    Option<Text>.None => "LLVM_ROOT is not set"
    Option<Text>.Some(path) => path
} => llvmRoot
```

The input name must be valid UTF-8 without an embedded zero byte. The returned
`Text` is a borrowed process-lifetime view. Linux borrows the stable `getenv`
storage because safe SL currently has no environment mutation API. Windows
queries the Unicode environment, converts a present value to UTF-8, retains it
in a runtime-owned allocation list, and releases the list at process exit.
Repeated Windows lookups may retain duplicate converted values until exit; a
future cache may deduplicate them without changing source semantics.

Lookup allocation or encoding failure traps rather than being reported as
`None`; `None` means only that the variable is absent. Browser wasm rejects
environment lookup until a host capability is explicitly supplied.

## Structured Child Processes

`sys.process.run` executes one program directly without invoking a shell:

```smalllang
import sys.process as process

["clang", "module.ll", "-o", "module.exe", ~] => argv
argv -> process.run => status
```

Its signature is `[Text; ~] -> Result<Int, Text>`. The first item is the
program path or search name and every remaining item is one literal argv entry;
spaces, Unicode, quotes, and backslashes are not reparsed as shell syntax.
`Ok(exitCode)` represents normal termination. `Err("spawn")`, `Err("wait")`,
and `Err("signal")` distinguish host launch failure, wait failure, and POSIX
signal termination. The argv owner remains valid and is dropped normally after
the call.

Windows strictly converts UTF-8 entries to UTF-16, applies the Microsoft argv
quoting rules, and waits through `_wspawnvp`. Linux creates temporary
zero-terminated argv storage, calls `posix_spawnp`, waits with `waitpid`, and
releases every temporary allocation. Browser wasm rejects the capability until
a host process interface is supplied. Example 87 verifies self-launch, spaces,
Hangul, exit status, and a missing executable on Windows and Linux.

## Generic Binary Scalar I/O

`sys.file` provides a generic writer alongside the legacy sorted-Int64 demo
API:

```smalllang
import sys.file as file

"values.bin" -> file.openWriter
UInt8(65) -> file.write
UInt16(258) -> file.write
Float32(1.5) -> file.write
file.closeWriter
```

`write<T>` is monomorphized for `Bool`, `CodePoint`, the fixed-width signed and
unsigned integers, `Float32`/`Float64`, and target-sized `Size`/`UIntSize`.
Unsupported values such as `Text`, arrays, dictionaries, boxes, and arbitrary
structs are rejected rather than dumping pointer-bearing in-memory layouts.

The current native targets are little-endian, and scalar files use the exact
little-endian bit representation and byte width of the specialized type. A
generic write flushes the legacy Int64 record buffer first so mixing old and new
calls cannot reorder bytes. I/O failure follows the existing fail-fast runtime
status path.

The reader uses explicit zero-input type application and property-call syntax:

```smalllang
"values.bin" -> file.openReader
file.read<UInt16> => value
file.closeReader
```

Its type is `read<T>: -> Result<Option<T>, Text>`. `Ok(Some(value))` is a full
scalar, `Ok(None)` is clean EOF, and `Err("truncated")`, `Err("invalid")`, or
`Err("io")` distinguish partial data, invalid `Bool`/`CodePoint` encodings, and
host failures. The supported specializations and exact native byte layouts are
the same as `write<T>`. Empty parentheses remain invalid for zero-input calls,
so `read<UInt16>()` is rejected. Arbitrary structs still require an explicit
serialization contract rather than implicit ABI dumping.

Double-quoted UTF-8 literals decode `\n`, `\r`, `\t`, and `\\` in text
segments and support optional identifier and expression interpolation. Unknown
backslash sequences remain literal for backward compatibility:

```smalllang
"Hello World"
"Hello, $name"
"next = $(score + 1)"
"object = { name: $name, score: $score }"
"first\nsecond"
```

Interpolation is statically typed. Builtin values use target-neutral writers
selected from the expression result type; they do not first allocate a
temporary Text. `Int` uses decimal output and `Bool` uses the canonical
`true`/`false` spellings. The intended user-defined extension is a statically
dispatched `Display` trait that writes into an interpolation sink. There is no
implicit reflection, debug formatting fallback, or automatic heap promotion.
Formatting adapters/options may be added explicitly without changing the
default `$name` and `$(expression)` syntax.

Triple-quoted raw literals preserve quotes, backslashes, and `$` markers as
ordinary text. A multiline raw literal removes its opening newline, closing
newline, and the indentation shared with its closing delimiter:

```smalllang
"""
JSON and paths need no escaping: C:\data\input.json
$(this remains text)
"""
```

The opening and closing delimiters must have matching indentation. Every
nonblank content line must include that indentation; embedded SmallLang source
should then be indented normally relative to the delimiters:

```smalllang
main {
    """
    main {
        7 => value
        value![0]
    }
    """ => source
}
```

Inline raw literals such as `"""a "quoted" path"""` are also supported.
The delimiter may contain more than three quotes when the content itself must
contain a shorter quote run; opening and closing delimiter widths must match.

Interpolation rules:

- `$name` inserts the current value of the binding named `name`.
- `$(expr)` inserts the value of a SmallLang expression.
- Interpolating an integer value uses its invariant decimal display form.
- `{` and `}` inside string literals are ordinary text characters.
- The older `{name}` interpolation form is removed from the preferred language
  surface because literal braces are common in JSON-like text, CSS-like text,
  blocks, and future dictionary/set syntax.

## Output Surface Semantics

`sys.io.print` and `sys.io.println` are standard library functions. The compiler
globally aliases them as `print` and `println` before user code is analyzed. The
preferred source form is a value-flow call:

```smalllang
"Hello, $name. square = $num" -> print
"Hello, $name. square = $num" -> println
"Hello, $name. square = $num" -> sys.io.print
```

The parenthesized forms remain valid and equivalent:

```smalllang
print("Hello, $name. square = $num")
println("Hello, $name. square = $num")
sys.io.print("Hello, $name. square = $num")
```

Semantically, it resolves to:

```text
sys.io.print(utf8_output_expression)
sys.io.println(utf8_output_expression)
```

`print` emits exactly the requested bytes. `println` emits the requested bytes
followed by a single line-feed byte in the current runtime slice.

## Input Surface Semantics

`sys.io.readInt` is the first input function implemented through the standard
library and globally aliased as `readInt`. The preferred form mirrors output
value flow:

```smalllang
"n = ? " -> readInt => n
"n = ? " -> sys.io.readInt => n
```

The parenthesized form is also valid:

```smalllang
n = readInt("n = ? ")
n = sys.io.readInt("n = ? ")
```

Semantically, it resolves to:

```text
sys.io.readInt(prompt_text) -> Int
```

The current runtime accepts a decimal integer line from standard input. Input
failure or a non-integer input must affect the process exit code; it must not
silently fall back to an arbitrary value.

## Range Loops

The first loop form is implemented as the built-in block function `each`. The
preferred explicit item form is:

```smalllang
1..9 -> each i {
    n * i => value
    "$n x $i = $value" -> println
}
```

When the item name is omitted, SmallLang provides the default binding `it`:

```smalllang
1..9 -> each {
    n * it => value
    "$n x $it = $value" -> println
}
```

The older compatibility spelling remains accepted:

```smalllang
each i in 1..9 {
    n * i => value
    "$n x $i = $value" -> println
}
```

The loop variable is immutable for the iteration and scoped to the loop body.
Bindings introduced inside the loop body are also scoped to that body. The
current range direction is ascending only; if the start is greater than the end,
the loop executes zero times.

`fold` uses the same range input shape but returns a value:

```smalllang
1..100 -> fold 0 sum, i {
    sum + i
} -> total
```

The first expression after `fold` is the initial accumulator value. The first
name is the accumulator binding inside the block and the second name is the
range item binding. The block must return the next accumulator value. The
current implementation supports integer accumulators and lowers the built-in
directly to LLVM loop blocks with accumulator and item phi values.

## Conditionals

The current conditional syntax is flow-oriented:

```smalllang
condition -> if {
    thenBody
} else {
    elseBody
}
```

The expression on the left must be `Bool`. `if` may be used as a statement when
both branches produce `Unit`; in that form the `else` branch may be omitted. When
`if` is used as a value, `else` is required and both branches must produce the
same type.

```smalllang
n == 9 -> if {
    "nine"
} else {
    "other"
} -> label
```

For multiple ordered conditions, `when` is preferred over chaining many nested
`else if` branches:

```smalllang
when {
    score >= 90 { "A" }
    score >= 80 { "B" }
    score >= 70 { "C" }
    else { "F" }
} -> grade
```

`when` checks arms in order. Each arm condition must be `Bool`; the `else` block
is required in the current expression form; all branch values must have the same
type. Branch-local bindings do not escape their branch body.

When every arm compares the same value, the value can flow into `when` once and
each arm can start with a comparison operator:

```smalllang
score -> when {
    >= 90 -> "A"
    >= 80 -> "B"
    >= 70 -> "C"
    else -> "F"
} -> grade
```

This form is equivalent to the full-condition form for ordered integer
comparisons, but the subject expression is evaluated once before the branch
chain. The current shorthand supports `==`, `!=`, `<`, `<=`, `>`, and `>=`.
It also supports inclusive range arms:

```smalllang
score -> when {
    90..100 -> "A"
    80..89 -> "B"
    70..79 -> "C"
    else -> "F"
} -> grade
```

When a one-input function uses the default `it` input, the subject can be
omitted entirely:

```smalllang
grade: Int -> Text => when {
    90..100 => "A"
    80..89 => "B"
    70..79 => "C"
    else => "F"
}
```

If the input is explicitly named, pass it explicitly into `when`:

```smalllang
grade score: Int -> Text => score -> when {
    >= 90 => "A"
    >= 80 => "B"
    >= 70 => "C"
    else => "F"
}
```

## Value-Flow Calls

SmallLang accepts `->` as the preferred direction for function calls where the
input value should be visually explicit:

```smalllang
main {
    getName() => name
    7 -> square => num
    "Hello, $name. square = $num" -> print
}
```

The expression on the left flows into the function or callable path on the
right. `->` is a fluent pipeline step, not a binding form. The example above is
semantically equivalent to:

```smalllang
print("Hello, $name. square = $num")
```

This makes argument flow and return flow visible without discarding normal
parenthesized calls where they are useful. Parenthesized calls remain valid for
ordinary calls such as `getName()`, but the value-flow form is the preferred
SmallLang style for single-primary-input operations.

Return values are bound with `=>`:

```smalllang
getName() => name
7 -> square => num
name -> greeting => message
```

Function targets in a value-flow statement should omit empty parentheses:

```smalllang
7 -> square => num
"Hello, $name. square = $num" -> print
```

The compatibility spelling `value -> function()` is still accepted in this
slice because the flowed value is the function input. A truly zero-input
function uses property syntax (`nowMillis`, not `nowMillis()`). Flow targets
with additional arguments are supported for receiver-style
operations such as `values! -> push(10)` and `scores! -> put(3, 300)`. When a
function-like target receives a brace code block argument, the block argument is
the call marker: `1..9 -> each { ... }` and `1..9 -> each i { ... }` remain
valid without `each()`.

The assignment form remains valid as a compatibility syntax, but the preferred
SmallLang style is still expression-first:

```smalllang
num = square(7)
n * i => value
```

The corresponding function type notation follows the same direction:

```smalllang
greeting: Text -> Text
print: Text -> Io<Unit>
stdout.write: Bytes -> Io<Int>
```

The current parser accepts:

```smalllang
value -> function
```

as a `FlowExpression`. Since binding is now explicit with `=>`, a bare flow
target is never interpreted as a binding. Semantic analysis resolves each target
as a callable path. The executable lowering remains equivalent to:

```smalllang
function(value)
```

for unary calls. Chained value-flow calls are parsed left-to-right:

```smalllang
text -> trim -> lower -> slugify => slug
```

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

For Linux, initial lowering targets a minimal stdin/stdout path equivalent to:

```text
write(stdout_fd, ptr, len)
read(stdin_fd, ptr, len)
```

Requirements:

- Treat short writes as an output failure in the current runtime slice.
- Treat read failure, EOF, or invalid integer text as input failure.
- Avoid heap allocation for static string literals.
- Keep the ABI boundary explicit.
- Use `main` as the Linux executable entry point.

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
    getName() => name
    7 -> square => num
    "Hello, $name. square = $num" -> print
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
-> resolve print as global alias for the SmallLang function sys.io.print
-> inline sys.io.print to sys.runtime.print
-> lower sys.runtime.print to selected backend output bytes
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
    getName() => name
    7 -> square => num
    "Hello, $name. square = $num" -> print
}
```

and the cumulative input and loop sample shown above.

Current backend:

- targets: Windows x64, Linux x64, and browser WebAssembly
- LLVM toolchain: LLVM 22.1.8, downloaded under `.tools` by `scripts/smalllang.ps1`
- lexer: generated from `syntax/smalllang.lexer` by a Roslyn incremental source
  generator
- parser: generated from `syntax/smalllang.grammar` by a Roslyn incremental source
  generator
- semantics: zero-argument and one-input function declarations, including
  default `it` inputs, explicit input names, local function scopes, standard
  library loading, global aliases for `sys.io`, built-in block-function calls,
  string, integer, and boolean bindings, checked integer `+`, `-`, `*`, `/`,
  `%`, unary `-`, parenthesized expressions, integer comparisons, short-circuit
  logical expressions, scalar interpolation, flow-oriented `if`,
  full-condition `when`, subject-value `when`, range-arm `when`, `fold`, `Int`
  static arrays, `Int` dynamic arrays, `{Int: Int}` dictionaries, checked
  indexing, mutable container bindings, move-consuming owner-returning container
  transforms, nominal inline `struct` values, complete field initialization,
  direct nested field access, readonly `self` methods in `impl` blocks,
  parenthesis-free computed members, payload `enum` values, exhaustive enum
  `when` patterns, nominal traits with explicit implementations, checked
  one- and two-type generics with trait bounds, associated-type inference, and
  monomorphization, compile-time
  `Int` value generics with explicit fluent specialization such as
  `value -> fill[4]`, trait associated types with static `impl` bindings and
  equality constraints such as `<T: Source<Item = Int>>`, receiver-argument
  flow targets, explicit `box T` owners, recursively sized user types through
  boxed fields or enum payloads, readonly owned-value borrows, static recursive
  drop glue, and expression-first bindings are type-checked for the current slice
- fixed array literals preserve homogeneous element type for `Int` and `Text`;
  `Text` arrays use 16-byte `%smalllang.text` elements, checked indexing returns
  `Text`, and their backing storage is deterministically released
- copyable user `struct` and `enum` elements receive an element-specific
  parametric array type and use their exact LLVM aggregate layout; arrays of
  recursively owned elements call static element drop glue for every initialized
  slot before freeing the backing buffer; owned-element indexing remains blocked
  until move extraction can transfer one slot without leaving a second owner
- growable arrays preserve `Text` and user-value element layouts, support typed
  empty capacity hints, checked indexing, `len`/`capacity`, type-checked mutable
  `push`, aggregate-aware growth copying, and runtime-length recursive drop
- value-flow calls: `value -> function` and compatibility spelling
  `value -> function()` are parsed as a flow AST and lowered by
  semantic/codegen stages according to target position; bare flow targets cannot
  introduce bindings
- input: `sys.io.readInt` and alias `readInt` lower to a selected stdin backend
  primitive and return an integer value
- file/random workflow: `seedRandom` initializes a deterministic LCG state,
  `randomBelow` returns a pseudo-random integer in `0..maxExclusive-1`,
  `openIntWriter`/`writeInt`/`closeIntWriter` write buffered binary `Int`
  records, and `openIntReader`/`closestInt`/`closeIntReader` query sorted binary
  `Int` records
- loops: `start..end -> each i { ... }` and `start..end -> each { ... }` are
  modeled as block-function calls and lower directly to LLVM basic blocks with
  an SSA phi value for the loop variable, without runtime closure allocation or
  dynamic block dispatch
- folds: `start..end -> fold initial acc, item { nextAcc }` lowers directly to
  LLVM basic blocks with SSA phi values for the item and accumulator, and returns
  the final accumulator value without runtime closure allocation or dynamic
  block dispatch
- IR output: immutable UTF-8 literal segments, runtime function calls, runtime
  i64 arithmetic/comparison, i1 boolean values, one-evaluated subject values for
  subject-value `when`, branch/phi conditional lowering, named inline LLVM
  aggregates for user structs, statically dispatched methods, inlined local
  functions, and runtime integer decimal output
- common emitter: `LlvmEmitter` owns function calls, bindings,
  interpolation, local-function inlining, `each` lowering, integer decimal
  output, containers, and `readInt` parsing. It is split into partial files by
  lowering area so target-independent LLVM emission stays navigable.
- platform runtime layer: `LlvmRuntimePlatform` owns the target triple, native
  entry point name, external OS declarations, stdin/stdout handle setup, and
  byte-level `smalllang_write`/`smalllang_read_stdin` primitives
- Windows entry point: `smalllang_start`
- Windows imports: `GetStdHandle`, `ReadFile`, `WriteFile`
- Windows linker: `lld-link`
- Windows CRT: none
- Linux entry point: `main`
- Linux imports: `read`, `write`
- Linux linker: WSL `cc` after producing an ELF object with Windows LLVM `clang`
- current representative Windows executable sizes: 1,536 bytes for `01-function-basic-hello.sl`
  and `05-function-local.sl`, 2,048 bytes for `08-block-each-default-it.sl`, and 2,560
  bytes for the sorted-int-file workflow samples

The current runtime backend keeps source-language lowering shared across
targets. It calls generated user SmallLang functions, inlines standard library
`sys.io` wrappers, converts integer output to decimal bytes at runtime, and
parses `readInt` in common LLVM IR. Only target triple selection, native entry
point setup, external OS declarations, byte writes, byte reads, and linker choice
are platform-specific.

## Current Module Layout

The compiler implementation is organized by responsibility:

- `Cli`: command line parsing and build orchestration
- `Lexing`: token model and generated lexer
- `Parsing`: parser helpers; the token-to-AST parser is generated
- `Syntax`: AST node definitions
- `Semantics`: current binding/interpolation/I/O/loop lowering
- `CodeGen`: shared LLVM IR generation plus target runtime platform layers
- `Tooling`: LLVM, Windows linker, and WSL Linux linker integration
- `stdlib/sys`: SmallLang standard library modules for I/O, random, file
  workflow wrappers, and intrinsic boundary declarations
- `tests/SmallLang.ExampleTests`: executable sample expected stdout runner

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
- Should string interpolation support additional display formatting later?
- What is the mutability/reassignment model after `=>` binding?
- What comment syntax should be adopted?
- What is the first official target matrix?
- Which LLVM integration strategy will the .NET compiler use?
- How much core library is required before the first executable?
