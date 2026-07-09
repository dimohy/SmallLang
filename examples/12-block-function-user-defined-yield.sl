runTimes count: Int -> Unit block turn: Int {
    1..count -> each turn {
        turn -> yield
    }
}

main {
    3 -> runTimes step {
        "custom block step $step" -> println
    }
}
