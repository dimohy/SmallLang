import sys.file as file

describeUInt16 result: Result<Option<UInt16>, Text> -> Text {
    result -> when {
        Result<Option<UInt16>, Text>.Ok(option) => option -> when {
            Option<UInt16>.Some(value) => "value"
            Option<UInt16>.None => "eof"
        }
        Result<Option<UInt16>, Text>.Err(error) => error
    }
}

describeBool result: Result<Option<Bool>, Text> -> Text {
    result -> when {
        Result<Option<Bool>, Text>.Ok(option) => option -> when {
            Option<Bool>.Some(value) => "value"
            Option<Bool>.None => "eof"
        }
        Result<Option<Bool>, Text>.Err(error) => error
    }
}

main {
    "artifacts/example-tests/86-generic-binary-reader.bin" -> file.openWriter
    UInt16(513) -> file.write
    true -> file.write
    file.closeWriter

    "artifacts/example-tests/86-generic-binary-reader.bin" -> file.openReader
    file.read<UInt16> -> describeUInt16 -> println
    file.read<Bool> -> describeBool -> println
    file.read<UInt16> -> describeUInt16 -> println
    file.closeReader

    "artifacts/example-tests/86-generic-binary-reader.bin" -> file.openWriter
    UInt8(7) -> file.write
    file.closeWriter
    "artifacts/example-tests/86-generic-binary-reader.bin" -> file.openReader
    file.read<UInt16> -> describeUInt16 -> println
    file.closeReader

    "artifacts/example-tests/86-generic-binary-reader.bin" -> file.openWriter
    UInt8(2) -> file.write
    file.closeWriter
    "artifacts/example-tests/86-generic-binary-reader.bin" -> file.openReader
    file.read<Bool> -> describeBool -> println
    file.closeReader
}
