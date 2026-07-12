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
   Function and method nodes additionally carry a secondary parameter/`self`
   token plus ownership flags (`1` move, `2` mutable borrow).
6. Reimplement `grammar build` itself in SL and require byte-identical output.
7. Remove the C# source generators only after the SL compiler reproduces all
   parser behavior and diagnostics.

## Semantic Bootstrap

`selfhost/semantic/symbols.sl` starts the semantic phase with a relocatable flat
symbol table. It collects nominal declarations, functions, fields, variants,
trait/impl members, methods, associated types, and generic clauses; connects
each entry to its nearest lexical owner; and stores name tokens, input/output
type AST indexes, and ownership flags without per-symbol heap allocation.
`selfhost/semantic/diagnostics.sl` performs UTF-8 byte-exact name comparison
inside each lexical owner and reports duplicate symbols with both symbol indexes
and the duplicate declaration's precise source span.
`selfhost/semantic/resolve.sl` walks from each name expression's nearest symbol
owner toward the root and resolves the first byte-equal declaration. Function
parameters and method `self` values are synthetic kind-35 symbols owned by their
declaration; unresolved names become code-2 semantic diagnostics.
`selfhost/semantic/types.sl` canonicalizes type annotations without allocating
normalized strings: it skips trivia, compares token kinds and UTF-8 payload
bytes, assigns stable canonical ids, and classifies named, slice, dynamic/fixed
array, dictionary, and box layouts. Array/box element and dictionary key/value
names are interned as canonical nominal ids; fixed arrays retain their value-
generic length token.
`selfhost/semantic/modules.sl` accepts multiple source texts, hashes qualified
namespace paths into deterministic 64-bit identities, and emits import edges
with source-module indexes, target identities, path spans, and alias tokens.
`selfhost/semantic/module_resolve.sl` resolves each edge to exactly one module
index and distinguishes resolved, missing, and duplicate target identities.
Declaration AST/symbol flags reserve bit 4 for explicit `public` visibility.
`selfhost/semantic/qualified.sl` matches the first segment of member-access AST
nodes to import aliases, resolves the target module, and distinguishes public,
missing, and non-public target symbols.
`selfhost/semantic/type_resolve.sl` walks each qualified path's AST ancestry to
its enclosing type annotation and links the source-local canonical type id to
the target module and nominal symbol. The link preserves missing/non-public
statuses instead of silently treating inaccessible declarations as local
types.
`selfhost/semantic/type_diagnostics.sl` turns those failed links into stable
multi-source diagnostics: code 3 is a missing imported nominal type and code 4
is a non-public imported nominal type. Diagnostic spans use the source-module
index as `fileId` and cover the complete qualified type annotation.
The diagnostic pass now consumes the unified nominal table, so an unresolved
unqualified local annotation produces the same code 3 as a missing imported
type instead of remaining a status-only row.

The generated module is bootstrap data, not the final parser implementation.
It deliberately makes the transition incremental and auditable.
Top-level declaration groups accept explicit newline boundaries before the next
group or `main`; this prevents a failed declaration repetition probe from
leaving a valid following entry point unreachable.
Each repeated declaration also owns its following newlines, so multiple structs,
enums, traits, impls, or functions in the same group remain parseable.

`selfhost/semantic/nominal_types.sl` presents one resolution table for builtin,
local, and imported named type annotations. Builtins receive stable table ids,
local declarations point at their module symbol, imported declarations retain
visibility status, and unresolved local names remain explicit status-2 rows.
`selfhost/semantic/type_check.sl` consumes that table for the first executable
type-checking slice. It infers integer and string literal return expressions,
compares them with declared function return annotations, and emits code 5 with
the exact mismatching expression span. Lexically resolved parameter names now
carry their declared nominal type into the same return check.
`selfhost/semantic/expression_types.sl` performs fixed-point bottom-up inference
over the flat AST. It seeds literals and resolved names, propagates Int through
arithmetic operators, and produces Bool for compatible equality/comparison and
logical operators. Return checking consumes the outermost inferred expression.
`selfhost/semantic/calls.sl` resolves local call targets to function symbols.
Resolved calls inherit the function's nominal return type, while type checking
compares the outermost argument expression with the declared input type and
emits code 6 on mismatch.
Calls with no matching local function symbol emit code 7 over the complete call
span, rather than remaining as untyped expressions that later passes could
silently ignore.
Binding symbols now acquire the inferred type of their value expression during
the same fixed-point pass. Every lexically resolved reference to the binding is
seeded with that identity, allowing later operators and calls to continue type
inference across local definitions.
Binary operators with two inferred but incompatible operand types now emit code
8 over the complete operator span. Nested parenthesized operands are selected
by nearest typed ancestry, and equivalent wrapper nodes are deduplicated.
Multi-source call resolution now correlates alias-qualified call paths with
import edges and target function symbols. Each result preserves local/imported
origin, target module identity, target source index, and public/non-public
status for later signature and return-type loading.
Generic clauses now preserve their first and optional second identifier as
lexical symbols owned by the function. Nominal resolution uses origin 3 for
those identities, so `T`/`E` annotations resolve by function scope and enter
the same return-type comparison used by concrete nominal types.
Call inference now runs inside the fixed-point expression pass. A generic
`T -> T` signature binds `T` to the inferred argument identity and substitutes
that identity into the call result, including when the argument type becomes
available only after operator or binding inference.
The identifier-shaped language literals `true` and `false` now seed stable Bool
type id 23 and are excluded from unresolved-name diagnostics. Logical operators
can therefore infer Bool and satisfy declared Bool return annotations in the
self-hosted checker.
Unary inference recognizes operator code -26 as `not` over Bool and token id 15
as numeric negation over Int. Incompatible operands emit code 8 with the unary
operator's complete span and expected/actual builtin identities.
`selfhost/semantic/composite_types.sl` resolves slice, dynamic/fixed array,
dictionary, and box component identities. Element/key/value slots preserve
builtin, local, or function-generic origins plus the fixed-array length token;
an unresolved component emits code 3 over the complete composite annotation.
Array expressions now lower as AST kind 37 and homogeneous dynamic literals
infer structural origin 13 plus their element identity. A `[T; ~] -> [T; ~]`
call substitutes the inferred element into its result, while concrete composite
input/return mismatches reuse codes 6 and 5.
Dictionary expressions now lower as AST kind 38. Homogeneous builtin entries
infer structural origin 15 with key/value ids, `{K: V} -> {K: V}` substitutes
both call-site identities, and concrete key/value mismatches participate in the
same argument and return diagnostics.
The `box` keyword now survives unary lowering as AST kind 23. Operand identity
is wrapped as structural origin 16, `box T -> box T` substitutes the call-site
element, and concrete boxed-element mismatches reuse codes 6 and 5. Duplicate
unary/box wrapper spans are inferred once.
Struct literals now lower as AST kind 39 and resolve to local or public imported
nominal symbols. Arrays and dictionaries can therefore carry user-defined
component ids. Imported call classification is restricted to qualified paths
before the call's left parenthesis, so qualified literals inside arguments are
not mistaken for the callee.
Struct field initializers now lower as AST kind 40. The checker resolves fields
through the local or imported owner symbol table, emits code 11 for an unknown
field, and code 12 when the inferred value identity differs from the declared
field type, preserving multi-source spans.
Each struct literal also covers every declared field symbol. Missing required
fields emit code 13 over the complete local or qualified literal and identify
the absent target field symbol.
Value member access now resolves a local/imported nominal base to its field
symbol and propagates named field types through fixed-point inference. An
unknown field emits code 14 at the member token. Return checking suppresses
cascading mismatches when an outer expression remains untyped.
Composite field annotations propagate their array, slice, fixed-array,
dictionary, and box component identities through the same member inference.
Postfix indexing lowers separately as AST kind 41, and indexing an inferred
array-like value propagates its element identity to the index expression.
Index checking emits code 15 for a non-array-like target and code 16 for a
non-`Int` index. The self-hosted lexer recognizes raw string delimiters of three
or more quotes, including embedded newlines and shorter quote runs.
Dictionary expression types now retain complete key and value
origin/module/symbol identities through bindings, generic calls, and member
access. Dictionary indexing validates the inferred key identity and propagates
the value identity as the result expression type; code 16 reports a mismatched
dictionary key as well as a non-`Int` array index.
Self-hosted compiler examples embed their input modules as raw multiline
strings. This keeps tested SL source readable as source, removes duplicated
newline escaping, and continuously exercises raw-string lexing in the bootstrap
compiler while the embedded text is consumed by the SL lexer and parser.
Expression inference loads a resolved imported function's return annotation
from the target source module. Call checking loads its input annotation from the
same target symbol, emits code 6 for cross-module argument mismatch, and code 9
for a non-public imported function.
Call checking also compares the signature's zero-or-one-input shape with call
syntax. Missing required arguments and parenthesized zero-input calls emit code
10 over the complete call, preserving SmallLang's property-call rule.
