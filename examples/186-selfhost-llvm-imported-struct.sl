import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        namespace sample.model
        public struct Pair {
            left: Int
            right: Int
        }
        public make seed: Int -> Pair => Pair {
            left: seed
            right: 2
        }
        """,
        """
        namespace app.main
        import sample.model as model
        forward value: model.Pair -> model.Pair => value
        create seed: Int -> model.Pair => model.make(seed)
        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
