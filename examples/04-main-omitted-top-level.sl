getName: -> Text {
    "dimohy"
}

square n: Int -> Int {
    n * n
}

getName() => name
7 -> square => num
"Hello, $name. square = $num" -> sys.io.print
