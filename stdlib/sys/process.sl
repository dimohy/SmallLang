namespace sys.process

arguments: -> Arguments = intrinsic

environment name: Text -> Option<Text> = intrinsic

# Runs one executable directly without a shell. The first item is the program
# and remaining items are literal argv entries.
run argv: [Text; ~] -> Result<Int, Text> = intrinsic
