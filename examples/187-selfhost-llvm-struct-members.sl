import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        namespace sample.model
        public struct Pair {
            left: Int
            right: Int
        }
        public first pair: Pair -> Int => pair.left
        """,
        """
        namespace app.main
        import sample.model as model
        second pair: model.Pair -> Int => pair.right
        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
