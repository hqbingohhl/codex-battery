import AppKit

final class ApplicationIconController {
    private let renderer = DispatchQueue(label: "app.codexbattery.macos.app-icon", qos: .utility)
    private let finderQueue: OperationQueue = {
        let queue = OperationQueue()
        queue.name = "app.codexbattery.macos.finder-icon"
        queue.maxConcurrentOperationCount = 1
        queue.qualityOfService = .utility
        return queue
    }()
    private let installedApp: URL? = {
        let url = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Applications/Codex Battery.app", isDirectory: true)
        return Bundle.main.bundleURL.standardizedFileURL.path == url.standardizedFileURL.path ? url : nil
    }()
    private var finderUpdate: BlockOperation?
    private var requestedKey: String?
    private var renderID = UUID()
    private(set) var finderNeedsRetry = false

    /// Pet selection and visibility changes arrive on the main thread. Routine
    /// quota refreshes and individual animation frames do not change the icon.
    func update(pose: CGImage?, artworkID: String?, retry: Bool = false) {
        let key = artworkID ?? "battery"
        guard requestedKey != key || (retry && finderNeedsRetry) else { return }
        finderUpdate?.cancel()
        finderNeedsRetry = false
        requestedKey = key
        let id = UUID()
        renderID = id
        renderer.async { [weak self] in
            let icns = try? PetIconFile.encode(pose: pose)
            DispatchQueue.main.async { [weak self] in
                guard let self, self.renderID == id else { return }
                guard let icns, let image = NSImage(data: icns) else {
                    self.requestedKey = nil
                    self.finderNeedsRetry = true
                    return
                }
                NSApp.applicationIconImage = image
                NSApp.dockTile.display()
                self.updateFinder(icns)
            }
        }
    }

    private func updateFinder(_ icns: Data) {
        guard let app = installedApp else { return }
        let work = BlockOperation()
        work.addExecutionBlock { [weak self, weak work] in
            guard let work, !work.isCancelled else { return }
            do {
                try FinderPetIcon.update(icns: icns, at: app, isCurrent: { !work.isCancelled })
            } catch {
                DispatchQueue.main.async { [weak self] in
                    guard let self, self.finderUpdate === work, !work.isCancelled else { return }
                    self.finderNeedsRetry = true
                }
            }
        }
        finderUpdate = work
        finderQueue.addOperation(work)
    }

    func stop() {
        renderID = UUID()
        requestedKey = nil
        finderUpdate?.cancel()
    }
}
