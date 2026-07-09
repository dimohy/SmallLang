main {
    [1, 2, ..] => values
    values -> append(3) => values
    values -> updated(0, 9) => values

    values -> len => count
    values[2] => appended
    values[0] => changedFirst

    "count = $count" -> println
    "appended = $appended" -> println
    "changedFirst = $changedFirst" -> println

    { 1: 100, 2: 200 } => scores
    scores -> updated(2, 250) => scores
    scores -> updated(3, 300) => scores

    scores[2] => changedScore
    scores -> len => addedCount
    scores[3] => addedScore

    "changedScore = $changedScore" -> println
    "addedCount = $addedCount" -> println
    "addedScore = $addedScore" -> println
}
