main {
    50000000 => arrayN
    20 => arrayScanRepeats
    8000000 => dictN

    nowMillis() => arrayBuildStart
    [..] => mut values
    1..arrayN -> each i {
        values -> push(i)
    }
    nowMillis() => arrayBuildEnd

    nowMillis() => arrayScanStart
    1..arrayScanRepeats -> fold 0 repeatedTotal, turn {
        values -> fold 0 total, value {
            total + value
        } => scanChecksum
        repeatedTotal + scanChecksum
    } => arrayChecksum
    nowMillis() => arrayScanEnd

    nowMillis() => dictBuildStart
    { 0: 0 } => mut scores
    1..dictN -> each i {
        scores -> put(i, i * 3)
    }
    nowMillis() => dictBuildEnd

    nowMillis() => dictLookupStart
    1..dictN -> fold 0 total, i {
        scores[i] => value
        total + value
    } => dictChecksum
    nowMillis() => dictLookupEnd

    values -> len => arrayLength
    values -> capacity => arrayCapacity
    scores -> len => dictLength
    scores -> capacity => dictCapacity
    arrayCapacity * 8 => arrayBackingBytes
    (dictCapacity + 7) / 8 * 8 => dictControlBytes
    dictCapacity * 16 => dictEntriesBytes
    dictControlBytes + dictEntriesBytes => dictBackingBytes

    arrayBuildEnd - arrayBuildStart => arrayBuildMillis
    arrayScanEnd - arrayScanStart => arrayScanMillis
    dictBuildEnd - dictBuildStart => dictBuildMillis
    dictLookupEnd - dictLookupStart => dictLookupMillis
    arrayN * arrayScanRepeats => arrayScanOperations

    arrayBuildMillis > 0 -> if {
        arrayN * 1000 / arrayBuildMillis
    } else {
        0
    } => arrayBuildOpsPerSecond
    arrayScanMillis > 0 -> if {
        arrayScanOperations * 1000 / arrayScanMillis
    } else {
        0
    } => arrayScanOpsPerSecond
    dictBuildMillis > 0 -> if {
        dictN * 1000 / dictBuildMillis
    } else {
        0
    } => dictBuildOpsPerSecond
    dictLookupMillis > 0 -> if {
        dictN * 1000 / dictLookupMillis
    } else {
        0
    } => dictLookupOpsPerSecond

    "benchmark = containers-throughput" -> println
    "arrayN = $arrayN" -> println
    "arrayScanRepeats = $arrayScanRepeats" -> println
    "dictN = $dictN" -> println
    "arrayLength = $arrayLength" -> println
    "arrayCapacity = $arrayCapacity" -> println
    "arrayBackingBytes = $arrayBackingBytes" -> println
    "arrayChecksum = $arrayChecksum" -> println
    "arrayBuildMillis = $arrayBuildMillis" -> println
    "arrayBuildOpsPerSecond = $arrayBuildOpsPerSecond" -> println
    "arrayScanMillis = $arrayScanMillis" -> println
    "arrayScanOpsPerSecond = $arrayScanOpsPerSecond" -> println
    "dictLength = $dictLength" -> println
    "dictCapacity = $dictCapacity" -> println
    "dictBackingBytes = $dictBackingBytes" -> println
    "dictChecksum = $dictChecksum" -> println
    "dictBuildMillis = $dictBuildMillis" -> println
    "dictBuildOpsPerSecond = $dictBuildOpsPerSecond" -> println
    "dictLookupMillis = $dictLookupMillis" -> println
    "dictLookupOpsPerSecond = $dictLookupOpsPerSecond" -> println
}
