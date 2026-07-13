main {
    0 => outer!
    outer! < 3 -> while {
        outer! + 1 => outer!
        0 => inner!
        inner! < 4 -> while {
            box inner! => owned
            inner! + 1 => inner!
            inner! == 2 -> if {
                continue
            }
            inner! == 3 -> if {
                break
            }
            "$(outer!):$(inner!)" -> println
        }
        "outer=$(outer!)" -> println
    }
}
