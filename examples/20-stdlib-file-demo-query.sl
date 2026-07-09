main {
    "artifacts/random-sorted-demo.i64" -> openIntReader
    5000 -> closestInt => closest
    "closest = $closest" -> println
    closeIntReader()
}
