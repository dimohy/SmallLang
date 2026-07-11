struct NumberSource {
    value: Int
}

trait Source {
    type Item
    read: self -> Item
}

impl Source for NumberSource {
    type Item = Int

    read: self -> Int {
        self.value
    }
}

sourceInt[T: Source[Item = Int]] value: T -> Int {
    value -> Source.read
}

main {
    NumberSource { value: 42 } -> sourceInt => answer
    "associated = $answer" -> println
}
