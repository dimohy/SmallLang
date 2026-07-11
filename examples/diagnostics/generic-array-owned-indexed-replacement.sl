struct Owned {
    value: box Int
}

main {
    [Owned; { value: box 1 }, ~] => values!
    Owned { value: box 2 } => values![0]
}
