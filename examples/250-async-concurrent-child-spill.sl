work value: Int -> async Int {
    value * 10 + 1
}

combine: -> async Int {
    3 -> work => firstTask
    7 -> work => secondTask
    firstTask -> await => first
    secondTask -> await => second
    first + second
}

main {
    combine => task
    task -> await => result
    "result=$result" -> println
}
