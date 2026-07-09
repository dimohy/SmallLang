# Container Optimization Plan - 2026-07-09

## Sources Checked

- Abseil Swiss Tables design notes: control-byte metadata, H1/H2 split, SIMD candidate filtering.
- Go SwissTable map article: 8-slot groups, 64-bit control word, load factor/probe length behavior.
- .NET `Dictionary<TKey,TValue>` source: value-type comparer specialization, bucket lookup, `FastMod` setup.
- Rust `hashbrown`: SwissTable adoption in Rust HashMap, faster hasher option, lower overhead claim.

## Applied Now

1. Replaced dictionary `% capacity` slot wrapping with `and (capacity - 1)`.
   SmallLang dictionary capacities are powers of two, so this is equivalent and cheaper.

2. Replaced `found`/`slot` stack alloca/load/store in dictionary find with SSA phi values.
   This removes a stack round-trip from put and lookup paths.

3. Reused the already computed H2 byte on insert.
   The previous put path recomputed the hash just to store the control byte.

4. Removed division from dictionary entry-offset calculation.
   With the current capacity model, only capacity `4` needs padding to `8`; all larger powers of two use `capacity` directly as the entry offset.

5. Replaced the heavy SplitMix-style integer hash with a cheaper integer-specialized multiplicative hash.
   This reduces both `put` and lookup cost because both paths hash every key.

6. Added a lookup-only dictionary probe path.
   Lookup now loads the matched value directly instead of calling the generic find path and then reloading by slot.

7. Replaced dictionary control-byte zeroing with LLVM `memset`.
   This keeps dictionary allocation initialization in a form the native optimizer can lower efficiently.

8. Switched the Windows native linker optimization level to `-O3`.
   This materially improves tight scan/probe loops in the generated native executable.

9. Raised dictionary grow threshold from 3/4 to 7/8.
   This follows the load-factor direction used by SwissTable-family designs and reduces grow/rehash pressure without pushing the current scalar linear probing too close to full.

## Measured And Rejected For Now

- Hoisting dictionary entries-pointer calculation out of probe loops increased the measured median. The extra live values appear to hurt more than the removed address calculation helps in the current IR shape.
- Replacing dynamic-array copy loops with LLVM `memcpy` slowed array build in this Windows native path. The manual loop stays for now.
- A 15/16 dictionary grow threshold was slightly faster on the sequential-key benchmark, but it is too aggressive for the current scalar linear probing design. Keep 7/8 until group probing is implemented.

## Result

Large benchmark median:

| Section | Before | After | Change |
| --- | ---: | ---: | ---: |
| Dictionary build | 422 ms | 188 ms | 55.5% faster |
| Dictionary lookup | 234 ms | 94 ms | 59.8% faster |

Current median from the same workload:

| Section | SmallLang after | C# | Go | Rust |
| --- | ---: | ---: | ---: | ---: |
| Dictionary build | 188 ms | 156 ms | 1,187 ms | 357 ms |
| Dictionary lookup | 94 ms | 55 ms | 259 ms | 150 ms |

## Next Targets

1. Implement real group probing.
   Current SmallLang is still scalar slot-by-slot probing. SwissTable designs compare a group of control bytes first, then only compare keys for matching H2 candidates. This should be the next major lookup optimization.

2. Add mirrored or sentinel control bytes.
   This avoids wrap handling at group boundaries and makes group probing simpler.

3. Make native optimization mode explicit and cross-platform.
   Windows native output now uses `-O3`; this should become a deliberate release/debug compiler option and be mirrored for Linux/WASM where appropriate. Runtime helpers still use conservative attributes and should be reviewed separately.

4. Add dictionary pre-sizing syntax or API.
   Benchmarks and real workloads that know the approximate entry count should avoid repeated grow/rehash cycles.

5. Add allocator counters.
   SmallLang currently reports estimated live backing storage. Allocation-count and total-allocated-byte counters would make memory benchmarking comparable to C# allocation metrics.
