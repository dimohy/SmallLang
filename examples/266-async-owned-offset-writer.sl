import sys.file as file

writeSucceeded result: Result<Unit, Text> -> Bool => result -> when {
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

writeFixture path: Text -> async Bool {
    file.openWrite(path) => opened
    opened -> when {
        Result<file.FileWriter, Text>.Ok(writer) {
            writer -> writeAtAsync(UInt16(1027), 3) => thirdTask
            writer -> writeAtAsync<UInt16>(513, 0) => firstTask
            writer -> writeAtAsync(UInt16(999), 5) => cancelledTask
            cancelledTask -> cancel
            thirdTask -> await -> writeSucceeded => thirdOk
            firstTask -> await -> writeSucceeded => firstOk
            firstOk and thirdOk
        }
        Result<file.FileWriter, Text>.Err(error) => false
    }
}

main {
    "artifacts/example-tests/266-async-owned-offset-writer.bin" -> writeFixture => writing
    writing -> await => writesOk

    file.openRead("artifacts/example-tests/266-async-owned-offset-writer.bin") => opened
    opened -> when {
        Result<file.File, Text>.Ok(reader) {
            reader -> readAt<UInt16>(0) -> valueOrZero => first
            reader -> readAt<UInt16>(3) -> valueOrZero => third
            reader -> readAt<UInt16>(5) -> valueOrZero => cancelled
            writesOk and first + third == 1540 and cancelled == 0 -> if {
                "writeAtAsync sum=1540,cancelled=0"
            } else {
                "unexpected"
            }
        }
        Result<file.File, Text>.Err(error) => error
    } -> println
}
