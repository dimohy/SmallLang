import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        import sys.file as file

        scan: -> UIntSize {
            [file.SourceText; ~] => owners!
            0 => index!
            index! < (owners! -> len) -> while {
                owners![index!] -> len => sourceLength
                index! + 1 => index!
            }
            owners! -> len
        }

        main { }
        """,
        """
        namespace sys.file

        public struct SourceText {
            token: UInt64
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
