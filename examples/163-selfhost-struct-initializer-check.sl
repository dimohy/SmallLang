import smalllang.compiler.semantic.type_check as typeCheck

main {
    ["struct Point {\nx: Int\n}\nmain {\nPoint { x: true }\nPoint { y: 1 }\n}", ~] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => text
        "initializer error = $(error.code),$(error.expectedSymbol),$(error.actualSymbol),$text,$(error.span.start),$(error.span.length)" -> println
    }
}
