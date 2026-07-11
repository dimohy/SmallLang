struct NumberSource {
    value: Int
}

trait Source {
    type Item
    read: self -> Item
}

impl Source for NumberSource {
    read: self -> Int {
        self.value
    }
}

main {
}
