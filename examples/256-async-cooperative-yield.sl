increment value: Int -> async Int {
    value + 1
}

finite limit: Int -> async Int {
    0 => index!
    [limit, ~] => values!
    index! < limit -> while {
        values! -> push(index!)
        index! + 1 => index!
        yield
    }
    index! + (values! -> len)
}

forever seed: Int -> async Int {
    0 => index!
    [seed, ~] => values!
    true -> while {
        box index! => iterationOwner
        values! -> push(index!)
        index! + 1 => index!
        yield
    }
    index!
}

mixed value: Int -> async Int {
    value * 2 => saved
    yield
    saved + 1 => current!
    current! > 0 -> if {
        yield
        current! + 10 => current!
    }
    current! -> increment => child
    child -> await => next
    yield
    current! + next
}

main {
    7 -> forever => spinning
    4 -> finite => finiteTask
    2 -> mixed => mixedTask
    41 -> increment => gate
    gate -> await => gateValue
    spinning -> cancel
    finiteTask -> await => finiteValue
    mixedTask -> await => mixedValue
    1 -> increment -> await => survivor
    "gate=$gateValue,finite=$finiteValue,mixed=$mixedValue,survivor=$survivor" -> println
}
