import sys.process as process

main {
    process.arguments => args
    args[0] => first
    first -> println
}
