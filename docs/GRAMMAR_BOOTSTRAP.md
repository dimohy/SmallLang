# SmallLang Grammar Bootstrap

Status: table generation, SL lexer, and SL parser recognizer implemented
Updated: 2026-07-12

## Goal

SmallLang must eventually compile its own lexer and parser without requiring a
C# source generator, a Rust-style macro system, or opaque generated parser
logic. The canonical inputs remain the concise files
`syntax/smalllang.lexer` and `syntax/smalllang.grammar`.

The selected architecture is:

```text
lexer DSL + EBNF grammar
    -> grammar build
    -> deterministic lexer descriptors + parser bytecode in ordinary .sl
    -> one reusable SL lexer/parser VM
    -> lossless CST
    -> ordinary SL CST-to-AST lowering
```

Grammar rules contain no embedded C#, SL, or target-specific semantic actions.
This keeps the grammar readable and prevents the grammar language from becoming
a second general-purpose programming language. AST construction and validation
remain normal, testable SL functions.

## Bootstrap Command

The bootstrap compiler now exposes an explicit command:

```powershell
dotnet run --project src/SmallLang.Compiler -- grammar build `
  syntax/smalllang.lexer syntax/smalllang.grammar `
  -o syntax/generated/smalllang_grammar.sl
```

The checked-in generated module contains:

- token and lexer-rule names;
- lexer rule kinds, target token ids, and literal indexes;
- lexer literal text;
- parser rule names and entry offsets;
- keyword text;
- a compact parser instruction stream;
- the start-rule id and a source SHA-256 comment.

The full example runner regenerates this module into `artifacts` and compares
its bytes with the checked-in file. A grammar edit therefore cannot silently
leave stale tables behind.

## Parser VM Instruction Set

| Code | Operation | Meaning |
| ---: | --- | --- |
| 0 | `return` | Complete the current rule |
| 1 | `token id` | Match one token kind |
| 2 | `keyword token,text` | Match an identifier token with exact text |
| 3 | `call rule` | Enter another grammar rule |
| 4 | `choice target` | Save input/stack state and try an alternative |
| 5 | `commit target` | Discard the saved alternative after success |
| 6 | `jump target` | Continue at an absolute instruction offset |
| 7 | `lookahead token` | Check a token without consuming it |

`?`, `*`, `+`, grouping, and alternatives compile to these operations. The
current SmallLang grammar already represents expression precedence as layered
non-left-recursive rules, so it does not require a more complicated LR
generator for the first self-hosted parser.

## Lexer Descriptor Kinds

The first descriptor format represents whitespace, line comments, identifiers,
quoted strings, numbers, newlines, end-of-input, and fixed literals. The SL
lexer VM will implement longest fixed-literal matching before the named scalar
patterns and will attach byte-offset spans to every token.

## CST Before AST

The table VM will initially produce a lossless concrete syntax tree containing
tokens, trivia, byte spans, rule ids, and explicit error nodes. A separate SL
lowering layer will build the compiler AST. This same CST can later power the
formatter and language server, avoiding a second editor-only grammar.

## Remaining Bootstrap Stages

1. Reusable byte-offset `SourceSpan` and `SyntaxToken` are present in
   `selfhost/syntax/source.sl`; add lossless CST node and diagnostic types.
2. Expand `selfhost/syntax/lexer.sl` from the executable bootstrap lexer to full
   escape, number, trivia, and error parity with the current C# lexer.
3. `selfhost/syntax/parser.sl` now emits deterministic enter-rule, exit-rule,
   token, and outcome events while recognizing input. Backtracking rewinds and
   overwrites abandoned events before the result escapes.
4. `selfhost/syntax/cst.sl` materializes the successful event stream into flat
   green nodes with stable indexes, parent links, token ranges, and UTF-8 byte
   spans. The lexer assigns generated trivia token ids to contiguous whitespace
   and line comments; the parser ignores them for grammar matching while
   retaining them in CST events. Unknown bytes receive a generated `Invalid`
   token id, remain in CST spans, and force a rejected parse. Add structured
   recovery for malformed sequences. `selfhost/syntax/diagnostics.sl` reports
   invalid bytes and the furthest unexpected token with stable UTF-8 spans. It
   also exposes the deduplicated set of token kinds and grammar keywords
   expected by every alternative at that furthest position.
5. Write CST-to-AST lowering in ordinary SL modules.
6. Reimplement `grammar build` itself in SL and require byte-identical output.
7. Remove the C# source generators only after the SL compiler reproduces all
   parser behavior and diagnostics.

The generated module is bootstrap data, not the final parser implementation.
It deliberately makes the transition incremental and auditable.
