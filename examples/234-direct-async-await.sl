square value: Int -> async Int {
    value * value
}

answer: -> async Int {
    42
}

main {
    6 -> square -> await => squared
    answer -> await => value
    "$(squared), $(value)" -> println
}
