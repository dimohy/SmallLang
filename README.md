<p align="center">
  <img src="assets/smalllang-logo.svg" alt="SmallLang logo" width="160" />
</p>

# SmallLang

SmallLang is a tiny native language experiment focused on simple syntax, fast
compiler structure, and LLVM-backed executable generation.

It currently accepts a compact `.sl` language slice, lowers it to LLVM IR, and
links minimal Windows x64 or Linux x64 executables. The language favors explicit
value flow with `value -> target` syntax.

## Quick Look

- `.sl` source files
- value-flow bindings and calls, such as `"text" -> print()` and
  `7 -> square() -> num`
- `main { ... }` or omitted `main` with top-level executable statements
- block-function calls such as `1..9 -> each i { ... }`
- flow-oriented `if` and `when` conditionals
- a SmallLang standard library under `stdlib/sys`
- source-generated lexer/parser code from compact grammar files
- LLVM-backed Windows x64 and Linux x64 executable output

## Example

```smalllang
getName: -> Text {
    "dimohy"
}

square: Int -> Int {
    it * it
}

main {
    getName() -> name
    7 -> square() -> num
    "Hello, {name}. square = {num}" -> print()
}
```

Verified output:

```text
Hello, dimohy. square = 49
```

Top-level executable statements can omit the `main` wrapper:

```smalllang
getName() -> name
7 -> square() -> num
"Hello, {name}. square = {num}" -> sys.io.print()
```

A range can flow into a block function:

```smalllang
"n = ? " -> readInt() -> n

1..9 -> each i {
    n * i -> value
    "{n} x {i} = {value}" -> println()
}
```

Subject-style conditionals keep the tested value on the left:

```smalllang
95 -> score

score -> when {
    90..100 -> "A"
    80..89 -> "B"
    else -> "Needs practice"
} -> grade
```

## Run A Sample

```powershell
.\scripts\smalllang.ps1 -Source examples\01-function-basic-hello.sl -Output artifacts\01-function-basic-hello.exe -KeepTemps
.\artifacts\01-function-basic-hello.exe
```

On first use, the script downloads LLVM 22.1.8 into `.tools`. LLVM binaries,
build outputs, and generated executables are intentionally ignored by Git.

## Documentation

- [Getting started and implementation guide](docs/GETTING_STARTED.md)
- [Language specification](docs/SPEC.md)
- [Decision log](docs/DECISIONS.md)
- [VS Code syntax extension](tools/vscode-smalllang/README.md)
- [Example programs](examples)

## Repository Map

- `examples`: cumulative `.sl` programs that track the grammar progression
- `stdlib/sys`: standard library modules written in SmallLang
- `syntax`: lexer and grammar rule sources
- `src/SmallLang.Compiler`: compiler CLI, semantic lowering, and LLVM codegen
- `src/SmallLang.Compiler.Generators`: source generators for lexing/parsing
- `tests/SmallLang.ExampleTests`: expected stdout test runner
- `tools/vscode-smalllang`: local VS Code syntax-highlighting extension

## License

SmallLang is licensed under the [Apache License 2.0](LICENSE).
