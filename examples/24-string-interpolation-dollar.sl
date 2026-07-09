main {
    7 => score
    "literal braces { score: $score, next: $(score + 1) }" -> println
    "old braces stay literal: {score}" -> println
}
