import sys.file as file

operationSucceeded result: Result<Unit, Text> -> Bool => result -> when {
    Result<Unit, Text>.Ok(done) => true
    Result<Unit, Text>.Err(error) => false
}

valueOrZero result: Result<Option<UInt16>, Text> -> Int => result -> when {
    Result<Option<UInt16>, Text>.Ok(option) => option -> when {
        Option<UInt16>.Some(value) => Int(value)
        Option<UInt16>.None => 0
    }
    Result<Option<UInt16>, Text>.Err(error) => 0
}

writeAndSync path: Text -> async Bool {
    file.openWrite(path) => opened
    opened -> when {
        Result<file.FileWriter, Text>.Ok(writer) {
            writer -> writeAtAsync(UInt16(513), 0) => firstTask
            writer -> writeAtAsync(UInt16(1027), 3) => thirdTask
            firstTask -> await -> operationSucceeded => firstOk
            thirdTask -> await -> operationSucceeded => thirdOk

            writer -> syncAsync => syncTask
            syncTask -> await -> operationSucceeded => syncOk
            writer -> syncAsync => cancelledSync
            cancelledSync -> cancel
            firstOk and thirdOk and syncOk
        }
        Result<file.FileWriter, Text>.Err(error) => false
    }
}

main {
    "artifacts/example-tests/268-async-owned-file-sync.bin" -> writeAndSync => writing
    writing -> await => durable

    file.openRead("artifacts/example-tests/268-async-owned-file-sync.bin") => opened
    opened -> when {
        Result<file.File, Text>.Ok(reader) {
            reader -> readAt<UInt16>(0) -> valueOrZero => first
            reader -> readAt<UInt16>(3) -> valueOrZero => third
            durable and first + third == 1540 -> if {
                "syncAsync sum=1540,cancelled=true"
            } else {
                "unexpected"
            }
        }
        Result<file.File, Text>.Err(error) => error
    } -> println
}
