main {
    { 1: 10, 2: 20 } => mut scores
    scores -> put(3, 30)
    scores -> put(4, 40)
    scores -> put(2, 222)

    scores[1] => score1
    scores[2] => score2
    scores[3] => score3
    scores[4] => score4
    scores -> len => count
    scores -> capacity => capacity

    "score1 = $score1" -> println
    "score2 = $score2" -> println
    "score3 = $score3" -> println
    "score4 = $score4" -> println
    "count = $count" -> println
    "capacity = $capacity" -> println
}
