import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        make: -> {Bool: Text} => {true: "yes", false: "no"}
        lookup values: {Bool: Text} -> Text => values![false]
        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
