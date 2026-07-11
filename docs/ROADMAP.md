# SmallLang Implementation Roadmap

Status: active
Updated: 2026-07-12

Every completed slice must add cumulative `.sl` examples, keep safe-code leak
freedom statically provable, build with zero warnings, and pass the complete
example suite. LLVM allocation assertions are required when placement or
ownership behavior is part of the feature.

## Memory Placement

- [x] Deterministic drop for owned dynamic arrays and dictionaries
- [x] Readonly, `mut`, and `move` container parameter modes
- [x] Lazy allocation for empty dynamic containers
- [x] Top-level readonly array and dictionary stack promotion
- [x] Function-entry stack-frame allocation plan
- [x] Binding lifetime intervals and non-overlapping slot reuse
- [x] Peak concurrent stack budget instead of cumulative candidate bytes
- [x] `llvm.lifetime.start/end` emission
- [x] Nested `if`, `when`, block, and loop stack promotion
- [x] Stack planning for local/standard-library inline function bodies
- [x] Function-entry placement for mutable container handles
- [x] Function-entry placement for small fixed arrays
- [x] Large fixed-array automatic heap placement

## User Types

- [x] Fixed-width `Int8/16/32/64`, `UInt8/16/32/64`, and `Float32/64` primitives
- [x] Embedded-friendly `Int = Int32`, `Long = Int64`, `Float = Float32`, and
  `Double = Float64` source aliases
- [x] Target-pointer-width `Size` and `UIntSize` ABI types
- [x] Explicit checked numeric conversions and same-type arithmetic
- [x] Nominal `struct` value types with exact field layout
- [x] Complete field initialization and direct field access
- [x] Recursive move/drop generation for owned fields
- [x] `impl` blocks and associated constructors
- [x] Readonly `self`, `mut self`, and `move self` method receivers
- [x] Object-oriented dot-call syntax without class inheritance
- [x] Payload `enum` values and exhaustive `when`
- [x] Standard `Option<T>` and `Result<T, E>` foundations
- [x] Move-aware postfix `?` propagation for owned `Result<T, E>` payloads

## Traits And Generics

- [x] Nominal `trait` declarations and explicit `impl`
- [x] Static trait dispatch as the default
- [x] Checked type generics with trait bounds
- [x] Two-parameter generics with associated-type inference
- [x] Fixed arrays with distinct `Int` and `Text` element layouts
- [x] Parametric fixed arrays for copyable user `struct` and `enum` values
- [x] Element-wise recursive drop for owned fixed-array elements
- [x] Parametric growable arrays with typed push/grow/index/drop
- [x] Parametric dictionaries with typed hash/equality, put/grow/index/drop
- [x] Readonly, `mut`, and `move` function ABI for parametric dictionaries
- [x] Readonly, `mut`, and `move` function ABI for parametric dynamic arrays
- [x] Type-preserving `each` for fixed and dynamic parametric arrays
- [x] Type-preserving `eachKey` and `eachValue` for parametric dictionaries
- [x] Static `Hash`/`Eq` trait dispatch for copyable nominal dictionary keys
- [x] Contextual struct-key literals in dictionary indexing
- [x] Compile-time `Int` value generics, `[Int; N]` parameters, and specialization
- [x] Monomorphization with deterministic ownership/drop behavior for inline values
- [x] Associated types and equality constraints for container and iterator contracts
- [x] Explicit `box T` for stable identity or recursive-size breaks
- [ ] Explicit `dyn Trait` and vtables for runtime polymorphism

## Design Direction

SmallLang combines Rust-style ownership, `struct`/`enum`/`trait`/`impl`, and
explicit dynamic dispatch with familiar object-oriented method calls and
encapsulation. Values and static dispatch are the defaults. Reference identity,
heap boxing, and runtime polymorphism must be explicit. Class inheritance and
implicit null are outside the intended safe language surface.
