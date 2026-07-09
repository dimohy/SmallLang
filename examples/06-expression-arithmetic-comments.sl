# Parentheses and arithmetic operators are regular expressions.
main {
    (7 + 5) * 2 => scaled
    scaled - 4 => adjusted
    adjusted / 5 => divided
    adjusted % 5 => remainder

    not (divided < 4 or remainder != 0) -> if {
        "arithmetic ok" -> println
    } else {
        "arithmetic failed" -> println
    }
}
