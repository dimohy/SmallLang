# SmallLang Self-Hosting Roadmap

Status: active
Updated: 2026-07-13

The end state is an SL compiler written in SL that reads a multi-file SL
program, performs lexical, syntactic, type, ownership, and module analysis,
emits LLVM IR, and invokes the platform toolchain. The existing C# compiler is
the bootstrap compiler until the SL compiler passes a reproducible stage-2
comparison.

## Research Basis

The design deliberately combines a small set of compatible ideas:

- Rust: affine ownership, explicit traits, associated types, and static
  dispatch by default. See the official
  [trait reference](https://doc.rust-lang.org/reference/items/traits.html) and
  [associated items](https://doc.rust-lang.org/stable/reference/items/associated-items.html).
- Rust tracks moves separately from values and elaborates destruction from that
  analysis; Swift makes consuming parameters part of the declaration contract.
  SL follows the same separation with a typed-IR move-event side table, while
  retaining structured regions until LLVM lowering. See rustc
  [move paths](https://rustc-dev-guide.rust-lang.org/borrow-check/moves-and-initialization/move-paths.html),
  Rust [partial moves](https://doc.rust-lang.org/rust-by-example/scope/move/partial_move.html),
  and Swift [declarations](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/declarations/).
- Mojo: compile-time type and value parameterization with specialization at use
  sites; SL uses angle brackets to keep this separate from arrays. See
  [generics](https://docs.modular.com/mojo/manual/generics/) and
  [parameterization](https://docs.modular.com/mojo/manual/parameters/).
- Zig: an explicit root-module dependency graph and declaration discovery from
  reachable imports. See the official
  [compilation model](https://ziglang.org/documentation/master/#Compilation-Model).
- Swift: source files belong to modules, packages group modules, declarations
  are internal by default, and public API is opt-in. See
  [access control](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/accesscontrol/)
  and [packages](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/introducingpackages/).
- Rust and Swift separate UTF-8 code units, Unicode scalar values, and
  user-perceived grapheme clusters. SL adopts Rust's fixed-width scalar model
  for compiler work while reserving grapheme segmentation for a library layer.
  See Rust [`char`](https://doc.rust-lang.org/std/primitive.char.html) and
  Swift [Strings and Characters](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/stringsandcharacters/).
- Rust routes default formatting through the static `Display` trait and a
  writer-like `Formatter`; Swift lowers interpolation into typed
  `appendLiteral`/`appendInterpolation` calls; Zig validates compile-time-known
  format descriptions in ordinary library code. SL combines those ideas:
  interpolation segments and result types are fixed at compile time, builtin
  values stream directly to the output sink, and user values will use a static
  `Display` trait rather than implicit reflection or heap-built temporary Text.
  See Rust [`std::fmt`](https://doc.rust-lang.org/stable/std/fmt/), Swift
  [`StringInterpolationProtocol`](https://developer.apple.com/documentation/swift/stringinterpolationprotocol),
  and the Zig [language reference](https://ziglang.org/documentation/master/).
- Zig recommends an arena when allocations share one lifetime and can all be
  released together; rustc describes arena allocation as a pointer bump. SL's
  byte arena follows that lifetime model while exposing checked offsets instead
  of raw pointers. See Zig [Choosing an Allocator](https://ziglang.org/documentation/master/#Choosing-an-Allocator)
  and rustc [`rustc_arena`](https://doc.rust-lang.org/stable/nightly-rustc/rustc_arena/index.html).
- MLIR SCF keeps `if` and loop bodies as structured regions, while Rust MIR
  makes the later control-flow graph explicit as typed basic blocks ending in
  terminators. LLVM then lowers an `if` to a conditional branch, two branch
  blocks, a continuation block, and a `phi` only when the expression produces
  a joined value. SL follows that staged boundary: structured regions in typed
  IR first, explicit LLVM CFG during backend lowering. See MLIR
  [SCF](https://mlir.llvm.org/docs/Dialects/SCFDialect/), the rustc guide to
  [MIR](https://rustc-dev-guide.rust-lang.org/mir/index.html), and LLVM's
  [control-flow tutorial](https://llvm.org/docs/tutorial/MyFirstLanguageFrontend/LangImpl05.html).
- Windows file mappings and POSIX `mmap` keep large files outside ordinary heap
  allocation while exposing bounded views. SL wraps those views in affine
  ownership and aligns hidden base mappings to the host granularity. See
  Microsoft [Creating a View Within a File](https://learn.microsoft.com/en-us/windows/win32/memory/creating-a-file-view)
  and Linux [`mmap(2)`](https://man7.org/linux/man-pages/man2/munmap.2.html).
- Rust exposes process-provided arguments separately from ordinary owned
  collections and cautions that argument zero is not a trusted executable path;
  Zig passes explicit process initialization state to `main`; Swift and Mojo
  keep child execution in a structured process API. SL follows those boundaries
  with a read-only `Arguments` view and a shell-free argv-based child-process API.
  See Rust [`args_os`](https://doc.rust-lang.org/std/env/fn.args_os.html),
  Swift [`Process`](https://developer.apple.com/documentation/foundation/process),
  and Mojo [`subprocess`](https://docs.modular.com/mojo/std/subprocess/).

SL keeps its own expression-first `=>` binding and fluent `->` application
syntax. It does not adopt class inheritance, implicit null, implicit garbage
collection, implicit heap allocation, or a default runtime dispatch layer.

## Progress Calculation

There are 60 auditable capability gates. A complete gate scores 1, a partial
gate scores 0.5, and a missing gate scores 0. The percentage is the score
divided by 60. A gate becomes complete only with a cumulative `.sl` example or
an automated compiler test. This count measures language/compiler capability,
not lines of code.

| Area | Gates | Complete | Partial | Missing | Score |
| --- | ---: | ---: | ---: | ---: | ---: |
| Core syntax and control flow | 10 | 8 | 2 | 0 | 9.0 |
| Types, traits, and generics | 12 | 10 | 1 | 1 | 10.5 |
| Ownership and storage | 10 | 7 | 2 | 1 | 8.0 |
| Modules, visibility, and builds | 8 | 4 | 2 | 2 | 5.0 |
| Compiler-construction primitives | 12 | 10 | 2 | 0 | 11.0 |
| Standard library and tooling | 8 | 2 | 3 | 3 | 3.5 |
| **Total** | **60** | **41** | **12** | **7** | **47.0 / 60** |

Current count-based progress: **78.3% (47.0 of 60 equivalent gates)**.
There are **13.0 equivalent gates remaining**. Because the remaining compiler
primitives are harder than early syntax gates, this is not an elapsed-time
estimate.

The async executor now has an owned, target-neutral task-control ABI with
context/resume/destroy pointers, FIFO ready linkage, lifecycle status, and a
resume-state field. Windows and Linux execute the same cooperative queue and no
longer allocate one OS thread per task. The self-hosted typed-IR module emits
stable `CoroutineSuspendPoint` records for `await` sites inside async functions
and typed `CoroutineFrameSlot` records for bindings live across each state. The
reference compiler lowers tail await, sequential direct await bindings,
`if`/`when` branch-nested await, and `while`-nested await to real multi-state
resume functions. Branch
states jump directly into the original structured CFG, use state-specific
frames, and merge resumed immutable or mutable storage through LLVM phi nodes.
Loop headers add value and mutable-storage phis so a back-edge can revisit the
same suspension state over many iterations, including iterations that branch
around the await. The self-host grammar now parses braced multi-line `when` arms
consistently with the reference parser, and typed IR assigns nested suspension
states per async function. Suspending-loop `break`/`continue` edges now drop
body-local owners, capture their surviving loop scope, and join dedicated
continue/exit phis; guarded forms use the same edge transport. This advances
the async gate but does not change the formal score. Bare async `yield` now
spills live state without a child Task, requeues the current Task at FIFO tail,
and makes long CPU loops explicitly cancelable. Self-host suspension metadata
distinguishes await and yield sites while sharing stable per-function state
numbering. Typed `Duration` and `sleep: Duration -> async Unit` now feed a
deadline-ordered runtime timer queue. Sleeping Tasks leave the ready queue,
due timers wake at FIFO tail, and cancellation unlinks timer waiters without a
per-Task OS thread. Self-host module/call resolution recognizes the separate
`sys.time` module and preserves the timer await suspension state. Generic
`readAsync<T>` now sends scalar file reads to one shared native worker and
returns completions through the same ready queue. Windows uses Events; Linux
uses `pthread`, `eventfd`, and `poll`. Cancellation defers destruction only
while the worker owns the request, and shutdown drains then joins the worker.
The self-host grammar now has a real `TypeApplicationExpression`, its parser
accepts expression entry rules before the synthetic End token, and imported
generic calls such as `file.readAsync<UInt16>` retain their await suspension.
Flow targets now also preserve type arguments such as
`reader -> readAtAsync<UInt16>(offset)`. Self-host call scanning ignores type
and runtime argument identifiers when selecting the target name.

`sys.file.File` is now an affine native reader owner with deterministic close.
`readAt<T>` and `readAtAsync<T>` use explicit UInt64 offsets; async Tasks own a
duplicated native handle, Windows uses overlapped reads, and Linux uses `pread`.
`sys.file.FileWriter` is a distinct affine write capability. `writeAt(value,
UInt64)` infers its scalar type, optionally accepts an explicit type context,
requires an all-or-error full scalar write, and lowers to overlapped `WriteFile`
or Linux `pwrite`. `writeAtAsync<T>` copies its scalar into the Task, duplicates
the writer handle, and shares the existing file worker and completion queue;
self-host call and suspension analysis retain its generic flow target.
`syncAsync` adds a durable-data barrier through Windows `FlushFileBuffers` and
Linux `fsync`, also with a Task-owned duplicate. Scope drop remains the close
operation because pending Tasks never borrow the source handle. Async open,
failure propagation, captures, and task groups remain partial.
Straight-line states now carry heap owners and
mutable locals safely: frame storage temporarily owns the value, resume restores
one owner, and async container stack promotion is disabled. Self-host frame-slot
flags preserve mutable, composite-owner, and affine-Task bits for destroy
lowering. State-specific branch frames avoid reading sibling-path slots that
were never initialized; loop back-edges use explicit initialization-dominating
phis for persistent loop-carried state.

The coordinated regression runner now schedules known self-host LLVM cases
first and uses a dynamically load-balanced partitioner across eight workers.
On the same checkout and machine, an unfiltered 343-case run fell from 793.8 to
382.8 seconds while retaining grammar determinism and isolated artifacts. This
removes worker starvation; compiler/module fingerprint caching remains the next
test-performance boundary.

## Gate Inventory

### Core syntax and control flow — 9.0 / 10

- Complete (8): functions, local functions, expressions, bindings, arithmetic
  and Boolean logic, `if`/`when`, ranges/loops, block-function calls.
- Partial (2): general multi-parameter functions; structured early exit with
  `return`/`break`/`continue` across ownership scopes.

### Types, traits, and generics — 10.5 / 12

- Complete (10): nominal structs, payload enums, exhaustive matching, impl
  methods, nominal traits/static dispatch, checked type/value specialization,
  associated types with equality constraints, two-parameter generic inference,
  standard `Option<T>`/`Result<T, E>` tagged values, fixed-width signed,
  unsigned, and IEEE-754 scalar layouts with stable `Int32`/`Float32` defaults
  plus `Long`/`Double` 64-bit aliases and target-ABI `Size`/`UIntSize`; arrays
  and dictionaries preserve scalar/user-value layouts and recursively drop
  owned elements.
- Partial (1): dictionary function contracts preserve concrete K/V types and
  dynamic-array function contracts preserve element types. Owned-element move
  extraction, fixed-array generic contracts, general
  two-operand equality methods, and owned nominal dictionary keys remain.
- Missing (1): explicit `dyn Trait`.

### Ownership and storage — 8.0 / 10

- Complete (7): unique owned values, readonly borrow by default, `mut` borrow,
  explicit `move`, recursive static drop glue, lifetime-based stack placement,
  explicit `box T`.
- Partial (2): borrow lifetimes are intentionally narrow; ownership through
  fully generic containers is not implemented.
- Missing (1): a complete path-sensitive borrow checker for references returned
  from functions and stored in user values.

### Modules, visibility, and builds — 5.0 / 8

- Complete (4): file namespaces/import aliases; multiple user source files in
  one compilation unit; root imports recursively discover module files with
  missing, cycle, namespace-mismatch, and duplicate-module diagnostics;
  functions, structs, enums, and traits are internal by default with explicit
  `public` exports and module-qualified nominal identity.
- Partial (2): stdlib loading uses a fixed bootstrap list; one root file is
  enforced by executable top-level statements rather than a module manifest.
- Missing (2): package manifest/dependency graph; module/interface cache.

### Compiler-construction primitives — 11.0 / 12

- Complete (10): Text values, validated UTF-8 iteration as fixed-width Unicode
  `CodePoint` scalar values, deterministic native file I/O wrappers needed by
  the existing demos, type-preserving array/dictionary iteration, and owned
  growable `UInt8` byte buffers with typed push/index/iteration/drop, plus typed
  copyable/owned `Result<T, E>` propagation with deterministic cleanup, and an
  owned aligned byte arena with stable offsets, growth, reset, checked access,
  move/mutable-borrow ABI, and one-shot backing-store release. Native memory
  mapping adds affine bounded `UInt8` views, 64-bit file offsets/sizes,
  target-sized view lengths/indices, writeback, and deterministic unmapping.
  Native host context includes lossless process arguments and `Option<Text>`
  environment lookup with process-lifetime UTF-8 views. Shell-free structured
  child execution accepts an owned Text argv array and returns a typed exit or
  launch/wait/signal error on Windows and Linux. Reusable source spans now flow
  through SL lexer tokens, flat green CST nodes, invalid-byte diagnostics, and
  furthest-unexpected-token diagnostics.
- Partial (2): generic arrays/dictionaries cover compiler-useful `Int`, `Text`,
  and user-value payloads plus function contracts; Text now has checked UTF-8
  byte length/index/slice primitives, but broader string processing remains.

### Standard library and tooling — 3.5 / 8

- Complete (2): basic `sys.io` and three LLVM-backed target link paths.
- Partial (3): file/random/time APIs are narrow compiler intrinsics; VS Code
  support is grammar-only; tests are example-driven without an SL unit-test
  framework. File I/O now monomorphizes canonical scalar `write<T>` and
  zero-input `read<T>` calls with explicit EOF/error results. Affine `File`
  owners and position-based `readAt<T>`/`readAtAsync<T>` remove shared-cursor
  races. Affine `FileWriter` and scalar `writeAt<T>` now provide the symmetric
  output path; `writeAtAsync<T>` owns copied bytes and a duplicate handle while
  it is pending, and `syncAsync` provides an explicit durability barrier. Async
  open and explicit user-value serialization remain.
- Missing (3): portable path/filesystem library, package/build command, formatter
  and language server based on the real parser.

## Critical Path To Self-Hosting

1. Finish the module graph: imports discover files, visibility is enforced, and
   a project manifest names the root module and dependencies.
2. Finish the reusable type substrate: multi-parameter generics, associated
   types, generic `Array<T>`/`Dictionary<K, V>`, `Option`, and `Result`.
3. Add compiler data primitives: bytes, source spans, Unicode iteration, arena
   allocation, filesystem traversal, arguments, and process execution.
4. Write the SL lexer and parser using generated bootstrap tables only where
   necessary; compare tokens and AST snapshots against the C# compiler.
5. Port semantic/type/ownership analysis and serialize a stable typed IR.
6. Implement an SL LLVM IR text builder, then compile representative programs
   with both compilers and compare normalized IR plus runtime output.
7. Stage 1: C# compiler builds the SL compiler. Stage 2: that SL compiler builds
   itself. Stage 3: the stage-2 compiler rebuilds itself byte-for-byte or with
   normalized-IR equivalence, depending on target linker determinism.

The grammar-bootstrap path now includes `smalllang grammar build`, an SL lexer,
and an SL parser VM. The build command
compiles the canonical lexer/EBNF specifications into a deterministic ordinary
SL module containing lexer descriptors and a 1,580-word parser VM program. The
full test runner checks byte-for-byte regeneration. The SL VM consumes those
tables, emits compact backtracking-aware CST events, and materializes flat green
nodes with parent/token/span metadata. Valid-source whitespace and comment
trivia are retained without affecting grammar matching. Unknown bytes are
preserved as invalid CST tokens and force rejection. Panic-mode recovery now
creates explicit green error nodes bounded by newline, right brace, or EOF
while retaining the entire source envelope. Multi-error continuation,
full CST-to-AST lowering, and semantic diagnostics remain as
described in [GRAMMAR_BOOTSTRAP.md](GRAMMAR_BOOTSTRAP.md). These additions
complete the reusable source-span/diagnostic gate. The formal count is now
**47.0 / 60 (78.3%)**; multi-error parser continuation remains.

The lowering path is also executable: generated stable rule ids drive an
ordinary SL module that selects module/declaration/function/main/binding/flow/
call/type/literal/name/path nodes from the green CST, reconnects AST parents
across skipped CST wrappers, and removes trivia from payload token ranges and
spans. Equality/comparison/arithmetic/unary/box wrappers are filtered by actual
operator presence and carry exact operator-token payloads, preserving grammar
precedence in AST parent links. Logical keyword operators use the same stable
negative keyword codes as parser diagnostics. Parameter and expression payload
coverage is still required before this can replace the bootstrap AST builder.
Nominal declarations, fields, variants, trait members, impl targets,
methods, associated types, and generic clauses now carry concrete identifier
token payloads resolved in a second lowering pass. Function parameters and
method `self` tokens carry explicit move/mutable-borrow flags for later ABI and
ownership analysis.

The semantic bootstrap has begun in a separate SL module. Its flat symbol table
collects declarations and members, resolves nearest lexical owner symbols, and
attaches concrete name tokens, primary/secondary type AST indexes, and
move/mutable-borrow flags. Duplicate checking is implemented for declarations
sharing a lexical owner, using byte-exact UTF-8 name comparison and structured
source-span diagnostics. Cross-module name resolution and type canonicalization
remain before semantic parity.

Single-module lexical resolution now walks outward through symbol owners,
including synthetic function-parameter and method-`self` symbols. Unresolved
name expressions produce structured code-2 diagnostics with exact UTF-8 spans.
Imported/module-qualified lookup remains on the critical path.

Self-hosted type canonicalization now deduplicates semantically identical type
token sequences while ignoring whitespace/comment trivia and classifies named,
slice, dynamic/fixed array, dictionary, and box shapes. Recursive element/key/
value links now point to interned nominal canonical ids, and fixed arrays retain
their value-generic length token. The multi-source module graph now assigns
deterministic 64-bit identities to qualified namespaces and links import paths
and aliases to source modules. Import-driven file loading and broader type-
system parity remain. Edge resolution now
distinguishes unique, missing, and duplicate target modules, while declaration
symbols preserve explicit `public` visibility in flag bit 4.

Alias-qualified member AST nodes now resolve through import edges into target
module symbol tables. Public exports succeed, missing members remain distinct,
and internal declarations produce a visibility failure. Minimal deterministic
string escapes (`\n`, `\r`, `\t`, `\\`) make real multiline source fixtures
possible without adding a separate test-only source representation.

Qualified paths nested in type annotations now link source-local canonical type
ids to the resolved target module and nominal symbol. Public imported types and
internal imported types use the same deterministic link record, with visibility
represented explicitly by the resolution status.

Missing and non-public imported nominal types now produce structured codes 3
and 4 with multi-source file ids and exact qualified-type spans. This moves
module type lookup from an inspectable table into the self-hosted compiler's
diagnostic pipeline.
Unresolved local named annotations now flow through the same code-3 path, making
named type resolution total across builtin, local, and imported origins.

The generated source-file grammar now consumes newline boundaries between
top-level declaration groups, including the boundary from a declaration to
`main`. The self-hosted parser therefore retains both declarations and the
entry point instead of rejecting the valid source at EOF.

Builtin, local, and imported named annotations now converge into one nominal
type resolution table. Stable builtin ids, module-local symbol ids, imported
module/symbol identities, visibility failures, and unresolved local names can
therefore feed the same later signature and expression type checks. Repeated
top-level declarations of the same category also retain their newline boundary.

The first self-hosted executable type check now compares inferred literal
return expressions with declared function return annotations. Integer/Text
agreement succeeds, while mismatches produce structured code 5 at the exact
return-expression span. Broader expression inference remains before semantic
parity.

Lexically resolved parameter names now propagate the parameter's nominal type
to the return expression. Identity returns type-check, while returning a Text
parameter from an Int function reports the same code-5 diagnostic model used by
literal inference.

Bottom-up expression inference now reaches arithmetic, equality/comparison,
and logical AST nodes. Int arithmetic preserves Int, compatible comparisons
produce Bool, and return checking selects the outermost inferred expression so
the diagnostic span covers the complete operator expression rather than its
last literal.

Local call expressions now resolve to function symbols and inherit their
declared nominal return type. The inferred call result participates in enclosing
return checks, and single-argument calls compare the complete argument
expression with the function input type using structured code 6 diagnostics.
Unresolved local call targets now produce structured code 7 diagnostics over
the complete call expression.

Local bindings now inherit literal, operator, or call-result types and propagate
them through lexical references. The fixed-point engine can therefore infer
later expressions such as `result + 1` after `double(2) => result`.

Incompatible typed binary operands no longer disappear as uninferred nodes.
They produce deduplicated code-8 diagnostics over the complete expression,
including parenthesized cases such as `Int + Bool`.

Alias-qualified calls now resolve across the self-hosted module graph to target
function symbols and source modules. Public imported functions succeed while
internal functions retain the explicit non-public status needed for a module
diagnostic instead of falling back to an unresolved local call.

One- and two-name generic clauses now lower to distinct lexical symbols, and
generic annotations resolve to function-owned nominal identities rather than
missing local types. The return checker can distinguish `T -> T` from an invalid
`T -> E` value return without specializing either parameter yet.

The first generic call specialization slice now handles `T -> T`: a call-site
argument binds the function-owned generic identity and the call result becomes
that concrete nominal type. Because calls participate in the fixed-point pass,
operator expressions such as `identity(1 + 2)` specialize to Int after their
argument is inferred. Trait constraints and independent `T -> E` inference
remain.

`true` and `false` are now recognized as Bool literals by self-hosted semantic
analysis rather than unresolved names. They seed logical-expression inference
and pass Bool return checking, removing a pervasive false diagnostic from the
compiler's own SL sources.

Unary expression typing now covers `not Bool -> Bool` and `-Int -> Int`.
Invalid `not Int` and `-Bool` expressions produce structured code-8 diagnostics
with exact unary spans.

Composite annotations now have a structural semantic record for array/slice,
dictionary, and box shapes. Their element/key/value identities distinguish
builtins, local declarations, and function generics; unresolved components such
as `[Unknown; ~]` produce code 3 over the complete annotation. Dictionary and
box call-site substitution remains beyond the array slice below.

Array literals now survive AST lowering and homogeneous dynamic arrays carry a
structural expression identity. Generic `[T; ~] -> [T; ~]` calls substitute the
argument element identity into the return type after fixed-point inference.
Concrete array element mismatches are checked at both call arguments (code 6)
and function returns (code 5).

Dictionary literals now carry structural origin 15 and separate builtin key and
value identities. Two-parameter `{K: V} -> {K: V}` calls specialize both slots,
while concrete dictionary key/value mismatches produce code 6 for arguments and
code 5 for returns. Local/imported nominal dictionary components remain to be
encoded beyond the current compact builtin representation.

Box expressions now carry structural origin 16 and their operand identity.
Generic `box T -> box T` calls specialize the boxed element, while concrete
boxed input/return mismatches use codes 6 and 5. The AST also preserves `box` as
kind 23 instead of dropping it as an unrecognized unary wrapper.

Struct literals now infer local or imported nominal identities (AST kind 39),
allowing `[Point; ~]`, `{Point: Point}`, and `[shapes.Point; ~]` literals to
match their composite signatures. Imported call resolution now uses the callee
token boundary, preventing qualified struct literals inside arguments from
being misclassified as the call target.

Struct initializer checking now resolves local and imported field symbols.
Unknown fields produce code 11 at the field name, and nominal value mismatches
produce code 12 at the initializer expression with the caller source file id.
Field coverage is now complete for local and imported literals: omitted required
fields produce code 13 over the complete literal with the absent field symbol.

Local and imported value-member access now propagates field types into later
expressions and return checks. Unknown members produce code 14 at the field
token, and an untyped outer member/operator/call suppresses misleading fallback
return mismatches from an inferred inner operand.
Composite member fields preserve their structural component identities.
Postfix index access now lowers as AST kind 41, and array, slice, and fixed-array
index results inherit the inferred element identity for subsequent checks.
Structured codes 15 and 16 distinguish a non-array-like indexed target from a
non-`Int` index. The SL lexer now tokenizes raw multiline strings with matching
three-or-more-quote delimiters, keeping its source envelope aligned with the
bootstrap lexer.
Dictionary expressions now preserve full key/value identities instead of
packing only their symbol ids. That metadata survives bindings, generic calls,
and composite fields, allowing dictionary indexing to check its key and infer
its value without confusing local, imported, generic, or builtin identities.

Typed semantic output now has an initial stable IR contract. A flat SL-owned
node table lowers each inferred function result as `function -> return ->
expression`, with stable kinds for Int, Text, and Bool constants and explicit
source-module, AST, symbol, type-identity, payload-token, and operand indexes.
Single- and multi-module snapshots fix this layout before LLVM text lowering is
added. Ownership and storage placement are not yet lowered, so this starts
rather than completes critical-path step 5.
The next typed-IR slice now flattens every inferred expression in AST order and
connects nested unary/binary operands by IR index. Calls carry the resolved
target source-module/function-symbol pair and their argument operand, while
operators retain stable opcode ids. Ownership and storage placement remain
before step 5 can be considered complete.

Critical-path step 6 has an executable first slice: SL lowers zero-input
Int/Bool functions, constants, nested arithmetic, comparisons, Boolean
negation, and returns into LLVM text with deterministic module/symbol names and
IR-index SSA registers. The example runner sends that stdout to pinned
`llvm-as`, proving it is accepted LLVM IR. Function calls, parameters, Text and
aggregate ABI, runtime declarations, target triples, ownership, and file output
remain before this is a usable compiler backend.

Function parameters and direct calls now cross the same boundary. Parameter
symbols lower to typed IR, name uses resolve to `%arg`, and imported calls use
their stable target module/symbol name with typed arguments and SSA results. A
two-file `sample.math -> app.main` snapshot is accepted by `llvm-as`. Text and
aggregate ABI, ownership/drop information, runtime declarations, non-empty
entry lowering, and file output remain.

Empty main blocks now lower to a typed-IR entry node and an actual `i32 @main`.
The two-module LLVM snapshot is assembled, linked into a Windows executable,
and run successfully by the automated suite. This proves the first complete
SL-source -> SL semantic/typed IR -> SL LLVM text -> native linker -> process
execution path. Main statements, observable program behavior, Text/aggregate
ABI, ownership/drop, runtime declarations, and direct file output remain.

Non-empty main blocks now begin lowering their inferred expression graph.
Self-hosted call resolution recognizes a zero-input function name as a property
call, so `main { ping }` emits and executes `call @ping()` before returning the
process exit code. This removes the empty-entry-only limitation, while local
bindings, control flow, runtime effects, and complete statement sequencing
remain on the critical path.

Self-hosted parsing and typed IR now preserve flow-oriented `if` explicitly.
The ordered grammar tries `IfFlowTarget` before a generic path and expression
statements before the permissive block-call shape, fixing the shared grammar
defect that previously captured `if` too early. AST nodes retain the control
target and both block-body regions. Typed IR links the Bool condition, ordered
`then`/`else` regions, and each region's first child in both functions and
`main`. The LLVM backend now lowers scalar statement conditionals to diamond
CFGs and inserts a merge/`phi` only for matching Int/Bool value branches.
Region descendants execute only beneath their branch labels. A native Windows
regression proves parameter-driven function branches, source-ordered effects,
a constant main branch, and an Int-producing conditional returning through a
`phi`. Nested statement and Int-producing conditionals now compose through an
explicit work stack, and outer `phi` nodes name an inner merge block when that
block is the actual predecessor. This avoids relying on recursive inline local
functions, which the bootstrap runtime intentionally does not support. The same
work fixes two semantic leaks found during self-hosting: a
control flow can no longer inherit a nested branch call target, and Bool-typed
names are no longer mistaken for Bool literals. Branch-local owned aggregates,
loop regions, and non-scalar joins remain.

The self-hosted grammar no longer treats `while` as a generic block call. A
dedicated AST target and typed-IR kind 20 retain the Bool condition and body
region. LLVM lowering emits `header`/`body`/`exit` blocks and a real back-edge
in functions and `main`; the explicit region task stack also handles while
nested inside if. Mutable scalar declarations and rebinds now retain their `!`
flag, name resolution selects the closest preceding definition, and LLVM
hoists one stack slot per mutable scalar. Integer comparison conditions reload
the complete Bool tree in every header, while body rebinds store the next value
before the back-edge. Logical `and`/`or` use explicit short-circuit blocks and
call-valued leaves run only on reachable paths. Declared result annotations now
determine emitted LLVM function signatures and the last top-level body
expression supplies the return operand, fixing effectful Bool helpers used by
loop conditions. This memory-form lowering is valid for nested CFGs and is designed
for LLVM `mem2reg`/SROA promotion to SSA `phi` nodes. Native regressions execute
terminating mutable loops in both a function and `main`, including observable
short-circuit calls. Dedicated loop-exit IR now links `break`/`continue` to the
closest structured while, suppresses unreachable siblings, and emits valid
branches through nested if/while regions. The C# reference backend drops
loop-local owned values before either transfer. The self-hosted backend now
materializes dynamic arrays and dictionaries inside control-flow regions and
routes `break`/`continue` through explicit cleanup blocks; normal back-edges
perform the same reverse-order frees. Consuming calls now produce typed-IR move
events attached to their nearest structured region, so cleanup edges after the
call omit the transferred array or dictionary without incorrectly suppressing
cleanup on sibling paths. Struct drop obligations now recurse through nested
struct fields into owned arrays and dictionaries on normal, moved-parameter,
and early-return edges. Static field-level partial moves now preserve sibling
drop obligations and reject overlapping reuse. Inline local-function returns,
field reinitialization, and branch joins remain before the structured
early-exit gate is complete.

Guard-flow loop control is also cumulative: `condition -> if continue` and
`condition -> if break` are compact Bool-guarded transfers. Both the reference
and self-hosted backends branch true through the same ownership cleanup path and
let false fall through. Postfix `?` remains unambiguous `Result` propagation.

Explicit `value -> return` and Unit `return` are now represented as terminating
statements. The reference backend validates the enclosing function and result
type, transfers a returned owner, and drops every other active owner before
`ret`. The self-hosted AST and typed IR carry a dedicated return terminator;
the LLVM slice executes an early scalar return from an `if` region and frees a
function-local dynamic array on both the early and fallthrough paths. Static
partial-move masks are now emitted; inline local-function returns and moved-path
reinitialization/joins keep the structured early-exit gate partial.

Typed IR now represents immutable local bindings explicitly and connects each
name use by stable symbol id. LLVM materializes scalar literal bindings as SSA
values in both functions and `main`, so bound values can be returned or passed
to calls instead of every name being mistaken for `%arg`. General topological
scheduling now combines operand readiness with source-ordered statement roots,
so values are emitted before their uses without moving independent effects
across nested control-flow boundaries in functions or `main`.
Aggregate/container dependencies, mutation, branch joins, cycle
diagnostics, and ownership-sensitive binding drops remain.

Aggregate-valued bindings now participate in the same dependency schedule.
Function-block parsing preserves the final expression after prior statements,
including `array![index]` and struct member access. Array, struct, and dictionary
bindings can therefore feed typed reads; owned array/dictionary storage is
released after its final scalar read. Returning a bound owned aggregate now
transfers its backing stores without producer-side frees, while a scalar-
returning consumer releases a `move` parameter exactly once. Branch-sensitive
liveness now covers region-local Int arrays and dictionaries on normal and
loop-exit edges. Whole-binding consuming calls now suppress only later cleanup
within the same structured region. Nested static field moves now retain sibling
drop obligations. Field reinitialization and nested owned aggregate member
mutation remain.

Self-hosted LLVM text selects descriptors implemented in the file module
`smalllang.compiler.llvm.target`. Windows x64/COFF, Linux x64/ELF, and
Wasm32/WebAssembly values each own their pinned-Clang triple, data layout,
pointer width, and object-format identity. Public `emit`, `emitLinux`, and
`emitWasm` entry points print the selected header and transfer their owned
source array into one private shared emitter. Complete modules for all three
targets assemble with `llvm-as`; Linux/Wasm regression examples prove that the
body is not a header-only fixture. Namespaced same-module calls now resolve by
falling back to the current canonical module name in semantic analysis and LLVM
code generation, fixing the private shared-emitter catalog boundary without
aliases or backend duplication. The bootstrap compiler now
gives owned struct fields stable parametric identities, LLVM aggregate ABI,
nested member access, and recursive backing-store drop. Typed-IR move events now
retain complete static member paths. LLVM skips only the exact moved leaf while
recursively dropping sibling obligations, and a separate ownership diagnostic
rejects later whole-owner or overlapping-path use while allowing diverging
siblings. Field reinitialization and branch-sensitive moved-path joins remain.
Target-specific runtime declarations and ABI lowering beyond the
currently supported shared IR subset also remain. Text `print`/`println` are
the first completed runtime effect slice: flow calls survive semantic lowering
as explicit runtime symbols, Windows emits a `putchar` loop, Linux emits
`write(2)`, and Wasm declares an `env.smalllang_write` import. Runtime helpers
are emitted only when referenced. Text parameters now cross effectful `Unit`
functions, where LLVM `void` calls and returns avoid phantom SSA results.
Main-local Text literals and immutable bindings now also form valid SSA before
dynamic runtime output, rather than referring to an undefined `%v` value. The
integer-interpolation lowering recognizes main-local `Int` bindings in a Text
literal, walks any number of `$name` segments in source order, and formats each
value through one target-neutral `i32` helper. It supports repeated names and
empty leading/trailing literal segments. The helper sign-extends before
negation, so `Int32`'s minimum value is handled without overflow. The same
lowering now resolves function `Int` parameters to `%arg` and function-local
bindings to their producer SSA values. LLVM unary negation was corrected to
emit `0 - value`, which the function execution regression verifies with
negative arguments and locals. `$(expression)`, broader numeric types, input,
allocation policy, files, process services, and target-native entrypoint/export
policy remain.

Text now crosses the self-hosted LLVM boundary as `{ ptr, i64 }`. UTF-8
literals become immutable globals with byte-accurate lengths and LLVM `\XX`
escaping, and Text parameters, returns, and imported calls share that ABI.
ASCII and Korean examples both pass assembly, link, and execution validation.
General interpolated/dynamic Text construction and lifetime ownership remain;
the direct `Int` output slice above does not allocate a temporary Text value.
For `$(expression)`, the generated parser VM, CST, and AST now accept an
expression grammar start rule. A standalone fragment preserves precedence,
unary operators, parent links, token payloads, and byte spans exactly as the
full-module path does. The fragment-to-scope attachment now exists in
`smalllang.compiler.ir.interpolation`: balanced
expression ranges lower to a flat operator tree, and parameter/local names use
the enclosing function's symbol identity. Nested arithmetic such as
`$((value + 1) * 2)` preserves the multiplication root and additive child.
Pass-through precedence wrappers are removed and the first top-level operator
is retained, preventing a later nested unary token from replacing the root.
Each literal/operator node now carries its stable builtin result type; lexical
name nodes retain their symbol identity and obtain the declared/inferred type
from typed IR. The LLVM emitter consumes the tree in both functions and `main`,
resolves parameter and local producer SSA values, and streams `Int` and `Bool`
results without a temporary Text allocation. Bool literals, names,
comparisons, equality, `and`/`or`, and `not` lower to `i1` SSA and the canonical
`true`/`false` spellings. Windows output
links and executes; Linux x64 and Wasm32 output assembles. Remaining work is
fixed-width numeric and user-defined static `Display` lowering, additional
expression result types, and owned dynamic Text construction where streaming
is insufficient.

Nominal struct ABI now uses deterministic module/symbol LLVM type names.
Struct-literal fields form a general typed-IR sibling chain and lower through
ordered `insertvalue` operations; parameters, returns, and imported calls pass
the aggregate by value without losing its declaring-module identity. Local and
cross-file struct executables assemble, link, and run. Dynamic-array and
dictionary fields now participate in emitted struct layouts and recursive drop
glue. Field-level moves and mutable aggregate updates remain.

Struct member reads now lower through typed IR to LLVM `extractvalue`. Field
ordinals come from the declaring module's symbol order rather than caller-local
layout guesses, so imported member access retains the same ABI. Local/imported
member executables pass assembly, link, and execution validation. Nested owned
fields are recursively released; partial field moves and mutable field updates
remain.

Owned dynamic Int arrays now cross LLVM as `{ data, length, capacity }`.
Literals allocate and initialize storage, move parameters/returns transfer the
aggregate, and readonly indexing emits extract/GEP/load. The executable backend
test passes assembly, link, and execution. Deterministic free/drop insertion,
borrow lifetime enforcement in typed IR, and generic element layouts remain.

Function-exit ownership lowering now inserts deterministic `free` calls for
unreturned dynamic-array temporaries and unused move parameters. Directly
returned literals and move parameters are recognized as transfers and are not
freed, preventing the first class of double-free errors. Snapshots assert both
the positive drop cases and the absence of drops on transfer. Conditional
paths, early exits, and nested owned struct fields now share the same recursive
drop obligations. Static partial moves release one path while preserving
siblings; reinitialization, branch joins, and general liveness remain.

Owned dictionaries now have a common self-hosted `%sl.dict` LLVM ABI with
separate key/value stores plus length/capacity, while typed IR drives concrete
storage size, alignment, load/store, and equality lowering. `Int -> Int` and
`Bool -> Text` snapshots cover literal construction, move parameter/return
transfer, deterministic lookup, explicit missing-key trap, and two-store drop.
This intentionally starts with a simple deterministic lookup representation;
broader scalar/nominal layouts, mutation, and the production Swiss-table
representation remain before dictionary backend parity.

Imported call signatures now participate in expression inference and checking:
the target module's return type becomes the caller's call-expression type, its
input type validates the caller argument, and non-public imported calls produce
structured code 9 over the complete qualified call.

Resolved calls now enforce the current zero-or-one-input arity surface. A
missing required argument and any parenthesized zero-input invocation produce
code 10; zero-input functions remain property calls such as `now`, not `now()`.

## Immediate Implementation Order

1. Multi-file compilation (implemented by example 52).
2. Import-driven file discovery with cycle and duplicate-module diagnostics
   (implemented after example 52).
3. Internal-by-default visibility with explicit `public` exports for functions,
   structs, enums, and traits (implemented).
4. Associated types and equality constraints (implemented by example 54).
5. Multi-parameter generics (implemented by example 55).
6. Generic collection element types and ownership/drop specialization
   (implemented for fixed/growable arrays and Swiss-table dictionaries by
   examples 56-71; fixed-array generic contracts remain).
7. `Option`/`Result` and compiler-grade byte/text/source-span libraries
   (`Option`, `Result`, bytes, Unicode code points, and byte arenas implemented;
   reusable source spans remain).
