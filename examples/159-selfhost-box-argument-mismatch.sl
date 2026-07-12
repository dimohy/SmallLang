import smalllang.compiler.semantic.type_check as typeCheck

main {
    ["take value: box Text -> Int => 1\nmain { take(box 1) }", ~] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => expression
        "box argument = $(error.code),$(error.expectedOrigin),$(error.expectedSymbol),$(error.actualOrigin),$(error.actualSymbol),$expression,$(error.span.start),$(error.span.length)" -> println
    }
}
