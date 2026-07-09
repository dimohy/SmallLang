# SmallLang Benchmarks

## Container Throughput

`containers-throughput.sl` measures the current array and dictionary container paths with the runtime timer exposed as `nowMillis()`.

Run:

```powershell
.\scripts\smalllang.ps1 -Source benchmarks\containers-throughput.sl -Output artifacts\benchmarks\containers-throughput.exe -KeepTemps
.\artifacts\benchmarks\containers-throughput.exe
```

Run the C# comparison baseline:

```powershell
dotnet build benchmarks\csharp\ContainersThroughput\ContainersThroughput.csproj -c Release --nologo
dotnet run --project benchmarks\csharp\ContainersThroughput\ContainersThroughput.csproj -c Release --no-build
```

Run the Go comparison baseline:

```powershell
Push-Location benchmarks\go\containers-throughput
& 'C:\Program Files\Go\bin\go.exe' build -o ..\..\..\artifacts\benchmarks\go-containers-throughput.exe .
Pop-Location
.\artifacts\benchmarks\go-containers-throughput.exe
```

Run the Rust comparison baseline:

```powershell
& "$env:USERPROFILE\.cargo\bin\cargo.exe" build --release --manifest-path benchmarks\rust\containers-throughput\Cargo.toml
.\benchmarks\rust\containers-throughput\target\release\containers-throughput.exe std
.\benchmarks\rust\containers-throughput\target\release\containers-throughput.exe hashbrown
```

Reported fields:

- `*Millis`: elapsed wall-clock milliseconds for the measured section.
- `*OpsPerSecond`: integer items-per-second throughput.
- `*Length` and `*Capacity`: final container size and backing capacity.
- `*BackingBytes`: estimated backing storage bytes for the container payload.
- `*AllocatedBytes`: C# managed allocation bytes for the measured section.
- `*Checksum`: correctness guard so the measured work cannot be removed later.

The benchmark follows common public benchmark metrics: elapsed time, input/iteration count, items-per-second throughput, and memory size. Exact allocation counters should be added when the SmallLang runtime exposes allocator statistics.

`containers-smoke.sl` is a smaller correctness check for `nowMillis()`, mutable array push, dictionary put, fold, and lookup.
