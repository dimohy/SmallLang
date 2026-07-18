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

### C. Native compute task group (7/7)

- [x] Windows pool uses bounded reusable native workers.
- [x] Linux pool uses bounded reusable native workers.
- [x] The available processor count and explicit build override are supported.
- [x] Workers claim indices atomically without a global result lock.
- [x] Parent-assisted waiting and structured join are implemented.
- [x] Cancellation and partial-result destruction are exactly once.
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
- [x] Windows and Linux full suites pass with zero warnings and errors.

Evidence: example 324 executes `block item: Int -> Int`; example 325 proves the
self-host grammar/parser accepts the same declaration and call form. The two
`block-callback-result-*` diagnostics cover missing and mismatched results.

Parallel-compilation progress is **28/28 checks (100%)**. This is a feature-local
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
The submitter reserves source index zero before waking workers, so this
observable parent-help property remains deterministic even for short queues;
30 repeated Windows executions all reported `parent-helped=true`.
This follows the helping-wait pattern documented by Java `ForkJoinPool` and
oneTBB task groups.

The Linux x86-64 backend now uses a bounded reusable pthread pool, `eventfd`
work/completion signals, and a futex generation barrier. Example 383 reuses the
same pool for 100 generations, and `scripts/verify-linux-parallel.ps1` executes
six focused WSL checks covering ordered output, parent help, reuse, LLVM emitted
by the native self-host compiler, and AddressSanitizer ownership cleanup.
Memory-output ownership is shared by
one runtime abstraction: platforms provide only the final writer adapter while
the common sink owns grow, append, canonical flush, and destruction.

The reference runtime and scalar self-host LLVM paths now implement the
fallible `tryParallel<T, R, E>` role.
It keeps the earliest failing source index, stops new claims at that boundary,
joins already-started callbacks, flushes only the successful output-sink prefix,
and moves or destroys every initialized `Result` payload exactly once. The
self-host emitter executes the same ABI from entry, ordinary-function, and
nested-region positions (examples 392-394). Example 395 proves deterministic
earliest-error selection and prefix-only output over competing failures.
Example 396 returns owned dynamic arrays from callbacks and verifies the error
path under Linux AddressSanitizer with leak detection enabled. This closes C.6;
only full Windows/Linux suite parity remains open.

- [POSIX `pthread_create`](https://pubs.opengroup.org/onlinepubs/000095399/functions/pthread_create.html)
- [POSIX `pthread_join`](https://pubs.opengroup.org/onlinepubs/009695399/functions/pthread_join.html)
- [Linux `eventfd`](https://man7.org/linux/man-pages/man2/eventfd.2.html)

- [Java `ForkJoinPool.awaitQuiescence`](https://docs.oracle.com/en/java/javase/25/docs/api/java.base/java/util/concurrent/ForkJoinPool.html#awaitQuiescence(long,java.util.concurrent.TimeUnit))
- [oneTBB `task_group`](https://uxlfoundation.github.io/oneTBB/main/specification/source/task_scheduler/task_group/task_group_cls.html)

The complete 28-source self-host compiler reached an exact
stage-2/stage-3 fixed point of 7,217,656 bytes with SHA-256
`1C026529C832C88AA54ACCC55B05FE0A7358BBFA4F2A31F6F6F1F1ECEF0FD0DD`;
the stage-3 output also assembles with `llvm-as`. The preceding source-worker
measurement used 377.77 CPU-seconds over 34.81 seconds wall time (10.85
effective cores). The parent-help fixed-point run used 376.91 CPU-seconds over
34.56 seconds wall time (10.91 effective cores). The Linux-pool and common-sink
run used 407.42 CPU-seconds over 36.86 seconds wall time (11.05 effective cores)
and peaked at 100.7 MiB. The earlier capture-safety run peaked at 77.5 MiB.
The complete Linux x64 suite now passes all 523 cases through WSL.

The `tryParallel` reference-runtime checkpoint passes the complete 516-case
Windows suite and its three runtime cases on Linux x86-64. The updated compiler
reaches an exact 7,247,585-byte stage-2/stage-3 fixed point with SHA-256
`C1D43534CFC873CC3BB18BA9DDE3CAF1F515FB8D9FEBA57ABDFE063F648F0723`;
stage 3 assembles with `llvm-as` and took 35.19 seconds to emit. This evidence
predates executable self-host owned-`Result` cleanup. That cleanup is now
covered by example 396; the complete Linux suite now covers the same 523-case
inventory as Windows.

The post-owned-cleanup Windows gate passes all 523 examples in a read-only
run with a zero-warning, zero-error Release build. That run also repaired a
self-host aggregate-flow defect exposed by slice and nested-region output: only
opcode `-1` kind-9 nodes are transparent wrappers, and two-operand calls select
the later resolved IR value. The six-step Linux verifier remains green, but it
remains the fast platform gate. The new `--target linux-x64` runner path also
compiles and executes all ordinary examples and diagnostics under WSL, emits
Linux LLVM for every reusable self-host case, assembles all of those modules,
and links/executes every case with a runtime expectation. The resulting Linux
gate passes 523/523, closing the final checklist item.
