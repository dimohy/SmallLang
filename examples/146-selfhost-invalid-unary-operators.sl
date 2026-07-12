import smalllang.compiler.semantic.type_check as typeCheck

main {
    ["main {\nnot 1\n-true\n}", ~] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => expression
        "unary error = $(error.code),$(error.expectedSymbol),$(error.actualSymbol),$expression,$(error.span.start),$(error.span.length)" -> println
    }
}
