main {
    "artifacts/random-sorted-demo.i64" -> openIntWriter
    20260708 -> seedRandom

    1..1000 -> each bucket {
        bucket - 1 => zeroBased
        zeroBased * 10 => base
        10 -> randomBelow => offset
        base + offset + 1 => value
        value -> writeInt
    }

    closeIntWriter()
}
