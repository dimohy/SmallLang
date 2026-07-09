"n = ? " -> readInt => n

n < 1 or n > 9 -> if {
    "n must be 1..9" -> println
} else {
    n == 9 -> if {
        "nine selected" -> println
    } else {
        "table selected" -> println
    }

    1..9 -> each i {
        n * i => value
        "$n x $i = $value" -> println
    }
}
