import sys.file as file

main {
    "artifacts/example-tests/unsupported.bin" -> file.openWriter
    "not a scalar" -> file.write
}
