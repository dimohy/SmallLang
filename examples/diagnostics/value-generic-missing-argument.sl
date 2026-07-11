fillCount[N: Int] value: Int -> Int {
    [value; N] => values
    values.len
}

main {
    7 -> fillCount => count
    "$count" -> println
}
