import sys.file as file

main {
    file.openWrite("artifacts/example-tests/unsupported-async-write.bin") => opened
    opened -> when {
        Result<file.FileWriter, Text>.Ok(writer) {
            writer -> writeAtAsync("unsupported", 0)
        }
        Result<file.FileWriter, Text>.Err(error) => error
    }
}
