# Deterministic Parallel Compilation

Status: accepted direction, implementation in progress  
Updated: 2026-07-18

This document defines SmallLang's CPU-parallel execution model and the concrete
self-host compiler migration. A worker-count message is not implementation
evidence. A checkbox is complete only when the corresponding runtime behavior,
compiler path, and regression evidence exist.

## Decision

`async` remains the structured latency/concurrency abstraction. CPU-bound data
parallelism uses an ordinary typed role named `parallel`:

```smalllang
sources -> parallel source {
    source -> analysis.analyzeSource
} => analyses!
```

`parallel` is not a keyword. It is a standard-library role built on a native
structured task group. This preserves the role-block invariants in
[`ROLE_BLOCKS.md`](ROLE_BLOCKS.md): normal name resolution, normal imports, and
no private subgrammar.

The callback-result extension has the type:

```text
Role<Source, Item, Result, Output>
Source -> Output block Item -> Result
```

The task group executes callback indices in an unspecified order but writes
each result into the slot for that index. It joins every child before returning
and exposes results in canonical input order. Scheduling therefore cannot alter
source indices, symbol IDs, type IDs, diagnostic order, or emitted bytes.

## Why This Fits SmallLang

- Swift task groups provide structured child lifetime and require the parent to
  await its children. Swift's `Sendable` model also rejects unsafe values that
  cross concurrency domains.
- Mojo's CPU `parallelize` executes indexed work items in parallel and returns
  only after all items complete. This is the right minimal runtime shape for a
  compiler's module and function arrays.
- SmallLang already has affine ownership and compile-time sendability checks for
  async inputs/results. The parallel role reuses those checks instead of adding
  shared mutable collections or implicit reference counting.

Primary references:

- [Swift Concurrency](https://docs.swift.org/swift-book/LanguageGuide/Concurrency.html)
- [Mojo CPU parallelize](https://docs.modular.com/mojo/std/algorithm/backend/cpu/parallelize/)
- [Mojo ownership](https://docs.modular.com/mojo/manual/)

## Runtime Algorithm

1. Start a bounded native pool lazily, with a default worker count equal to the
   available logical processors and an explicit positive override for builds.
2. Publish a task-group record containing immutable input storage, an atomic
   next-index counter, disjoint result slots, cancellation state, and remaining
   work count.
3. Each worker claims an index atomically and invokes the outlined callback.
4. A worker owns only its callback input and result slot. Captured values must
   be immutable and sendable; mutable borrows and non-sendable owners are
   compile-time errors.
5. The parent participates in work while waiting, then joins the group.
6. Package and LLVM products are merged in canonical source/function order.
7. Failure cancels unclaimed work, joins started work, and destroys every
   initialized result exactly once.

The pool is bounded; SmallLang must not create one OS thread per item. File I/O
continues to use its separate operation worker and does not consume compute-pool
capacity while blocked.

## Self-Host Decomposition

The first safe parallel boundary is source-local analysis:

```text
SourceText view
  -> AST + tokens + symbols + resolved names + type terms + type uses
  -> SourceAnalysis result slot
  -> canonical PackageAnalysis merge
```

Global module/import/symbol/type facts remain sequential until frozen. After
that barrier, function-local typed IR and LLVM bodies can use the same indexed
task-group primitive and ordered merge.

## Completion Checklist

### A. Source-local product boundary (4/4)

- [x] `SourceAnalysis` owns every source-local output array.
- [x] `analyzeSource(Text)` borrows immutable source text only.
- [x] `analyzeSources` consumes source-local products in source order.
- [x] Flat package/context and LLVM module-call regressions pass.

Evidence: `selfhost/semantic/analysis.sl`; examples 182, 293, and 294.

### B. Typed role surface (5/5)

- [x] Block callbacks can return a typed result.
- [x] `parallel` is implemented as an imported standard-library role.
- [x] Input/item/result types are inferred through ordinary generics.
- [x] Non-sendable captures and mutable borrows are rejected.
- [x] Owned callback results transfer exactly once.

### C. Native compute task group (5/7)

- [x] Windows pool uses bounded reusable native workers.
- [ ] Linux pool uses bounded reusable native workers.
- [x] The available processor count and explicit build override are supported.
- [x] Workers claim indices atomically without a global result lock.
- [x] Parent-assisted waiting and structured join are implemented.
- [ ] Cancellation and partial-result destruction are exactly once.
- [x] File-operation waiting remains outside the compute pool.

### D. Self-host compiler integration (6/6)

- [x] Nested imported calls cannot overwrite the enclosing runtime call target.
- [x] Source-local analysis uses `parallel` and ordered package assembly.
- [x] Global semantic facts form an explicit read-only barrier.
- [x] Function-local typed IR uses indexed parallel work.
- [x] LLVM function bodies use per-function buffers and ordered emission.
- [x] The self-host driver accepts and reports the effective worker count.

Evidence for the completed call-identity fix: example 322. Example 323 proves a
literal-returning function in the second module still emits `ret i32 42`.
Example 378 proves the self-host worker-limit intrinsic together with native
parallel execution. The complete stage-2 LLVM contains an indexed callback that
invokes typed-IR `lowerFunction`, and the fixed-point verifier checks that
callback plus identical stage-1/stage-2 `--jobs 2` output. `SemanticSnapshot`
is the named ownership barrier consumed read-only by semantic, typed-IR,
ownership, effect, and LLVM passes; example 379 proves the frozen package,
module, import, and resolved-import views.
The reference compiler rejects direct and transitive mutable captures and
structurally non-sendable values. The self-host ownership pass reports the same
direct callback boundary as diagnostic codes 18 and 19; example 380 proves
mutable, non-sendable, and immutable cases. Before its own parallel LLVM-body
emission, the self-host emitter snapshots construction-time mutable indexes so
worker callbacks capture only immutable arrays.

### E. Verification (5/6)

- [x] A 24-processor machine shows more than two active frontend workers.
- [x] Frontend CPU-time/wall-time ratio materially exceeds 2.0.
- [x] Full self-host frontend wall time improves from the recorded baseline.
- [x] Three repeated LLVM outputs are byte-for-byte identical.
- [x] Peak runtime memory stays within the documented budget.
- [ ] Windows and Linux full suites pass with zero warnings and errors.

Evidence: example 324 executes `block item: Int -> Int`; example 325 proves the
self-host grammar/parser accepts the same declaration and call form. The two
`block-callback-result-*` diagnostics cover missing and mismatched results.

Parallel-compilation progress is **25/28 checks (89.3%)**. This is a feature-local
metric and does not promote the canonical self-host roadmap, which remains
**48.5/60 equivalent gates (80.8%)** until a full checklist audit proves a gate.

## Definition of Done

The feature is complete only when all 28 checks are proven. In particular,
`--jobs 24`, LLVM's 24 native partitions, the test runner's workers, or visible
`n/total` output do not prove frontend parallelism. Completion requires active
compute workers inside the long-running self-host frontend plus deterministic
output and measured wall-time improvement.

## Current Evidence

On the 24-logical-processor Windows development machine, example 329 created
24 bounded compute workers and observed 12 callbacks executing concurrently.
Examples 328, 329, and 294 pass together: ordered `Int -> Int` mapping, active
worker instrumentation, and the self-host `SourceAnalysis` boundary.

Example 377 proves 100 generations of borrowed `SourceText` input and owned
struct/array output through native workers. Example 378 proves an explicit
positive worker limit, while the driver reports the effective count as a valid
LLVM comment. Example 381 limits the pool to one native worker and observes a
peak of two active callbacks, proving that the submitting parent claims work
instead of idling. The parent exhausts the same atomic index queue, waits for
all native workers, and only then flushes ordered sinks and destroys the group.
This follows the helping-wait pattern documented by Java `ForkJoinPool` and
oneTBB task groups.

- [Java `ForkJoinPool.awaitQuiescence`](https://docs.oracle.com/en/java/javase/25/docs/api/java.base/java/util/concurrent/ForkJoinPool.html#awaitQuiescence(long,java.util.concurrent.TimeUnit))
- [oneTBB `task_group`](https://uxlfoundation.github.io/oneTBB/main/specification/source/task_scheduler/task_group/task_group_cls.html)

The complete 28-source self-host compiler reached an exact
stage-2/stage-3 fixed point of 7,198,336 bytes with SHA-256
`CBCED4918D9AF37C71AF792D99016A27C2F4CC9D4407CD123CD866BF32DB555F`;
the stage-3 output also assembles with `llvm-as`. The preceding source-worker
measurement used 377.77 CPU-seconds over 34.81 seconds wall time (10.85
effective cores). The parent-help fixed-point run used 376.91 CPU-seconds over
34.56 seconds wall time (10.91 effective cores). The earlier capture-safety run
peaked at 77.5 MiB, below the 100 MiB frontend budget.
Linux full-suite parity remains the outstanding platform check.
