import syntax.generated.smalllang as grammar

main {
    grammar.tokenNames => tokens
    grammar.ruleNames => rules
    grammar.parserProgram => program
    tokens -> len => tokenCount
    rules -> len => ruleCount
    program -> len => wordCount
    "grammar tokens = $tokenCount" -> println
    "grammar rules = $ruleCount" -> println
    "grammar words = $wordCount" -> println
}
