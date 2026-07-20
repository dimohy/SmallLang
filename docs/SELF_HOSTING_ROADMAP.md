# Sollang Self-Hosting Roadmap

Status: active
Updated: 2026-07-20

The end state is an Sollang compiler written in Sollang that reads a multi-file Sollang
program, performs lexical, syntactic, type, ownership, and module analysis,
emits LLVM IR, and invokes the platform toolchain. The existing C# compiler is
the bootstrap compiler until the Sollang compiler passes a reproducible stage-2
comparison.

## Research Basis

The design deliberately combines a small set of compatible ideas:

- Rust: affine ownership, explicit traits, associated types, and static
  dispatch by default. See the official
  [trait reference](https://doc.rust-lang.org/reference/items/traits.html) and
  [associated items](https://doc.rust-lang.org/stable/reference/items/associated-items.html).
- Rust tracks moves separately from values and elaborates destruction from that
  analysis; Swift makes consuming parameters part of the declaration contract.
  Sollang follows the same separation with a typed-IR move-event side table, while
  retaining structured regions until LLVM lowering. See rustc
  [move paths](https://rustc-dev-guide.rust-lang.org/borrow-check/moves-and-initialization/move-paths.html),
  Rust [partial moves](https://doc.rust-lang.org/rust-by-example/scope/move/partial_move.html),
  and Swift [declarations](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/declarations/).
- Rust requires explicit types on exposed named functions but infers closure
  inputs and results inside a narrow use context; Swift closure expressions may
  omit either side only when context determines one answer. Sollang applies
  that boundary to local helpers and single-consumer private functions, while
  keeping public signatures explicit. See Rust [closure inference](https://doc.rust-lang.org/stable/book/ch13-01-closures.html)
  and Swift [closure expressions](https://docs.swift.org/swift-book/ReferenceManual/Expressions.html#ID544).
- Mojo: compile-time type and value parameterization with specialization at use
  sites; Sollang uses angle brackets to keep this separate from arrays. See
  [generics](https://docs.modular.com/mojo/manual/generics/) and
  [parameterization](https://docs.modular.com/mojo/manual/parameters/).
- Mojo's current ownership model defaults function inputs to immutable `read`
  borrows and separates `mut`, owned `var`, and lifetime-tracked `ref`
  conventions. This supports Sollang's existing readonly-by-default, explicit
  mutable-borrow, and explicit ownership-transfer direction without importing
  Mojo's surface syntax. See Mojo [ownership](https://docs.modular.com/mojo/manual/values/ownership/).
- Zig: an explicit root-module dependency graph and declaration discovery from
  reachable imports. See the official
  [compilation model](https://ziglang.org/documentation/master/#Compilation-Model).
- Swift: source files belong to modules, packages group modules, declarations
  are internal by default, and public API is opt-in. See
  [access control](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/accesscontrol/)
  and [packages](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/introducingpackages/).
- Cargo workspaces use an explicit member set, ancestor discovery, shared output,
  and one dependency-resolution boundary; SwiftPM keeps packages, products, and
  local path dependencies distinct; Zig models builds as a deterministic DAG.
  Sollang combines those boundaries in a confined workspace, exact Git pins,
  and a sparse static registry whose remote bytes are authenticated by the
  workspace lock. See
  Cargo [workspaces](https://doc.rust-lang.org/cargo/reference/workspaces.html),
  SwiftPM [adding dependencies](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/addingdependencies/),
  and the Zig [build system](https://ziglang.org/learn/build-system/).
- Swift structured task groups and sendability plus Mojo's indexed CPU
  `parallelize` shape the deterministic compiler worker design. Sollang uses bounded
  native workers, disjoint indexed result slots, structured join, and canonical
  ordered merge. See [Deterministic Parallel Compilation](PARALLEL_COMPILATION.md).
- Rust and Swift separate UTF-8 code units, Unicode scalar values, and
  user-perceived grapheme clusters. Sollang adopts Rust's fixed-width scalar model
  for compiler work while reserving grapheme segmentation for a library layer.
  See Rust [`char`](https://doc.rust-lang.org/std/primitive.char.html) and
  Swift [Strings and Characters](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/stringsandcharacters/).
- Rust routes default formatting through the static `Display` trait and a
  writer-like `Formatter`; Swift lowers interpolation into typed
  `appendLiteral`/`appendInterpolation` calls; Zig validates compile-time-known
  format descriptions in ordinary library code. Sollang combines those ideas:
  interpolation segments and result types are fixed at compile time, builtin
  values stream directly to the output sink, and user values will use a static
  `Display` trait rather than implicit reflection or heap-built temporary Text.
  See Rust [`std::fmt`](https://doc.rust-lang.org/stable/std/fmt/), Swift
  [`StringInterpolationProtocol`](https://developer.apple.com/documentation/swift/stringinterpolationprotocol),
  and the Zig [language reference](https://ziglang.org/documentation/master/).
- Zig recommends an arena when allocations share one lifetime and can all be
  released together; rustc describes arena allocation as a pointer bump. Sollang's
  byte arena follows that lifetime model while exposing checked offsets instead
  of raw pointers. See Zig [Choosing an Allocator](https://ziglang.org/documentation/master/#Choosing-an-Allocator)
  and rustc [`rustc_arena`](https://doc.rust-lang.org/stable/nightly-rustc/rustc_arena/index.html).
- MLIR SCF keeps `if` and loop bodies as structured regions, while Rust MIR
  makes the later control-flow graph explicit as typed basic blocks ending in
  terminators. LLVM then lowers an `if` to a conditional branch, two branch
  blocks, a continuation block, and a `phi` only when the expression produces
  a joined value. Sollang follows that staged boundary: structured regions in typed
  IR first, explicit LLVM CFG during backend lowering. See MLIR
  [SCF](https://mlir.llvm.org/docs/Dialects/SCFDialect/), the rustc guide to
  [MIR](https://rustc-dev-guide.rust-lang.org/mir/index.html), and LLVM's
  [control-flow tutorial](https://llvm.org/docs/tutorial/MyFirstLanguageFrontend/LangImpl05.html).
- Windows file mappings and POSIX `mmap` keep large files outside ordinary heap
  allocation while exposing bounded views. Sollang wraps those views in affine
  ownership and aligns hidden base mappings to the host granularity. See
  Microsoft [Creating a View Within a File](https://learn.microsoft.com/en-us/windows/win32/memory/creating-a-file-view)
  and Linux [`mmap(2)`](https://man7.org/linux/man-pages/man2/munmap.2.html).
- Rust exposes process-provided arguments separately from ordinary owned
  collections and cautions that argument zero is not a trusted executable path;
  Zig passes explicit process initialization state to `main`; Swift and Mojo
  keep child execution in a structured process API. Sollang follows those boundaries
  with a read-only `Arguments` view and a shell-free argv-based child-process API.
  See Rust [`args_os`](https://doc.rust-lang.org/std/env/fn.args_os.html),
  Swift [`Process`](https://developer.apple.com/documentation/foundation/process),
  and Mojo [`subprocess`](https://docs.modular.com/mojo/std/subprocess/).
- Rust incremental compilation identifies dependency nodes with stable
  fingerprints that do not contain session-local ids, while Clang module caches
  rebuild a module when one of its source inputs or imported modules changes.
  Sollang's future module/interface cache will therefore key artifacts by compiler
  ABI, target configuration, source/interface content, and dependency
  fingerprints rather than timestamps alone. See rustc
  [dependency-node fingerprints](https://doc.rust-lang.org/stable/nightly-rustc/rustc_query_system/dep_graph/dep_node/index.html)
  and Clang [module caches](https://clang.llvm.org/docs/Modules.html#compilation-model).
- Zig 0.16 keeps incremental compilation explicit while its dependency graph,
  frontend, code generation, and linker support mature; its local cache is
  disposable and never a source of correctness. Sollang adopts that recovery
  property, but requires cached and clean builds to remain byte-identical before
  incremental mode can become the default. See Zig's
  [0.16 incremental compilation notes](https://ziglang.org/download/0.16.0/release-notes.html#Incremental-Compilation)
  and [build-system cache model](https://ziglang.org/learn/build-system/).

Sollang keeps its own expression-first `=>` binding and fluent `->` application
syntax. It does not adopt class inheritance, implicit null, implicit garbage
collection, implicit heap allocation, or a default runtime dispatch layer.

## Progress Calculation

There are 60 auditable capability gates. A complete gate scores 1, a partial
gate scores 0.5, and a missing gate scores 0. The percentage is the score
divided by 60. A gate becomes complete only with a cumulative `.slg` example or
an automated compiler test. This count measures language/compiler capability,
not lines of code.

| Area | Gates | Complete | Partial | Missing | Score |
| --- | ---: | ---: | ---: | ---: | ---: |
| Core syntax and control flow | 10 | 10 | 0 | 0 | 10.0 |
| Types, traits, and generics | 12 | 11 | 0 | 1 | 11.0 |
| Ownership and storage | 10 | 7 | 2 | 1 | 8.0 |
| Modules, visibility, and builds | 8 | 8 | 0 | 0 | 8.0 |
| Compiler-construction primitives | 12 | 11 | 1 | 0 | 11.5 |
| Standard library and tooling | 8 | 2 | 5 | 1 | 4.5 |
| **Total** | **60** | **49** | **8** | **3** | **53.0 / 60** |

Current count-based progress: **88.3% (53 of 60 equivalent gates)**.

The frontend parallel-compilation subproject is **28/28 checks (100%)**. Its
source-local product boundary, typed callback-result role slice, nested-call
identity regression, Windows native compute pool, and source-local parallel
frontend execution are complete. Owned source-analysis results and ordered
LLVM-body sinks now cross worker boundaries safely, and parallel callbacks
reject mutable or structurally non-sendable captures. The submitting parent now
helps drain its task group before the structured join. Exact cancellation and
partial-result destruction plus full Windows/Linux suite parity are proven.
This completed feature-local subproject does not promote a roadmap gate.
There are **7 equivalent gates remaining**. Because the remaining compiler
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
for every case: one native Sollang compiler driver accepts target mode and source
file paths, memory-maps every module, then emits Windows, Linux, or Wasm LLVM.
Its timestamp fingerprint covers the compiler, source manifest, all listed Sollang
modules, and the standard library. A focused warm invocation completed in 1.1
seconds, including 0.12 seconds in the self-host compiler; the one-time cold
bootstrap completed in 59.7 seconds. Two specialized introspection examples
retain the ordinary reference-compiler path.

Current native bootstrap chain:

- [x] Build one reusable native stage-1 `sollangc` with the C# bootstrap compiler.
- [x] Read and own multiple source files through affine `SourceText` mappings.
- [x] Compile those modules with the Sollang frontend and emit valid target LLVM IR.
- [x] Reuse the current stage-1 executable across self-host LLVM fixtures.
- [x] Invoke `clang`/`lld` from `sollangc` and produce ordinary final executables directly.
- [x] Closure-convert local compiler functions so native optimization partitions use all cores.
- [x] Emit and assemble the complete stage-2 module with `llvm-as`.
- [x] Link stage 2 with the platform entry shim, runtime, and imported stdlib definitions.
- [x] Run stage 2 and compile single-file and imported multi-file Sollang smoke programs with it.
- [x] Rebuild `sollangc` with stage 1 and compare reproducible stage-2 LLVM artifacts.

The native bootstrap chain is now **10/10 complete (100%)**. The complete
28-source compiler emits a 6,730,900-byte Windows LLVM module, which assembles
and links as a stage-2 compiler. Examples 365 and 366 cover a minimal program
and a two-file imported module. After newline normalization, stage 1 and stage 2
produce identical LLVM SHA-256 values for both programs; both stage-2 outputs
also assemble, link, and execute. `scripts/verify-selfhost-stage2.ps1` preserves
this as a six-step cached differential gate. This completes the bootstrap
milestone without changing the broader 60-gate language-capability score.

## Gate Inventory

### Core syntax and control flow — 10 / 10

- Complete (10): functions including fluent and direct multi-parameter calls,
  local functions, expressions, bindings, arithmetic and Boolean logic,
  `if`/`when`, ranges/loops, block-function calls, and structured early exit
  with `return`/`break`/`continue` across ownership scopes. Moved direct fields
  may be reinitialized; a branch or loop must repair every partial move before
  it rejoins its parent region.

### Types, traits, and generics — 11.0 / 12

- Complete (11): nominal structs, payload enums, exhaustive matching, impl
  methods, nominal traits/static dispatch, checked type/value specialization,
  associated types with equality constraints, two-parameter generic inference,
  standard `Option<T>`/`Result<T, E>` tagged values, fixed-width signed,
  unsigned, and IEEE-754 scalar layouts with stable `Int32`/`Float32` defaults
  plus `Long`/`Double` 64-bit aliases and target-ABI `Size`/`UIntSize`; arrays
  and dictionaries preserve scalar/user-value layouts and recursively drop
  owned elements. Generic collection function contracts preserve concrete
  component types, fixed arrays infer `T` from `[T; N]` while checking `N`, and
  indexed extraction transfers owned array/dictionary elements exactly once.
- Partial (0).
- Missing (1): explicit `dyn Trait`.

### Ownership and storage — 8.0 / 10

- Complete (7): unique owned values, readonly borrow by default, `mut` borrow,
  explicit `move`, recursive static drop glue, lifetime-based stack placement,
  explicit `box T`.
- Partial (2): borrow lifetimes are intentionally narrow; ownership through
  fully generic containers is not implemented.
- Missing (1): a complete path-sensitive borrow checker for references returned
  from functions and stored in user values.

### Modules, visibility, and builds — 8 / 8

- Complete (8): file namespaces/import aliases; multiple user source files in
  one compilation unit; root imports recursively discover module files with
  missing, cycle, namespace-mismatch, and duplicate-module diagnostics;
  functions, structs, enums, and traits are internal by default with explicit
  `public` exports and module-qualified nominal identity; `sollang.project`
  names a confined root source and output identity, and source-free
  `sollang build` discovers it from ancestor directories; the standard library
  recursively discovers every `.slg` module below its confined root in stable
  relative-path order and verifies path-to-namespace identity; ordinary builds
  use validated exact-input frontend and product generations, persistent
  prefix/module/suffix LLVM units, and schema-4 partial-source semantic body
  rehydration with atomic publication and Windows/Linux invalidation parity;
  the package graph has deterministic multiple-product selection, exact local
  path dependencies,
  direct-dependency visibility, transitive resolution, and cycle/name-collision
  diagnostics. A confined `sollang.workspace` now provides explicit local member
  discovery, package selection, workspace-closed dependency validation, shared
  target/package output roots, cache identity, and ancestor discovery. Exact Git
  revisions and a checksummed sparse static registry share lock format 2,
  preserve compatible pins on ordinary builds, and update only through explicit
  `sollang resolve`.
- Partial (0).
- Missing (0).

### Compiler-construction primitives — 11.5 / 12

- Complete (11): Text values with allocation-free UTF-8 byte search, prefix,
  suffix, containment, ordinal comparison, and ASCII case-insensitive equality;
  validated UTF-8 iteration as fixed-width Unicode
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
  through Sollang lexer tokens, flat green CST nodes, invalid-byte diagnostics, and
  furthest-unexpected-token diagnostics.
- Partial (1): generic arrays/dictionaries cover compiler-useful `Int`, `Text`,
  and user-value payloads plus function contracts, but fully general generic
  container ownership remains tied to the ownership/storage gate.

### Standard library and tooling — 4.5 / 8

- Complete (2): basic `sys.io` and three LLVM-backed target link paths.
- Partial (5): file/random/time APIs are narrow compiler intrinsics; VS Code
  support is grammar-only; tests are example-driven without an Sollang unit-test
  framework. File I/O now monomorphizes canonical scalar `write<T>` and
  zero-input `read<T>` calls with explicit EOF/error results. Affine `File`
  owners and position-based `readAt<T>`/`readAtAsync<T>` remove shared-cursor
  races. Affine `FileWriter` and scalar `writeAt<T>` now provide the symmetric
  output path; `writeAtAsync<T>` owns copied bytes and a duplicate handle while
  it is pending, `syncAsync` provides an explicit durability barrier, and async
  open owns its path and transfers its newly opened handle on await. Explicit
  user-value serialization remains. The package/build surface has confined
  roots, automatic discovery, selected products, deterministic local dependency
  resolution, recursive imports, target output, and confined local workspaces,
  with versioned path/Git/registry resolution and a reproducible lock. Publishing,
  private-registry authentication, package signing, and a general build DAG
  remain tooling work.
  The owned portable Path layer has explicit Posix/Windows lexical normalization
  and confined joins. Windows/Linux directory reads now return sorted owned
  snapshots with entry kind metadata; canonical queries and richer metadata
  remain.
- Missing (1): formatter and language server based on the real parser.

## Critical Path To Self-Hosting

1. Finish distributable packages: local dependency products, direct visibility,
   and workspace-wide local resolution work; version constraints, remote
   sources, and a lock file remain.
2. Finish the reusable type substrate: multi-parameter generics, associated
   types, generic `Array<T>`/`Dictionary<K, V>`, `Option`, and `Result`.
3. Add compiler data primitives: bytes, source spans, Unicode iteration, arena
   allocation, filesystem traversal, arguments, and process execution.
4. Write the Sollang lexer and parser using generated bootstrap tables only where
   necessary; compare tokens and AST snapshots against the C# compiler.
5. Port semantic/type/ownership analysis and serialize a stable typed IR.
6. Implement an Sollang LLVM IR text builder, then compile representative programs
   with both compilers and compare normalized IR plus runtime output.
7. Stage 1: C# compiler builds the Sollang compiler. Stage 2: that Sollang compiler builds
   itself. Stage 3: the stage-2 compiler rebuilds itself byte-for-byte or with
   normalized-IR equivalence, depending on target linker determinism.

The grammar-bootstrap path now includes `sollang grammar build`, an Sollang lexer,
and an Sollang parser VM. The build command
compiles the canonical lexer/EBNF specifications into a deterministic ordinary
Sollang module containing lexer descriptors and a 1,580-word parser VM program. The
full test runner checks byte-for-byte regeneration. The Sollang VM consumes those
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
ordinary Sollang module that selects module/declaration/function/main/binding/flow/
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

The semantic bootstrap has begun in a separate Sollang module. Its flat symbol table
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
compiler's own Sollang sources.

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
non-`Int` index. The Sollang lexer now tokenizes raw multiline strings with matching
three-or-more-quote delimiters, keeping its source envelope aligned with the
bootstrap lexer.
Dictionary expressions now preserve full key/value identities instead of
packing only their symbol ids. That metadata survives bindings, generic calls,
and composite fields, allowing dictionary indexing to check its key and infer
its value without confusing local, imported, generic, or builtin identities.

Typed semantic output now has an initial stable IR contract. A flat Sollang-owned
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

Critical-path step 6 has an executable first slice: Sollang lowers zero-input
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
Sollang-source -> Sollang semantic/typed IR -> Sollang LLVM text -> native linker -> process
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
drop obligations and reject overlapping reuse. Local functions emitted as
independent LLVM functions now accept explicit early returns and run the same
reverse-order ownership cleanup as module functions. Direct moved fields can
now be reinitialized, and branch/loop regions may rejoin only after repairing
their partial moves, completing the structured early-exit gate without runtime
drop flags.

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
partial-move masks are now emitted. Local-function returns share that cleanup
path. Reinitialization clears the exact direct-field mask before later use and
cleanup; branch and loop joins reject any unrepaired partial-move state.

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
drop obligations. Direct-field reinitialization is complete; nested owned
aggregate member mutation remains outside the current assignment surface.

The reference frontend now supports context-inferred primary input and return
types for local functions and for non-public top-level helpers consumed by one
function or `main` scope. Empty signature slots retain the existing punctuation,
and a fixed-point constraint pass combines call arguments, tail expressions,
and explicit returns. Public/generic/impl declarations, multiple consumer
scopes, conflicting calls, and underconstrained recursion are rejected. The
self-host semantic type table still needs the corresponding inferred-signature
records before this convenience is available through native `sollangc`.

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
`sollang.compiler.llvm.target`. Windows x64/COFF, Linux x64/ELF, and
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
siblings. Direct-field assignment repairs the matching move path, while
branch/loop joins reject unrepaired partial moves.
Target-specific runtime declarations and ABI lowering beyond the
currently supported shared IR subset also remain. Text `print`/`println` are
the first completed runtime effect slice: flow calls survive semantic lowering
as explicit runtime symbols, Windows emits a `putchar` loop, Linux emits
`write(2)`, and Wasm declares an `env.slg_write` import. Runtime helpers
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
`sollang.compiler.ir.interpolation`: balanced
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

Owned dictionaries now have a common self-hosted `%sollang.dict` LLVM ABI with
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

- [x] map and own multiple Sollang source modules;
- [x] emit target-specific LLVM from the cached native stage-1 compiler;
- [x] redirect LLVM to a file through a typed, shell-free process API;
- [x] invoke Clang and produce a runnable native executable;
- [x] prove the path with a multi-module Sollang program;
- [x] restore complete compiler emission after the structured native-build path;
- [x] rebuild `sollangc-stage2` from that complete module;
- [x] re-establish reproducible stage-1/stage-2 output comparison.

The stage-2 checklist is again **8/8 complete (100%)**. The complete input now
includes the public `sys.process` module, and canonical typed IR identifies
`arguments`, `run`, and `runToFile` by imported module/symbol identity. The
self-host LLVM backend lowers the latter two through a portable process-result
contract and materializes the language-level `Result<Int, Text>` value.

The verifier builds the 8,185,153-byte Windows stage-2 compiler, compares
single-file, grouped-Boolean, and imported multi-file LLVM from stage 1 and
stage 2, assembles and executes every smoke module, and compares C# and native
Sollang behavior. It also invokes the generated stage-2 compiler's own
`build-windows` command: stage 2 redirects its LLVM with `runToFile`, invokes
the pinned Clang through `run`, and the resulting executable prints
`stage2-single-ok`.

Linux now has the matching SourceText owner runtime. It copies the Sollang path into
a null-terminated buffer, opens the file read-only, determines its length with
`lseek`, maps it through `mmap`, and releases the mapping with `munmap` at the
owned value's deterministic drop. The dedicated Linux stage-2 verifier is
**5/5 complete (100%)**:

- [x] emit the complete 29-source Linux stage-2 compiler;
- [x] assemble it and produce a Linux object;
- [x] link and run the generated Linux compiler;
- [x] prove stage-1/stage-2 identity for single and imported multi-file input;
- [x] assemble, link, and execute both stage-2-produced programs.

The complete Linux compiler is 8,185,015 LLVM bytes. Its generated single-file
and imported multi-file products are byte-normalized hash-identical to stage 1
and execute as `stage2-single-ok` and `stage2-multi-ok`. Windows Stage2 remains
8/8 at 8,185,153 LLVM bytes, and both target suites pass 527/527.
The formal language-capability score remains 42 complete, 13 partial, 5 missing
(48.5/60, 80.8%) until the remaining ownership, generic-container, package,
tooling, and library gates are implemented.

## Self-host Compile-Time Baseline (2026-07-18)

Stage 3 is a periodic fixed-point gate, not part of every Stage2 verification.
The normal feature checkpoint ends after the Windows/Linux Stage2 checks. Run
`scripts/verify-selfhost-stage3.ps1` after **10 Stage2-verified feature
checkpoints**, or earlier when a bootstrap, intrinsic, ABI, or compiler-emitter
change can plausibly alter self-reproduction. An explicit Stage3 request also
overrides the cadence. The latest fixed-point verification reset the counter to
**0/10** on 2026-07-19. The Sollang/`.slg` migration is the first subsequent
Stage2-verified feature checkpoint, so the current cadence is **1/10**.

The compiler-sized stage-2 path now caches function captures and function-end
boundaries, indexes call targets by canonical module symbol, and buffers
redirected Windows stdout in 1 MiB blocks. The self-host emitter generates the
same buffered runtime, so stage 3 no longer writes LLVM one byte at a time.

- [x] assemble, link, and execute the complete stage-2 compiler;
- [x] compare C# stage 1 and Sollang stage 2 on single and imported multi-file LLVM;
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
- [x] stream C# reference LLVM through composable memory/file output sinks
  without a second complete-module string.

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

The Linux bounded-pool and shared memory-output-sink revision reaches an exact
7,217,656-byte stage-2/stage-3 fixed point with SHA-256
`1C026529C832C88AA54ACCC55B05FE0A7358BBFA4F2A31F6F6F1F1ECEF0FD0DD`.
Stage 3 assembles with `llvm-as`, takes 36.86 seconds wall and 407.42 CPU-seconds
(11.05 effective cores), and peaks at 100.7 MiB. The Windows suite passes
508/508, the focused Linux verifier passes 5/5, and stage-2 differential
verification passes 6/6. Parallel progress is 26/28 (92.9%); the canonical
roadmap remains 48.5/60 (80.8%).

The self-host generic-enum execution boundary now preserves qualified
`Option`/`Result` constructors into typed IR and emits the reference-compatible
`{ i32 tag, [N x i64] payload }` LLVM ABI. Example 390 assembles, links, and
executes construction plus contextual `when` matching for both `Ok(Int)` and
`Err(Text)`, including typed payload bindings. Tag-directed outer Result
destruction and scalar self-host `tryParallel` lowering now execute in entry,
function, and nested-region positions through the native compute pool. Example
395 fixes deterministic competing failures, while example 396 verifies nested
owned callback-payload cleanup with Linux AddressSanitizer leak detection. The
parallel checklist is now 27/28 (96.4%); Linux full-suite parity remains. This
feature-local checkpoint does not advance the 48.5/60 canonical gate count.

The following complete Windows regression found and fixed two stabilization
issues before accepting updated LLVM fixtures. Self-host aggregate value lookup
now distinguishes transparent opcode `-1` wrappers from real kind-9 slice
operations, and two-operand calls choose the later resolved IR value. The
Windows and Linux compute runtimes reserve the submitting parent's first work
item before releasing workers, making the parent-help contract deterministic;
the Windows executable reported `parent-helped=true` in 30/30 repeated runs.
The read-only Windows suite passes 523/523, the Release build has zero warnings
and errors, and the focused Linux verifier passes 6/6. Linux full-suite parity
was subsequently implemented as a target-aware 523-case runner. Ordinary
examples and diagnostics compile for Linux x64 and execute under WSL; reusable
self-host examples emit Linux LLVM, every module assembles, and cases with
runtime expectations link and execute natively. The Linux suite passes 523/523,
so the parallel checklist reaches 28/28 (100%). This closes the feature-local
parallel plan; the canonical roadmap remains 48.5/60 (80.8%).

## Canonical Generic-Container Specialization Baseline (2026-07-19)

Sollang follows a statically specialized value-witness model. A concrete collection
type carries canonical component IDs into LLVM lowering; those IDs select size,
alignment, LLVM representation, and recursive ownership traits. This keeps the
runtime representation compact and avoids a per-value metadata pointer while
retaining the same essential information that Swift value witnesses expose.
Rust's sound generic-drop rule and Mojo's trait-constrained containers support
making ownership behavior a property of the concrete element type rather than
of a shallow container spelling.

- [x] globally intern recursive collection component type IDs;
- [x] compute target-aware component size and alignment from those IDs;
- [x] specialize contextual dynamic-array return literals;
- [x] specialize contextual dictionary return literals;
- [x] use canonical dictionary key/value types for lookup and LLVM loads;
- [x] recursively destroy owned dynamic-array and dictionary elements;
- [x] implement move extraction of owned indexed elements;
- [x] complete fixed-array generic function contracts.

This focused migration is **8/8 checks (100%)**. Examples 397 and 398 assemble,
link, and execute on Windows and Linux. They prove that `{UInt16: Int64}` uses
2-byte keys and 8-byte values and that `[UInt16; ~]` uses a 2-byte stride. Before
this correction the producer stored default `Int32` components while the
consumer loaded the declared widths, producing `21474836489` instead of `9`.

Example 400 adds the recursive ownership gate. The emitter computes the active
owned-type dependency closure from canonical type IDs and emits one specialized
drop witness per reachable array, fixed array, dictionary, box, nominal struct,
`Option`, `Result`, or `SourceText` type. Dynamic arrays destroy each owned
element before their backing allocation; dictionaries do the same for owned
keys and values. Partial nominal moves retain field-path cleanup so an already
moved field is never destroyed twice. `scripts/verify-recursive-container-drop.ps1`
assembles the generated Linux LLVM and executes it under AddressSanitizer with
leak detection enabled.

Examples 401-404 add explicit mutating extraction with
`owner! -> take(indexOrKey) => value!`. Ordinary indexing remains a borrow/read
operation and still rejects copying an owned element. Array extraction closes
the ordered gap, while dictionary extraction removes the stored key/value
entry. The extracted value becomes a new owner and the shortened source owner
retains exactly the remaining elements. The dedicated Linux verifier executes
the reference and both self-host forms under AddressSanitizer and LeakSanitizer.

Example 51 now applies `[T; N]` contracts to `Int`, `Text`, user structs, and
owned user values. Calls spell only the compile-time length, such as
`values -> fixedLength<3>`; the element type is inferred from the fixed array.
The host compiler monomorphizes by both length and element type while passing a
borrowed pointer/length ABI, so the callee neither copies nor consumes owned
elements. Example 405 proves the same structural inference in the Sollang semantic
compiler and rejects a mismatched explicit length. Dynamic arrays are rejected
by a dedicated diagnostic. Windows/Linux execution and a Linux ASan/LSan run
cover the owned-array case.

The formal roadmap is now **50/60 (83.3%)**. Fixed-array generic function
contracts close the canonical generic-container gate, and general
multi-parameter functions close the corresponding core syntax gate.

Private signature inference now also survives native self-host lowering. The
flat AST marks omitted primary-input and return positions without inventing
declared type nodes; canonical type-ID inference propagates the single-consumer
call constraint through local parameters, bindings, function bodies, and call
results. Examples 413-415 cover native LLVM execution plus the AST/symbol
contract. The Windows suite passes 552/552 and Stage2 passes 6/6 at 8,881,548
LLVM bytes. This is Stage2 checkpoint 4/10, so the periodic Stage3 gate remains
deferred.

Standard-library loading no longer embeds a compiler-side list of `sys`
modules. The compiler discovers all `.slg` sources recursively below the
confined `stdlib` root, orders them by ordinal relative path, and requires each
file path to match its declared namespace. Duplicate namespaces and executable
top-level statements are rejected before semantic binding. Example 416 proves
that the newly added `sys.text` module is compiled and executed without adding
it to a bootstrap manifest. The Release build has zero warnings and errors,
the Windows suite passes 553/553, and Stage2 passes 6/6 at 8,881,548 LLVM bytes
with differential hashes preserved. This is checkpoint 5/10, so Stage3 remains
deferred. The change completes the standard-library source-set gate and raises
the formal roadmap to **45 complete, 10 partial, 5 missing: 50/60 (83.3%)**.

Examples 406-409 prove fluent/direct calls, methods, generics, independent
readonly/`mut`/`move` modes, structured async, self-host LLVM execution, and
self-host arity/type diagnostics. The complete Windows and Linux suites pass
543/543. Windows Stage2 passes 6/6 at 8,392,752 LLVM bytes and Linux Stage2
passes 5/5 at 8,392,614 bytes with differential hashes preserved.

Research basis:

- [Rust drop check](https://doc.rust-lang.org/nightly/nomicon/dropck.html)
- [Swift generics implementation model](https://download.swift.org/docs/assets/generics.pdf)
- [Mojo generic traits and containers](https://mojolang.org/docs/manual/traits/)

## Owned Portable Path Checkpoint (D203)

`sys.path.Path` now owns canonical UInt8 storage and carries explicit Posix or
Windows lexical rules. Confined normalization handles repeated separators,
`.`/`..`, drive roots, and UNC roots without consulting the host filesystem;
joining an absolute child is an error. The reference backend emits only
reachable standard-library functions that require independent ownership or
control-flow frames, so imported `move` functions preserve field transfers and
early returns without polluting unrelated programs. Reserved Path, Style, and
UInt8-buffer type IDs keep existing user and parametric LLVM identities stable.

Example 423 executes the real standard-library module. Example 424 proves the
self-host LLVM compiler can emit, assemble, link, and run an imported owned
Path-shaped module. Windows passes 561/561 and Stage2 passes 6/6 at 8,919,060
bytes with unchanged differential hashes. This is checkpoint 8/10; directory
handles, metadata, canonical queries, and deterministic traversal keep the
filesystem gate partial. The formal roadmap is **51.5/60 (85.8%)**.

## Deterministic Directory Snapshot Checkpoint (D204)

`sys.directory.read(Path)` now returns an owned snapshot on Windows and Linux.
The platform layer enumerates once, excludes `.` and `..`, inserts names in raw
UTF-8 byte order, records file/directory/symlink/other kind, serializes a compact
length-prefixed buffer, and closes the native handle before returning. The
stdlib decoder copies each basename into an independently owned `Path`, so no
borrowed `WIN32_FIND_DATA` or `dirent` storage escapes.

Directory-only LLVM types, drop helpers, declarations, and runtime functions are
emitted only when traversal is reachable. Stable reserved identities prevent
the new stdlib types from renumbering existing snapshots. The work also fixes
nested owned-field transfer into containers, branch-local enum payload transfer,
and code generation for enum constructors with multi-segment namespace paths.

Example 425 passes on both Windows and Linux and proves deterministic ordering
and directory-kind classification. The Release build has zero warnings and
errors, the complete Windows suite passes 562/562, and Stage2 passes 6/6 at
8,919,060 LLVM bytes with all three differential hashes preserved. This is
checkpoint 9/10, so Stage3 is intentionally deferred. Canonical filesystem
queries and richer metadata keep the filesystem gate partial; the formal
roadmap remains **51.5/60 (85.8%)**.

## Target-Native Path Source Mapping Checkpoint (D205)

`sys.file.mapPath(Path)` now maps a compiler source directly from the owned,
target-explicit Path produced by directory discovery. It borrows the Path bytes,
checks the carried style against the output target, and returns an independent
affine `SourceText`; no intermediate Text allocation or host-style path
interpretation is required. `sys.path.nativeStyle` exposes the reference
compiler's output-target style for source-root construction.

The self-host typed IR recognizes `mapPath` as a SourceText-producing intrinsic,
and its LLVM emitter performs the same style validation before reusing the
native mapped-source runtime. Example 426 exercises the real standard library on
Windows and Linux. Example 427 exercises self-host LLVM assembly, linking, and
execution. This is the prerequisite boundary for D206 to combine sorted
directory snapshots with deterministic source-root loading. Canonical queries
and richer metadata keep the filesystem gate partial, so the formal roadmap
remains **51.5/60 (85.8%)**.

The Release build has zero warnings and errors and the complete Windows suite
passes 564/564. Stage2 passes 6/6 at 8,958,755 LLVM bytes with differential
hashes unchanged. Stage3 reaches the identical 8,958,755-byte compiler at hash
`A8FF3B396E03DD487017C3EC04521CE605501A61860B87849DF55714DE48CA39`.
This completes checkpoint 10/10 and resets the periodic Stage3 cadence to 0/10.
To remove a bootstrap-only partial-move drift without shallow aliasing, compiler
preparation copies its borrowed Text source table into the returned emit context,
while module-call resolution borrows request sources through completion and
drops the request as one owner.

## Deterministic Source-Root Discovery Checkpoint (D206)

The self-host compiler now discovers a source root breadth-first through sorted,
owned directory snapshots, filters regular `.slg` files, and maps each resulting
target-explicit Path without an intermediate Text copy. Examples 428 and 430
cover deterministic and empty roots; example 429 drives the discovered sources
through self-host LLVM emission. The local effectful `mapSource` helper omits its
inferred return type, exercising the private-signature inference boundary in the
real compiler pipeline.

Windows Stage2 passes 6/6 at 9,361,816 LLVM bytes and Linux Stage2 passes 5/5 at
9,360,301 bytes, with all target differential hashes preserved. The complete
Windows suite passes 567/567. This is checkpoint 1/10 after the D205 Stage3
reset, so Stage3 remains deferred. Canonical filesystem queries and richer
metadata keep the formal filesystem gate partial at **51.5/60 (85.8%)**.

## Stable Module Fingerprint Foundation (D207A)

The self-host semantic layer now derives separate stable identities for a
module's exported interface, normalized implementation, and ordered direct
imports. Example 431 proves trivia stability, body/private isolation, and
public-signature/import invalidation. The reusable native compiler exposes a
`fingerprint` mode, and the Stage2 gate compares three real modules byte-for-byte
between the C#-built Stage1 compiler and the Sollang-built Stage2 compiler.

This foundation deliberately does not claim the missing module-cache gate yet.
Versioned canonical serialization, atomic publication, corruption validation,
and dependency-driven hit/miss reuse remain D207B. D207A is Stage2 checkpoint
2/10 after the D205 reset. The formal count remains **47 complete, 9 partial,
4 missing: 51.5/60 (85.8%)**.

Windows passes 568/568 examples and Stage2 6/6 at 9,401,740 LLVM bytes. Linux
Stage2 passes 5/5 at 9,400,225 bytes. D207B is the next implementation slice.

## Canonical Interfaces and Atomic Publication (D207B1)

Schema-1 canonical interface words now preserve every exported-signature token
and source byte while excluding trivia, bodies, private declaration positions,
and session-local indices. Cache lookup still uses a UInt64 fingerprint, but
reuse requires complete canonical-stream equality. `FileWriter.sync` plus
`AtomicReplaceRequest -> atomicReplace` supplies the staged-write publication
boundary on Windows and Linux; example 432 executes the full replacement path.

Windows passes 569/569 examples and Stage2 6/6 at 9,464,194 LLVM bytes. Linux
Stage2 passes 5/5 at 9,462,679 bytes. D207B1 is Stage2 checkpoint 3/10 after the
D205 reset. Persistent cache decoding, corruption rejection, dependency
hit/miss integration, and body-only consumer reuse remain D207B2, leaving the
formal roadmap at **47 complete, 9 partial, 4 missing: 51.5/60 (85.8%)**.

## Validated Cache Planner (D207B2)

The schema-1 cache validator now rejects truncated input, magic/schema/context
and target mismatches, declared-length mismatches, checksum corruption, and
malformed record bounds. Reuse requires full canonical interface equality and
ordered direct-dependency interface equality. Example 433 proves warm hits,
body-only dependency edits that preserve the consumer, public-signature edits
that invalidate both modules, and atomic persistent publication on Windows and
Linux through the reference backend.

The pure cache codec and planner are Stage2-compatible and run in the reusable
compiler's `interface-cache` mode. Persistent I/O is isolated in
`module_cache_io.slg` because owned File open/read/write/sync/replace lowering
is still absent from the self-host LLVM backend. That remaining parity item is
tracked explicitly and keeps the module/interface-cache gate partial.

Windows passes 570/570 examples and Stage2 6/6 at 9,545,859 LLVM bytes. Linux
Stage2 passes 5/5 at 9,544,344 bytes. D207B2 is Stage2 checkpoint 4/10 after the
D205 reset; Stage3 remains deferred. The formal roadmap remains **47 complete,
9 partial, 4 missing: 51.5/60 (85.8%)**.

## Self-Host Persistent Cache I/O (D207B3)

The self-host LLVM backend now lowers affine `File`/`FileWriter` open,
positioned scalar read/write, durability sync, deterministic close, and atomic
replacement on Windows. `module_cache_io.slg` therefore executes the same
schema-1 load, bounded read, staged write, close-before-publish, and validation
path in the C#-built Stage1 compiler and the Sollang-built Stage2 compiler.

Windows Stage2 passes 6/6. The three established differential hashes remain
unchanged, native execution and build pass, and the persistent planner reports
the same `0,3,0,0,1` result in Stage1 and Stage2. Example 434 independently
locks down the LLVM regression exposed by this work: a payloadless `when` arm
whose value is a no-argument function call must emit that call before storing
the arm result.

D207B3 is checkpoint 5/10 after the D205 Stage3 reset. It closes self-host I/O
parity, but does not yet make an ordinary build skip frontend or codegen work;
the module/interface-cache gate therefore remains partial at **47 complete,
9 partial, 4 missing: 51.5/60 (85.8%)**.

## Incremental Build Integration (D207C, complete)

The next slice moves the cache from a verification mode into the ordinary build
pipeline. The design uses two immutable generations, following rustc's old/new
dependency-graph model: load the previous manifest, construct the current plan,
reuse only green module artifacts, and atomically publish a complete new
generation after a successful link. A cache hit requires all of the following:

1. compiler schema, target, ABI, and build configuration match;
2. the module's full source identity or normalized implementation identity
   matches the cached record;
3. every ordered direct dependency has the same full canonical interface;
4. the cached typed-IR/LLVM fragment passes its length, checksum, and identity
   checks; and
5. clean and cached builds produce identical normalized LLVM and runtime output.

Implementation order:

- [x] Add stable raw-source identities so unchanged files can be recognized
  before full semantic analysis.
- [x] Serialize module-level reusable semantic/typed-IR artifacts without
  session-local indexes.
  - [x] Encode a canonical module envelope ordered by stable path hash, with IR
    references rewritten as module hash plus module-local ordinal.
  - [x] Add the canonical structural type table and decode/rehydration path.
- [x] Split deterministic LLVM output into cacheable module/codegen units and a
  canonical ordered merge.
  - [x] Define, validate, decode, and canonically merge the persistent
    codegen-unit artifact independently of source input order.
  - [x] Route the real LLVM emitter into shared-prefix, per-module, and
    shared-suffix sinks and consume reused fragments in an ordinary build.
- [x] Integrate old-generation load and new-generation atomic publication into
  normal `sollang build`.
  - [x] Load, validate, reuse, and atomically publish the C# bootstrap
    compiler's LLVM codegen units after a successful link.
  - [x] Load the stable raw-source and semantic generations before semantic
    body validation so frontend work can be skipped wholly or per green body.
    - [x] Skip the complete frontend when every exact source and build-context
      input matches a snapshot bound to the validated codegen generation.
    - [x] Skip linking when the exact final product remains bound to the same
      validated source and codegen generations.
    - [x] Rehydrate reusable semantic body state for unchanged modules after a
      partial source miss.
      - [x] Replace session-local type IDs and AST object addresses with stable
        structural function and resolved-call-site identities.
      - [x] Persist, validate, and remap those identities through a canonical
        checksummed semantic generation.
      - [x] Store the structural semantic payload needed to bypass unchanged
        function-body validation and load it before that phase.
        - [x] Restore cached binding and captured-binding types before validating
          unchanged functions without local or generic-call state.
        - [x] Restore complete local-function trees atomically and reuse a green
          main scope when neither contains specialization state.
        - [x] Restore generic/specialized call-site state so every green module
          body can bypass validation.
- [x] Prove cold, warm, body-only, public-interface, corruption, target-change,
  and clean-vs-cached byte-equivalence cases on Windows and Linux.
  - [x] Prove the complete matrix for ordinary LLVM codegen-unit reuse with
    `scripts/verify-codegen-cache.ps1`.
  - [x] Prove exact-input frontend skipping and source-snapshot/codegen
    corruption fallback on Windows and Linux.
  - [x] Prove the same invalidation matrix for pre-semantic semantic-body reuse.

This deliberately starts with module/codegen-unit granularity. Rust's query
graph shows the eventual finer-grained direction, while Clang demonstrates that
semantic module imports need strict configuration consistency. Zig's current
incremental LLVM path also confirms that frontend reuse and LLVM object emission
have distinct cost boundaries; Sollang will measure and cache them separately.

D207C1 completes the first of five integration slices (**1/5, 20%**). Cache
schema 2 records a stable hash of the exact source bytes and exposes a cheap
`preflight` check before semantic analysis. Semantic reuse deliberately remains
based on normalized implementation and canonical public-interface identities,
so harmless trivia can fail the raw-source fast path without invalidating
downstream modules. Windows and Linux Stage2 verify cache encoding, atomic
persistence, reload, preflight, and Stage1/Stage2 planner parity. This is
checkpoint 6/10;
the formal roadmap remains **51.5/60 (85.8%)** until ordinary builds consume
reusable artifacts.

D207C2A completes the stable typed-IR envelope half of the second slice. The
artifact format excludes session-local `typeId`, zig-zag encodes signed IR
metadata, rewrites module references to path hashes, rewrites IR references to
module-local ordinals, sorts module records canonically, and protects the full
payload with length and checksum validation. Example 435 proves validation,
source-input-order independence, and corruption rejection. Stage1 and Stage2
also execute the same three-module artifact path and agree on its result.

The structural-type half is complete: schema 2 carries canonical type records,
stable structural hashes, explicit optional references, and a decoder that
rebuilds fresh type and IR arenas. D207C is therefore **2/5 (40%)**, this is
checkpoint 8/10, and the formal roadmap remains **51.5/60 (85.8%)**.

D207C3A completes the artifact-contract half of the codegen-unit slice. Schema
1 stores exactly one shared prefix and suffix plus module fragments ordered by
the stable module-path hash with an explicit unsigned comparison. Every module
record retains its full canonical namespace bytes and recomputes the path hash,
so equal lookup hashes cannot silently identify different modules. Compiler,
target, and configuration identities are part of the envelope. LLVM fragment
bytes are packed eight per `UInt64`, preserving output bytes without the 8x
storage expansion of one byte per word; declared byte/word lengths, zero
padding, per-fragment checksums, the envelope checksum, unit cardinality, and
canonical order are all validated before decode or merge.

Example 436 proves source-order-independent serialization, exact unaligned
fragment concatenation, decode, context mismatch rejection, path-hash collision
rejection, and corruption rejection. Windows Stage2 and Linux Stage2 execute
the same `llvm-codegen-units` path through Stage1 and Stage2 and agree on
`codegen units = 0,2,6`. This is deliberately not yet ordinary-build reuse: the
current emitter still writes a monolithic stream. D207C is now **2.5/5 (50%)**,
the periodic Stage3 cadence advances to **9/10**, and the formal roadmap remains
**47 complete, 9 partial, 4 missing: 51.5/60 (85.8%)** until real emitter
fragments are loaded and merged by `sollang build`.

D207C3B routes the production C# LLVM emitter through one shared prefix, a
stable-hash-ordered unit for every module that emits user functions, and one
shared suffix. String globals carry a unit-stable identity and SSA temporaries
restart at each function, so a fragment no longer depends on which earlier
module happened to emit first. A completely warm generation returns the cached
fragments before running the emitter; a partial generation emits only invalid
modules and canonically merges them with validated old fragments.

Normal `sollang build` now retains exact source bytes per discovered module,
computes canonical public-declaration and transitive-import fingerprints, adds
the concrete specialization inventory, compiler MVID, target, and optimization
configuration to its keys, and stores the resulting fragments in a disposable
schema-1 packed-`UInt64` generation beside the output—the same magic, record
layout, little-endian byte packing, and checksum contract as D207C3A. The reader bounds every length, requires
one prefix and suffix plus strictly ordered unique modules, validates UTF-8,
the schema-1 per-fragment and whole-envelope checksums, and reports corruption
before rebuilding. Publication uses a same-directory write-through temporary
file and atomic replacement only after linking succeeds.

The focused verifier proves Windows and Linux cold `0/5`, warm `5/5`, body-only
dependency edits with the unaffected consumer and root reused as `2/5`, public
interface edits with transitive invalidation `0/5`, target isolation, explicit
corruption rejection, recovery, native execution, and clean-vs-cached LLVM byte
identity. The Windows and Linux full suites each pass 573/573. This completes
the production codegen-unit slice: D207C is **3/5 (60%)**. Windows Stage2 passes
6/6 at 10,553,582 LLVM bytes and Linux Stage2 passes 5/5 at 10,550,185 bytes.

The checkpoint-10 Stage3 run also made the complete compiler input set an
explicit invariant. Stage2 formerly included `selfhost/runtime/file.slg` while
Stage3's duplicated list omitted it, leaving `sys.file.openWrite/openRead`
unresolved and their following enum matches without subject IR. Windows Stage2,
Linux Stage2, and Stage3 now consume the same
`selfhost-compiler-runtime.sources.txt` manifest and include that manifest in
freshness checks. Stage3 reaches the identical 10,553,582-byte compiler with
normalized SHA-256
`21A504DB039BE52029D594580D3EA4B9002AB17C5C45B7C36EDD52BD7BF349E6`, so the
periodic cadence resets from **10/10** to **0/10**. The module/interface-cache gate
advances from missing to partial, so the formal roadmap is now **47 complete,
10 partial, 3 missing: 52.0/60 (86.7%)**. Frontend raw-source/typed-IR reuse in
ordinary builds remains the next integration boundary.

D207C4A adds the first pre-semantic production fast path. After a successful
link, the bootstrap compiler writes a bounded, checksummed `.sources`
generation containing the exact ordered roots, project manifests, standard
library, and discovered user-source bytes. The snapshot also stores the SHA-256
digest of its matching `.cgu` generation, so an interrupted publication cannot
pair source identity from one generation with LLVM units from another. Reads
stream source comparison instead of materializing a second compiler-sized copy,
validate source sets and build context, and accept cached LLVM only after both
envelopes and their generation binding pass.

On an exact hit, normal `sollang build` bypasses source loading, lexing, parsing,
semantic analysis, specialization discovery, and LLVM emission, then directly
merges and links the validated units. A changed source misses this whole-build
fast path and returns to the existing frontend plus partial codegen-unit reuse;
this checkpoint does not yet claim module-granular typed-IR rehydration.
`verify-codegen-cache.ps1` now proves seven Windows and seven Linux states:
cold, exact warm, body-only, public-interface, corrupt frontend snapshot,
corrupt/mismatched codegen generation, and repaired exact warm, including native
output and LLVM byte identity. Both full suites pass 573/573, Windows Stage2
passes 6/6 at 10,553,582 bytes, and Linux Stage2 passes 5/5 at 10,550,185 bytes.

This completes half of the fourth slice, so D207C is **3.5/5 (70%)**. It is
checkpoint **1/10** after the latest Stage3 fixed point; Stage3 is intentionally
deferred. Because partial source changes still rebuild the complete frontend,
the formal roadmap remains **47 complete, 10 partial, 3 missing: 52.0/60
(86.7%)**.

D207C4B closes the exact-build artifact chain. A fixed-size checksummed
`.product` generation records compiler/target/configuration identity and the
SHA-256 digests of the validated `.sources`, `.cgu`, and final target artifact.
When all three generations match, `sollang build` is a true no-op build: it
streams identity checks and skips the frontend, LLVM emission, and linker. If
only the executable is missing or changed, the compiler relinks from validated
LLVM units and atomically repairs the product generation without repeating
semantic analysis.

The focused verifier now covers eight states on both Windows and Linux,
including output corruption followed by frontend-free relinking and a repaired
no-op build. Exact warm compilation of the 13-source focused project takes
54.7 ms on the verification machine. Both full suites pass 573/573, Windows
Stage2 passes 6/6 at 10,553,582 bytes, and Linux Stage2 passes 5/5 at
10,550,185 bytes. This completes the fourth D207C slice, so D207C is **4/5
(80%)** and the Stage3 cadence advances to **2/10**. Partial
typed-IR rehydration for changed-source builds remains the final slice; the
formal roadmap therefore remains **47 complete, 10 partial, 3 missing: 52.0/60
(86.7%)**.

D207C5A establishes the cross-session identity boundary required by partial
typed-IR reuse. Semantic types are now named structurally rather than by the
current process's `TypeId`; functions include their module, signature,
ownership, generic specialization, associated types, effects, and enclosing
local-function identity; resolved generic call sites use a deterministic
owner-local ordinal instead of an AST object address. The production codegen
interface hash now consumes the same stable function identity and correctly
excludes private structs and enums from downstream invalidation.

Normal builds publish a bounded, canonical, SHA-256-protected `.semantic`
generation after a successful link. It is bound to compiler, target, and
configuration identity, rejects invalid lengths, ordering, duplicates, UTF-8,
or checksums, and atomically replaces the previous generation. On a changed
source build the compiler maps the previous function and call-site identities
onto the newly constructed semantic session. This is intentionally reported as
`mapped`, not `reused`: semantic analysis still runs, and typed-IR bodies are
not yet decoded before that phase.

The focused invalidation matrix now has nine states for each target. It adds a
private-declaration edit that retains the two unaffected consumer units, proves
semantic-generation corruption rejection and repair, and preserves the existing
public-interface transitive invalidation and clean/cached LLVM equality checks.
Windows and Linux pass 9/9, both full suites pass 573/573, Windows Stage2 passes
6/6 at 10,553,582 bytes, and Linux Stage2 passes 5/5 at 10,550,185 bytes.

This completes the identity/artifact half of the final slice: D207C is
**4.25/5 (85%)** and the Stage3 cadence advances to **3/10**. Actual module
typed-IR payload serialization and pre-semantic rehydration remain pending, so
the formal roadmap remains **47 complete, 10 partial, 3 missing: 52.0/60
(86.7%)**.

D207C5B turns that identity bridge into the first real partial-source semantic
reuse path. `SemanticCompiler` now separates declaration construction from
function-body validation. Before validation it compares a stable SHA-256
fingerprint of every visible struct, enum, trait, and function declaration with
the previous generation. The cache independently hashes each module's exact
ordered source set. Only a function whose module bytes are unchanged and whose
visible declaration universe is green may restore its binding and captured-
binding type maps and skip `ValidateUserFunction`.

The `.semantic` schema is now version 2. Besides canonical functions and
resolved-call mappings it stores the declaration fingerprint, module source
digests, and per-function semantic binding payloads. Every type in those maps is
structural and is re-interned into the fresh session's type table. The reader
retains strict count, length, order, uniqueness, UTF-8, compiler/target/config,
and whole-file SHA-256 validation. Publication remains write-through and atomic
after a successful link.

Functions with local functions or resolved generic/specialized call sites still
run normal validation because their AST-object relationships are not yet
rehydrated. Main-scope bindings also remain fresh. This makes the reuse count
honest and keeps the final slice open instead of treating a safe subset as the
finished architecture.

The focused Windows/Linux matrix now has ten states. A body-only dependency edit
must reuse at least one unchanged semantic function; corruption must reject the
semantic generation; a private declaration edit/removal preserves reuse; and a
public-interface edit must reuse zero semantic functions. Existing codegen,
frontend, product, execution, and byte-equality checks remain. Both targets pass
10/10, both full suites pass 573/573, Windows Stage2 passes 6/6 at 10,553,582
bytes, and Linux Stage2 passes 5/5 at 10,550,185 bytes.

D207C is now **4.5/5 (90%)** and the Stage3 cadence advances to **4/10**. The
formal roadmap remains **47 complete, 10 partial, 3 missing: 52.0/60 (86.7%)**
until local/generic/main semantic state is fully reusable.

D207C5C expands schema 3 to local-function trees and main-scope bindings.
Local records use their enclosing function's stable identity, and a top-level
function is reusable only when its complete recursively nested tree is present.
The compiler first collects the whole tree without mutating the fresh session,
then restores every binding and captured-binding map together. A missing child
therefore falls back to normal validation for the entire parent.

The semantic generation may also carry the executable module identity and its
stable main binding map. Main is reusable only when that exact source module and
the visible declaration universe are green and the previous main had no
resolved generic/specialized call-site state. Storage placement still runs over
the current AST, independently preserving stack and lifetime planning.

The focused project now embeds a local function in its unchanged consumer. A
body-only provider edit restores that parent/local tree and main; the measured
Linux build reuses **43/45 semantic functions plus main** and **2/5 LLVM
units**. Public-interface edits rebuild both functions and main. The two
ten-state matrices, both 573/573 full suites, Windows Stage2 6/6 at 10,553,582
bytes, and Linux Stage2 5/5 at 10,550,185 bytes all pass.

D207C is now **4.75/5 (95%)** and the Stage3 cadence advances to **5/10**.
Generic/specialized call-site reconstruction remains the last D207C boundary,
so the formal roadmap stays **47 complete, 10 partial, 3 missing: 52.0/60
(86.7%)**.

D207C5D closes that boundary with schema 4. Stable call-site identity is now
assigned before semantic resolution by walking every potential call in source
order: ordinary calls, explicit type applications, fluent flow targets, and
block-function calls. The identity therefore does not depend on which nodes a
particular semantic session happened to resolve.

The semantic generation stores each resolved identity edge plus a canonical
specialization recipe. A recipe either names the current generic template and
its structural type/value arguments, or carries the concrete signature of a
synthesized runtime specialization such as `readAt<UInt16>`. Rehydration
re-interns every structural type, rebuilds the current-session `BoundFunction`,
verifies its complete stable identity, restores user-specialization bindings,
and follows nested generic-template call edges. Function trees and main perform
this work transactionally; a missing node, recipe, target, or invalid type
rolls back the attempted call state and runs normal validation.

The ten-state focused matrix now contains a local function, a normal type
generic, a nested type generic, a compile-time value generic, and a synthesized
file scalar specialization. After a provider body-only edit, Windows restores
**44/45 functions plus main and 5/5 call sites**; Linux restores **44/46 plus
main and 5/5 call sites**. Corruption, private/public declaration changes,
target isolation, executable output, and clean/cached LLVM equality remain
covered. Both full suites pass 573/573. Windows Stage2 passes 6/6 at 10,553,582
bytes and Linux Stage2 passes 5/5 at 10,550,185 bytes.

D207C is now **5/5 (100%)**. This is periodic Stage3 checkpoint **6/10**, so a
new Stage3 run is not due yet. Completing the module/interface incremental-build
gate moves the formal roadmap to **48 complete, 9 partial, 3 missing: 52.5/60
(87.5%)**. The reference backend still emits LLVM from the current AST plus
rehydrated semantic maps; this result does not claim a separate serialized
monolithic typed-AST representation.

## Confined Local Workspaces (D208A)

D208A adds deterministic, path-only local workspaces through
`sollang.workspace`. A workspace explicitly lists member project directories;
each member's `sollang.project` remains authoritative for its package name,
products, and dependency edges. Member paths are relative to and confined by
the workspace root, package names must be unique, and every selected package's
dependency closure must consist entirely of declared members. The CLI supports
`--workspace` and `--package`, ancestor discovery from a member directory, and
stable output placement under `build/<target>/<package>/<product>`.

The self-host compiler now includes `selfhost/workspace.slg`, which tokenizes
and validates the same language-shaped manifest boundary. Examples 437 and 438
cover reference builds and the self-host parser, while six diagnostics cover
selection, escaping roots, undeclared dependencies, duplicate package names,
unknown fields, and empty member sets. Native and wasm test products also use
separate platform-qualified stems so cached native verification cannot consume
a stale wasm LLVM temporary file.

Both Windows and Linux full suites pass **581/581**. Windows Stage2 passes
**6/6** at **10,590,477 LLVM bytes**, and Linux Stage2 passes **5/5** at
**10,587,080 LLVM bytes**. The periodic Stage3 cadence advances to **7/10**, so
Stage3 is not due at this checkpoint. D208 distributable package work is
**1/5 (20%)**: local workspaces are complete; semantic versions, registries,
Git sources, and a lock file remain. Therefore the formal roadmap stays
**48 complete, 9 partial, 3 missing: 52.5/60 (87.5%)**, with **7.5 equivalent
gates** remaining.

The design combines Cargo's explicit workspace membership and shared build
context, SwiftPM's package/product dependency separation, and Zig's explicit
deterministic build graph. Glob discovery and remote dependency syntax remain
deliberately outside D208A.

References:

- [Cargo workspaces](https://doc.rust-lang.org/cargo/reference/workspaces.html)
- [SwiftPM package dependencies](https://docs.swift.org/swiftpm/documentation/packagemanagerdocs/addingdependencies/)
- [Zig build system](https://ziglang.org/learn/build-system/)

## Versioned Local Packages And Workspace Lock (D208B)

D208B gives every project a SemVer identity, checks local dependency version
requirements, and introduces one deterministic `sollang.lock` per workspace.
The lock records the complete workspace member graph in sorted `name@version`
order, exact dependency identities, and normalized local `path:` sources.
`sollang resolve` writes it explicitly, workspace builds refresh it, and
`--locked` rejects drift. Local source bytes remain governed by source control;
remote content hashes are deliberately not claimed by this checkpoint.

The native self-host package now contains independent SemVer/requirement and
lock-manifest parsers. Examples 440 and 441 cover valid parsing, leading-zero
and prerelease rejection, release precedence, compatible/comparator ranges,
canonical lock counts, invalid package versions, and duplicate IDs. Reference
diagnostics additionally cover missing/invalid project versions, malformed and
unsatisfied dependency requirements, missing dependency paths, and stale locks.

D208 distributable package work is now **3/5 (60%)**: local workspaces,
semantic versions, and deterministic locks are implemented; registries and
content-pinned Git sources remain. The formal roadmap stays **48 complete,
9 partial, 3 missing: 52.5/60 (87.5%)** because D208 remains a partial gate.
Release build is warning/error-free. Windows and Linux each pass **590/590**;
Windows Stage2 passes **6/6** at **10,752,017 LLVM text bytes**, and Linux
Stage2 passes **5/5** at **10,748,620 LLVM text bytes**. The Windows native
Stage2 executable is about **1.4 MiB**; the roughly 10.8 MB figures are emitted
LLVM text rather than executable size. Periodic Stage3 cadence advances to
**8/10**, so Stage3 is not due at this checkpoint.

## Content-Pinned Git Package Sources (D208C)

D208C adds `{ git, rev, version }` dependencies. Full 40- or 64-digit revisions
are required, the fetched commit must match exactly, and the materialized tree
is independently pinned by a deterministic SHA-256 digest in lock format 2.
The cache contains source bytes without `.git`, rejects symbolic links, and
detects mutation before compilation. The self-host lock parser accepts the same
Git source and checksum contract. Example 442 builds through a freshly-created
local Git repository, including a confined transitive path package that inherits
the Git identity, while example 441 validates the self-host lock shape.

D208 distribution work is now **4/5 (80%)**: local workspaces, semantic
versions, deterministic locks, and content-pinned Git sources are implemented;
registries remain. The formal roadmap remains **48 complete, 9 partial,
3 missing: 52.5/60 (87.5%)** until the D208 gate closes. Validation results
are: zero-warning Release build, Windows/Linux full suites at **594/594**,
Windows Stage2 **6/6** at **10,772,923 bytes**, and Linux Stage2 **5/5** at
**10,769,526 bytes**. The periodic Stage3 cadence is now **9/10**, so Stage3 is
still deferred.

## Static Checksummed Package Registry (D208D)

D208D completes the package-distribution gate with a read-only, static HTTPS
registry protocol. `{ registry, version }` dependencies read one language-shaped
`v1/<package>/index.slg`, select the highest compatible non-yanked stable release,
and verify the exact bytes of `v1/<package>/<version>.zip` with SHA-256 before
safe bounded extraction. Prereleases require an explicitly prerelease-shaped
constraint.

Normal builds preserve compatible registry pins from lock format 2, `--locked`
rejects missing or incompatible pins, and only `sollang resolve` searches the
index for a newer compatible release. The content-addressed cache verifies both
the archive and extracted tree, while path confinement, symbolic-link rejection,
portable path collision checks, entry and byte limits, and atomic materialization
protect the compiler boundary. The self-hosted registry parser independently
performs SemVer selection and validates registry lock sources.

Examples 443 and 444 prove newest-compatible resolution, yanked/prerelease
exclusion, explicit update, checksum rejection, and a lock-preserved older
version. Example 445 proves self-host selection; example 441 covers registry
lock parsing. The normative protocol and tracked limitations are in
`docs/PACKAGE_REGISTRY.md`.

D208 distribution work is **5/5 (100%)**. This promotes the modules/builds gate
from partial to complete, moving the formal roadmap to **49 complete, 8 partial,
3 missing: 53/60 (88.3%)**, with **7 equivalent gates remaining**. Validation
is zero-warning Release build, Windows/Linux full suites at **600/600**,
Windows Stage2 **6/6** at **10,851,049 bytes**, and Linux Stage2 **5/5** at
**10,847,652 bytes**. The required Stage3 compiler emits **10,851,049 bytes**
and matches Stage2 at normalized SHA-256
`0A8E471CCCC2A97895537FB6279DC84579D052AD4AFECBAA03BDFBA4794FE0DD`.
The periodic cadence therefore resets from **10/10** to **0/10**.

## Move-Aware Owned Array Element Replacement (D209A)

D209A turns a mutable dynamic-array index into a real owned place. For an owned
element type, `replacement! => values![index]` now evaluates the replacement,
checks the index, loads and recursively drops the previous element, stores the
new value, and marks the source binding transferred. Fresh struct/container
expressions follow the same path. Reusing the containing array as its own
replacement remains a compile-time error.

The reference backend performs the operation from the canonical concrete
element type. The self-host typed IR records index replacement as a move site,
its expression/type-ID passes retain the assignment's `Unit` type, and its LLVM
backend calls the specialized `sollang_drop_t<ID>` witness before the store.
Main-entry struct construction was added to the self-host emitter because the
new place operation exposed a function/main parity gap.

Examples 446 and 447 execute the reference and self-host forms on Windows and
Linux. `scripts/verify-owned-indexed-replacement.ps1` additionally instruments
both Linux modules with AddressSanitizer and UndefinedBehaviorSanitizer and
passes leak, double-free, and UB detection. Release builds have zero warnings
and errors; both full suites pass **602/602**. Windows Stage2 passes **6/6** at
**10,904,470 LLVM bytes** and Linux Stage2 passes **5/5** at **10,901,073
bytes**. This closes the array-replacement slice, while generic dictionary
replacement and the broader borrow/container gates remain. Formal progress is
therefore still **53/60 (88.3%)**, and the periodic Stage3 cadence advances to
**1/10**.

Research basis: Rust assignment drops the old place before moving or copying
the new value, while Mojo requires unique ownership transfer and deterministic
destruction. Sollang applies those rules statically without a per-element
runtime witness table.

- [Rust assignment expressions](https://doc.rust-lang.org/stable/reference/expressions/operator-expr.html#assignment-expressions)
- [Rust `mem::replace`](https://doc.rust-lang.org/std/mem/fn.replace.html)
- [Mojo ownership](https://docs.modular.com/mojo/manual/values/ownership/)

## Move-Aware Owned Dictionary Value Replacement (D209B)

D209B makes `replacement! => values![key]` an occupied-entry replacement for
generic dictionaries. The replacement is evaluated before key lookup. A
missing key traps; a present key stays canonical while its previous owned value
is recursively dropped and replaced. The replacement source then becomes
unavailable. Contextual struct keys and values use their inferred concrete
types in the reference compiler.

Generic `put` now uses the same replacement primitive for an occupied entry.
It preserves the stored key, destroys the old value, and installs the new
value. Its backend is also ready to destroy an incoming equal key when the
admitted concrete key type owns storage; fully owned key admission remains a
later gate. A vacant entry instead receives incoming key/value storage. This
distinction prevents both the former raw-overwrite leak and accidental double
ownership.

The reference backend performs SwissTable lookup and writes only the value
field. The self-host backend performs equivalent checked replacement over its
canonical parallel key/value representation and calls the concrete
`sollang_drop_t<ID>` witness. Examples 448-450 and two move diagnostics cover
reference replacement, self-host lowering, `put` update/insertion, and source
invalidation. `scripts/verify-owned-dictionary-replacement.ps1` runs all three
Linux products under ASan/UBSan with leak detection.

Release builds have zero warnings and errors; Windows/Linux full suites pass
**607/607**. Windows Stage2 passes **6/6** at **10,962,922 LLVM bytes** and
Linux Stage2 passes **5/5** at **10,959,525 bytes**. Owned-key generality and
wider path-sensitive container borrows still keep the formal roadmap at
**53/60 (88.3%)**, with **7 equivalent gates remaining**. The periodic Stage3
cadence advances to **2/10**.

Research basis: Rust models occupied and vacant hash-map entries separately,
while Swift returns the displaced value from explicit dictionary replacement.
Sollang keeps its concise index/`put` syntax while making the same distinction
inside static ownership lowering.

- [Rust `HashMap::insert`](https://doc.rust-lang.org/std/collections/hash_map/struct.HashMap.html#method.insert)
- [Rust `HashMap::Entry`](https://doc.rust-lang.org/stable/std/collections/hash_map/enum.Entry.html)
- [Swift `Dictionary.updateValue`](https://developer.apple.com/documentation/swift/dictionary/updatevalue(_:forkey:))

## Borrowed Equality For Owned Dictionary Keys (D210A)

D210A admits local nominal dictionary keys that own recursive storage once the
type provides valid `Hash` and `Eq` impls. Literal/insertion paths move the
stored key exactly once; lookup, indexed replacement, and `take` invoke
readonly `Eq.eq` without consuming stored or query keys. `take` destroys the
removed stored key and preserves the independent query owner.

The reference compiler now tracks transfers recursively through aggregate
literals. The self-host compiler derives exact synthetic-`self` types from impl
targets, lowers impl methods, and emits canonical key layout plus Eq calls in
ordinary functions, main, and nested regions. Newline-tolerant impl parsing and
qualified-name filtering prevent a parsed impl declaration from shadowing its
target type. Examples 451-453 and two diagnostics cover these paths;
`scripts/verify-owned-dictionary-keys.ps1` passes ASan/UBSan leak, double-free,
use-after-free, and UB checks.

Validation is zero-warning Release build, Windows/Linux full suites at
**612/612**, Windows Stage2 **6/6** at **11,249,244 bytes**, and Linux Stage2
**5/5** at **11,245,847 bytes**. This is Stage3 cadence **3/10**. Formal
progress remains **49 complete, 8 partial, 3 missing: 53/60 (88.3%)**, with
**7 equivalent gates remaining**: imported composite-key inference and wider
path-sensitive container borrows are still open.

Research basis: Rust separates owned map storage from borrowed lookup through
`Borrow`, while Swift requires custom dictionary keys to satisfy `Hashable`
and equality coherently. Sollang monomorphizes those witnesses without runtime
type erasure.

- [Rust `Borrow`](https://doc.rust-lang.org/std/borrow/trait.Borrow.html)
- [Swift `Hashable`](https://developer.apple.com/documentation/swift/hashable)
- [Swift `Dictionary`](https://developer.apple.com/documentation/swift/dictionary)

## Canonical Imported Dictionary Key Types (D210B)

D210B closes the imported composite-key inference boundary left by D210A.
Typed nonempty dictionaries and function contracts accept qualified recursive
annotations such as `{keys.OwnedKey: Int}`. Both the C# and generated parsers
preserve the explicit type context; self-host expression inference constructs
the dictionary ID from canonical `TypeAnnotation` references instead of token
ordinals, and legacy composite projections reuse the prepared type arena.

Reference and self-host LLVM resolve `Hash`/`Eq` implementations in the key's
defining module, retain the imported nominal layout, borrow lookup keys, and
recursively destroy only transferred stored keys. Examples 454 and 455 plus
the expanded ASan/UBSan gate cover typed literals, typed function input,
lookup, replacement, `take`, and drop across the module boundary.

Validation is a zero-warning Release build, Windows/Linux full suites at
**614/614**, Windows Stage2 **6/6** at **11,278,354 bytes**, and Linux Stage2
**5/5** at **11,274,957 bytes**. The periodic Stage3 cadence is **4/10**.
Formal progress remains **49 complete, 8 partial, 3 missing: 53/60 (88.3%)**;
this was a tracked sub-gate inside existing module/type/container capability,
not a separate one of the 60 top-level gates.

Research basis: Rust gives imported items canonical identities independent of
their source aliases and permits paths in type syntax; Swift composes custom
dictionary key types across module APIs and requires `Hashable` conformance.
Sollang uses the same identity boundary with dot-qualified paths and static
witness specialization.

- [Rust paths](https://doc.rust-lang.org/reference/paths.html)
- [Rust implementation coherence](https://doc.rust-lang.org/stable/reference/items/implementations.html)
- [Swift dictionary types](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/types/)
- [Swift access control](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/accesscontrol/)

## Call-Scoped Indexed Place Borrows (D211A)

D211A extends the existing array-element borrow boundary to recursively owned
dictionary values. `symbols![key] -> inspect` and
`inspect(symbols![key])` treat the indexed value as a readonly place borrow for
that direct call only. The dictionary remains the sole owner, the callee gains
no drop obligation, and replacement or `take` remains valid after the call.
Binding, returning, storing, or mutating through the indexed result is rejected;
`take` remains the explicit ownership-transfer operation.

The reference semantic compiler admits the borrow only while checking a
default readonly call input. Reference and self-host LLVM load the aggregate
for that ABI without creating a second owner. The new nominal-struct case also
exposed an LLVM type-ordering requirement: drop glue that performs a sized GEP
through a struct containing `Text` needs `%sollang.text` complete first. The
self-host emitter now moves that declaration early only for the affected drop
closure, preserving every unrelated LLVM snapshot.

Examples 456 and 457 cover reference and self-host LLVM compilation, repeated
borrows, replacement, extraction, and deterministic cleanup. The new escape
diagnostic rejects a bound indexed owner, and
`scripts/verify-call-scoped-container-borrows.ps1` instruments both backends
with ASan/UBSan leak, double-free, use-after-free, and undefined-behavior
checks.

Validation is a zero-warning Release build, Windows/Linux full suites at
**617/617**, Windows Stage2 **6/6** at **11,285,200 bytes**, and Linux Stage2
**5/5** at **11,281,803 bytes**. The periodic Stage3 cadence advances to
**5/10**. Formal progress remains **49 complete, 8 partial, 3 missing: 53/60
(88.3%)** because stored and returned references plus full path-sensitive
conflict analysis remain open within the existing ownership/storage gate.

Research basis: Rust defines indexing as a place expression and makes borrowing
explicitly refer to a place without transferring its value. Rust's borrow
splitting discussion shows why disjoint paths require deeper structural
analysis, while Mojo's origins tie a reference lifetime to owned storage.
Sollang takes the conservative call-lifetime subset now and keeps wider
reference escape for the next ownership gate.

- [Rust place expressions](https://doc.rust-lang.org/stable/reference/expressions.html#place-expressions-and-value-expressions)
- [Rust borrow expressions](https://doc.rust-lang.org/reference/expressions/operator-expr.html#borrow-operators)
- [Rust field borrowing](https://doc.rust-lang.org/reference/expressions/field-expr.html#borrowing)
- [Rust borrow splitting](https://doc.rust-lang.org/nomicon/borrow-splitting.html)
- [Mojo lifetimes, origins, and references](https://docs.modular.com/mojo/manual/values/lifetimes/)

## Projected Indexed Place Borrows (D211B)

D211B preserves the D211A place-borrow context through field and nested-index
projections. A readonly call may therefore consume `symbols![key].payload`,
`symbols![key].name`, or `(symbols![key].payload)[index]` without copying the
recursively owned dictionary value or transferring any drop obligation. The
borrow still ends at the direct call; binding a projected owner remains an
escape error, and `take` remains the explicit ownership-transfer operation.

The reference semantic compiler propagates the call-only admission context
through `FieldAccessExpression` and nested `IndexExpression` nodes. The
reference LLVM emitter now discovers drop helpers from dictionary key/value
storage only when those types recursively own data, preserving allocation-free
stack promotion for scalar containers. The self-host main emitter gains the
dynamic-array branch that its ordinary-function index emitter already had, so
a projected array element is scheduled and defined before its consuming call.

Examples 458 and 459 cover dictionary-to-struct-to-array projections in the
reference and self-host paths. The projection-escape diagnostic preserves the
non-escaping boundary. The expanded
`scripts/verify-call-scoped-container-borrows.ps1` validates all four D211
products on Linux and instruments all four products with ASan/UBSan leak,
double-free, use-after-free, and undefined-behavior checks.

Validation is a zero-warning Release build, Windows/Linux full suites at
**620/620**, Windows Stage2 **6/6** at **11,297,708 bytes**, and Linux Stage2
**5/5** at **11,294,311 bytes**. The periodic Stage3 cadence advances to
**6/10**. Formal progress remains **49 complete, 8 partial, 3 missing: 53/60
(88.3%)** because escaping references and simultaneous path-sensitive borrow
conflicts remain open inside the existing ownership/storage gate.

Research basis: Rust field expressions borrow their base place and its borrow
splitting rules distinguish disjoint projections. Mojo origins similarly carry
the lifetime source through derived references. Sollang keeps the narrower
direct-call lifetime while now retaining the complete projected place path.

- [Rust field borrowing](https://doc.rust-lang.org/reference/expressions/field-expr.html#borrowing)
- [Rust place expressions](https://doc.rust-lang.org/stable/reference/expressions.html#place-expressions-and-value-expressions)
- [Rust borrow splitting](https://doc.rust-lang.org/nomicon/borrow-splitting.html)
- [Mojo lifetimes, origins, and references](https://docs.modular.com/mojo/manual/values/lifetimes/)

## Mixed Postfix Chain Parity (D211C)

D211C makes field and index suffixes one left-associated chain in both compiler
implementations. The parser now accepts arbitrary dot/index ordering such as
`symbols![1].payload![0]` and `branches![1].items![0].value`; no grouping
parentheses are required between suffix kinds. Pure qualified/member paths keep
their compact existing AST representation, while chains containing an index
are normalized by the self-host frontend into explicit left-associated prefix
nodes. This preserves existing symbol-path behavior and gives typed IR the same
tree shape as the reference compiler.

Examples 460 and 461 cover index-to-field-to-index and
index-to-field-to-index-to-field evaluation in the reference and self-host LLVM
paths. The call-borrow verifier now runs all six D211 products and instruments
the three reference modules plus three self-host modules with ASan/UBSan.

The first Stage2 attempt also exposed a pre-existing self-host scheduling edge:
using `array[(array -> len) - 1]` directly as another call's argument could emit
the index conversion after its use. The AST normalizer now retains its last
synthetic node while constructing the chain instead of recovering it through
that nested expression. This keeps Stage1 and Stage2 LLVM valid and identical
without introducing a hidden copy.

Validation is a zero-warning Release build, Windows/Linux full suites at
**622/622**, Windows Stage2 **6/6** at **11,313,892 LLVM text bytes** with a
**1,570,304-byte native executable**, and Linux Stage2 **5/5** at **11,310,495
LLVM text bytes** with a **3,116,392-byte native executable**. The periodic
Stage3 cadence advances to **7/10**. Formal progress remains **49 complete, 8
partial, 3 missing: 53/60 (88.3%)** because this closes parser/self-host parity
inside an existing gate; escaping references and simultaneous path-sensitive
borrow conflicts remain open.

Research basis: Kotlin models a postfix expression as one primary followed by
repeated postfix suffixes. Rust likewise gives field and index expressions the
same high precedence and evaluates nested expressions left to right. Sollang
adopts that uniform chain while retaining its own `![index]` mutable-place
spelling and postfix `?` propagation.

- [Kotlin expressions specification](https://kotlinlang.org/spec/expressions.html)
- [Kotlin grammar](https://kotlinlang.org/grammar/)
- [Rust expression precedence and order](https://doc.rust-lang.org/stable/reference/expressions.html)
- [Rust field expressions](https://doc.rust-lang.org/reference/expressions/field-expr.html)
- [Rust array and slice indexing](https://doc.rust-lang.org/reference/expressions/array-expr.html)

## Concrete Index Operand Dependencies (D212A)

D212A closes the general self-host failure exposed while finishing D211C. An
index such as `values![(values! -> len) - 1] -> last` lowered its computed index
under a transparent flow wrapper. The index node referred to that wrapper even
though wrappers emit no runtime value, so LLVM could contain a GEP using an
undefined `%vN` while the real subtraction appeared later.

The self-host typed-IR finalization pass now resolves both source and index
operands of every index node through any transparent value wrappers. It selects
the concrete non-Unit value child, repeats through nested wrappers, and stores
that relocatable IR index as the dependency. The existing scheduler then emits
the subtraction before the GEP naturally; the LLVM text emitter needs no
source-specific ordering exception. The reference compiler already recursively
evaluated the same expression in the required order and remains the differential
contract.

Examples 462-464 cover reference execution, self-host LLVM assembly/execution,
and the typed-IR edge itself: the index operand changes from the undefined
wrapper node to the concrete binary node. Validation is a zero-warning Release
build, Windows/Linux full suites at **625/625**, Windows Stage2 **6/6** at
**11,325,985 LLVM text bytes** with a **1,570,816-byte native executable**, and
Linux Stage2 **5/5** at **11,322,588 LLVM text bytes** with a **3,120,488-byte
native executable**. The periodic Stage3 cadence advances to **8/10**. Formal
progress remains **49 complete, 8 partial, 3 missing: 53/60 (88.3%)** because
this strengthens the existing self-host compiler gate rather than completing a
remaining top-level ownership or tooling gate.

Research basis: LLVM requires an SSA definition to dominate every use. Rust
specifies recursive operand evaluation from inner expressions outward and
left-to-right order for siblings. Kotlin likewise evaluates a call receiver and
then explicit arguments left to right. Sollang records those source semantics
in concrete typed-IR dependency edges before emitting LLVM.

- [LLVM language reference: well-formed SSA](https://llvm.org/docs/LangRef.html#well-formedness)
- [Rust operand evaluation order](https://doc.rust-lang.org/stable/reference/expressions.html#evaluation-order-of-operands)
- [Kotlin function-call evaluation](https://kotlinlang.org/spec/expressions.html#function-calls-and-property-access)

## Inferred Borrowed Text Return Origins (D213A)

D213A introduces the first safe reference that survives a call. A direct Text
slice returned from one default SourceText input inherits that input's symbolic
origin without adding lifetime syntax. The reference compiler records the
caller owner on the result binding and conservatively freezes that owner until
scope exit. A later move, aggregate transfer, replacement, or mutation is a
compile-time error; returning a borrowed Text inside an aggregate remains an
escape error.

The self-host ownership analyzer reconstructs the same relationship from flat
typed IR and emits diagnostic 21 when a move event targets the recorded owner.
The self-host LLVM backend executes the returned two-word view with no copy or
runtime ownership machinery. Examples 465, 466, and 468 prove reference
execution, self-host LLVM assembly/execution, and self-host conflict analysis.

Validation is a zero-warning Release build, Windows/Linux full suites at
**628/628**, Windows Stage2 **6/6** at **11,325,985 LLVM text bytes** with a
**1,570,816-byte native executable**, and Linux Stage2 **5/5** at **11,322,588
LLVM text bytes** with a **3,120,488-byte native executable**. The Stage3 cadence
advances to **9/10**. Formal progress remains **49 complete, 8 partial, 3
missing: 53/60 (88.3%)**. Multiple input origins, origin unions, last-use
shortening, projected simultaneous borrow conflicts, and standalone Stage2
driver enforcement remain open.

Research basis:

- [Rust lifetime elision](https://doc.rust-lang.org/reference/lifetime-elision.html)
- [Mojo lifetimes, origins, and references](https://mojolang.org/docs/manual/values/lifetimes/)
- [Swift memory safety](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/memorysafety/)

## Production Ownership-Diagnostic Gate (D213C)

D213C connects borrowed-Text diagnostic E21 to the native compiler's checked
file-list and source-root entry points. Ownership validation runs before target
headers or LLVM are printed. A live borrowed view followed by an owner move now
produces E21 and a nonzero process exit in both Stage1 and Stage2; valid programs
retain the existing LLVM-only output contract. E17-E20 remain nonfatal until
their path-sensitive false positives are resolved.

The driver uses the new `sys.process.exit(Int)` intrinsic, lowered consistently
by the reference and self-host backends. Windows flushes buffered output and
calls `ExitProcess`; Linux calls `exit`. The cross-target gate verifies the
failure code, message, and absence of an LLVM target header.

Validation is a zero-warning Release build, Windows/Linux full suites at
**631/631**, Windows Stage2 **7/7** at **11,581,500 LLVM bytes** with a
**1,625,088-byte executable**, and Linux Stage2 **6/6** at **11,578,079 LLVM
bytes**. The fresh checks took 101.8 seconds and 253.9 seconds respectively.
Typed IR is currently lowered separately for ownership and emission because
the self-host backend cannot yet safely return an owned IR array from an
analysis aggregate or capture a borrowed IR parameter in a local helper. That
duplicate lowering and the pre-output 0-byte progress interval are tracked as
performance follow-ups.

The Stage3 cadence is **1/10** after D213B's reset, so Stage3 is not due. Formal
progress remains **49 complete, 8 partial, 3 missing: 53/60 (88.3%)** because
the wider ownership/storage gate still includes CFG-sensitive lifetimes,
multiple/union origins, aggregate borrowed returns, projection conflicts, and
production enforcement of E17-E20.

Research basis:

- [Rust compiler diagnostics](https://rustc-dev-guide.rust-lang.org/diagnostics.html)
- [Rust `ErrorGuaranteed`](https://rustc-dev-guide.rust-lang.org/diagnostics/error-guaranteed.html)
- [Clang command stages](https://clang.llvm.org/docs/CommandGuide/clang.html)
- [Clang diagnostics internals](https://clang.llvm.org/docs/InternalsManual.html)

## Straight-Line Last-Use Borrow Regions (D213B)

D213B shortens a returned Text view's inferred SourceText borrow from lexical
scope end to its last use in a straight-line function or main statement
sequence. The reference compiler examines remaining statements plus the final
result expression before each statement. The self-host ownership analyzer
checks whether a typed-IR read of the borrowed binding remains after a move.
Consequently, an owner can be moved after the last view use, while moving it
before a later view read still produces diagnostic 21.

This is the first local non-lexical region, not the final CFG solver. Branch and
loop liveness, joins, reference reassignment, multiple or union origins,
aggregate borrowed returns, and disjoint projection conflicts remain open.
The implementation follows Rust NLL's minimum-live-region principle, Mojo's
symbolic origin inference, and Swift's duration-overlap conflict model without
adding lifetime syntax to Sollang's common case.

Example 471 also closes a self-host LLVM defect found during honest execution
testing: function-local `println` now formats Bool and signed/unsigned
8/16/32/64-bit values, including SourceText `len` as UIntSize, instead of
assuming every non-Int32 value is Text. Runtime helper discovery covers these
direct numeric calls.

Validation is a zero-warning Release build, Windows/Linux focused checks at
**5/5**, Windows/Linux full suites at **631/631**, Windows Stage2 **6/6** at
**11,348,275 LLVM text bytes** with a **1,573,376-byte native executable**, and
Linux Stage2 **5/5** at **11,344,878 LLVM text bytes** with a **3,128,680-byte
native executable**. Stage3 emits the same **11,348,275 bytes** and passes fixed
point hash
`390F7C0482933D3C2918421B9CE1994762712C4FA459F240407A1C5A302D0976`.
Cadence checkpoint **10/10** is complete and resets to **0/10**. Formal progress
remains **49 complete, 8 partial, 3 missing: 53/60 (88.3%)** because this is a
sub-gate inside the remaining ownership/storage work.

Research basis:

- [Rust RFC 2094: non-lexical lifetimes](https://rust-lang.github.io/rfcs/2094-nll.html)
- [Mojo lifetimes, origins, and references](https://mojolang.org/docs/manual/values/lifetimes/)
- [Swift memory safety](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/memorysafety/)

## Shared Canonical Typed IR and Observable Stage2 Phases (D213D)

D213D removes the duplicate typed-IR lowering introduced when D213C connected
ownership validation to production code generation. LLVM preparation now
lowers once, lends the canonical array read-only to the ownership analyzer,
and retains the same array for emission. Standalone ownership analysis keeps
its convenience API and may lower independently.

Making this path real exposed and fixed a self-host closure ABI bug: a local
function capturing an owned additional parameter always stored primary `%arg`
into its borrow slot. Capture emission now resolves the parameter ordinal and
uses `%arg`, `%arg1`, and later arguments correctly. Stage2 assembly proves the
fix against the complete compiler.

The Stage2 scripts now report two actual observable phases. Phase 1 prints the
exact input count (**46 files / 32,303 lines**) and a ten-second analysis
heartbeat. Phase 2 reports LLVM bytes and reaches 100.0%. It no longer presents
the pre-output interval as repeated 0.0% LLVM progress.

Fresh Windows Stage2 passes **7/7 in 68.17 seconds**, improving from 101.8
seconds by **33.0%**, at **11,585,512 LLVM bytes** with a **1,626,624-byte
executable**. Fresh Linux Stage2 passes **6/6 in 145.13 seconds**, improving
from 253.9 seconds by **42.8%**, at **11,582,091 LLVM bytes** with a
**3,260,840-byte executable**. Focused checks pass **6/6**, Windows/Linux full
suites pass **631/631**, and Release build warnings/errors remain zero.

The periodic Stage3 cadence advances to **2/10**, so Stage3 is not due. Formal
progress remains **49 complete, 8 partial, 3 missing: 53/60 (88.3%)** because
this checkpoint improves the compiler pipeline inside the existing ownership
gate rather than completing CFG-sensitive lifetimes, origin unions, aggregate
borrowed returns, projected conflicts, or E17-E20 production enforcement.

Research basis:

- [Rust compiler overview and query model](https://rustc-dev-guide.rust-lang.org/overview.html)
- [Rust incremental compilation and backend integration](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation-in-detail.html)
- [LLVM pass instrumentation](https://llvm.org/doxygen/PassInstrumentation_8h.html)
- [MLIR pass instrumentation](https://mlir.llvm.org/docs/PassManagement/#pass-instrumentation)

## CFG-Reachable Borrow Liveness (D213E)

D213E replaces source-order-only E21 analysis with structured control-flow
reachability for the current inferred SourceText-to-Text origin. The reference
compiler carries outer continuation uses into branch blocks, analyzes `if` and
`when` alternatives independently, and admits branch-local last-use shortening
only when every outgoing path consumes the owner consistently. Mixed
consumption is a compile-time join error.

The self-host analyzer recognizes typed-IR branch regions, linked `when`
alternatives, and loop containment. Sibling alternatives are mutually
exclusive, a post-join use is reachable, and a use earlier in a loop body is
reachable from a later move through the back-edge. Examples 472-474 and the new
mixed-consumption diagnostic cover reference execution, self-host `0,1,1`
classification, `if`, `when`, and loop behavior.

Validation is a zero-warning Release build, focused checks **8/8**,
Windows/Linux full suites at **635/635**, Windows Stage2 **7/7 in 70.5 seconds**
at **11,606,935 LLVM bytes** with a **1,628,160-byte executable**, and Linux
Stage2 **6/6 in 141.2 seconds** at **11,603,514 LLVM bytes** with a
**3,265,096-byte executable**. The periodic Stage3 cadence advances to
**3/10**, so Stage3 is not due.

Formal progress remains **49 complete, 8 partial, 3 missing: 53/60 (88.3%)**.
The remaining ownership gate still includes multiple/union origins, reference
reassignment, aggregate borrowed returns, disjoint projected conflicts, and
production enforcement of E17-E20.

Research basis:

- [Rust RFC 2094: non-lexical lifetimes](https://rust-lang.github.io/rfcs/2094-nll.html)
- [Rust MIR dataflow](https://rustc-dev-guide.rust-lang.org/mir/dataflow.html)
- [Polonius loan analysis](https://rust-lang.github.io/polonius/rules/loans.html)
- [Polonius CFG input relations](https://rust-lang.github.io/polonius/rules/relations.html)
- [2026 Polonius alpha project goal](https://rust-lang.github.io/rust-project-goals/2026/polonius.html)

## Inferred Union Origins for Returned Text Views (D213F)

D213F generalizes the returned `Text` lifetime contract from one borrowed
`SourceText` parameter to the union of every input origin that can reach the
return value. A function such as `pick useLeft: Bool, left: SourceText, right:
SourceText -> Text` needs no lifetime punctuation: the semantic compiler walks
the `if`/`when` result paths, records `{left, right}`, and substitutes the
caller's concrete owners at each call. Transitive calls use the same fixed-point
contract discovery. A live result therefore freezes both possible owners even
when the runtime condition selects only one; after the result's CFG last use,
both owners may move independently.

The reference compiler now stores immutable origin sets for both function
contracts and active borrowed bindings. Direct and fluent calls map parameter
ordinals to caller expressions and flatten transitive sets. The self-host
ownership analyzer records one `BorrowedTextReturn` row per contributing
parameter, follows the linked typed-IR argument chain by ordinal, and creates
one owner edge per distinct concrete owner. This is compile-time metadata only;
the emitted `Text` ABI remains pointer plus length with no runtime origin tag.

Example 475 executes a two-input conditional view and proves both owners can
move after its final use. Example 476 asks the Sollang-written analyzer to move
the left and right owners separately and obtains `1,1` E21 conflicts. The
`borrowed-text-origin-union-move` diagnostic checks the reference compiler, and
the Stage2 union fixture proves Stage1 and Stage2 reject the same unsafe program
before emitting an LLVM target header.

Validation is a zero-warning, zero-error Release build and focused ownership
regression at **14/14**. Windows and Linux full suites pass **638/638**. Fresh
Windows Stage2 passes **7/7 in 68.5 seconds** at **11,612,260 LLVM bytes**,
**3,435,204 bitcode bytes**, and a **1,628,672-byte executable**. Fresh Linux
Stage2 passes **6/6 in 149.3 seconds** at **11,608,839 LLVM bytes**,
**3,433,412 bitcode bytes**, and a **3,265,096-byte executable**. The periodic
Stage3 cadence advances to **4/10**, so Stage3 is intentionally deferred.

Formal progress remains **49 complete, 8 partial, 3 missing: 53/60 (88.3%)**.
Union origins close one major sub-gate, but reference reassignment, aggregate
borrowed returns, disjoint projected conflicts, and production enforcement of
E17-E20 remain inside the partial ownership/storage gate.

The design follows Mojo's explicit union-origin rule: a reference that may come
from either input extends both lifetimes. Polonius supplies the compatible set
model—origins contain loans that flow through subset relations and remain
subject to point-specific CFG liveness. Sollang infers the same conservative
union from the body so ordinary code keeps its punctuation-free surface.

Research basis:

- [Mojo lifetimes, origins, and union origins](https://mojolang.org/docs/manual/values/lifetimes/)
- [Polonius origin and subset relations](https://rust-lang.github.io/polonius/rules/relations.html)
- [Polonius loan propagation and liveness](https://rust-lang.github.io/polonius/rules/loans.html)
- [Rust lifetime elision](https://doc.rust-lang.org/reference/lifetime-elision.html)

## Inferred Origin Transfer Across Reference Reassignment (D213G)

D213G makes the inferred origin of a mutable `Text` binding follow its current
value. Borrowed reassignment replaces the old origin set, assignment from an
owned or static value clears it, and an alias receives the full origin set of
the source view. No runtime lifetime object is added.

At control-flow joins, possible exit origins are unioned. If every `if` or
`when` alternative overwrites the binding, the prior loan is killed; mixed
alternatives retain both possible states. A loop retains its entry origin as
well as any body-exit origin because zero iterations are possible. The
self-host analyzer implements the same rules over branch-local typed-IR
definitions, preserving exact definition edges and using source-token identity
only where lowering splits one source binding into multiple definitions.

Examples 477-480 and four diagnostics cover replacement, alias transfer,
all-branch kills, mixed joins, and loop back-edge/zero-iteration behavior. The
Stage2 production gate checks single, union, and transferred origins with both
the Stage1 and Stage2 compilers.

Validation is a zero-warning, zero-error Release build and focused ownership
coverage of **23 tests**. Windows/Linux full suites pass **646/646** in **56.5
seconds** and **59.5 seconds** respectively. Fresh
Windows Stage2 passes **7/7 in 70.5 seconds** at **11,663,233 LLVM text
bytes**, **3,448,648 bitcode bytes**, and a **1,635,328-byte native
executable**. Fresh Linux Stage2 passes **6/6 in 146.0 seconds** at **11,659,812
LLVM text bytes**, **3,446,856 bitcode bytes**, and a **3,285,776-byte native
executable**.

The periodic Stage3 cadence advances to **5/10**, so Stage3 is not due. Formal
progress remains **49 complete, 8 partial, 3 missing: 53/60 (88.3%)**. The
remaining ownership work is aggregate borrowed returns, disjoint projected
conflicts, and production precision for E17-E20.

Research basis:

- [Mojo lifetimes, origins, and reference bindings](https://mojolang.org/docs/manual/values/lifetimes/)
- [Mojo variables and reference bindings](https://mojolang.org/docs/manual/variables/)
- [Mojo ownership](https://mojolang.org/docs/manual/values/ownership/)
- [Polonius loan-kill and loan-liveness rules](https://rust-lang.github.io/polonius/rules/loans.html)

## Collection-Argument ABI Integrity at the Stage3 Fixed Point (D213H)

The voluntary Stage3 probe after D213G found that `[first, second, ~]` could
lose its array-literal typed-IR node when both elements were binding reads. The
LLVM call then exposed the two Text elements as separate parameters to a
function declared with one array parameter. Typed lowering now retains array
literals independent of shallow type inference and assigns non-empty growable
arrays their canonical element-derived type.

The follow-up fixed a second ABI edge in short-circuit boolean lowering. Its
specialized while-value emitter previously scheduled and wrote only a call's
first argument. It now traverses the complete argument chain and emits nested
Text literals as inline Text constants. Examples 481 and 482 cover the array
aggregate and nested multi-argument call independently; example 480 compiled
by Stage2 again produces `0,1,1,1,1,1` ownership conflicts.

Windows/Linux full suites pass **648/648**. Windows Stage2 passes **7/7** at
**11,698,851 LLVM bytes**, **3,457,924 bitcode bytes**, and a **1,637,376-byte
executable**. Linux Stage2 passes **6/6** at **11,695,430 LLVM bytes**,
**3,456,132 bitcode bytes**, and a **3,294,008-byte executable**. Voluntary
Stage3 regenerates the Windows LLVM byte-for-byte and passes fixed-point hash
`07281B4A9C220FC5C49A474705F9108EA64FE2E8F0C159CF2EF7A1DA55E8A75D`.

The periodic cadence advances to **6/10** and is not reset by this early
fixed-point proof. Formal progress remains **49 complete, 8 partial, 3
missing: 53/60 (88.3%)**; this checkpoint restores correctness but does not
claim one of the remaining feature gates.

## Inferred Aggregate Borrow Origins (D213I)

D213I closes the aggregate-return boundary left by D213A-D213G. A returned
struct, fixed or growable array, or dictionary now carries the union of every
borrowed `Text` reachable from its value. The contract is inferred through
direct and explicit early returns, local aliases, fields, indexes, and
`Text`-containing parameter forwarding. Static or owned `Text` aggregates do
not acquire a false origin merely because their type contains `Text`.

The reference compiler recursively analyzes aggregate expressions and maps a
SourceText parameter to its concrete caller owner while propagating only
already-active origins through aggregate parameters. The self-host analyzer
classifies recursive semantic types bottom-up, discovers contracts only below
the canonical or explicit return operand, and transfers transitive origin rows
through moved container bindings. The classification is allocation-free;
avoiding a per-query work list also preserves Linux native parity.

Examples 483-490 cover valid struct fields, growable arrays, dictionaries,
field/index projections, aggregate move forwarding, explicit early returns,
self-host E21 analysis, and self-host LLVM execution. The aggregate diagnostic
and Stage2 fixture prove that a source owner cannot move before the final
derived use. The obsolete diagnostic that rejected every sliced-Text array
return has been removed because that program is now safe and supported.

Windows/Linux full suites pass **656/656**. Windows Stage2 passes **7/7** at
**11,727,474 LLVM bytes**, **3,465,668 bitcode bytes**, and a **1,638,400-byte
executable**. Linux Stage2 passes **6/6** at **11,724,053 LLVM bytes**,
**3,463,876 bitcode bytes**, and a **3,298,104-byte executable**. Both Stage1
and Stage2 reject single, union, transferred, and aggregate-origin E21 before
LLVM emission.

The Stage3 cadence advances to **7/10**. Formal progress remains **49 complete,
8 partial, 3 missing: 53/60 (88.3%)** because aggregate escape completes a
major ownership sub-boundary, while simultaneous disjoint projected borrows
and production precision for E17-E20 still keep the broader ownership/storage
gate partial.

Research basis:

- [Rust lifetime elision](https://doc.rust-lang.org/stable/reference/lifetime-elision.html)
- [Rust references stored in structs](https://doc.rust-lang.org/book/ch10-03-lifetime-syntax.html)
- [Mojo lifetimes, origins, and references](https://docs.modular.com/mojo/manual/values/lifetimes/)
- [Swift memory safety](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/memorysafety/)

## Disjoint Projected Borrow Origins (D213J)

D213J makes the borrow-origin model path-sensitive. A canonical place consists
of its root binding plus field and index projections. Equal paths, a whole
owner and any descendant, and prefix-related projections overlap. Different
stored fields and unequal compile-time numeric indices are provably disjoint;
dynamic indices remain conservative.

The reference compiler constructs and invalidates exact projected places. The
self-host analyzer reconstructs the same paths from typed IR, carries them
through borrowed aliases, and recovers `take(index)` from the source binding AST
where the current typed IR has flattened the move. This requires no new syntax,
runtime lifetime token, or ABI change.

Examples 491-494 and two diagnostics cover reference execution, self-host E21,
self-host LLVM, equal projections, and dynamic indices. The Stage2 nested-place
fixture proves production rejection before LLVM emission. Each new example now
states its verification purpose in an English `#` comment, with scenario-level
comments where one source exercises multiple outcomes.

Windows/Linux full suites pass **662/662**. Windows Stage2 passes **7/7** at
**11,795,808 LLVM bytes**, **3,483,932 bitcode bytes**, and a **1,645,056-byte
executable**. Linux Stage2 passes **6/6** at **11,792,387 LLVM bytes**,
**3,482,144 bitcode bytes**, and a **3,318,912-byte executable**. Both Stage1
and Stage2 reject the projected-origin E21 fixture before LLVM emission.

The Stage3 cadence advances to **8/10**. Formal progress remains **49 complete,
8 partial, 3 missing: 53/60 (88.3%)** because production E17-E20 precision is
still open inside the broader ownership/storage gate.

Research basis:

- [Rust field expressions](https://doc.rust-lang.org/reference/expressions/field-expr.html)
- [Rust borrow splitting](https://doc.rust-lang.org/nomicon/borrow-splitting.html)
- [rustc place-conflict analysis](https://doc.rust-lang.org/stable/nightly-rustc/rustc_borrowck/places_conflict/index.html)
- [Mojo lifetimes and origins](https://docs.modular.com/mojo/manual/values/lifetimes/)
- [Swift memory safety](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/memorysafety/)

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
   examples 51 and 56-71, including fixed-array value/type contracts).
7. `Option`/`Result` and compiler-grade byte/text/source-span libraries
   (implemented for `Option`, `Result`, bytes, Unicode code points, byte arenas,
   reusable lexer/CST/parser source spans, and allocation-free Text search and
   comparison; multi-error continuation remains).
