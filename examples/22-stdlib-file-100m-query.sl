main {
    "artifacts/random-sorted-100m.i64" -> openIntReader
    500000000 -> closestInt => closest
    "closest = $closest" -> println
    closeIntReader()
}
