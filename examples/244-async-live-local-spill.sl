square value: Int -> async Int {
    value * value
}

combine value: Int -> async Int {
    value * 3 => scaled
    value + 1000 => dead
    value -> square => pending
    pending -> await => squared
    scaled + squared
}

main {
    5 -> combine => task
    task -> await => result
    "result=$result" -> println
}
