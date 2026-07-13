struct Score {
    base: Int
    bonus: Int
}

square value: Int -> async Int {
    value * value
}

score value: Int -> async Int {
    Score { base: value * 2, bonus: 3 } => saved
    value -> square => pending
    pending -> await => squared
    squared > saved.base -> if {
        squared + saved.bonus
    } else {
        saved.base
    }
}

main {
    4 -> score => task
    task -> await => result
    "result=$result" -> println
}
