# SmallLang Self-Hosting Roadmap

Status: active
Updated: 2026-07-17

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
- Mojo's current ownership model defaults function inputs to immutable `read`
  borrows and separates `mut`, owned `var`, and lifetime-tracked `ref`
  conventions. This supports SL's existing readonly-by-default, explicit
  mutable-borrow, and explicit ownership-transfer direction without importing
  Mojo's surface syntax. See Mojo [ownership](https://docs.modular.com/mojo/manual/values/ownership/).
- Zig: an explicit root-module dependency graph and declaration discovery from
  reachable imports. See the official
  [compilation model](https://ziglang.org/documentation/master/#Compilation-Model).
- Swift: source files belong to modules, packages group modules, declarations
  are internal by default, and public API is opt-in. See
  [access control](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/accesscontrol/)
  and [packages](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/introducingpackages/).
- Swift structured task groups and sendability plus Mojo's indexed CPU
  `parallelize` shape the deterministic compiler worker design. SL uses bounded
  native workers, disjoint indexed result slots, structured join, and canonical
  ordered merge. See [Deterministic Parallel Compilation](PARALLEL_COMPILATION.md).
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
- Rust incremental compilation identifies dependency nodes with stable
  fingerprints that do not contain session-local ids, while Clang module caches
  rebuild a module when one of its source inputs or imported modules changes.
  SL's future module/interface cache will therefore key artifacts by compiler
  ABI, target configuration, source/interface content, and dependency
  fingerprints rather than timestamps alone. See rustc
  [dependency-node fingerprints](https://doc.rust-lang.org/stable/nightly-rustc/rustc_query_system/dep_graph/dep_node/index.html)
  and Clang [module caches](https://clang.llvm.org/docs/Modules.html#compilation-model).

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
| Modules, visibility, and builds | 8 | 5 | 2 | 1 | 6.0 |
| Compiler-construction primitives | 12 | 10 | 2 | 0 | 11.0 |
| Standard library and tooling | 8 | 2 | 4 | 2 | 4.0 |
| **Total** | **60** | **42** | **13** | **5** | **48.5 / 60** |

Current count-based progress: **80.8% (48.5 of 60 equivalent gates)**.

The frontend parallel-compilation subproject is **25/28 checks (89.3%)**. Its
source-local product boundary, typed callback-result role slice, nested-call
identity regression, Windows native compute pool, and source-local parallel
frontend execution are complete. Owned source-analysis results and ordered
LLVM-body sinks now cross worker boundaries safely, and parallel callbacks
reject mutable or structurally non-sendable captures. The submitting parent now
helps drain its task group before the structured join. Linux parity and exact
cancellation/partial-result destruction remain pending. This subproject does
not promote a roadmap gate yet.
There are **11.5 equivalent gates remaining**. Because the remaining compiler
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
operation because pending Tasks never borrow the source handle. `openReadAsync`
and `openWriteAsync` copy path bytes into their Task context and transfer the
new native handle only after successful await; cancellation closes a completed
but unclaimed handle. The self-host grammar now parses nested generic type
annotations such as `Result<File, Text>`. Native completion backends, broader
failure propagation, captures, and task groups remain partial.
Straight-line states now carry heap owners and
mutable locals safely: frame storage temporarily owns the value, resume restores
one owner, and async container stack promotion is disabled. Self-host frame-slot
flags preserve mutable, composite-owner, and affine-Task bits for destroy
lowering. State-specific branch frames avoid reading sibling-path slots that
were never initialized; loop back-edges use explicit initialization-dominating
phis for persistent loop-carried state.

The coordinated regression runner has stable `reference`, `semantic`,
`selfhost`, `llvm`, `fast`, and `full` layers plus exact-name and affected-source
selection. Self-host LLVM fixtures no longer rebuild the same compiler modules
for every case: one native SL compiler driver accepts target mode and source
file paths, memory-maps every module, then emits Windows, Linux, or Wasm LLVM.
Its timestamp fingerprint covers the compiler, source manifest, all listed SL
modules, and the standard library. A focused warm invocation completed in 1.1
seconds, including 0.12 seconds in the self-host compiler; the one-time cold
bootstrap completed in 59.7 seconds. Two specialized introspection examples
retain the ordinary reference-compiler path.

Current native bootstrap chain:

- [x] Build one reusable native stage-1 `slc` with the C# bootstrap compiler.
- [x] Read and own multiple source files through affine `SourceText` mappings.
- [x] Compile those modules with the SL frontend and emit valid target LLVM IR.
- [x] Reuse the current stage-1 executable across self-host LLVM fixtures.
- [x] Invoke `clang`/`lld` from `slc` and produce ordinary final executables directly.
- [x] Closure-convert local compiler functions so native optimization partitions use all cores.
- [x] Emit and assemble the complete stage-2 module with `llvm-as`.
- [x] Link stage 2 with the platform entry shim, runtime, and imported stdlib definitions.
- [x] Run stage 2 and compile single-file and imported multi-file SL smoke programs with it.
- [x] Rebuild `slc` with stage 1 and compare reproducible stage-2 LLVM artifacts.

The native bootstrap chain is now **10/10 complete (100%)**. The complete
28-source compiler emits a 6,730,900-byte Windows LLVM module, which assembles
and links as a stage-2 compiler. Examples 365 and 366 cover a minimal program
and a two-file imported module. After newline normalization, stage 1 and stage 2
produce identical LLVM SHA-256 values for both programs; both stage-2 outputs
also assemble, link, and execute. `scripts/verify-selfhost-stage2.ps1` preserves
this as a six-step cached differential gate. This completes the bootstrap
milestone without changing the broader 60-gate language-capability score.

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
  insertion now transfers a named owner into a dynamic array and invalidates
  the source binding; owned-element index extraction, fixed-array generic
  contracts, general
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

### Modules, visibility, and builds — 6.0 / 8

- Complete (5): file namespaces/import aliases; multiple user source files in
  one compilation unit; root imports recursively discover module files with
  missing, cycle, namespace-mismatch, and duplicate-module diagnostics;
  functions, structs, enums, and traits are internal by default with explicit
  `public` exports and module-qualified nominal identity; `smalllang.project`
  names a confined root source and output identity, and source-free
  `smalllang build` discovers it from ancestor directories.
- Partial (2): stdlib loading uses a fixed bootstrap list; the package graph has
  deterministic multiple-product selection, exact local path dependencies,
  direct-dependency visibility, transitive resolution, and cycle/name-collision
  diagnostics, but not versions, registries, Git sources, a lock file, or
  workspaces.
- Missing (1): module/interface cache.

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

### Standard library and tooling — 4.0 / 8

- Complete (2): basic `sys.io` and three LLVM-backed target link paths.
- Partial (4): file/random/time APIs are narrow compiler intrinsics; VS Code
  support is grammar-only; tests are example-driven without an SL unit-test
  framework. File I/O now monomorphizes canonical scalar `write<T>` and
  zero-input `read<T>` calls with explicit EOF/error results. Affine `File`
  owners and position-based `readAt<T>`/`readAtAsync<T>` remove shared-cursor
  races. Affine `FileWriter` and scalar `writeAt<T>` now provide the symmetric
  output path; `writeAtAsync<T>` owns copied bytes and a duplicate handle while
  it is pending, `syncAsync` provides an explicit durability barrier, and async
  open owns its path and transfers its newly opened handle on await. Explicit
  user-value serialization remains. The package/build surface has confined
  roots, automatic discovery, selected products, deterministic local dependency
  resolution, recursive imports, and target output, but not versioned/remote
  resolution, a lock file, workspaces, tests, or a general build DAG.
- Missing (2): portable path/filesystem library; formatter and language server
  based on the real parser.

## Critical Path To Self-Hosting

1. Finish distributable packages: local dependency products and direct
   visibility work; version constraints, remote sources, a lock file, and
   workspace-wide resolution remain.
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
**48.5 / 60 (80.8%)**; multi-error parser continuation remains.

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

Result-producing block functions now reuse the ordinary function tail
expression and bind their result after the caller block with `=> name`.
`build`, `with`, and `handle` therefore remain ordinary resolvable function
names rather than parser keywords. Unit block functions are source-compatible,
owned results transfer exactly once, and the generated grammar gives explicit
control-flow expressions priority before user block calls so `if`/`while`
retain their self-host AST kind. The accepted design and evidence checklist are
tracked in [`ROLE_BLOCKS.md`](ROLE_BLOCKS.md). The common foundation is
implemented, while builder mutation, scoped capability escape checking,
effect-set enforcement, and self-host LLVM lowering remain partial. The
self-host AST now assigns kind 48 to result-producing block calls, records the
role target and optional result binding, resolves the role as an ordinary
function, propagates its return type through the bound name, and lowers the
call, binding, and body operations into flat typed IR. Block-input contract
checking now gives the caller item a lexical parameter type, restricts source
selection to the expression before the role target, validates nominal and
composite source types, and rejects ordinary functions used as roles. Generic
block items are now specialized outside-in for nominal parameters, generic
composite components, shape-identical composites, and `T -> [T; ~]`
recomposition, including imported roles. Block item declarations accept full
recursive type annotations. A new self-host semantic type-term arena interns
nested trees and performs full-depth structural substitution, with
`Result<[T; ~], {Text: box T}>` as executable evidence. Migrating expression,
checking, IR, and LLVM consumers from the older shallow fields remains. The
first migration boundary now globally interns annotation types by declaration
identity across modules, seeds stable builtin IDs, and gives annotation-backed
name and call expressions their complete recursive type ID. Return and call
argument checking now uses exact recursive IDs for fully concrete annotated
values across module boundaries, while retaining the older diagnostic path for
not-yet-specialized generic expressions. Generic calls now structurally unify
complete input trees, consistently bind repeated and multiple parameters,
rebuild canonical result trees, and carry successful concrete expression IDs
into typed IR. Dynamic-array, dictionary, box, and local/imported struct
literals participate in this boundary. Canonical ownership traits now fold
through fixed arrays and nominal applications, propagate across calls,
bindings, and member fields, govern partial-member move recognition, and mark
heap-reaching coroutine slots that cross `await`. Nominal declaration fields
now form canonical owner-to-field type edges, and a fixed-point propagates
ownership through nested structs and generic applications. Typed IR carries
the canonical kind, and LLVM type/edge-cleanup selection prefers canonical
facts when an ID exists. Canonical types now retain concrete fixed-array
lengths; LLVM computes target-aware size/alignment and padded nominal layouts
from the type/field graph, and aggregate declarations use those canonical
edges. Dynamic-array/dictionary component lowering, recursive drop glue,
generic application lowering, self-host capability/effect
enforcement, and role-specific
ownership/effect checks still keep semantic parity partial;
the canonical gate count therefore remains 42 complete, 13 partial, and 5
missing (48.5/60, 80.8%).

One borrowed `CompilationContext` now carries canonical type/reference/field,
nominal, composite, module, qualified-name, resolved-call, and flattened
per-source AST/token/symbol/resolved-name/type-term/type-use products. Source
ranges preserve local indexes while allowing type checking, shallow inference,
recursive expression IDs, typed IR, and LLVM orchestration to read one aggregate
without rebuilding those products. Module/import/qualified/call resolution and
canonical type-ID, nominal, composite, and imported-type resolution consume the
same package. Source-only APIs remain thin compatibility wrappers, while the
type-term and type-use lowerers expose prepared syntax entry points. Ownership/
type diagnostics and partial-move ownership diagnostics now consume the same
context. Coroutine suspension, frame-slot, and destruction tables are produced
together from one typed IR in `CoroutinePlan`; recursive/partial-move LLVM drop
glue reads the flat AST/token/symbol tables instead of rebuilding them. The
reference compiler now parses closed `uses` sets, enforces transitive callee
capabilities, treats `main` as the root boundary, and covers Console, File,
Clock, Random, Process, Environment, mapped files, generics, and role blocks.
The self-host semantic layer now derives flat source-qualified `FunctionEffect`
and `EffectDiagnostic` products from one borrowed `CompilationContext`. It
checks local, imported, and builtin-alias calls without rebuilding syntax.
The grammar and AST now represent map expressions directly; map construction
and mapped-view `flush` derive File requirements from prepared facts. Fixed
external capabilities are deliberately not discharged by ordinary role blocks;
the self-host frontend now represents module-level user effect declarations,
typed operation signatures, qualified `uses` requirements, operation calls,
visibility, and structured diagnostics in one context-derived analysis product.
Reference-parser parity, canonical operation type checking, matching handler
discharge, resumptions, and LLVM lowering remain. The LLVM
text emitter's 67 direct
`lexer.lex`, `ast.lower`, and `symbols.collect` sites now read these flat package
products through source ranges, covering scheduling, control flow, cleanup,
operands, member layout, and interpolation name lookup. Interpolation lowering
now also accepts prepared source AST/token/symbol tables. LLVM builds one flat
interpolation table with a source range per module and reuses it for functions,
`main`, and runtime-helper selection instead of rebuilding the enclosing source
for each call. Only the embedded `$(expression)` fragment itself is parsed.
The recent coordinated 410-case runs passed in 388.7, 395.4, and 398.0 seconds;
these are treated as full-run observations, not isolated benchmark proof.
The reference effect-set slice then passed the coordinated 416-case suite in
393.2 seconds with flushed monotonic progress. Example 297 adds executable
self-host effect-product evidence; its focused call/grammar/effect set passed
22/22 and the coordinated suite passed 417/417 in 397.4 seconds with flushed
monotonic progress. The following map/flush parity slice passed 12/12 focused
cases and 417/417 in 392.1 seconds. The formal gate count remains unchanged
because the new declaration/operation slice is frontend-only; handler matching
and LLVM lowering do not exist yet. Example 298 proves the new self-host user
effect facts and diagnostics. Its focused overlapping slice passed 26/26, and
the coordinated eight-worker suite passed 418/418 in 431.5 seconds with
flushed monotonic progress and a zero-warning, zero-error Release build.

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

## Native Stage-1 Build and Stage-2 Checklist

The native stage-1 driver owns a structured build path. It emits its
multi-file result to disk with `sys.process.runToFile`, invokes the pinned Clang
driver without a shell, and checks both child exit codes. A C#-bootstrapped
driver produced and ran a two-module Windows executable whose output was
`module answer = 42`. The test runner caches this native driver and bootstraps
it at `-O0`; a cold 166-case self-host verification, including that rebuild,
passed 166/166 in 40.5 seconds. Dynamic LLVM allocas are hoisted to function
entry so unoptimized loop execution has bounded stack use.

- [x] map and own multiple SL source modules;
- [x] emit target-specific LLVM from the cached native stage-1 compiler;
- [x] redirect LLVM to a file through a typed, shell-free process API;
- [x] invoke Clang and produce a runnable native executable;
- [x] prove the path with a multi-module SL program;
- [x] emit the complete 28-source compiler without an LLVM-lowering trap;
- [x] build `slc-stage2` from that complete module;
- [x] compare stage-1 and stage-2 output reproducibly.

The stage-2 checklist is now **8/8 complete (100%)**. The cached verifier checks
the complete compiler link, single-file execution, imported multi-file
execution, normalized stage-1/stage-2 LLVM identity, and C#/SL runtime parity.
The formal language-capability score remains 42 complete, 13 partial, 5 missing
(48.5/60, 80.8%) until the remaining ownership, generic-container, package,
tooling, and library gates are implemented.

## Self-host Compile-Time Baseline (2026-07-18)

The compiler-sized stage-2 path now caches function captures and function-end
boundaries, indexes call targets by canonical module symbol, and buffers
redirected Windows stdout in 1 MiB blocks. The self-host emitter generates the
same buffered runtime, so stage 3 no longer writes LLVM one byte at a time.

- [x] assemble, link, and execute the complete stage-2 compiler;
- [x] compare C# stage 1 and SL stage 2 on single and imported multi-file LLVM;
- [x] generate stage 3 and prove byte-identical stage-2/stage-3 LLVM;
- [x] keep compiler emission working set below 60 MiB in the measured run;
- [x] reduce the measured stage-2 verification path from about 376 s to 261 s;
- [x] lower capture-safe `parallel` callbacks to the native compute pool;
- [x] make the generated stage-2 compiler use more than one effective core;
- [x] scope emitter scheduling state and dependency searches to one function;
- [x] reduce compiler-sized stage-3 LLVM emission below 45 seconds;
- [x] lower no-capture scalar-to-scalar callbacks through the compute pool;
- [x] connect entry-body role results and lower top-level entry `parallel`;
- [x] make no-capture `SourceText` analysis worker-safe before parallelizing it;
- [x] lower `parallel` inside nested `if`/`while` regions;
- [x] buffer independent `emitCore` function bodies in owned memory sinks and
  merge them in canonical root order.

The generated stage-2 compiler lowers capture-safe `parallel` callbacks to a
Windows compute pool. No-capture `SourceText -> SourceAnalysis` callbacks now
use the same pool when their owned result graph contains only worker-transferable
scalars, structs, and arrays. Example 377 exercises that transfer for 100
generations. The complete 28-source stage 3 ran five times without a worker
fault and produced the same fixed-point LLVM on every run.

The next measurement exposed a larger algorithmic bottleneck inside
`emitCore`: every function and nested control region allocated scheduling
state for the complete program IR and repeatedly searched that complete IR.
Function-local state, bounded searches, and canonical function-symbol lookup
reduced complete generation to 40.27 s for stage 2 and 42.72 s for stage 3.
Both 6,942,593-byte outputs have SHA-256
`3A82A8584A13BBA12A64DBA719A20CE52F2A3787745229C4DFFD8E3B323E5EF3`.
The affected LLVM suite passes 72/72 and the six-step stage-2 differential
verifier passes. Ordered parallel LLVM body emission remains useful, but it is
now a secondary improvement after removing the accidental quadratic work.

No-capture callbacks are no longer rejected as one broad class. Arrays whose
input and result elements are self-contained numeric or `Bool` values use the
compute pool without a capture environment; a 100-generation execution test
proves the null-environment ABI. Entry-body role bindings now receive the same
explicit call-result edge repair as ordinary functions, and top-level entry
`parallel` has both compute-pool and serial target lowering.
The ordered memory-output sink gives every parallel callback index an owned
`{data, length, capacity}` buffer. Worker output never touches the shared
stdout buffer; completion merges and frees sinks in source/root order. Function
bodies before and after `main` are parallelized as separate canonical batches,
so entry placement stays byte-identical to the former serial traversal.

The compiler fixed point is now exact at 7,195,817 bytes with SHA-256
`B57FB15B373CB0348EB16EAA7B1727D56D3B382F5FA5E01C1FF0280F3BCA7410`;
the complete stage-3 output assembles with `llvm-as`. The preceding
source-worker revision completed five stage-3 runs in 33.90-37.75 seconds. A
separately instrumented run took 34.81 seconds and 377.77 CPU-seconds, averaging
10.85 effective cores with an 88.7 MiB peak working set. This remains 90.4%
faster than the original 360.7-second serial path while parallelizing the
source-analysis boundary.

The capture-safety fixed-point run completed stage 3 in 38.50 seconds with
400.38 CPU-seconds (10.40 effective cores) and a 77.5 MiB peak working set.
The parent-assisted join revision reaches an exact 7,198,336-byte fixed point
with SHA-256
`CBCED4918D9AF37C71AF792D99016A27C2F4CC9D4407CD123CD866BF32DB555F`.
Its stage-3 run took 34.56 seconds wall and 376.91 CPU-seconds (10.91 effective
cores), and the full Windows regression passes 506/506. The canonical roadmap
remains 48.5/60 (80.8%) because this closes a feature-local parallel checklist
item, not an additional top-level gate.

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
   (implemented for `Option`, `Result`, bytes, Unicode code points, byte arenas,
   and reusable lexer/CST/parser source spans; multi-error continuation and
   broader string processing remain).
