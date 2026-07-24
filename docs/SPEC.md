# Sollang Language Specification

Status: implemented living specification
Updated: 2026-07-22

This document is the living specification for Sollang. Normative language
rules follow the current compiler and executable examples; historical design
rationale is retained where it still explains the accepted syntax.

## Current Boundary

The implemented language includes:

- expression-first bindings, fluent calls, functions, local functions,
  structured control flow, block roles, compile-time ranges, and interpolation;
- fixed-width and target-sized numerics, strict UTF-8 `Text`, raw strings,
  nominal structs/enums/traits, associated types, type/value generics, `box`,
  static trait dispatch, and explicit owned `dyn<Trait>` dispatch;
- generic fixed/growable arrays and Swiss-table dictionaries, checked places,
  readonly references with inferred origins, mutable borrows, move paths,
  deterministic recursive drop, and structured async/parallel ownership;
- multi-file namespaces/imports, visibility, deterministic projects/products,
  packages/workspaces, path/Git/registry dependencies, and reproducible locks;
- canonical scalar and affine async/random-access file I/O, portable paths and
  directory snapshots, child processes, memory maps, and explicit user-value
  serialization through `sys.file.BinarySerializable`;
- a parser-backed formatter, language server, VS Code integration, and native
  `sollang test` harness generation; and
- a Sollang-written compiler pipeline from lexer/parser/CST/AST through
  semantics, typed IR, ownership, LLVM emission, and Windows/Linux native
  toolchain orchestration.

The auditable self-hosting boundary is complete at **60/60 gates (100%)**.
Browser WebAssembly remains a supported output target with a deliberately
narrower host-capability surface. Publishing/signing services, broader codecs,
and richer editor features are product extensions, not implicit language rules.

## Core Goals

- Provide a beautiful, simple, and powerful source language.
- Parse and tokenize source code very effectively.
- Compile through LLVM into highly optimized native executables.
- Keep the compiler pipeline efficient from source text to final executable.
- Support cross-platform native output from the beginning.
- Prefer clear compile-time errors over silent fallback behavior.
- Use the latest .NET and latest C# Preview features for the compiler
  implementation unless a later constraint requires otherwise.

## Non-Goals

- No tracing garbage collector, implicit reference counting, or hidden owner
  duplication in the safe language surface.
- No class inheritance, implicit null, or implicit runtime dispatch.
- No silent target fallback, target-dependent ABI serialization, or unchecked
  safe indexing.
- No private grammar embedded inside block roles or package manifests.
- No requirement that bootstrap-maintenance tooling be written in Sollang when
  the Sollang compiler can already consume the canonical generated product.

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
`while` condition tree is regenerated in the header so every iteration observes
the latest stored value. Logical `and` and `or` lower to short-circuit branches
rather than eager bitwise evaluation; `not` exchanges the continuation targets,
and a call-valued leaf executes only when its path is reached. LLVM may then
promote the slot to SSA and synthesize
the loop-carried `phi`; the language semantics do not require a heap allocation
or expose this intermediate representation.

## First Program

The first valid Sollang program is:

```sollang
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

Expected stdout bytes:

```text
Hello, dimohy. square = 49
```

The one-input function can also name its input explicitly:

```sollang
square n: Int -> Int {
    n * n
}
```

The current cumulative input and loop example is:

```sollang
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

```sollang
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

```sollang
getName => name
7 -> square => num
"Hello, $name. square = $num" -> sys.io.print
```

The input and output functions can also be addressed by their canonical module
path:

```sollang
"n = ? " -> sys.io.readInt => n

1..9 -> each i {
    n * i => value
    "$n x $i = $value" -> sys.io.println
}
```

The current conditional form keeps the condition as the value flowing into a
block function-like control expression:

```sollang
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

```sollang
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

```sollang
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

Sollang starts with an explicit `main` block instead of a fully general function
declaration. Local bindings do not use `let`, `var`, or a declaration keyword:

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

For short executable scripts, the `main` wrapper may be omitted:

```sollang
getName => name
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
- `getName => name` and `7 -> square => num` make returned values bindable
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
function_signature := "->" type_annotation? | type_annotation? "->" type_annotation?
block_function_body := "block" identifier ":" type_name "{" statement* "}"
function_body := "{" function_declaration* statement* expression "}" | "=>" expression | "->" expression | "=" "intrinsic"
main_block   := "main" block
block        := "{" statement* "}"
statement    := guard_loop_control_statement | loop_control_statement | each_statement | binding_statement | index_assignment_statement | field_assignment_statement | expression_statement | block_function_call
block_function_call := range_or_logical_expression "->" path identifier? block
each_statement := "each" identifier "in" range_expression block
binding_statement := identifier "=" expression statement_end | expression "=>" identifier "!"? statement_end
index_assignment_statement := expression "=>" identifier "!"? "[" expression "]" statement_end
field_assignment_statement := expression "=>" identifier "!"? "." identifier statement_end
expression_statement := expression statement_end
loop_control_statement := ("break" | "continue") statement_end
guard_loop_control_statement := range_or_logical_expression "->" "if" ("break" | "continue") statement_end
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
primary      := atom postfix_suffix*
postfix_suffix := ("!"? "[" expression "]") | ("." identifier) | "?"
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
- Field, index, mutable-index-view, and propagation suffixes form one repeated
  postfix chain and associate from left to right. Dot and index suffixes may be
  freely interleaved, so `symbols![1].payload![0]` means
  `((symbols![1]).payload)![0]` without requiring grouping parentheses.
- Expression operands are evaluated recursively from inner to outer, and
  sibling operands and call inputs are evaluated from left to right as written.
  Typed IR consumers must reference the concrete value-producing node rather
  than a transparent syntax wrapper; generated LLVM definitions therefore
  dominate every use without changing source evaluation order.
- If `main { ... }` is omitted, remaining top-level statements after function
  declarations are treated as the executable main body.
- A function whose body is a single expression should use `=> expression`
  instead of an outer body block. `-> expression` remains accepted as
  compatibility syntax.
- A statement-level expression followed by `=> name` introduces a local binding
  in the current block.
- `source -> path item? { ... }` introduces a block-function call. Built-in
  `each`, `repeat`, and `fold`, user-defined result roles, and the standard
  `parallel`/`tryParallel` roles use this surface. The following brace block is
  the call marker, so empty parentheses are omitted.
- `condition -> if { ... } else { ... }` introduces the current conditional
  expression form. The value flowing into `if` must be `Bool`.
- A newline may precede `and` or `or`. The line-leading operator continues the
  preceding boolean expression, so a long condition can end with a separately
  visible `-> if` continuation without changing precedence or evaluation order.
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
- `+`, `-`, `*`, and `/` require matching numeric types; `%` is integer-only.
- Unary `-` accepts a signed numeric operand.
- Parentheses group expressions.
- `#` starts a line comment outside string literals.
- numeric comparison operators require compatible numeric operands and return
  `Bool`.
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
  one primary input plus explicitly typed additional inputs. A one-input
  function uses `it` when no input name is supplied, and uses the supplied name
  in `square n: Int -> Int { ... }`.
- A local function may omit its primary input type, return type, or both by
  leaving the corresponding signature slot empty: `map value: -> Int`,
  `map value: Int ->`, or `map value: ->`. A non-public top-level function may
  do the same only when exactly one function or `main` scope consumes it.
  Public, generic, impl, multiply-consumed, conflicting, and underconstrained
  signatures remain explicit.
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
- Imports use the path's final segment as their default alias, so
  `import sollang.compiler.lexer` is equivalent to
  `import sollang.compiler.lexer as lexer`. An explicit `as` may select a
  different alias, such as `import sys.runtime as rt`. Default and explicit
  aliases share the same duplicate-alias diagnostic.
- A `sollang.project` manifest names the project and either one `root` source
  or a nonempty `products` map. The forms are mutually exclusive. Product roots
  are confined existing `.slg` files. `sollang build` searches ancestors;
  `--project` selects a manifest and `--product` selects among multiple products.
- A manifest may contain a `dependencies` map from an import-safe project name
  to an exact relative local directory or manifest path. The key must equal the
  referenced project's name, and that project must expose a same-named product.
  Graph traversal is ordinal and rejects cycles and one-name/multiple-path
  collisions. One source path cannot belong to different packages. A package
  may import only direct dependencies; transitive packages remain visible only
  to the package that declared them.
- A `sollang.workspace` manifest contains one nonempty `members` array of
  relative project directories or manifest paths. Member paths are confined to
  the workspace directory. Project names remain authoritative in each
  `sollang.project`; duplicate member paths or names are rejected. The selected
  package's complete local dependency closure must consist only of declared
  workspace members.
- `--workspace` selects a workspace and `--package` selects one member. A
  workspace with one member may omit `--package`; a multi-member workspace must
  not choose implicitly. A source-free build inside a declared member discovers
  the enclosing workspace and selects that member.
- Local modules resolve relative to the selected product root. The dependency
  name is its first module-path segment; a one-segment import selects its root
  product and deeper paths resolve beneath that product's source directory.
- A manifest build without `-o` writes to `build/<product>` with the target's
  `.exe` or `.wasm` suffix where applicable. Explicit CLI options override only
  build settings, not manifest identity or root ownership.
- A workspace build without `-o` writes to
  `build/<target>/<package>/<product>` under the workspace root, with the same
  target suffix rules. The workspace manifest is part of incremental input
  identity; deleting its disposable cache never changes source state.

## Bindings

The preferred binding syntax is:

```sollang
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

The first function input is the fluent subject. Additional runtime inputs are
declared after it and supplied positionally in parentheses:

```sollang
square: Int -> Int {
    it * it
}

square n: Int -> Int {
    n * n
}

weighted value: Int, scale: Int, offset: Int -> Int {
    value * scale + offset
}

7 -> weighted(3, 2) => flowed
weighted(7, 3, 2) => direct
```

When the input name is omitted, the function body receives the value as `it`.
When the input name is supplied after the function name, the body receives the
value through that binding. This mirrors `start..end -> each { ... }` and
`start..end -> each item { ... }`. In a direct call every runtime input appears
inside the parentheses. In a fluent call the value on the left supplies the
first declared input, and the parenthesized arguments supply the remaining
inputs in declaration order.

Each input independently accepts the existing readonly, `mut`, or `move`
ownership mode. Compile-time generic parameters remain in angle brackets and
are not counted as runtime inputs. Additional runtime arguments are statically
checked for count, type, ownership, and duplicate parameter names.

## Structured Async Functions

`async` is a function effect written immediately before the result type. Calling
an async function starts a child task and returns an affine `Task<T>` owner;
flowing that owner to `await` consumes it exactly once and produces `T`:

```sollang
square value: Int -> async Int {
    value * value
}

main {
    20 -> square => first
    22 -> square => second
    first -> await => a
    second -> await => b
}
```

When no concurrency is needed, the temporary task can be awaited directly in
the same left-to-right flow:

```sollang
6 -> square -> await => squared
```

Naming multiple task-producing calls starts concurrent children. Flowing a
call immediately into `await` expresses sequential suspension without an
otherwise unnecessary task binding.

Tasks are structured resources, not detached handles. Every task must finish
before its lexical scope exits. An explicit `await` chooses where its result is
needed; otherwise scope cleanup joins the task and discards the result. A task
cannot be awaited twice or used after `await`. `main` is the implicit root async
scope, while other functions must declare `async` before using `await`.

The Windows x64 and Linux x64 runtimes represent every task with the same
two-pointer value while an owned task-control record stores its context,
resume/destroy entries, ready-queue link, lifecycle status, and resume state.
The heap context stores input and result slots specialized for their exact
types. Scalar values, immutable `Text`, and value-only structs/enums are
structurally sendable and need no annotation. Heap-owning arrays, dictionaries,
structs, enums, and boxes cross into an async worker only through a `move` input;
the task becomes their sole owner. Mutable borrows and borrowed views are rejected
because the caller could otherwise access the same storage while the worker runs.

`Unit`, numeric,
`Bool`, `Text`, dynamic array/dictionary, struct, enum, and `box` results cross
the task boundary without erasing their type. Owned results transfer to the
awaiting scope; if a task leaves scope unawaited, cleanup joins it and drops the
result before freeing the context. If native worker creation fails, a moved input
is dropped before its context is released. The LLVM emitter calls the common
`sollang_task_start`, `sollang_task_join`, and `sollang_task_release`
runtime boundary. Both native targets now use one cooperative FIFO ready queue;
`await` pumps ready work until its affine target completes, and release invokes
the context destroy entry exactly once. There is no OS thread per task and Linux
does not require pthread linkage. Resume entries return `false` while pending
and `true` when complete. Tail await, sequential direct await bindings,
bindings nested in `if` or `when` branches, and bindings inside `while` bodies
lower to real state machines. The
parent stores its child task, transfers active path values into an exactly laid
out state-specific spill frame, returns to the scheduler, and reloads those
values on resume. A function-entry state switch may target a resume label inside
the original structured branch CFG; branch joins use value or storage-pointer
phi nodes, so immutable values, mutable scalars, mutable owners, and other live
Tasks retain one coherent post-join representation. Straight-line planning
spills only later-referenced values; the first CFG implementation conservatively
spills all active branch bindings. Numeric/Boolean values and scalar-only
structs/enums can cross multiple state 0/1/2/... awaits; ordinary control flow
may execute after the final resume. The self-hosted grammar and IR recognize the
same nested sites, assign stable one-based states per async function, and export
typed `CoroutineFrameSlot` records for live binding symbols. A suspending
`while` creates explicit header phis for every loop-carried value or mutable
storage pointer. Its back-edge can therefore revisit the same numbered await
state on every iteration without replaying earlier iterations. Iterations may
also branch around an await; both the initial and resumed paths converge on the
same back-edge representation.
For sequential direct awaits, heap-owning and mutable values are now supported:
the spill frame temporarily becomes their unique owner, the source local is
removed before cleanup, and resume reconstructs one owner (plus a fresh mutable
slot when needed). Async containers are never stack-promoted because their
buffers must outlive a native resume invocation. The current state number
selects the exact active frame layout and cancel path; the pending-frame destroy
entry cancels the active child and drops initialized owners. `break`,
`continue`, and their compact guarded forms work inside suspending loops. Each
early edge drops body-local owners first, captures the surviving loop-carried
representation, and joins either the continue or exit phi. Consuming a required
outer owner on only one edge is rejected as inconsistent ownership.

A bare `yield` statement is an async-only cooperative suspension point. It
spills the same typed live state as `await`, records its numbered resume state,
returns pending, and lets the FIFO executor append the current Task behind
other ready work. It has no child Task. Cancellation while it is queued invokes
the state-specific destroy path, so CPU loops become cancelable exactly where
the programmer places `yield`. In contrast, `value -> yield` remains the
existing block-function value transfer. Bare `yield` is rejected in `main` and
ordinary synchronous functions because neither has a resumable Task frame.
For a `move` input, each suspension state records whether the original input
owner is still live; cancellation drops either that context owner or the owner
to which it was transferred, never both.

Time suspension uses the public `Duration` value type and the affine
`sleep: Duration -> async Unit` intrinsic. `milliseconds` and `seconds` build a
duration without losing the unit at the call site:

```sollang
250 -> milliseconds -> sleep -> await
```

Integer literals are contextually checked as the constructor's `Long` input,
so the concise spelling keeps the full 64-bit range without an explicit cast.

`sleep` registers its Task in the executor's deadline-ordered timer queue. It
does not allocate an OS thread and does not remain in the runnable queue. When
there is no ready work, the executor waits only until the nearest monotonic
deadline, then moves every due timer to the FIFO ready tail. Zero and negative
durations complete immediately. Canceling a sleeping Task unlinks it from the
timer queue and destroys its context exactly once. File-descriptor readiness,
task groups, closure-capture analysis, and failure propagation follow.

## Local Functions

Functions may declare local helper functions before their final body expression:

```sollang
scale n: Int -> Int {
    double value: -> {
        value * 2
    }

    addBase value: Int -> {
        value + n
    }

    n -> double -> addBase
}
```

Local functions use the same input naming rule as top-level functions. Their
names are visible only inside the containing function and functions nested below
it. They can read bindings from the containing function, such as `n` above.
Omitted primary input types are constrained by all calls in that lexical scope;
omitted return types are constrained by tail expressions and explicit returns.
All constraints must agree. Local functions lower to private LLVM functions
with stable local symbols and explicit capture parameters.

When a `parallel` or `tryParallel` callback calls a local helper, every capture
reachable through that helper's transitive local call graph is part of the
callback capture set. A mutable binding is rejected even when the helper only
reads it, because the submitting scope still names mutable storage that worker
invocations could share. Immutable structurally sendable values remain legal
read-only captures. Recursive helper graphs terminate through a compiler
visited set and one unsafe binding produces one diagnostic per callback.

## Block Functions

Sollang models executable blocks as values passed to block functions at the
semantic layer:

```sollang
1..9 -> each i {
    n * i => value
    "$n x $i = $value" -> println
}
```

`each`, `repeat`, and `fold` are built-in block-function targets. User-defined
block functions and typed result roles use the same grammar and ownership
rules. For `each`, the range expression flows into `each`, the optional
identifier names the block input for each invocation, and the brace body is the
executable block argument. For `repeat`, an integer count flows into `repeat` and
the block receives repeat numbers from `1` through that count:

```sollang
3 -> repeat turn {
    "repeat turn $turn" -> println
}
```

Because the code block is the argument, these forms are written
`-> each i { ... }` and `-> repeat turn { ... }`, not `-> each() { ... }` or
`-> repeat() { ... }`.

Users can define block functions with a typed `block` parameter. Its type is a
full `TypeAnnotation`, so generic and composite capabilities such as `[T; ~]`
are legal. The block-function body calls the passed executable block with
`value -> yield`:

```sollang
runTimes count: Int -> Unit block turn: Int {
    1..count -> each turn {
        turn -> yield
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

## Dynamic Trait Objects

Static trait dispatch remains the default. Runtime polymorphism is explicit:

```sollang
Cat { value: 1 } -> dyn<Speak> => speaker!
speaker! -> Speak.sound => value
```

`dyn<Trait>` is an affine two-pointer value containing an erased data pointer
and a vtable pointer. Slot 0 is the erased drop function. Subsequent slots are
trait methods in declaration order. Conversion allocates storage for the
concrete value and selects its concrete implementation table; dispatch loads
the requested slot and calls it indirectly. Scope cleanup calls slot 0 exactly
once.

The implemented dyn-compatible subset requires a trait without associated
types and synchronous, capture-free methods with readonly `self`, no additional
arguments, and an `Int` result. Concrete values with nested owned storage are
rejected until conversion has an explicit recursive move-transfer contract.
These restrictions are diagnostics, not implicit fallback to static dispatch.

## Standard Library Imports And Aliases

The current standard library implements the `sys.io` module in Sollang:

```sollang
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

```sollang
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

General-purpose sequence operations live in `std.sequence` and require an
explicit import. `range(start, endInclusive)` returns a first-class `Range`
that stores only its two `Int` bounds. `each` consumes that value as a direct
inclusive loop, so no element array is allocated. `map<R>` is declared with
`-> stream R`: when followed by `each`, every emitted value is sent directly to
that consumer without a mapped result array. Streaming stages may be chained;
the final stage is `each` or a `Unit` block function.

```sollang
public map<R> values: Range -> stream R block item: Int -> R {
    values -> each item {
        item -> yield -> emit
    }
}
```

The growable-array counterpart is named `mapArray<T, R>` because the current
language does not overload public functions by source type. One module import
provides the complete sequence vocabulary without an ambiguous `map`:

```sollang
import std.sequence
```

The Range mapper declares `R` only in its callback result and stream element:

```sollang
public map<R> values: Range -> stream R block item: Int -> R
```

For a generic block function whose source and callback input are concrete, the
callback's final expression may infer an otherwise output-only type parameter.
This keeps `Range` lazy while allowing the projected type to be any valid
Sollang value. `filter<T>` conditionally emits its input, and `tap<T>` invokes
an effectful callback before emitting the unchanged value. `take<T>` and
`skip<T>` are ordinary library stream functions, not compiler intrinsics.
Generic parameters may also be inferred from additional arguments, allowing
`scan<T, S>` to derive `S` directly from its initial accumulator.

A stream function can declare pipeline-lifetime mutable state with
`state name! = value`. The initializer runs once when that stage is created;
ordinary statements run once per incoming item. `stop` requests upstream
cancellation after the current item finishes downstream processing. Every
enclosing source loop observes the same signal, including loops nested by
`flatMap`.

```sollang
public take<T> value: T, count: Int -> stream T {
    state taken! = 0
    taken! < count -> if {
        taken! + 1 => taken!
        taken! >= count -> if { stop }
        value -> emit
    } else {
        stop
    }
}
```

`scan<T, S>` combines callback execution and pipeline-lifetime state. For every
incoming `T`, the callback receives the current `S` and the item, and its result
becomes both the next state and the emitted value:

```sollang
public scan<T, S> value: T, initial: S -> stream S block accumulated: S, item: T -> S {
    state current! = initial
    current! -> yield(value) => current!
    current! -> emit
}
```

`yield` invokes the caller-supplied block and returns its result. `emit` forwards
that result to the downstream consumer. Streaming is therefore a declared
property of any user block function, not a compiler rule attached to `map`.

```sollang
values
    -> map item { item * 10 }
    -> each mapped {
        "$mapped" -> println
    }
```

The downstream `each` owns the mapped value for one iteration. Its `break` and
`continue` control the original source loop, so mapping stops as soon as the
consumer stops. `beforeEach` runs immediately before an outer range item enters
the downstream chain. `afterEach` first emits the item and runs its callback
only after downstream processing returns. This expresses scoped preprocessing
and postprocessing without testing the first or last inner index:

```sollang
2..9
    -> beforeEach dan { "$(dan)단" -> println }
    -> afterEach dan { println() }
    -> flatMap(1..9) dan, multiplier {
        "$dan × $multiplier = $(dan * multiplier)"
    }
    -> each line {
        line -> println
    }
```

`Stream<T>` is the affine, first-class form of a deferred producer. It is a
three-word ABI value containing an opaque context, a typed `next` function, and
a `drop` function. The value may be bound to a name, moved through a library
function, returned, and then consumed by `each`. It never owns a materialized
element collection. A second terminal use is an ownership error.

`std.sequence.defer` converts a `Range` into `Stream<Int>` by allocating only
the small producer context that stores the current and inclusive end values.
Local block pipelines continue to use fused loop lowering and allocate no
producer object at all. The public ABI is used only when the stream must become
a runtime value across a function or library boundary.

`EventStream<T>` uses the same affine producer shape but represents a hot
source whose `next` may wait for external input. Dropping it performs structured
cancellation: signal cancellation, wake or interrupt the producer, join its
worker, restore platform state, and release its fixed storage.

`sys.event.mouseEvents(capacity, overflow)` provides the first concrete event
source. `capacity` must be in `2..65536`. Its ring buffer never grows, and
`EventOverflowPolicy` is one of:

- `DropNewest`: retain queued events and discard the incoming event.
- `DropOldest`: discard the oldest queued event and retain the incoming event.
- `CoalesceMotion`: replace the newest pending motion event when possible;
  button and wheel transitions otherwise use oldest-drop behavior.

Windows reads `MOUSE_EVENT_RECORD` values with `ReadConsoleInputW`. Linux
enables SGR 1003/1006 reporting and parses terminal input. Browser WebAssembly
rejects this blocking pull adapter with a target capability diagnostic. DOM
events require a future host-driven callback lowering because synchronous Wasm
execution on the browser main thread cannot wait for an event without blocking
its delivery.

The compiler composes these callbacks as synchronous continuations and lowers
the whole chain into the source loops. No intermediate array, iterator object,
virtual dispatch, or callback allocation is introduced. Interpolated Text is
kept as an ordered formatting plan whose holes are evaluated exactly once when
the value is produced. A following print consumer writes those evaluated
segments into the platform's buffered output sink without first allocating an
owned Text buffer. This is a general stream-value rule rather than a special
case for `flatMap` or `println`.

Importing a module exposes each direct public symbol both through the module
alias and as an unqualified name. Therefore `import std.sequence` permits both
`sequence.flatMap` and `flatMap`. An import path may also end at one public
symbol when a source wants to import only that name or assign an explicit
alias:

```sollang
import std.sequence

2..9
    -> beforeEach outer { "$outer단" -> println }
    -> afterEach outer { println() }
    -> flatMap(1..9) outer, inner {
        "$outer × $inner"
    }
    -> each line {
        line -> println
    }
```

Local declarations and explicit import aliases take precedence over names
opened by a module import. If multiple imported modules expose the same short
name, using that short name is a compile-time ambiguity. The diagnostic lists
the candidates and directs the caller to a qualified spelling such as
`sequence.flatMap` or to an alias for the module. Qualified module access
remains valid regardless of short-name collisions.

Block functions may declare multiple callback inputs after `block`, separated
by commas. Calls provide the same number of item names. Inside the block
function implementation, the flowed value supplies the first callback input
and `yield` arguments supply the rest:

```sollang
pair source: Source -> Unit block outer: Int, inner: Text {
    source.outer -> yield(source.inner)
}
```

Source code can use either spelling:

```sollang
"Hello" -> print
"Hello" -> sys.io.print
"n = ? " -> readInt => n
"n = ? " -> sys.io.readInt => n
```

These functions are resolved through the same function table as user functions.
They are not parsed as keywords or statement-specific built-ins. Their only
current privilege is the global alias layer. The backend inlines the Sollang
`sys.io` wrappers and lowers the `sys.runtime` intrinsic boundary to the
selected platform I/O implementation.

The current purpose-oriented file and random libraries follow the same wrapper
pattern:

```sollang
seedRandom value: Int -> Unit
randomBelow maxExclusive: Int -> Int

openIntWriter path: Text -> Unit
writeInt value: Int -> Unit
closeIntWriter: -> Unit

openIntReader path: Text -> Unit
closestInt target: Int -> Int
closeIntReader: -> Unit
```

The legacy sorted-`Int` file format is binary, little-endian, signed 64-bit records.
`writeInt` appends to the current writer through an internal buffer. `closestInt`
expects the current reader file to be sorted ascending and performs a binary
search over fixed-width records. General affine sync/async file handles,
position-based reads/writes, canonical scalar I/O, mapping, and explicit
user-value serialization are specified in the later file sections. Broader
text/binary codecs remain format-specific library work.

The 100,000,000-record generator avoids a separate sort by dividing
`1..1,000,000,000` into 100,000,000 10-wide buckets and choosing one
pseudo-random value from each bucket in increasing bucket order:

```sollang
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

    closeIntWriter
}
```

This produces sorted unique values with one pseudo-random choice per bucket. It
is not a uniform sample over all possible 100,000,000-element subsets of
`1..1,000,000,000`.

## Numeric Expressions

Numeric expressions use stable, fixed-width primitives:

```sollang
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
- Binary arithmetic requires equal operand types; Sollang does not silently widen,
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

```sollang
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

```sollang
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

```sollang
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

```sollang
[1, 2, 3] => numbers
[0; 8] => zeros
numbers[0] => first
numbers -> len => count
```

Dynamic arrays:

```sollang
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

```sollang
{ 1: 100, 2: 200 } => scores!
scores! -> put(3, 300)
scores![3] => score
scores! -> len => count

{ 1: 100, 2: 200 } => frozenScores
frozenScores -> updated(2, 250) => frozenScores
```

Container rules:

- Static arrays are owned fixed-size `[T; N]` values stored inline in the owner.
- Dynamic arrays own `ptr`, `len`, and `capacity` metadata plus their payload
  storage. The normal payload placement is heap storage.
- A nonempty dynamic-array literal bound to an immutable local may instead use
  inline stack payload storage when the compiler proves every remaining use is
  readonly and the owner does not escape. This optimization does not change the
  source type or syntax.
- Dictionaries infer homogeneous key/value types and own Swiss-style control
  bytes plus type-aligned key-value entries. `Int` and `Text` currently provide
  the required built-in hash/equality operations; local/imported nominal keys
  use explicit static `Hash`/`Eq` implementations. Values may be scalar, `Text`,
  or inline/owned user values; owned entries receive recursive destruction.
  `{Key: Value; N~}` creates an empty typed dictionary with a capacity hint.
  `{Key: Value; keyExpression: valueExpression, ...}` gives nonempty entries
  an explicit type context. Key and value annotations accept imported qualified
  paths and the same recursive type syntax as function signatures.
  The legacy `{Int: Int}` layout also supports readonly stack promotion; other
  specializations currently use heap payload storage.
- Typed empty arrays and dictionaries without capacity hints begin with a null
  pointer and zero capacity. Their first mutation allocates initial storage;
  readonly use of the empty value performs no heap allocation.
- Indexing is checked. Out-of-bounds array access and missing dictionary keys
  trap in the current runtime slice.
- An indexed element that recursively owns storage is a place, not a copied
  value. Field and nested-index projections preserve that place identity, so
  `symbols![key].payload -> inspect` and `symbols![key].payload![0] -> inspect`
  may be passed directly to a default readonly function input. That borrow
  lasts only for the call expression. Binding, returning, storing, or mutating
  through the indexed result or one of its projections is rejected. Use
  `owner! -> take(indexOrKey) => value!` to transfer ownership out explicitly.
- Extracting a stored field whose type reaches heap-backed storage partially
  deinitializes that exact move path. Sibling fields remain usable, while the
  whole owner, the same path, and descendants of that path are unavailable
  until the field is reinitialized. A scalar-only nominal field is a copyable
  value and does not partially move its parent. If the moving control-flow path
  returns before a later use, that use is reachable only from a non-moving path
  and remains valid. The checked self-host compiler reports reachable explicit
  extraction/call/assignment violations as E17 before printing an LLVM target
  header. Implicit owned projections nested inside readonly request literals
  retain drop-tracking metadata but are not classified as explicit E17 moves.
- A function may return `Text` produced by `slice` from one or more
  default-borrowed `sys.file.SourceText` inputs. The compiler infers the union
  of every possible input origin; no lifetime parameter is written in the
  common case. The same origin union is inferred recursively when borrowed
  `Text` values are stored in a returned struct, fixed or growable array, or
  dictionary, including explicit early-return paths. Passing such an aggregate
  through a `Text`-containing function parameter transfers its existing origin
  union without treating an aggregate made only from static or owned `Text` as
  borrowed. Field and index projections inherit the aggregate's origins.
  Moving, replacing, or mutating any origin while a derived value is live is a
  compile-time error. CFG last-use analysis ends the borrow after the final
  reachable aggregate or projection use, so the origin may be moved later.
  Origin metadata is compile-time-only and does not alter struct or container
  ABI. Borrow conflicts are compared by canonical place path. The whole owner
  overlaps every projection, equal paths overlap, and a path overlaps each of
  its descendants. Different stored struct fields and unequal compile-time
  numeric array indices are disjoint at their first difference. A runtime
  index, or projections whose disjointness cannot otherwise be proven, remain
  conservatively overlapping.
- A user struct field may have type `ref T`. The reference remains a plain
  pointer in the runtime layout, while the compiler attaches its inferred
  origin to the containing value. A function may return such a struct only
  when every possible stored reference originates from a reference-bearing
  input; returning a reference to callee-owned storage is E22. Moving,
  rebinding, or mutating an overlapping owner while a stored reference has a
  reachable later use is E23. Liveness is field-sensitive: using a scalar
  sibling after the stored reference's final use does not keep the reference
  live. No lifetime or origin parameters appear in source syntax. Reference
  fields in enum payloads and array/dictionary element storage remain outside
  this implemented struct vertical.
- `push`, `put`, and indexed assignment require a named mutable owner binding
  created with `=> name!`.
- `array -> each item { ... }` binds `item` to the concrete element type for
  fixed and dynamic arrays. Owned elements are readonly borrows for one block
  invocation and are never dropped separately from their array owner.
- `dictionary -> eachKey key { ... }` and `dictionary -> eachValue value {
  ... }` scan occupied Swiss-table slots and bind the concrete K or V type.
  Iteration order is unspecified. Owned items are readonly per-slot borrows.
- `Int` and `Text` have built-in dictionary hash/equality. A local or imported
  nominal key, including one that recursively owns storage, must implement
  `Hash.hash: self -> Int` and `Eq.eq: self -> Int`.
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
- Fixed-array generic contracts, type-preserving collection iterators, and
  user-defined local/imported dictionary `Hash`/`Eq` key dispatch are
  implemented. Compile-time `Int` value parameters specialize fixed repeat
  counts and `[T; N]` input contracts; a fixed array must match the specialized
  compile-time length.

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

```sollang
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
  used bytes. Raw pointers are not exposed in safe Sollang.
- `reset` retains capacity and sets used bytes to zero. Existing offsets become
  logically invalid; subsequent checked access is relative to the new contents.
- `Arena` is affine: readonly borrowing, `mut Arena`, and `move Arena` follow the
  ordinary ownership rules. Final drop frees the current backing block exactly
  once. Individual arena allocations are never freed separately.
- `deferredText -> materialize(arena!)` formats into one contiguous arena
  allocation and returns an ordinary non-owning `Text` view. Interpolation
  holes have already been evaluated once in source order; materialization only
  formats their retained values.
- A materialized `Text` borrows its arena owner. While that view is live,
  `alloc`, `store`, `reset`, move, and drop of the conflicting arena are
  rejected. The loan ends at the view's last reachable use and propagates
  through functions that return the view.
- Immediate sinks consume deferred interpolation directly and do not
  materialize. The explicit boundary is for storage, return, indexing, or code
  that intentionally fixes allocation timing and lifetime.
- The native targets currently support arenas. Browser wasm remains blocked by
  its existing no-heap runtime boundary.

## Memory-Mapped Bytes

Native Sollang programs can map a file directly as an owned byte view:

```sollang
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

```sollang
import sys.process as process

process.arguments => args
args -> len => count
args[1] => sourcePath
args -> each argument {
    "argument = $argument" -> println
}
```

A block function with a non-`Unit` result may appear before another block stage.
The previous result becomes the next source directly; no source-visible
temporary binding is introduced:

```sollang
5
    -> map { it * 3 }
    -> tap { "mapped=$it" -> println }
    -> filter { it > 10 }
    => result
```

Every stage uses ordinary function lookup and the declared source, callback,
and result types. `map`, `tap`, and `filter` are examples of user-defined names,
not keywords or compiler intrinsics. An intermediate `Unit` block is rejected
because it has no value to feed into the following stage.

Generic block types are specialized from the ordinary input before either the
function's `yield` sites or the caller body are checked. For example,
`block items: [T; ~]` becomes `[Int; ~]` when an `Int` source fixes `T = Int`.
The caller body cannot feed constraints back into that choice.

`Arguments` is a copyable, process-lifetime, read-only view rather than an owned
`[Text; ~]`. Its `len` and index use `UIntSize`; indexing returns borrowed
`Text`; `each` binds `Text`. The first item is the executable name supplied by
the host and must not be treated as a canonical or security-checked path.

On Windows, Sollang uses the operating system's Unicode command-line parser and
converts each UTF-16 item to validated UTF-8 storage retained until program
exit. That storage is released exactly once by the runtime. On Linux, the
native `argc`/`argv` entry ABI supplies stable byte spans directly. Browser wasm
does not currently define host process arguments and rejects the property.

Argument setup and its allocation helpers are emitted only when the program
actually references `sys.process.arguments`, preserving allocation-free LLVM
for programs that do not use this host capability.

Environment lookup uses the same module and returns an option so a present
empty value is distinct from a missing name:

```sollang
process.environment("LLVM_ROOT") -> when {
    Option<Text>.None => "LLVM_ROOT is not set"
    Option<Text>.Some(path) => path
} => llvmRoot
```

The input name must be valid UTF-8 without an embedded zero byte. The returned
`Text` is a borrowed process-lifetime view. Linux borrows the stable `getenv`
storage because safe Sollang currently has no environment mutation API. Windows
queries the Unicode environment, converts a present value to UTF-8, retains it
in a runtime-owned allocation list, and releases the list at process exit.
Repeated Windows lookups may retain duplicate converted values until exit; a
future cache may deduplicate them without changing source semantics.

Lookup allocation or encoding failure traps rather than being reported as
`None`; `None` means only that the variable is absent. Browser wasm rejects
environment lookup until a host capability is explicitly supplied.

## Structured Child Processes

`sys.process.run` executes one program directly without invoking a shell:

```sollang
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

## Owned Portable Paths

`sys.path.Path` owns its UTF-8 byte storage rather than borrowing a `Text`.
The representation carries an explicit `Style.Posix` or `Style.Windows`, so a
cross-compiler applies the target's lexical path rules instead of silently
using the host operating system:

```sollang
import sys.path as path

"src//compiler/./../main.slg" -> path.fromText(path.Style.Posix) => source!
source! -> path.normalizeConfined => normalized!
normalized! -> when {
    Ok(value) => value -> path.equalsText("src/main.slg")
    Err(error) => false
}
```

`normalizeConfined(move Path)` is purely lexical and performs no filesystem
access. It removes repeated separators and `.` components, resolves `..`, and
returns `Err("path escapes root")` rather than allowing a parent component to
cross the starting root. Windows drive roots and UNC server/share roots are
preserved; an incomplete UNC prefix is rejected. `join(move Path, Text)` accepts
only a relative child and rejects an absolute child instead of discarding the
base path. `byteCount`, `equalsText`, and `isAbsolute` borrow the Path.

Path bytes use a dedicated canonical `[UInt8; ~]` type identity. `Path`,
`Style`, and their byte-buffer identity are reserved standard-library IDs, so
adding the module cannot renumber unrelated user nominal or parametric types.

`path.nativeStyle` is a target constant, not a host query. It yields
`Style.Windows` for a `windows-x64` compilation and `Style.Posix` for Linux and
wasm targets, even when the compiler itself runs on another operating system.
`sys.file.mapPath(Path)` borrows the Path bytes and returns the same affine
`SourceText` mapping as `mapText(Text)`, but first requires the carried style to
match the compilation target:

```sollang
import sys.path as path
import sys.file as file

"src/main.slg" -> path.fromText(path.nativeStyle) => sourcePath!
sourcePath! -> file.mapPath => source!
source! -> len -> println
```

The Path owner remains valid and is dropped independently; the returned
`SourceText` owns only its native mapping. A style mismatch traps at the
intrinsic boundary instead of interpreting target syntax with host rules. This
provides the ownership-safe bridge needed for deterministic directory discovery
to feed compiler source loading without rebuilding every entry name as Text.

## Deterministic Directory Snapshots

`sys.directory.read(Path)` returns an owned `ReadResult`. A successful snapshot
contains independently owned `Entry` values with an owned basename `Path` and a
`Kind` of `File`, `Directory`, `Symlink`, or `Other`:

```sollang
import sys.path as path
import sys.directory as directory

"src" -> path.fromText(path.Style.Posix) -> directory.read -> when {
    Ok(entries) => entries -> each entry { entry.name -> path.byteCount }
    Err(error) => error -> println
}
```

The native runtime excludes `.` and `..`, orders names lexicographically by raw
UTF-8 bytes, closes the operating-system directory handle before returning, and
serializes the result once into an owned snapshot. The standard-library layer
then gives each entry its own byte buffer, so neither entries nor paths borrow a
native enumeration buffer. Ordering is therefore reproducible even though the
operating system does not promise enumeration order. Windows and Linux support
the operation; browser wasm rejects it until a host filesystem capability is
provided. The supplied `Path.Style` must match the target path syntax.

`sys.path.query` adds target-native canonical resolution, file/directory kind,
byte length, and nanosecond modification time. Lexical normalization remains a
separate operation and does not claim to resolve symlinks by itself.

## Generic Binary Scalar I/O

`sys.file` provides a generic writer alongside the legacy sorted-Int64 demo
API:

```sollang
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

```sollang
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

## Explicit User-Value Serialization

`sys.file.BinarySerializable` is the standard opt-in contract for converting a
user-defined value into canonical binary data:

```sollang
import sys.file as file

struct Packet {
    first: UInt8
    second: UInt8
}

impl file.BinarySerializable for Packet {
    serialize: self -> [UInt8; ~] {
        [UInt8; ~] => bytes!
        bytes! -> push(self.first)
        bytes! -> push(self.second)
        bytes!
    }
}

Packet { first: 65, second: 90 }
    -> file.BinarySerializable.serialize => bytes!
```

An integer literal in a struct initializer inherits the declared integer type
of its field. This removes a conversion that repeats information already visible
in the declaration. The rule is literal-only: `first: 65` is context-typed as
`UInt8`, but an `Int` variable or arithmetic expression is not silently narrowed.
The compiler checks the literal at compile time, so `first: 256` is rejected as
outside `UInt8`'s `0..255` range. Explicit conversions remain available when a
runtime value intentionally crosses numeric types.

The required signature is `serialize: self -> [UInt8; ~]`. The result owns its
storage. The implementation defines field order, framing, integer byte order,
version tags, and any validation metadata required by its format. The compiler
does not reflect over fields or copy a struct's target-dependent ABI layout.
Pointer-bearing values therefore cannot leak addresses or padding through an
implicit fallback. Callers explicitly write or otherwise consume the returned
bytes; decoding remains a separate format-specific contract.

Qualified calls to imported traits use static dispatch when the receiver has a
known concrete type. Both bootstrap and self-host compilers resolve the actual
implementation, including a trait imported through a module alias, and transfer
the owned byte result to the caller.

The asynchronous counterpart is a zero-input generic property as well:

```sollang
file.readAsync<UInt16> => pending
pending -> await => value
```

Its declared type is
`readAsync<T>: -> async Result<Option<T>, Text>`. It preserves the same EOF,
encoding, truncation, and I/O result model as `read<T>`, but the blocking host
file call runs on one shared native file worker. The Task leaves the ready
queue while its request is pending; completion returns it to the FIFO ready
tail. One worker serves all file Tasks, so the runtime never creates one OS
thread per read. Windows uses auto-reset request/completion events and Linux
uses `eventfd` plus `poll`; both feed the same target-neutral completion queue.

Cancellation consumes the Task immediately. A request already owned by the
worker keeps its control record until completion, then destroys its context
exactly once without waking a former waiter. Runtime shutdown drains canceled
requests, stops and joins the shared worker, and releases its native event
resources. Synchronous reads and reader open/close wait for already-submitted
asynchronous work before touching the shared cursor.

The compatibility reader still has one process-wide cursor, but new code can
use an affine native file owner and position-independent reads:

```sollang
file.openReadAsync("values.bin") => opening
opening -> await => opened
opened -> when {
    Ok(reader) {
        reader -> readAt<UInt16>(0) => header
        reader -> readAtAsync<UInt16>(128) => pending
        pending -> await => record
    }
    Err(error) => error
}
```

`openReadAsync(Text)` returns `Task<Result<File, Text>>`; `openWriteAsync(Text)`
returns the corresponding writer Task. The Task owns a copy of the path until
the worker completes. A successful `await` transfers the new native handle
into the Result, while failure and cancellation retain no handle.

`File` is non-copyable and closes deterministically at owner-scope exit.
`readAt<T>(UInt64)` and `readAtAsync<T>(UInt64)` never advance a shared cursor.
The asynchronous form duplicates the native handle into the Task, so the Task
and original File have independent, exactly-once close obligations. Windows
uses an overlapped offset and Linux uses `pread`; high-bit offsets unsupported
by the signed host APIs return `Err("io")`.

Writes use a separate affine capability so a read-only handle cannot be used as
a writer:

```sollang
file.openWrite("values.bin") => opened
opened -> when {
    Ok(writer) {
        writer -> writeAt(UInt16(513), 0) => inferred
        writer -> writeAt<UInt16>(1027, 3) => contextual
        writer -> writeAtAsync(UInt16(2049), 8) => pending
        pending -> await => asynchronous
        writer -> syncAsync => syncing
        syncing -> await => durable
    }
    Err(error) => error
}
```

`openWrite` creates or truncates the file. `writeAt(value, UInt64)` infers the
scalar type from `value`; the optional type argument gives an untyped literal a
context. The operation returns `Result<Unit, Text>` and succeeds only after the
entire scalar is written. It never advances a cursor and extends the file when
the offset lies beyond its end. Windows uses an overlapped offset and Linux
uses `pwrite` without append mode.

`writeAtAsync` has the same inference and explicit-context forms and returns
`Task<Result<Unit, Text>>`. Submission copies the scalar bytes and duplicates
the writer's native handle into the affine Task; it never borrows caller stack
storage or the original writer. Completion and cancellation therefore retain
independent exactly-once close obligations. The shared file worker dispatches
both reads and writes.

`syncAsync: -> async Result<Unit, Text>` is a durability barrier. It waits for
file data and metadata to reach the filesystem through `FlushFileBuffers` on
Windows and `fsync` on Linux. The operation owns a duplicate writer handle and
shares the same FIFO worker, so earlier submitted writes complete before the
barrier. Sollang deliberately calls this `sync`, not `flush`: random-access writers
have no hidden language buffer to empty. Deterministic scope drop closes the
original handle immediately and does not await Tasks because every pending
operation owns its own duplicate. A future IOCP/io_uring completion backend
remains before the general file-I/O gate is complete.

Compiler and cache code may use the synchronous `writer -> file.sync` barrier.
After the affine writer's scope closes, `file.AtomicReplaceRequest` passed to
`file.atomicReplace` publishes the staged path over the destination atomically.
Windows uses `MoveFileExA` with replace-existing and write-through flags; Linux
uses `rename` after `fsync`. Wasm reports `false` because it has no host
filesystem. The destination is therefore either the previous complete file or
the new complete file, never a partially written cache entry.

Double-quoted UTF-8 literals decode `\n`, `\r`, `\t`, and `\\` in text
segments and support optional identifier and expression interpolation. Unknown
backslash sequences remain literal for backward compatibility:

```sollang
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

```sollang
"""
JSON and paths need no escaping: C:\data\input.json
$(this remains text)
"""
```

The opening and closing delimiters must have matching indentation. Every
nonblank content line must include that indentation; embedded Sollang source
should then be indented normally relative to the delimiters:

```sollang
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
- `$(expr)` inserts the value of a Sollang expression.
- Interpolating an integer value uses its invariant decimal display form.
- `{` and `}` inside string literals are ordinary text characters.
- The older `{name}` interpolation form is removed from the preferred language
  surface because literal braces are common in JSON-like text, CSS-like text,
  blocks, and future dictionary/set syntax.

## Effect Sets And Capability Boundaries

Every function is pure by default. A function that performs an external effect
declares the required capability set after its return type:

```sollang
announce text: Text -> Unit uses Console {
    text -> println
}

archive path: Text -> Result<file.File, Text> uses File, Clock {
    path -> file.openRead
}
```

The initial closed set is `Console`, `File`, `Clock`, `Random`, `Process`, and
`Environment`. Unknown or duplicate names are compile errors. Calling an
effectful function requires the caller to declare every callee effect, so the
set propagates through ordinary, local, generic, block, member, and imported
calls. `map` and mapped-view `flush` require `File` even though they are
language forms rather than ordinary functions. `main` is the unrestricted root
capability boundary.

`async` is intentionally separate: it describes suspension and scheduling,
not permission to access the clock, filesystem, console, or process state. An
async timer therefore declares both parts, for example
`delayed: -> async Int uses Clock`.

These six names are fixed external capabilities, not user-replaceable
algebraic operations. A normal `handle` role therefore cannot erase them and
make real I/O appear pure. Future handleable effects use separately declared
effect signatures and operations; only those operations may be discharged by
a matching lexical handler. The current implementation checks the closed
capability set and does not silently infer or erase authority.

The accepted user-defined surface keeps operation types inside a module-level
effect declaration and references that effect from `uses`:

```sollang
public effect Failure {
    fail message: Text -> Int
}

parse text: Text -> Int uses Failure {
    text -> fail
}
```

An imported declaration is named through its normal import alias, for example
`uses fx.Failure`. Inside a function, an unqualified operation such as `fail`
is selected only from that function's declared user effects. Ordinary lexical
functions keep precedence. An explicit `Failure.fail` call still requires
`uses Failure`; qualification does not grant an effect capability. Duplicate
operations, unknown effects, inaccessible imported effects, missing `uses`,
and ambiguous operations are compile-time diagnostics.

This surface is currently implemented as a self-host grammar, AST, symbol, and
semantic-analysis product. Reference-compiler parsing, canonical operation
type checking, lexical handler discharge, resumptions, and LLVM lowering are
not implemented yet, so user effect operations are not executable runtime
features at this stage.

## Output Surface Semantics

`sys.io.print` and `sys.io.println` are standard library functions. The compiler
globally aliases them as `print` and `println` before user code is analyzed. The
preferred source form is a value-flow call:

```sollang
"Hello, $name. square = $num" -> print
"Hello, $name. square = $num" -> println
"Hello, $name. square = $num" -> sys.io.print
```

The parenthesized forms remain valid and equivalent:

```sollang
print("Hello, $name. square = $num")
println("Hello, $name. square = $num")
sys.io.print("Hello, $name. square = $num")
```

The explicit zero-argument call `println()` emits one empty line without
requiring an empty string literal:

```sollang
"first section" -> println
println()
"second section" -> println
```

This call form is statement-only. It does not change the declared
`println value: Text -> Unit` function signature and does not apply to `print`.

Semantically, it resolves to:

```text
sys.io.print(utf8_output_expression)
sys.io.println(utf8_output_expression)
```

`print` emits exactly the requested text. `println` emits the requested text
followed by one platform line break; `println()` emits only that line break.

## Input Surface Semantics

`sys.io.readInt` is the first input function implemented through the standard
library and globally aliased as `readInt`. The preferred form mirrors output
value flow:

```sollang
"n = ? " -> readInt => n
"n = ? " -> sys.io.readInt => n
```

The parenthesized form is also valid:

```sollang
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

```sollang
1..9 -> each i {
    n * i => value
    "$n x $i = $value" -> println
}
```

When the item name is omitted, Sollang provides the default binding `it`:

```sollang
1..9 -> each {
    n * it => value
    "$n x $it = $value" -> println
}
```

The older compatibility spelling remains accepted:

```sollang
each i in 1..9 {
    n * i => value
    "$n x $i = $value" -> println
}
```

`break` exits the closest lexically enclosing loop and `continue` transfers to
that loop's next condition/iteration block. Both are statements without a
value. Using either outside a loop is a semantic error. A control transfer
drops every owned local created since the target loop was entered before the
LLVM branch is emitted; nested loops therefore clean up and target their own
innermost scope.

A single conditional transfer may use the guard-flow shorthand:

```sollang
inner! == 2 -> if continue
inner! == 3 -> if break
```

The condition must be `Bool`. This is exactly the braceless form of
`condition -> if { continue }` or `condition -> if { break }`; false continues
with the next statement. Sollang deliberately does not use `?` here because
postfix `?` already means typed `Result` propagation.

The self-hosted LLVM backend represents an early loop transfer as a dedicated
loop-exit IR node; guarded exits additionally carry their Bool condition. A
true guard branches through an explicit cleanup basic block while false reaches
the following statement. Region-local
dynamic arrays release their backing pointer; dictionaries release key and
value stores in reverse declaration order. The normal back-edge invokes the
same scope cleanup so every iteration has identical ownership semantics.

An explicit early return keeps Sollang's left-to-right flow:

```sollang
value -> return
return # Unit functions only
```

The returned owner transfers to the caller. Every other active owned local is
dropped in reverse declaration order before the LLVM `ret` terminator. The
reference compiler supports scalar, aggregate, and Unit returns; the
self-hosted LLVM slice proves a scalar return from a structured region while
cleaning an owned array. Local functions may use the same explicit return form;
their local owners are cleaned before either an early or tail return. General
moved-region paths remain part of the structured early-exit follow-up.

The loop variable is immutable for the iteration and scoped to the loop body.
Bindings introduced inside the loop body are also scoped to that body. The
current range direction is ascending only; if the start is greater than the end,
the loop executes zero times.

`fold` uses the same range input shape but returns a value:

```sollang
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

```sollang
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

```sollang
n == 9 -> if {
    "nine"
} else {
    "other"
} -> label
```

For multiple ordered conditions, `when` is preferred over chaining many nested
`else if` branches:

```sollang
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

```sollang
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

```sollang
score -> when {
    90..100 -> "A"
    80..89 -> "B"
    70..79 -> "C"
    else -> "F"
} -> grade
```

When a one-input function uses the default `it` input, the subject can be
omitted entirely:

```sollang
grade: Int -> Text => when {
    90..100 => "A"
    80..89 => "B"
    70..79 => "C"
    else => "F"
}
```

If the input is explicitly named, pass it explicitly into `when`:

```sollang
grade score: Int -> Text => score -> when {
    >= 90 => "A"
    >= 80 => "B"
    >= 70 => "C"
    else => "F"
}
```

## Value-Flow Calls

Sollang accepts `->` as the preferred direction for function calls where the
input value should be visually explicit:

```sollang
main {
    getName => name
    7 -> square => num
    "Hello, $name. square = $num" -> print
}
```

The expression on the left flows into the function or callable path on the
right. `->` is a fluent pipeline step, not a binding form. The example above is
semantically equivalent to:

```sollang
print("Hello, $name. square = $num")
```

This makes argument flow and return flow visible without discarding normal
parenthesized calls where they supply arguments. Zero-input functions use
property syntax such as `getName`; the value-flow form is the preferred
Sollang style for single-primary-input operations.

Return values are bound with `=>`:

```sollang
getName => name
7 -> square => num
name -> greeting => message
```

Function targets in a value-flow statement should omit empty parentheses:

```sollang
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
Sollang style is still expression-first:

```sollang
num = square(7)
n * i => value
```

The corresponding function type notation follows the same direction:

```sollang
greeting: Text -> Text
print: Text -> Io<Unit>
stdout.write: Bytes -> Io<Int>
```

The current parser accepts:

```sollang
value -> function
```

as a `FlowExpression`. Since binding is now explicit with `=>`, a bare flow
target is never interpreted as a binding. Semantic analysis resolves each target
as a callable path. The executable lowering remains equivalent to:

```sollang
function(value)
```

for unary calls. Chained value-flow calls are parsed left-to-right:

```sollang
text -> trim -> lower -> slugify => slug
```

A newline may appear before a continuing `->` or the result-binding `=>`.
Because neither token can begin an independent statement, the continuation is
unambiguous:

```sollang
text
    -> trim
    -> lower
    -> slugify
    => slug
```

Boolean composition may likewise continue with line-leading `and` and `or`:

```sollang
user.isActive
    and user.profile.isVerified
    and request.canWrite
    -> if {
        save
    }
```

The formatter indents continuation arrows and boolean operators one level
beyond the source line. A block opened by a continuation keeps that additional
indentation for its body and closing brace.
It preserves the exact left-to-right evaluation order; the vertical form does
not create a different AST or an implicit scope.

Role-local names may be omitted when they are uniquely determined. Function
inputs and block items default to `it`. A fold with no explicit names defaults
its accumulator to `acc` and its item to `it`:

```sollang
1..100 -> fold 0 {
    acc + it
} => total
```

The explicit `fold 0 total, value { ... }` form remains available when domain
names communicate more meaning.

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
-> resolve print as global alias for the Sollang function sys.io.print
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

## Implemented Language Surface

The current compiler supports:

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

and the cumulative input and loop sample shown above.

Current backend:

- targets: Windows x64, Linux x64, and browser WebAssembly
- LLVM toolchain: LLVM 22.1.8, downloaded under `.tools` by `scripts/sollang.ps1`
- lexer: generated from `syntax/sollang.lexer` by a Roslyn incremental source
  generator
- parser: generated from `syntax/sollang.grammar` by a Roslyn incremental source
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
  `when` patterns, nominal traits with explicit implementations, explicit
  affine `dyn<Trait>` conversion and vtable dispatch, checked
  one- and two-type generics with trait bounds, associated-type inference, and
  monomorphization, compile-time
  `Int` value generics with explicit fluent specialization such as
  `value -> fill[4]`, trait associated types with static `impl` bindings and
  equality constraints such as `<T: Source<Item = Int>>`, receiver-argument
  flow targets, explicit `box T` owners, recursively sized user types through
  boxed fields or enum payloads, readonly owned-value borrows, static recursive
  drop glue, and expression-first bindings are type-checked
- fixed array literals preserve homogeneous element type for `Int` and `Text`;
  `Text` arrays use 16-byte `%sollang.text` elements, checked indexing returns
  `Text`, and their backing storage is deterministically released
- copyable user `struct` and `enum` elements receive an element-specific
  parametric array type and use their exact LLVM aggregate layout; arrays of
  recursively owned elements call static element drop glue for every initialized
  slot before freeing the backing buffer; an owned indexed element can be
  borrowed only as a direct readonly-call argument, while `take` transfers one
  slot without leaving a second owner
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
- incremental LLVM units: the production emitter writes one target/configuration
  prefix, stable-hash-ordered per-module function units, and one program suffix.
  Unit-local string symbols and function-local SSA numbering make module text
  independent of earlier emission order. `sollang build` may reuse a module only
  when its exact implementation, transitive public interfaces, concrete
  specialization inventory, compiler identity, target, and configuration match.
  The persistent binary generation has bounded lengths, canonical unit order,
  full module identities, the schema-1 per-fragment and envelope checksums, and is
  atomically replaced only after a successful link. A corrupt or incompatible
  generation is reported and rebuilt; cached and clean LLVM bytes must match.
- exact-input frontend cache: after a successful link, `sollang build` records
  the exact root, manifest, standard-library, and discovered user-source bytes in
  a checksummed snapshot bound to the SHA-256 digest of the matching LLVM-unit
  generation. If compiler, target, configuration, source sets, bytes, snapshot
  checksum, codegen digest, and codegen envelope all match, a warm build skips
  lexing, parsing, semantic analysis, and LLVM emission and links the validated
  units directly. Any source change falls back to the normal frontend and may
  still reuse independently valid LLVM units. Snapshot comparison is streamed;
  publication uses a same-directory write-through temporary file and atomic
  replacement after a successful link.
- exact-product cache: a small checksummed `.product` generation binds the
  validated source snapshot, LLVM-unit generation, and final target artifact by
  SHA-256 together with compiler, target, and configuration identity. If all
  three files still match, an exact warm build also skips the linker. A missing
  or changed final artifact relinks from the already validated LLVM units and
  atomically publishes a repaired product generation without rerunning the
  frontend.
- semantic identity generation: a schema-4 `.semantic` generation stores
  canonical structural identities for bound functions and resolved generic call
  sites, the visible declaration-universe fingerprint, exact per-module source
  digests, and reusable function binding/captured-binding type maps.
  It never persists process-local `TypeId` values or AST object addresses, is
  bound to compiler/target/configuration identity, uses strict UTF-8 and bounded
  canonically ordered records, and protects the complete payload with SHA-256.
  After declarations are rebuilt, a changed-source build may skip body
  validation only when the function's module digest and the complete visible
  declaration fingerprint are unchanged. Persisted structural types are
  re-interned into the fresh type table. A parent and all recursively nested
  local functions are restored as one atomic tree. Stable syntax-call identities
  are assigned before resolution across ordinary calls, type applications,
  fluent targets, and block-function calls. Resolved edges carry either a
  generic template plus structural type/value arguments or the concrete
  signature of a synthesized runtime specialization. Rehydration reconstructs
  the current-session target, verifies its full stable identity, follows nested
  specialization edges, and restores user-specialization bindings. Main uses
  the same process when its executable module is exact. Function and main call
  restoration is transactional; any missing or invalid node, recipe, type, or
  target falls back to normal validation without publishing partial state.
  Publication is write-through and atomic after a successful link.
- common emitter: `LlvmEmitter` owns function calls, bindings,
  interpolation, local-function inlining, `each` lowering, integer decimal
  output, containers, and `readInt` parsing. It is split into partial files by
  lowering area so target-independent LLVM emission stays navigable.
- platform runtime layer: `LlvmRuntimePlatform` owns the target triple, native
  entry point name, external OS declarations, stdin/stdout handle setup, and
  byte-level `sollang_write`/`sollang_read_stdin` primitives
- Windows entry point: `sollang_start`
- Windows imports: `GetStdHandle`, `ReadFile`, `WriteFile`
- Windows linker: `lld-link`
- Windows CRT: none
- Linux entry point: `main`
- Linux imports: `read`, `write`
- Linux linker: WSL `cc` after producing an ELF object with Windows LLVM `clang`
- executable size is toolchain, optimization-level, and feature dependent;
  dated measurements belong in `benchmarks/`, not in the normative language
  contract

The current runtime backend keeps source-language lowering shared across
targets. It calls generated user Sollang functions, inlines standard library
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
- `stdlib/sys`: Sollang standard library modules for I/O, random, file
  workflow wrappers, and intrinsic boundary declarations
- `tests/Sollang.ExampleTests`: executable sample expected stdout runner
- `selfhost`: Sollang lexer/parser/CST/AST, semantics, typed IR, ownership,
  incremental cache, LLVM emission, and native compiler driver modules

Lexer rules are expressed in the compact `syntax/sollang.lexer` file. The source
generator reads that file as an MSBuild `AdditionalFiles` input and emits
`TokenKind` plus the deterministic lexer during C# compilation.

Parser rules are expressed in the compact `syntax/sollang.grammar` file. The
source generator emits the C# bootstrap parser, while the generated ordinary
Sollang table drives the self-host lexer/parser/CST/AST pipeline. Byte-for-byte
table determinism prevents the two paths from silently drifting.

## Follow-On Design Extensions

The core syntax, ownership model, target matrix, compiler pipeline, and
self-hosting boundary are implemented. Possible future specifications must be
accepted explicitly and include their own executable evidence. Current
extension areas include richer format-specific serialization/decoding,
publishing/signing services, additional editor refactorings, and wider browser
host capabilities. None of these imply a fallback or weaken the existing
Windows/Linux/native ownership guarantees.
