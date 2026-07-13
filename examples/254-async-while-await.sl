step value: Int -> async Int {
    value + 1
}

accumulate count: Int -> async Int {
    0 => index!
    0 => total!
    [count, ~] => values!
    index! < count -> while {
        box index! => owned
        index! -> step => firstTask
        index! + 10 -> step => secondTask
        firstTask -> await => first
        secondTask -> await => second
        total! + first + second => total!
        values! -> push(first + second)
        index! + 1 => index!
    }
    total! + (values! -> len)
}

selective count: Int -> async Int {
    0 => index!
    0 => total!
    index! < count -> while {
        index! % 2 == 0 -> if {
            index! -> step => pending
            pending -> await => next
            total! + next => total!
        } else {
            total! + 10 => total!
        }
        index! + 1 => index!
    }
    total!
}

controlled count: Int -> async Int {
    0 => index!
    0 => total!
    [count, ~] => values!
    index! < count -> while {
        box index! => iterationOwner
        index! -> step => pending
        pending -> await => next
        index! + 1 => index!
        index! == 2 -> if continue
        total! + next => total!
        index! == 4 -> if {
            break
        }
        values! -> push(next)
    }
    total! + (values! -> len)
}

breakFirst count: Int -> async Int {
    0 => index!
    0 => result!
    index! < count -> while {
        index! -> step => pending
        pending -> await => next
        next => result!
        break
    }
    result!
}

continueOnly count: Int -> async Int {
    0 => index!
    index! < count -> while {
        index! -> step => pending
        pending -> await => next
        index! + 1 => index!
        continue
    }
    index!
}

main {
    3 -> accumulate => repeated
    0 -> accumulate => empty
    5 -> accumulate => cancelled
    4 -> selective => selected
    6 -> controlled => controlledTask
    1 -> breakFirst => breakTask
    3 -> continueOnly => continueTask
    1 -> step => gate
    gate -> await => ignored
    cancelled -> cancel
    repeated -> await => repeatedValue
    empty -> await => emptyValue
    selected -> await => selectedValue
    controlledTask -> await => controlledValue
    breakTask -> await => breakValue
    continueTask -> await => continueValue
    "loop=$repeatedValue,empty=$emptyValue,branch=$selectedValue,control=$controlledValue,break=$breakValue,continue=$continueValue" -> println
}
