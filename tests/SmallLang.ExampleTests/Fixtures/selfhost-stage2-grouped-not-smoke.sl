main {
    not (false or false) => ready
    ready -> if {
        "grouped-not-ok" -> println
    }
}
