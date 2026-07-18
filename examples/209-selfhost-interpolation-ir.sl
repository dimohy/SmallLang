import smalllang.compiler.ir.interpolation as interpolation

main {
    """
    calculate value: Int -> Unit {
        "result=$(value + 2 * -3), nested=$((value + 1) * 2), unary=$(-value), bool=$(true), compare=$(value > 0), logic=$(true and not false), equality=$(true != false), short=$value, again=$value" -> println
    }

    main { }
    """ => source
    source -> interpolation.lower => nodes!
    nodes! -> each node {
        source -> slice(node.payloadStart, node.payloadLength) => payload
        "node = $(node.kind),$(node.segment),$(node.parent),$(node.symbol),$(node.ownerSymbol),$(node.opcode),$payload,$(node.operand0),$(node.operand1),$(node.sourceToken),$(node.literalStart),$(node.literalLength),$(node.expressionStart),$(node.expressionLength),$(node.typeSymbol)" -> println
    }
}
