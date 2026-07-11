fixedLength[N: Int] values: [Int; N] -> Int {
    values -> len
}

main {
    [10, 20, 30] => threeValues
    [1, 2, 3, 4, 5] => fiveValues
    threeValues -> fixedLength[3] => three
    fiveValues -> fixedLength[5] => five
    "three = $three" -> println
    "five = $five" -> println
}
