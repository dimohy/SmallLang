choose value: Int -> Int {
    [1, 2, 3, ~] => values
    value < 0 -> if {
        10 -> return
    }
    values[0]
}

make flag: Bool -> [Int; ~] {
    [7, 8, ~] => values
    flag -> if {
        values -> return
    }
    values
}

stop early: Bool -> Unit {
    box 42 => owned
    early -> if {
        return
    }
    "after" -> println
}

main {
    -1 -> choose => negative
    "$(negative)" -> println
    1 -> choose => positive
    "$(positive)" -> println
    true -> make => first
    first[0] => firstValue
    "$(firstValue)" -> println
    false -> make => second
    second[1] => secondValue
    "$(secondValue)" -> println
    true -> stop
    false -> stop
}
