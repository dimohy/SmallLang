struct Counter {
    value: Int
}

impl Counter {
    weighted: self, scale: Int, offset: Int -> Int {
        self.value * scale + offset
    }
}

weighted value: Int, scale: Int, offset: Int -> Int {
    value * scale + offset
}

choose<T> value: T, fallback: T -> T => value

insert score: Int, scores: mut {Int: Int} -> Unit {
    scores -> put(score, score * 10)
}

consume marker: Int, scores: move {Int: Int} -> Int {
    marker + (scores -> len)
}

weightedAsync value: Int, scale: Int, offset: Int -> async Int {
    value * scale + offset
}

consumeAsync marker: Int, scores: move {Int: Int} -> async Int {
    marker + (scores -> len)
}

forward marker: Int, scores: move {Int: Int} -> {Int: Int} => scores

main {
    7 -> weighted(3, 2) => flowed
    weighted(7, 3, 2) => direct
    "left" -> choose("right") => selected

    {Int: Int} => scores!
    4 -> insert(scores!)
    scores![4] => inserted

    {Int: Int} => consumed!
    consumed! -> put(1, 10)
    20 -> consume(consumed!) => consumedResult
    9 -> weightedAsync(2, 1) -> await => asyncResult
    {Int: Int} => asyncScores!
    asyncScores! -> put(7, 70)
    30 -> consumeAsync(asyncScores!) -> await => asyncConsumed
    {Int: Int} => forwardedSource!
    forwardedSource! -> put(8, 80)
    0 -> forward(forwardedSource!) => forwarded!
    forwarded! -> len => forwardedCount
    Counter { value: 5 } => counter
    counter.weighted(6, 2) => methodResult

    "flowed=$flowed, direct=$direct, selected=$selected, inserted=$inserted, consumed=$consumedResult, async=$asyncResult, asyncConsumed=$asyncConsumed, forwarded=$forwardedCount, method=$methodResult" -> println
}
