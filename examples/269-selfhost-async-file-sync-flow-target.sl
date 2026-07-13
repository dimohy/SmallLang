import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.semantic.calls as calls

main {
    """
    struct FileWriter {
        token: UInt64
    }

    syncAsync writer: FileWriter -> async Int {
        1
    }

    run: -> async Int {
        FileWriter { token: UInt64(0) } => writer
        writer -> syncAsync => pending
        pending -> await
    }

    main {
        run => pending
    }
    """ => source

    source -> calls.resolve => resolved!
    [source, ~] => sources!
    sources! -> typedIr.suspensions => points!
    resolved! -> len => callCount
    points! -> len => suspensionCount
    (callCount == 3 and suspensionCount == 1) -> if {
        "async file sync calls=1,suspensions=1" -> println
    } else {
        "async file sync failed" -> println
    }
}
