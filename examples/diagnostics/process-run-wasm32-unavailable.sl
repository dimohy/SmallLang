import sys.process as process

main {
    ["tool", ~] => argv
    argv -> process.run => status
}
