main {
    [1, 2, 3] => numbers
    numbers[0] => first
    numbers -> len => count
    numbers -> fold 0 total, value {
        total + value
    } => sum

    "first = $first" -> println
    "count = $count" -> println
    "sum = $sum" -> println

    [10, 20, ..] => mut values
    values -> push(30)
    values[2] => third
    values -> len => dynamicCount
    values -> capacity => dynamicCapacity

    "third = $third" -> println
    "dynamicCount = $dynamicCount" -> println
    "dynamicCapacity = $dynamicCapacity" -> println

    { 1: 100, 2: 200 } => mut scores
    scores -> put(3, 300)
    scores[3] => score
    scores -> len => scoreCount

    "score = $score" -> println
    "scoreCount = $scoreCount" -> println
}
