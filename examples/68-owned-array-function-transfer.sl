struct OwnedNode {
    payload: box Int
}

forwardNodes nodes: move [OwnedNode; ~] -> [OwnedNode; ~] => nodes

main {
    [OwnedNode { payload: box 10 }, OwnedNode { payload: box 20 }, ~] => nodes
    nodes -> forwardNodes => nodes
    nodes -> len => count
    "owned transfer count = $count" -> println
}
