main {
    ["lexer", "parser", "llvm"] => stages
    stages[1] => middle
    stages -> len => count
    "stage = $middle" -> println
    "count = $count" -> println
}
