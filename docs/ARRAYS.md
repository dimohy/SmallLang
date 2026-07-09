# SmallLang Array And Ownership Design

Status: implemented slice plus future design
Date: 2026-07-09

This document records the proposed static and dynamic array model for
SmallLang and the first implemented container slice.

The implemented slice is intentionally narrower than the final design. It
supports `Int` static arrays, `Int` dynamic arrays, and `{Int: Int}`
dictionaries, with deterministic drop insertion for owned heap containers.
It also supports move-consuming owner transforms for growable arrays and
dictionaries. Borrowed slices, generic element types, mutable indexing
assignment, and typed empty dictionaries remain future work.

## Rust Reference Points

SmallLang should use Rust as the main reference for this area, but should not
copy Rust syntax blindly.

- Rust manages memory through compile-time ownership rules rather than a tracing
  garbage collector:
  <https://doc.rust-lang.org/book/ch04-01-what-is-ownership.html>
- Rust arrays are fixed-size `[T; N]` values whose size is a constant
  expression:
  <https://doc.rust-lang.org/reference/types/array.html>
- Rust slices are borrowed views into a sequence, such as `&[T]` and
  `&mut [T]`:
  <https://doc.rust-lang.org/reference/types/slice.html>
- Rust `Vec<T>` is a contiguous growable heap allocation with length and
  capacity. SmallLang should take the ownership model, not the `Vec` surface
  name:
  <https://doc.rust-lang.org/std/vec/>

Further web review changed the emphasis:

- Rust is the best mainstream reference for ownership, borrowing, fixed arrays,
  slices, and growable arrays, but Rust does not guarantee leak freedom. Safe
  Rust can intentionally forget values, and reference-count cycles can leak:
  <https://doc.rust-lang.org/std/mem/fn.forget.html>
  <https://doc.rust-lang.org/book/ch15-06-reference-cycles.html>
- Zig is a useful reference for explicit allocator design and the "where are
  the bytes?" mindset, but it leaves memory management to the programmer rather
  than proving leak freedom at compile time:
  <https://ziglang.org/documentation/master/#Memory>
- Austral is a useful reference for linear resource checking. Its specification
  rejects linear values that are never consumed, consumed more than once, or
  still unconsumed at function return:
  <https://austral-lang.org/spec/spec.html#Linear-Types>

Conclusion: SmallLang should keep the Rust-shaped array model, but make safe
SmallLang stricter than Rust by removing safe `forget`/`leak`, implicit shared
ownership, and unproven cyclic ownership from the safe surface.

## Core Decision

SmallLang should split array concepts by ownership:

```text
[T; N]      owned fixed-size array
[T; ..]     owned growable heap array
&[T]        shared borrowed slice view
&mut [T]    exclusive mutable borrowed slice view
```

This avoids the common GC-language ambiguity where a dynamic array value may or
may not own its backing storage. In SmallLang, a value either owns storage or it
borrows storage from another owner.

The source syntax should also look like SmallLang, not Rust. `Vec<T>` is only a
reference model for the internal ownership shape. User code should say
`[T; ..]`, not `Vec<T>`.

`{ ... }` should not be used for dynamic arrays. Braces already delimit blocks,
and they are a better future fit for dictionaries or maps. Dynamic arrays stay
inside the `[]` family and use `..` to show that the sequence is open and
growable.

## Implemented Container Slice

The current compiler implements the first `Int` container slice:

```smalllang
main {
    [1, 2, 3] => numbers
    numbers[0] => first
    numbers -> len => count

    [10, 20, ..] => mut values
    values -> push(30)
    values[2] => third
    values -> capacity => capacity

    [10, 20, ..] => values
    values -> append(30) => values
    values -> updated(0, 99) => values

    { 1: 100, 2: 200 } => mut scores
    scores -> put(3, 300)
    scores[3] => score

    { 1: 100, 2: 200 } => frozenScores
    frozenScores -> updated(2, 250) => frozenScores
}
```

Supported now:

- `[1, 2, 3]` creates an owned fixed-size `Int` array stored inline in the
  owner.
- `[0; 8]` creates a repeated fixed-size `Int` array.
- `[1, 2, ..]` and `[..]` create owned growable `Int` arrays.
- `{ 1: 100, 2: 200 }` creates an owned `{Int: Int}` dictionary.
- `value => mut name` creates a mutable binding needed by mutating container
  operations.
- `array[index]` and `dictionary[key]` are checked reads.
- `array -> len`, `array -> capacity`, `array -> push(value)`,
  `dictionary -> len`, `dictionary -> capacity`, and
  `dictionary -> put(key, value)` are implemented as receiver-flow operations.
- `array -> append(value)` consumes the source growable array owner and returns
  the moved owner with the appended value.
- `container -> updated(keyOrIndex, value)` consumes the source growable array
  or dictionary owner and returns the moved owner with one value changed or
  inserted.
- Static and dynamic `Int` arrays work with `each` and `fold`.
- Native Windows and Linux targets allocate through the selected platform
  runtime and emit deterministic cleanup at scope exit.

Current safety boundary:

- Heap-owning containers must be created directly at the binding site, such as
  `[1, 2, ..] => mut values` or `{ 1: 100 } => mut scores`.
- Heap-owning containers cannot be produced as anonymous intermediate values in
  a flow chain because the compiler would have no stable drop owner yet.
- Mutating operations such as `push` and `put` require a named mutable owner.
- Move-consuming heap-owning transforms such as `append` and `updated` must be
  final flow targets and must be bound directly with `=>`, so the moved owner
  has a known deterministic drop point.
- After a move-consuming transform, the source binding is no longer live. The
  target may reuse the same name, such as `values -> append(30) => values`,
  because the old owner is consumed before the new owner is bound.
- Container creation inside nested block-function bodies is rejected in this
  slice until block-local drop scopes are implemented.
- Browser WebAssembly currently rejects heap-owning containers because the
  browser target does not yet provide a linear-memory allocator.
- `Text` arrays, generic arrays, slices, container parameters/returns, and
  typed empty dictionaries are not implemented yet.

## Function Call Surface

Web review also suggests that `func!` should not be the ordinary function-call
marker. Rust uses `name!(...)` for macros, Elixir uses trailing bang for
raising variants, and Julia uses trailing bang for functions that mutate their
arguments. Reusing `!` for ordinary calls would spend a valuable punctuation
mark on the wrong meaning:

- <https://doc.rust-lang.org/reference/macros.html>
- <https://hexdocs.pm/elixir/main/naming-conventions.html#trailing-bang-foo>
- <https://docs.julialang.org/en/v1/manual/style-guide/#bang-convention>

SmallLang should instead remove the empty-parentheses marker for value-flow
calls whose only explicit input is the value on the left:

```smalllang
getName() => name
7 -> square => num
values -> len => count
```

Parentheses should remain only when the flow target receives additional
arguments beyond the primary left value:

```smalllang
values -> push(10)
values -> reserve(1024)
```

This keeps the common case closer to pipeline languages, while preserving a
familiar argument list when extra arguments are present. It should supersede the
current empty-parentheses-only value-flow call marker when the parser and
semantic checks are updated.

## Static Arrays

Static arrays are fixed-size owned values:

```smalllang
[1, 2, 3] => numbers          # inferred as [Int; 3]
[0; 8] => zeros               # inferred as [Int; 8]
```

The type form is:

```smalllang
[Int; 3]
[Text; 4]
```

Rules:

- `N` is a compile-time constant expression.
- All elements are initialized before the array value exists.
- Safe indexing is bounds-checked.
- A local static array is stored inline where the owner lives unless later
  lowering decides a better storage class.
- Moving a static array moves the whole owned value.
- Copying a static array is only implicit when the element type is `Copy`.
- Array elements are dropped deterministically when the array owner is dropped.

Storage placement:

- `[T; N]` is stored inline inside its owner.
- If the owner is a local binding, the array is a stack allocation candidate.
- If the owner itself is heap-allocated, the fixed array lives inline inside
  that heap allocation.
- `[T; N]` does not mean "always stack"; it means "fixed-size inline storage".
- Large fixed arrays can be explicitly moved to heap-owned storage later with a
  flow operation:

```smalllang
[0; 1000000] -> heap => buffer
```

The `heap()` operation should produce an owned heap value whose drop recursively
drops the contained fixed array and deallocates the heap block. It is not a
borrow and not a garbage-collected reference.

## Dynamic Arrays

Dynamic arrays are owned growable array values. The internal model is
Rust `Vec<T>`-like, but the SmallLang source surface uses array syntax:

```smalllang
[..] => mut values
[10, 20, ..] => mut seeded
values -> push(10)
values -> push(20)
values -> len => count
```

The runtime representation is conceptually:

```text
ptr: *mut T
len: Int
capacity: Int
```

Rules:

- A dynamic array owns its heap allocation.
- Moving a dynamic array moves the ownership of the buffer; the old binding
  cannot be used afterward.
- Dropping a dynamic array drops initialized elements and then deallocates the
  buffer.
- `push` may reallocate and therefore requires exclusive mutable access.
- Any slice or element borrow into a dynamic array must end before a mutating
  operation that may reallocate.
- A dynamic array has no implicit shared ownership and no hidden reference
  counting.

Storage placement:

- The dynamic array owner value is a small handle: pointer, length, and capacity.
- A local dynamic array binding can store that handle on the stack.
- The element buffer is always heap-allocated when capacity is nonzero.
- Moving a dynamic array moves the handle and transfers ownership of the heap
  buffer. It does not copy the buffer.
- Reallocation may move the element buffer, so active element/slice borrows must
  end before `push`, `reserve`, or any operation that can reallocate.

The type form is:

```smalllang
[Int; ..]
[Text; ..]
```

The literal form uses an open tail marker:

```smalllang
[1, 2, 3, ..] => mut values
```

The `..` marker means the sequence is not a closed fixed-size value anymore; it
is an owned growable array initialized with the listed elements.

The first implementation can support `[Int; ..]` only, but the language design
should keep `[T; ..]` generic from the start.

## Dictionaries

Dictionaries use braces because braces are a natural fit for key-value data and
because dynamic arrays stay in the `[]` family:

```smalllang
{ 1: 100, 2: 200 } => mut scores
scores[1] => firstScore
scores -> put(3, 300)
scores -> len => count
scores -> capacity => capacity
```

The final type form should be:

```smalllang
{Text: Int}
{Int: Text}
```

The implemented slice supports only `{Int: Int}`. It stores owned heap data
behind a small owner handle and frees that storage at the owning binding's drop
point. Lookup is checked: a missing key traps in the current runtime slice
instead of returning an arbitrary fallback value. A later `get` API should
return `Option<T>` once option types exist.

The empty literal `{}` is intentionally not accepted yet because it needs an
explicit type annotation or constructor form to avoid guessing key and value
types.

## Borrowed Slices

Most read-only array functions should accept a slice, not an owned dynamic
array:

```smalllang
sum values: &[Int] -> Int {
    values -> fold 0 total, value {
        total + value
    }
}

[1, 2, 3] => numbers
numbers -> sum => total
```

Slices are non-owning views:

```text
&[T]      ptr + len, read-only
&mut [T]  ptr + len, exclusive mutable
```

Rules:

- A slice never drops or deallocates the data it points to.
- A slice cannot outlive the owner it borrows from.
- Multiple shared slices may exist at the same time.
- One mutable slice may exist only when there are no other active borrows of the
  same data.
- Static arrays and dynamic arrays can both be borrowed as slices.

This keeps APIs flexible. A function that only reads elements can work with a
static array, a dynamic array, or a sub-slice without taking ownership.

## Mutability

SmallLang should keep immutable bindings as the default and introduce explicit
mutable bindings when arrays need in-place updates:

```smalllang
[..] => mut values
values -> push(10)
values -> push(20)

99 => values[1]
```

Rules:

- `value => name` creates an immutable binding.
- `value => mut name` creates a mutable binding.
- Assigning to `values[index]` requires `values` to be mutable or mutably
  borrowed.
- Borrowing a mutable binding as `&mut` is exclusive for the duration of the
  borrow.

This is Rust-inspired, but keeps the binding direction aligned with SmallLang's
existing flow syntax.

Immutable bindings can still produce changed values by moving the owner into a
new owner:

```smalllang
[1, 2, ..] => values
values -> append(3) => values
values -> updated(0, 9) => values

{ 1: 100, 2: 200 } => scores
scores -> updated(2, 250) => scores
```

These operations consume the source owner. After `values -> append(3) =>
values`, the old `values` owner is dead and the target binding receives the
moved owner. Reusing the same name is allowed only because the old owner is
consumed first.

The compiler lowers this as a unique-owner transform:

- `append` reuses the existing dynamic-array buffer when capacity remains. When
  capacity is full, it grows the buffer and frees the old allocation, matching
  the amortized O(1) shape of Rust `Vec` growth.
- Dynamic-array `updated` checks bounds and writes into the moved buffer in
  place.
- Dictionary `updated` reuses the `put` path: existing keys update in place,
  while new keys probe into a Swiss-style hash table and grow/rehash only when
  the load factor requires it.

### Optimization Note: Move-Consuming Transforms

The current implementation now avoids whole-container copies for ordinary
move-consuming transforms. The remaining performance work is no longer
"append copies every time"; it is the next layer of construction and sharing
design.

Known tradeoffs:

- Repeated append is amortized O(1), but there is no bulk-reserve or builder API
  yet.
- `updated` consumes the source owner, so keeping multiple immutable versions
  alive still requires a future persistent-container design.
- Dictionaries now use a scalar Swiss-style open-addressed hash table. Lookup,
  update, and insert are expected O(1), with a 75% grow threshold. The current
  implementation scans control bytes scalar-by-scalar; target-specific SIMD
  group scans remain a future optimization.

Recommended follow-up direction:

- Keep `push`/`put` as explicit in-place mutation for `=> mut` owners.
- Add a builder/transient form for bulk construction. A temporary unique builder
  can perform many local updates and then freeze into an immutable owner without
  copying on every step.
- If SmallLang later needs multiple immutable versions alive with efficient
  sharing, add a separate persistent container type based on a structural
  sharing design such as HAMT/RRB-vector. Do not hide this behind the ordinary
  growable array until ownership, reference tracking, and drop of shared nodes
  are statically modeled.
- Add benchmarks before replacing the lowering: repeated append, random update,
  fold/iteration over resulting arrays, and dictionary update/lookup.

## Indexing And Iteration

Indexing:

```smalllang
numbers[0] => first
99 => values[1]
```

Rules:

- Safe indexing checks bounds.
- Out-of-bounds access is a runtime failure in the first slice, not undefined
  behavior and not an arbitrary fallback value.
- A later `get` API can return an `Option<T>` once option types exist.

Iteration should extend the existing block-function model:

```smalllang
numbers -> each value {
    value -> println
}

numbers -> fold 0 total, value {
    total + value
} => sum
```

For `Int`, iteration can copy the item value into the block binding. For
non-`Copy` element types, the final design should decide whether `each` yields a
shared borrow by default or requires an explicit move/borrow iteration mode.

## Flow Calls With Additional Arguments

Flow target calls accept receiver-style additional arguments:

```smalllang
7 -> square
values -> push(10)
```

Arrays and dictionaries use this shape for mutating operations:

```smalllang
values -> push(10)
scores -> put(3, 300)
```

Design rule:

- The value on the left is still the primary first argument.
- Parentheses on the target may contain additional arguments.
- The target function signature decides whether the left value is moved,
  borrowed shared, or borrowed mutable.

Examples:

```text
len: &[T] -> Int
push: &mut [T; ..], T -> Unit
reserve: &mut [T; ..], Int -> Unit
```

The target function or intrinsic decides whether the left value is read,
mutably updated, or moved. In the implemented slice, `push` and `put` require a
named mutable binding so the compiler can update the tracked owner value and
still emit exactly one drop.

## Allocation And Failure

There is no garbage collector.

Dynamic arrays allocate through a selected target allocator. The first runtime
slice can treat allocation failure as a runtime failure/trap, matching the
current preference for explicit failure over silent fallback. Later, fallible
APIs such as `tryPush` can return `Result<Unit, AllocError>` once `Result` is in
the language.

Target notes:

- Windows/Linux native targets can use the selected runtime allocator.
- Browser WebAssembly needs a linear-memory allocator before dynamic arrays can
  be fully supported there.
- Unsupported allocation targets must fail clearly at compile time or runtime,
  not pretend to support dynamic arrays.

## Leak Prevention

SmallLang's safe language surface must make memory-leak freedom a compile-time
property. The goal is not "catch most leaks"; the goal is that any safe program
which could leak owned memory is rejected unless the compiler can prove a unique
owner will drop that memory on every exit path.

If the compiler cannot prove ownership, lifetime, and drop coverage, the program
does not compile. Features that cannot meet this bar must remain outside the
safe surface until an explicit static model exists for them.

Compile-time checks:

- every allocation is immediately captured by an owned value;
- every owned value has exactly one live owner;
- every normal control-flow path out of a scope runs the required drops;
- every move transfers the drop obligation exactly once;
- every moved-from binding is rejected until reinitialized;
- every slice or element borrow is proven not to outlive its owner;
- every operation that can reallocate is rejected while borrows into the buffer
  are live;
- every partially initialized aggregate has a statically known initialized
  prefix for cleanup on failure paths;
- every generic container type must define how its owned fields are dropped;
- every API that stores or returns a borrow must express the required lifetime
  relationship in the type system.

### Owned Values Drop Deterministically

Every owned value has exactly one owner at a time. When that owner leaves its
drop scope, the compiler emits cleanup for the value:

```smalllang
[..] => mut values
values -> push(10)
values -> push(20)

# leaving the scope drops 10 and 20, then deallocates the buffer
```

Drop rules:

- Static array drop recursively drops each initialized element.
- Dynamic array drop recursively drops all initialized elements and deallocates
  its heap buffer.
- Heap-owned fixed arrays produced by `heap()` drop the contained value, then
  deallocate the heap block.
- Drop runs on every normal control-flow exit path from the scope.
- A moved-from binding is considered uninitialized and is not dropped again.
- Partially initialized arrays track initialized elements so failure during
  construction drops only the initialized prefix.

### Move Prevents Double Free

Moving an owned array transfers the obligation to drop it:

```smalllang
[1, 2, 3, ..] => mut values
values -> takeArray => result

# values is moved and cannot be used or dropped here
```

After a move, the source binding is no longer usable unless it is assigned a new
owned value. This prevents both double-free and use-after-free.

### Borrow Prevents Dangling Views

Borrowed slices do not own storage and therefore never deallocate it. The
compiler must prove that a slice cannot outlive the owner it points into:

```smalllang
[1, 2, 3] => numbers
numbers -> slice => view

# view must end before numbers drops
```

Borrow rules:

- Multiple shared borrows are allowed while no mutable borrow is active.
- One mutable borrow is allowed only while no other borrow is active.
- A dynamic array cannot reallocate while any slice or element borrow into its
  buffer is active.
- A function cannot return a slice into a local array or local dynamic array
  unless the owner is also returned or otherwise proven to outlive the slice.

### No Untracked Allocation In Safe Code

The safe language surface must not expose raw allocator pairs such as
`alloc/free`, raw owning pointers, or APIs that allocate without returning an
owned value. Allocation enters safe code only through an owned type such as
`[T; ..]` or the owned result of `heap()`.

If unsafe/raw interop is added later, it should be isolated behind an explicit
unsafe boundary and must not be required for ordinary arrays.

### No Cyclic Ownership In The First Model

The first array model has no reference-counted ownership and no implicit shared
owners, so cyclic ownership leaks are not part of the safe surface. If shared
ownership is added later, it must be a separate explicit type with a clear cycle
story that preserves compile-time leak freedom. If that cannot be proven, the
feature does not belong in safe SmallLang.

### Intentional Leaks Are Not A Default Feature

Rust has APIs that can intentionally forget or leak values. SmallLang should not
add an equivalent operation to the safe surface initially. If an explicit
`leak` or `forget` operation is added for systems interop later, it should be
clearly marked as unsafe or advanced and must not be used by normal array code.

### Compile-Time Failure Examples

Returning a slice into a local owner is rejected:

```smalllang
makeView: -> &[Int] {
    [1, 2, 3] => numbers
    numbers -> slice
}
```

Mutating a dynamic array while a slice borrow is live is rejected:

```smalllang
[1, 2, 3, ..] => mut values
values -> slice => view
values -> push(4)
```

The `push` can reallocate the heap buffer, so the compiler must reject it while
`view` is still live.

Moving and then using the moved binding is rejected:

```smalllang
[1, 2, 3, ..] => mut values
values -> consume
values -> len => count
```

The `consume` call moves ownership. The later `len` call would read a
moved-from binding, so it fails at compile time.

## First Implementation Slice

The first useful implementation is now present:

- `[` and `]` tokens plus array literals.
- `[Int; N]` static arrays with inferred length.
- read-only indexing for `[Int; N]`.
- `[Int; ..]` dynamic arrays with inferred `Int` element type.
- `{Int: Int}` dictionary literals.
- read-only indexing for dynamic arrays and dictionaries.
- `len` and `capacity` receiver-flow operations.
- `push(value)` for mutable dynamic arrays.
- `put(key, value)` for mutable dictionaries.
- `append(value)` for move-consuming dynamic-array growth with buffer reuse
  when capacity remains.
- `updated(keyOrIndex, value)` for move-consuming dynamic-array or dictionary
  update.
- `array -> each item { ... }` for `Int` arrays.
- `array -> fold initial acc, item { ... }` for `Int` arrays.
- `value => mut name` mutable binding syntax.
- deterministic drop emission for heap-owning local dynamic arrays and
  dictionaries on supported native targets.

The next slice should add:

- builder/transient containers for efficient bulk immutable construction
- mutable indexing assignment
- borrowing static arrays and dynamic arrays as `&[Int]`
- passing `&[Int]` and `&mut [Int; ..]` to functions
- typed empty dictionaries
- generic `[T; N]`, `[T; ..]`, and `{K: V}` containers
- general owned container moves and returns with full drop-scope tracking

`pop`, `get`, and fallible allocation APIs should wait until `Option` and
`Result` exist.

## Non-Goals

- Do not add tracing garbage collection.
- Do not make dynamic arrays implicitly reference-counted.
- Do not make dynamic array assignment copy the backing buffer implicitly.
- Do not expose unchecked indexing in the safe language surface.
- Do not let a slice outlive the array or dynamic array it points into.
- Do not silently map unsupported allocation behavior to target-specific hacks.
