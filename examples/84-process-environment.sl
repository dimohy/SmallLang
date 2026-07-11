import sys.process as process

describe value: Option<Text> -> Text {
    value -> when {
        Option<Text>.None => "missing"
        Option<Text>.Some(text) => text
    }
}

main {
    process.environment("SMALLLANG_TEST_VALUE") -> describe => present
    process.environment("SMALLLANG_TEST_EMPTY") -> describe => empty
    process.environment("SMALLLANG_TEST_MISSING") -> describe => missing

    present -> println
    empty -> println
    missing -> println
}
