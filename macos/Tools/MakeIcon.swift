import AppKit

@main
enum MakeIcon {
    static func main() throws {
        let args = CommandLine.arguments
        guard args.count == 2 else {
            fputs("Usage: MakeIcon OUTPUT.icns\n", stderr)
            exit(2)
        }
        try PetIconFile.encode(pose: nil).write(to: URL(fileURLWithPath: args[1]), options: .atomic)
    }
}
