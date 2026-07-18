import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        struct Request {
            caller: Int
            binding: Int
        }

        make captures: [Int; ~] -> Int {
            captures => local!
            0 => index!
            0 => result!
            index! < (local! -> len) -> while {
                Request { caller: 7, binding: local![index!] } => request
                request.binding => result!
                index! + 1 => index!
            }
            result!
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
