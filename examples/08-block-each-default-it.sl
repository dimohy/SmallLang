main {
    "n = ? " -> readInt => n

    1..9 -> each {
        n * it => value
        "$n x $it = $value" -> println
    }
}
