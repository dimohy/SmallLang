import sys.process as process

main {
    [1, 2, ~] => argv
    argv -> process.run => status
}
