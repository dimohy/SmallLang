main {
    "n = ? " -> readInt -> n

    each i in 1..9 {
        value = n * i
        "{n} x {i} = {value}" -> println
    }
}
