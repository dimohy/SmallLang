consumeBox value: move box Int -> async Int {
    11
}

holdBox value: move box Int -> async Int {
    yield
    7
}

relayBox value: move box Int -> async Int {
    value -> consumeBox => child
    yield
    child -> await
}

step value: Int -> async Int {
    value + 1
}

main {
    box 1 => cancelledInput!
    cancelledInput! -> holdBox => cancelled
    40 -> step => gate
    gate -> await => gateValue
    cancelled -> cancel

    box 2 => completedInput!
    completedInput! -> holdBox => completed
    completed -> await => completedValue

    box 3 => transferredInput!
    transferredInput! -> relayBox => transferred
    transferred -> await => transferredValue

    "gate=$gateValue,completed=$completedValue,transferred=$transferredValue" -> println
}
