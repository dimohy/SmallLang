grade: Int -> Text => when {
    90..100 => "A"
    80..89 => "B"
    70..79 => "C"
    else => "F"
}

gradeNamed score: Int -> Text => score -> when {
    >= 90 => "A"
    >= 80 => "B"
    >= 70 => "C"
    else => "F"
}

main {
    85 -> grade => compact
    92 -> gradeNamed => named

    "compact = $compact" -> println
    "named = $named" -> println
}
