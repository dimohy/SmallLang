main {
    { "lexer": 1, "parser": 2 } => stages!
    stages! -> put("semantic", 3)
    stages! -> put("llvm", 4)
    stages! -> put("parser", 20)

    stages!["parser"] => parser
    stages!["llvm"] => llvm
    stages! -> len => count
    stages! -> capacity => capacity
    "parser stage = $parser" -> println
    "llvm stage = $llvm" -> println
    "dictionary count = $count" -> println
    "dictionary capacity = $capacity" -> println
}
