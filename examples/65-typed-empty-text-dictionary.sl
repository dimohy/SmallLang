main {
    {Text: Text; 2~} => messages!
    messages! -> put("lexer", "tokens")
    messages! -> put("parser", "syntax")
    messages!["parser"] => result
    "typed dictionary = $result" -> println
}
