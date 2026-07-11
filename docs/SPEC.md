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
- integer bindings with decimal integer literals
- integer `+`, `-`, `*`, `/`, `%`, unary `-`, and parenthesized expressions
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
statement    := block_function_call | each_statement | binding_statement | expression_statement
block_function_call := range_or_logical_expression "->" path identifier? block
each_statement := "each" identifier "in" range_expression block
binding_statement := identifier "=" expression statement_end | expression "=>" identifier "!"? statement_end
index_assignment_statement := expression "=>" identifier "!"? "[" expression "]" statement_end
expression_statement := expression statement_end
statement_end := newline+ | "}" lookahead
range_expression := logical_or_expression ".." logical_or_expression
expression   := flow_expression
flow_expression := range_or_logical_expression ("->" (path flow_target_call? | if_flow_target | when_flow_target | fold_flow_target))*
flow_target_call := "(" argument_list? ")"
range_or_logical_expression := range_expression | logical_or_expression
if_flow_target := "if" block_body ("else" block_body)?
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
string_literal := "\"" string_part* "\""
string_part  := string_text | interpolation
interpolation := "$" identifier | "$(" expression ")"
```

Notes:

- Newline is a statement separator, not an indentation rule.
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

## Containers

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
- Dictionaries currently support `{Int: Int}` and own Swiss-style control bytes
  plus slot-aligned key-value entries. Heap storage is the normal placement, but
  a small nonempty dictionary literal may use one aligned stack block when its
  immutable owner is proven readonly and non-escaping. `{Int: Int}` creates an
  empty typed dictionary.
- Typed empty arrays and dictionaries without capacity hints begin with a null
  pointer and zero capacity. Their first mutation allocates initial storage;
  readonly use of the empty value performs no heap allocation.
- Indexing is checked. Out-of-bounds array access and missing dictionary keys
  trap in the current runtime slice.
- `push`, `put`, and indexed assignment require a named mutable owner binding
  created with `=> name!`.
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
- User functions may return `[Int; ~]` and `{Int: Int}` owners. The returned
  owner must be bound directly by the caller so the caller owns the drop point.
  Calling such a function as an anonymous flow source is rejected.
- User functions may accept `[Int]` readonly views. A static `Int` array or
  growable `Int` array can be passed to `[Int]` without transferring ownership.
  The callee can read with indexing, `len`, `each`, and `fold`, but cannot
  mutate or store the view beyond the call.
- User functions may accept `{Int: Int}` as a readonly dictionary view. The
  callee receives `ptr`, `len`, and `capacity` metadata by value and may use
  indexing, `len`, and `capacity`. `put`, indexed assignment, `updated`, return,
  and storage beyond the call are rejected. The caller remains the owner.
- User functions may accept `mut [Int; ~]` and `mut {Int: Int}` mutable
  borrows. The caller must pass a named mutable owner such as `values!` or
  `scores!`. The callee can use existing mutable operations such as `push`,
  `put`, and indexed assignment, and the caller keeps ownership after the call.
- User functions may accept `move [Int; ~]` and `move {Int: Int}` owners.
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
- General generic `[T; N]`, `[T; ~]`, and `{K: V}` containers remain future
  work. Compile-time `Int` value parameters may already specialize fixed repeat
  counts inside functions, for example `[value; N]` in `fill[N: Int]`.

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

The exact string escape set is not finalized. The first required string form is
a double-quoted UTF-8 literal with optional identifier and expression
interpolation:

```smalllang
"Hello World"
"Hello, $name"
"next = $(score + 1)"
"object = { name: $name, score: $score }"
```

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
slice. `square()` as a normal call still means a zero-argument parenthesized
call. Flow targets with additional arguments are supported for receiver-style
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
  single-type generics with trait bounds and monomorphization, compile-time
  `Int` value generics with explicit fluent specialization such as
  `value -> fill[4]`, receiver-argument
  flow targets, explicit `box T` owners, recursively sized user types through
  boxed fields or enum payloads, readonly owned-value borrows, static recursive
  drop glue, and expression-first bindings are type-checked for the current slice
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
- What numeric types beyond the initial signed 64-bit integer should exist?
- What comment syntax should be adopted?
- What is the first official target matrix?
- Which LLVM integration strategy will the .NET compiler use?
- How much core library is required before the first executable?
