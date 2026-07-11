sumFilled[N: Int] value: Int -> Int {
    [value; N] => values
    values -> fold 0 total, item {
        total + item
    }
}

main {
    7 -> sumFilled[3] => three
    7 -> sumFilled[5] => five
    "three = $three" -> println
    "five = $five" -> println
}
