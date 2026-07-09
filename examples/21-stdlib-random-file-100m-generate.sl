main {
    "artifacts/random-sorted-100m.i64" -> openIntWriter
    20260708 -> seedRandom

    1..100000000 -> each bucket {
        bucket - 1 => zeroBased
        zeroBased * 10 => base
        10 -> randomBelow => offset
        base + offset + 1 => value
        value -> writeInt
    }

    closeIntWriter()
}
