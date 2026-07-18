import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        import smalllang.compiler.ast as ast

        inspect: -> UIntSize {
            "main {}" -> ast.lower => nodes!
            nodes![0].start => firstStart
            UIntSize(10) => best!
            true -> if {
                firstStart < best! -> if {
                    firstStart => best!
                }
            }
            best!
        }

        main { }
        """,
        """
        namespace smalllang.compiler.ast

        public struct AstNode {
            kind: Int
            parent: Int
            firstToken: Int
            tokenCount: Int
            firstChild: Int
            childCount: Int
            flags: Int
            nameToken: Int
            typeNode: Int
            valueNode: Int
            start: UIntSize
            length: UIntSize
        }

        public lower source: Text -> [AstNode; ~] {
            [AstNode { kind: 0, parent: -1, firstToken: 0, tokenCount: 0, firstChild: 0, childCount: 0, flags: 0, nameToken: -1, typeNode: -1, valueNode: -1, start: UIntSize(7), length: UIntSize(2) }, ~]
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
