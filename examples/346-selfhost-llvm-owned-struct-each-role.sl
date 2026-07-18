import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        struct Batch {
            nodes: [Int; ~]
        }

        merge: -> Int {
            [Batch; ~] => batches!
            batches! -> push(Batch { nodes: [1, 2, ~] })
            0 => sum!
            batches! -> each batch {
                0 => index!
                index! < (batch.nodes -> len) -> while {
                    sum! + batch.nodes[index!] => sum!
                    index! + 1 => index!
                }
            }
            sum!
        }

        main {
            merge
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
