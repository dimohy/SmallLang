main {
    "n = ? " -> readInt => n

    1..9 -> each i {
        n * i => value
        "$n x $i = $value" -> println
    }
}
