grade score: Int -> Text {
    score -> when {
        90..100 {
            "A"
        }
        80..89 {
            "B"
        }
        70..79 {
            "C"
        }
        else {
            "F"
        }
    }
}

main {
    85 -> grade => result
    "grade = $result" -> println
}
