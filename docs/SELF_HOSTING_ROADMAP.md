# SmallLang Self-Hosting Roadmap

Status: active
Updated: 2026-07-12

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
- Zig recommends an arena when allocations share one lifetime and can all be
  released together; rustc describes arena allocation as a pointer bump. SL's
  byte arena follows that lifetime model while exposing checked offsets instead
  of raw pointers. See Zig [Choosing an Allocator](https://ziglang.org/documentation/master/#Choosing-an-Allocator)
  and rustc [`rustc_arena`](https://doc.rust-lang.org/stable/nightly-rustc/rustc_arena/index.html).
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
  zero-input `read<T>` calls with explicit EOF/error results, while explicit
  user-value serialization remains.
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
SL module containing lexer descriptors and a 1,508-word parser VM program. The
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
