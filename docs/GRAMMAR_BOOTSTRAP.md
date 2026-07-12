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
   expected by every alternative at that furthest position. The parser emits a
   panic-mode error-range event through the next newline, right brace, or EOF;
   the CST materializer turns it into a `ruleId: -1` green node and preserves
   the complete file envelope for later recovery-aware lowering.
5. `selfhost/syntax/ast.sl` now lowers source, namespace/import, nominal
   declaration, implementation, function/signature, main, binding, flow/call,
   type, literal, name, and path rules into a flat parent-indexed AST. It skips
   non-semantic CST wrappers, reconnects each node to its nearest AST ancestor,
   and trims trivia from payload spans. Equality, comparison, additive,
   multiplicative, unary, and box nodes are emitted only when their operator is
   present; each records the exact operator token and preserves precedence in
   AST parent links. Logical `or`, `and`, and `not` plus `box` use the same
   negative keyword-code convention as syntax diagnostics, with exact payload
   token indexes. Expand this ordinary SL lowering to declaration
   parameters and every remaining rule. A second lowering pass now resolves
   namespace/import/impl/function names from their nearest path child and
   extracts nominal type names, struct fields, enum variants, trait members,
   associated types, methods, and generic clauses directly from header tokens.
6. Reimplement `grammar build` itself in SL and require byte-identical output.
7. Remove the C# source generators only after the SL compiler reproduces all
   parser behavior and diagnostics.

The generated module is bootstrap data, not the final parser implementation.
It deliberately makes the transition incremental and auditable.
