struct OwnedEntry {
    payload: box Int
}

main {
    { 1: OwnedEntry { payload: box 10 }, 2: OwnedEntry { payload: box 20 } } => entries
    entries -> len => count
    "owned dictionary count = $count" -> println
}
