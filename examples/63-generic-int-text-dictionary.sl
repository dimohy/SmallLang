main {
    { 1: "parse", 2: "check", 3: "lower" } => phases!
    phases! -> put(4, "emit")
    phases! -> put(2, "type-check")
    phases![2] => second
    phases![4] => fourth
    "phase 2 = $second" -> println
    "phase 4 = $fourth" -> println
}
