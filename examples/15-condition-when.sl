grade score: Int -> Text {
    when {
        score >= 90 {
            "A"
        }
        score >= 80 {
            "B"
        }
        score >= 70 {
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
