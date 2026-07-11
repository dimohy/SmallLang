import sys.file as file

main {
    "artifacts/example-tests/85-generic-binary-writer.bin" -> file.openWriter
    UInt8(65) -> file.write
    UInt16(258) -> file.write
    UInt32(16_909_060) -> file.write
    true -> file.write
    file.closeWriter

    map read "artifacts/example-tests/85-generic-binary-writer.bin" => bytes
    bytes -> len => count
    "binary byte count = $count" -> println
    bytes -> each byte {
        "binary byte = $byte" -> println
    }
}
