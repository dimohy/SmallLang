import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        make: -> {Int: Int} => {1: 10, 2: 20}
        forward values: move {Int: Int} -> {Int: Int} => values
        lookup values: {Int: Int} -> Int => values![2]
        discard: -> Int {
            {3: 30, 4: 40}
            0
        }
        consume values: move {Int: Int} -> Int => 0
        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
