# Typed Role Blocks

Status: accepted design, common foundation implemented; roles in progress  
Updated: 2026-07-14

This document is the canonical design and completion checklist for SmallLang
blocks that perform a typed role. A checkbox is completed only when the linked
compiler path and regression evidence exist. Design agreement alone never
counts as implementation.

## Decision

SmallLang will express builders, scoped contexts, and effect handlers through
one result-producing block-function mechanism. `build`, `with`, and `handle`
are ordinary function names, not reserved words and not parser extensions.

```smalllang
spec -> build field {
    field.name("compiler")
    field.optimize(true)
} => product

file -> with bytes {
    bytes -> checksum => digest
} => digest

program -> handle effect {
    effect -> recover
} => result
```

The declaration uses the existing block-function form. Its final expression is
the function result, just as it is for an ordinary braced function.

```smalllang
build spec: BuildSpec -> Product block field: ProductBuilder {
    spec -> ProductBuilder => builder!
    builder -> yield
    builder.finish
}
```

Existing Unit block functions remain valid and unchanged:

```smalllang
runTimes count: Int -> Unit block turn: Int {
    1..count -> each turn {
        turn -> yield
    }
}
```

## Non-Intrusion Invariants

- `TypeName { field: value }` always remains a struct literal.
- `source -> role item { ... }` always remains a block-function call.
- `=> name` after the closing brace binds the block function's return value.
- `build`, `with`, and `handle` are resolved through normal name, import,
  generic, and trait lookup. They are never context-sensitive keywords.
- A role body contains the normal SmallLang AST. It cannot introduce arbitrary
  tokens or a private subgrammar.
- A Unit-returning block call cannot use `=> name`.
- A non-Unit-returning block call must bind or otherwise consume its owned
  result according to the normal ownership rules.
- Struct construction and role execution must coexist in the same source file
  without parser backtracking ambiguity.

## Semantic Model

A role block has four relevant types:

```text
Role<Source, Item, Result>
Source -> Result block Item -> Unit
```

- `Source` is the value to the left of `->`.
- `Item` is the value supplied by the role implementation through `yield`.
- The caller block currently returns `Unit`; it configures, observes, or handles
  the yielded item using ordinary statements.
- `Result` is the role function's final expression and is bound by `=> name`.

This first model deliberately keeps `yield` one-way. A future callback-result
extension may add `Block<Item, YieldResult>`, but it is not required for the
three accepted roles and is not part of this checklist.

### Builder

A builder yields a scoped mutable builder or typed construction capability,
then validates and returns a completed value. Builder internals must not escape
unless their declared type and ownership permit it.

### Scoped Context

A context role acquires or borrows a capability, yields it only within the
caller block, and releases it after the block. A borrowed or affine capability
must not escape through the role result or a captured outer binding.

### Handler

A handler installs a statically known effect capability while the caller block
runs and returns the handled result. The initial implementation uses ordinary
typed functions and static dispatch; resumable algebraic effects require a
separate accepted design and are not implied by the word `handle`.

## Completion Checklist

### A. Common result-block foundation

- [x] Grammar accepts a tail expression in a block-function declaration.
- [x] Grammar accepts `=> result` after a block-function call.
- [x] AST records the block result binding without changing struct literals.
- [x] Semantic analysis validates the declared return type and binds the result.
- [x] Unit block functions remain source-compatible.
- [x] LLVM lowering emits the result and preserves caller/block scopes.
- [x] Owned results transfer exactly once and are dropped exactly once.
- [x] Invalid Unit binding, missing non-Unit result, and type mismatch diagnostics
  have regression tests.
- [x] Struct literal plus role block ambiguity has a regression test.

Evidence:

- [`274-result-role-block.sl`](../examples/274-result-role-block.sl) covers a
  struct literal and a user-defined `build` block in one source file.
- [`275-owned-result-role-block.sl`](../examples/275-owned-result-role-block.sl)
  covers mutable owned-result transfer and subsequent use.
- `examples/diagnostics/block-*.sl` covers Unit binding, missing result, result
  type mismatch, and discarded owned result.
- [`12-block-function-user-defined-yield.sl`](../examples/12-block-function-user-defined-yield.sl)
  preserves the original Unit block behavior.

### B. Builder role

- [ ] A library-defined `build` role is implemented without compiler keyword
  special-casing.
- [ ] Nested builder blocks type-check.
- [ ] Invalid builder fields or operations fail at compile time.
- [ ] Builder-only capabilities cannot escape their scope.
- [ ] A native Windows and Linux example returns the expected built value.

Current evidence: [`277-typed-role-block-forms.sl`](../examples/277-typed-role-block-forms.sl)
proves that `build` is an ordinary user-defined result block and that its
before/body/after phases are ordered. Builder-specific mutation and escape
rules remain unchecked.

### C. Scoped context role

- [ ] A library-defined `with` role is implemented without compiler keyword
  special-casing.
- [x] Acquisition, body execution, and deterministic release are ordered on the
  normal completion path.
- [ ] Borrowed and affine context capabilities cannot escape.
- [ ] Early return, loop control, and failure paths release resources once.
- [ ] A native Windows and Linux example proves cleanup behavior.

Current evidence: example 277 proves normal-path acquire/body/release ordering.
Control-flow cleanup and capability escape analysis remain unchecked.

### D. Handler role

- [ ] A library-defined `handle` role is implemented without compiler keyword
  special-casing.
- [ ] Effect operations require an in-scope typed capability.
- [ ] Unhandled operations produce a compile-time diagnostic.
- [ ] Handler capabilities cannot escape their scope.
- [ ] Nested handlers resolve deterministically.
- [ ] A native Windows and Linux example proves handled behavior.

Current evidence: example 277 proves that `handle` is an ordinary user-defined
result block with statically typed input and deterministic install/body/resolve
ordering. Example 297 now proves flat self-host effect declarations plus
missing local/imported/builtin/map/flush capability diagnostics. The closed
Console/File/Clock/Random/Process/Environment set represents fixed external
authority and is intentionally not removable by a user role. The handler
checkboxes remain open until separately declared effect operations can be
matched, discharged, and lowered by a lexical handler.

### E. Self-hosting and release evidence

- [x] `syntax/smalllang.grammar` and the generated SL grammar agree.
- [x] The self-host parser recognizes result-producing role blocks.
- [ ] The self-host semantic phase enforces the same role contracts.
- [ ] The self-host LLVM emitter lowers the same examples.
- [x] Grammar generation is deterministic.
- [x] Focused parser, semantic, ownership, and LLVM tests pass.
- [x] The full regression suite passes with zero build warnings and errors.
- [x] The roadmap records exact completed, partial, and missing gate counts.
- [x] The implementation is committed as a reproducible baseline.

Parser evidence: [`276-selfhost-result-role-block-parser.sl`](../examples/276-selfhost-result-role-block-parser.sl)
executes the generated grammar through the SL lexer/parser VM. The grammar
places block-function statements before general expression statements, matching
the bootstrap parser's deterministic dispatch order.

Partial semantic/IR evidence:
[`279-selfhost-result-role-block-semantics.sl`](../examples/279-selfhost-result-role-block-semantics.sl)
proves that the SL compiler's own AST records the role target and result name,
its symbol/call/type passes resolve and propagate the result and typed block
item, its type checker accepts the declared source/item contract, and flat
typed IR retains the call, result binding, and nested body operation.
[`280-selfhost-role-block-contract-check.sl`](../examples/280-selfhost-role-block-contract-check.sl)
proves that the source expression is selected only from the region before the
role target, a mismatched source emits code 6, and role syntax targeting an
ordinary function without a block input emits code 17. Runtime calls nested in
the caller block are excluded from source-module lookup.
[`281-selfhost-generic-role-specialization.sl`](../examples/281-selfhost-generic-role-specialization.sl)
proves outside-in generic specialization for scalar `T`, `[T; ~] -> item: T`,
`T -> items: [T; ~]`, an imported role reached through a default import alias,
caller-body operators, and typed IR.
[`283-selfhost-recursive-type-terms.sl`](../examples/283-selfhost-recursive-type-terms.sl)
proves recursive canonical type terms and full-depth substitution independent
of the former shallow component slots.
[`284-generic-composite-role-block.sl`](../examples/284-generic-composite-role-block.sl)
proves reference semantic specialization, `yield`, caller binding, LLVM
execution, and owned cleanup for a composite generic item; the matching
diagnostic rejects a specialized `yield` mismatch. The semantic checkbox
remains open until every expression/type consumer uses the recursive arena and
ownership/capability escape and effect rules have matching self-host diagnostics.

Regression evidence on 2026-07-14: the Release solution build completed with
zero warnings and errors. After adding recursive semantic type terms and
generic composite role items, the coordinated eight-worker runner passed all
399 cases plus byte-for-byte grammar table determinism in 389.8 seconds.
The canonical roadmap remains 42 complete, 13 partial, and 5 missing gates
(48.5/60, 80.8%). This partial role-block slice does not promote a roadmap gate.
The common foundation baseline is commit `d2b07db`; self-host semantic/IR
recognition and the first block-input contract slice are covered by examples
279 and 280.

## Definition of Done

The feature is complete only when every checkbox in sections A through E is
checked with repository evidence. If only the common foundation or one role is
implemented, the document remains `implementation in progress` and progress is
reported by subsection rather than as a completed language feature.
