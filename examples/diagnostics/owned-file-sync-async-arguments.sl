import sys.file as file

main {
    file.openWrite("artifacts/example-tests/unsupported-sync-argument.bin") => opened
    opened -> when {
        Result<file.FileWriter, Text>.Ok(writer) {
            writer -> syncAsync(1)
        }
        Result<file.FileWriter, Text>.Err(error) => error
    }
}
