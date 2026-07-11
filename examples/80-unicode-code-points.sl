main {
    "A한é🐶" -> each scalar {
        "U+$(UInt32(scalar))" -> println
    }

    CodePoint(UInt32(128054)) => dog
    "dog = $(UInt32(dog))" -> println
}
