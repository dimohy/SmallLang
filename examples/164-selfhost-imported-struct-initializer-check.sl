import smalllang.compiler.semantic.type_check as typeCheck

main {
    ["namespace sample.shapes\npublic struct Point {\nx: Int\n}", "namespace app.main\nimport sample.shapes as shapes\nmain { shapes.Point { x: true } }", ~] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => text
        "imported initializer = $(error.code),$(error.expectedSymbol),$(error.actualSymbol),$text,$(error.span.fileId),$(error.span.start),$(error.span.length)" -> println
    }
}
