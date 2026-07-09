scale n: Int -> Int {
    double value: Int -> Int {
        value * 2
    }

    addBase value: Int -> Int {
        value + n
    }

    n -> double -> addBase
}

main {
    7 -> scale => num
    "local function result = $num" -> println
}
