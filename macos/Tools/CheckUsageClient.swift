// Compile together with Sources/UsageClient.swift. The child emulates app-server over real pipes.
import Foundation

@main
struct CheckUsageClient {
    static func main() {
        if CommandLine.arguments.contains("app-server") {
            serve()
            return
        }
        precondition(WindowQuota(["usedPercent": true]) == nil)
        precondition(WindowQuota(["usedPercent": 22.5])?.remaining == 77)
        let app = URL(fileURLWithPath: CommandLine.arguments[0]).standardizedFileURL
        let client = UsageClient(binary: app)
        var received: [Int] = []
        var activity: [Bool] = []
        var errors = 0
        var complete = false
        client.onActivityChange = { activity.append(client.isRefreshing) }
        client.onError = { message in
            precondition(!client.isRefreshing, "Stuck after error")
            precondition(received == [80, 50] && errors == 0, "Unexpected error: \(message)")
            errors += 1
            client.refresh()
        }
        client.onUpdate = { quotas in
            precondition(quotas.secondary?.remaining == 90, "Partial push erased weekly quota")
            precondition(!client.isRefreshing)
            received.append(quotas.primary!.remaining)
            if received.count == 3 { complete = true }
        }
        client.refresh()
        client.refresh() // Only one pending request.
        let expires = Date().addingTimeInterval(12)
        while !complete && Date() < expires {
            _ = RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.1))
        }
        client.stop()
        precondition(received == [80, 50, 80] && errors == 1, "Expected full refresh and reconnect")
        precondition(activity.first == true && activity.last == false)
        print("Usage client check passed: handshake, legacy retry, sparse push, bucket isolation, reconnect.")
    }

    private static func serve() {
        func emit(_ object: [String: Any]) {
            var data = try! JSONSerialization.data(withJSONObject: object)
            data.append(10)
            // Split each reply across pipe writes just as a real server can.
            let mid = data.count / 2
            FileHandle.standardOutput.write(data.prefix(mid))
            FileHandle.standardOutput.write(data.suffix(from: mid))
        }
        var reads = 0
        while let line = readLine(),
              let message = try? JSONSerialization.jsonObject(with: Data(line.utf8)) as? [String: Any] {
            guard let id = message["id"] as? Int else { continue }
            if id == 1 {
                emit(["id": id, "result": [:]])
                continue
            }
            if message["params"] != nil {
                emit(["id": id, "error": ["code": -32602, "message": "unexpected parameter"]])
                continue
            }
            reads += 1
            let used = reads == 1 ? 20 : 50
            let full: [String: Any] = ["primary": ["usedPercent": used], "secondary": ["usedPercent": 10]]
            emit(["id": id, "result": ["rateLimitsByLimitId": ["codex": full]]])
            guard reads == 1 else { return } // Exercise unexpected EOF and explicit reconnect.
            emit(["method": "account/rateLimits/updated", "params": ["rateLimits": ["limitId": "other", "primary": ["usedPercent": 99]]]])
            emit(["method": "account/rateLimits/updated", "params": ["rateLimits": ["limitId": "codex", "primary": ["usedPercent": 50]]]])
        }
    }
}
