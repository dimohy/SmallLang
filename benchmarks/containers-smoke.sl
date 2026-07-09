main {
    10 => n
    nowMillis() => started

    [..] => mut values
    1..10 -> each i {
        values -> push(i)
    }

    values -> fold 0 total, value {
        total + value
    } => arraySum

    { 0: 0 } => mut scores
    1..10 -> each i {
        scores -> put(i, i * 2)
    }

    1..10 -> fold 0 total, i {
        scores[i] => value
        total + value
    } => dictSum

    nowMillis() => finished
    finished - started => elapsedMillis

    "n = $n" -> println
    "arraySum = $arraySum" -> println
    "dictSum = $dictSum" -> println
    "elapsedMillis = $elapsedMillis" -> println
}
