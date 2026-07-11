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
  with a read-only `Arguments` view now and an argv-based child-process API next.
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
| Compiler-construction primitives | 12 | 8 | 3 | 1 | 9.5 |
| Standard library and tooling | 8 | 2 | 3 | 3 | 3.5 |
| **Total** | **60** | **39** | **13** | **8** | **45.5 / 60** |

Current count-based progress: **75.8% (45.5 of 60 equivalent gates)**.
There are **14.5 equivalent gates remaining**. Because the missing compiler
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

### Compiler-construction primitives — 9.5 / 12

- Complete (8): Text values, validated UTF-8 iteration as fixed-width Unicode
  `CodePoint` scalar values, deterministic native file I/O wrappers needed by
  the existing demos, type-preserving array/dictionary iteration, and owned
  growable `UInt8` byte buffers with typed push/index/iteration/drop, plus typed
  copyable/owned `Result<T, E>` propagation with deterministic cleanup, and an
  owned aligned byte arena with stable offsets, growth, reset, checked access,
  move/mutable-borrow ABI, and one-shot backing-store release. Native memory
  mapping adds affine bounded `UInt8` views, 64-bit file offsets/sizes,
  target-sized view lengths/indices, writeback, and deterministic unmapping.
  Native host context includes lossless process arguments and `Option<Text>`
  environment lookup with process-lifetime UTF-8 views.
- Partial (3): generic arrays/dictionaries cover compiler-useful `Int`, `Text`,
  and user-value payloads plus function contracts; string processing is
  output-oriented; diagnostics have no reusable source-span type.
- Missing (1): structured child-process execution.

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
