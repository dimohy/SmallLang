step value: Int -> async Int {
    value + 1
}

consume boxed: move box Int -> async Int {
    1
}

probe count: Int -> async Int {
    box 1 => owned
    0 => index!
    index! < count -> while {
        index! -> step => pending
        pending -> await => next
        owned -> consume => consumed
        consumed -> await => ignored
        index! + next => index!
        continue
    }
    index!
}

main { }
