namespace sample.same

answer: -> Int => 21

double value: Int -> Int => value * 2

identity<T> value: T -> T => value

public run: -> Unit {
    answer => value
    double(value) => direct
    5 -> double => flowed
    7 -> identity => generic
    "same module = $direct,$flowed,$generic" -> println
}

main {
}
