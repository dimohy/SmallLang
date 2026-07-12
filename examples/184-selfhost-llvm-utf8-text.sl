import smalllang.compiler.llvm.text as llvm

main {
    [
        """"
        korean: -> Text => "한글"
        main { }
        """",
        ~
    ] => sources!
    sources! -> llvm.emit
}
