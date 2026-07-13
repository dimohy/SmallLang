import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        struct Inner {
            values: [Int; ~]
        }

        struct Outer {
            inner: Inner
            marker: Int
        }

        consume outer: move Outer -> Int {
            7
        }

        dropLocal flag: Bool -> Int {
            Outer {
                inner: Inner {
                    values: [1, 2, ~]
                }
                marker: 3
            } => outer!
            11
        }

        moveLocal flag: Bool -> Int {
            Outer {
                inner: Inner {
                    values: [4, 5, ~]
                }
                marker: 6
            } => outer!
            outer! -> consume => result
            result
        }

        early flag: Bool -> Int {
            Outer {
                inner: Inner {
                    values: [8, 9, ~]
                }
                marker: 10
            } => outer!
            flag -> if {
                13 -> return
            }
            14
        }

        main {
            dropLocal(true) => dropped
            moveLocal(true) => moved
            early(true) => earlyResult
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
