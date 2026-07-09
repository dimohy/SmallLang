main {
    1..100 -> fold 0 sum, i {
        sum + i
    } => total

    "sum = $total" -> println
}
