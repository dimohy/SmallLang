import smalllang.compiler.llvm.text as llvm
import sys.process as process

collect: -> [Text; ~] uses Process {
    process.arguments => arguments
    arguments -> len => argumentCount
    [Text; ~] => sources!
    UIntSize(2) => index!
    index! < argumentCount -> while {
        arguments[index!] => source
        sources! -> push(source)
        index! + UIntSize(1) => index!
    }
    sources!
}

emitLinux: -> Unit uses Console, Process {
    collect => sources!
    sources! -> llvm.emitLinux
}

emitWasm: -> Unit uses Console, Process {
    collect => sources!
    sources! -> llvm.emitWasm
}

emitWindows: -> Unit uses Console, Process {
    collect => sources!
    sources! -> llvm.emit
}

main {
    process.arguments => arguments

    arguments[1] -> len => targetLength
    targetLength == UIntSize(5) -> if {
        emitLinux
    } else {
        targetLength == UIntSize(4) -> if {
            emitWasm
        } else {
            emitWindows
        }
    }
}
