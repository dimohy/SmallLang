square value: Int -> async Int {
    value * value
}

increment value: Int -> async Int {
    value + 1
}

combine value: Int -> async Int {
    value * 2 => base
    value -> square => firstTask
    firstTask -> await => squared
    squared -> increment => secondTask
    secondTask -> await => incremented
    base + incremented
}

main {
    4 -> combine => task
    task -> await => result
    "result=$result" -> println
}
