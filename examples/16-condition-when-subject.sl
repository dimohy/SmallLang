grade score: Int -> Text {
    score -> when {
        >= 90 {
            "A"
        }
        >= 80 {
            "B"
        }
        >= 70 {
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
