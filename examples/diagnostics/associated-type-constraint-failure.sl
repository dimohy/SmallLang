struct TextSource {
    value: Text
}

trait Source {
    type Item
    read: self -> Item
}

impl Source for TextSource {
    type Item = Text

    read: self -> Text {
        self.value
    }
}

sourceInt[T: Source[Item = Int]] value: T -> Int {
    0
}

main {
    TextSource { value: "text" } -> sourceInt => answer
    "$answer" -> println
}
