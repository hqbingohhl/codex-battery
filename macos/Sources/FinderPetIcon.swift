import AppKit
import Security

/// Updates the locally built application as a complete, strictly verified bundle.
/// The caller serializes updates and supplies the canonical installed application URL.
enum FinderPetIcon {
    private enum Failure: Error { case unexpectedBundle, changedInstallation, signature, exchange }

    // A newer installation (including a rollback to an older build) must win over this process.
    // An icon-only re-sign changes the code hash, so remember the hash we ourselves installed.
    private static var installedHash: Data?
    private static let iconPath = "Contents/Resources/AppIcon.icns"

    static func update(icns: Data, at app: URL, isCurrent: () -> Bool) throws {
        guard isCurrent() else { return }
        let fm = FileManager.default
        guard app.pathExtension == "app", app.standardizedFileURL.path == app.resolvingSymlinksInPath().path,
              icns.count >= 8, icns.starts(with: Data("icns".utf8))
        else { throw Failure.unexpectedBundle }
        let original = try inventory(app)
        let icon = app.appendingPathComponent(iconPath)
        if try Data(contentsOf: icon, options: .mappedIfSafe) == icns { return }

        let originalCode = try signedBundle(app)
        let expectedHash = try installedHash ?? runningHash()
        guard expectedHash == originalCode else { throw Failure.changedInstallation }

        let folder = app.deletingLastPathComponent().appendingPathComponent(".codex-icon-" + UUID().uuidString)
        try fm.createDirectory(at: folder, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        let staged = folder.appendingPathComponent(app.lastPathComponent)
        var canCleanUp = true
        defer { if canCleanUp { try? fm.removeItem(at: folder) } }

        try fm.copyItem(at: app, to: staged)
        let copy = try inventory(staged)
        guard copy.keys == original.keys, copy.allSatisfy({ path, stamp in
            original[path]?.inode != stamp.inode
        }) else { throw Failure.unexpectedBundle }
        try icns.write(to: staged.appendingPathComponent(iconPath), options: .atomic)
        try sign(staged)
        let newCode = try signedBundle(staged)
        // Finder caches bundle images at second granularity, even across an atomic replacement.
        let oldTime = original[""]!.modified
        let time = max(Int(Date().timeIntervalSince1970), oldTime + 1)
        try fm.setAttributes([.modificationDate: Date(timeIntervalSince1970: Double(time))], ofItemAtPath: staged.path)
        guard isCurrent() else { return }
        guard try inventory(app) == original else { throw Failure.changedInstallation }
        try exchange(app, staged)

        // renamex_np has no compare-and-swap mode. If another installer raced the check,
        // put its displaced bundle back and never delete a bundle we cannot account for.
        canCleanUp = false
        var displaced = try? inventory(staged)
        let displacedRoot = displaced?.removeValue(forKey: "")
        var oldItems = original
        let oldRoot = oldItems.removeValue(forKey: "")
        // An exchange can update the root directory's ctime. Compare its inode
        // and device, and verify the complete metadata of everything beneath it.
        guard displacedRoot?.inode == oldRoot?.inode, displacedRoot?.device == oldRoot?.device,
              displaced == oldItems else {
            if (try? Stamp(app).inode) == copy[""]?.inode {
                try exchange(app, staged)
                canCleanUp = true
            }
            throw Failure.changedInstallation
        }
        canCleanUp = true
        installedHash = newCode
        DispatchQueue.main.async { NSWorkspace.shared.noteFileSystemChanged(app.path) }
    }

    private struct Stamp: Equatable {
        let device: dev_t
        let inode: ino_t
        let modified: Int
        let nanoseconds: Int
        let changed: Int
        let changedNanoseconds: Int
        let size: off_t
        let directory: Bool

        init(_ url: URL) throws {
            var value = stat()
            guard lstat(url.path, &value) == 0, value.st_uid == getuid() else { throw Failure.unexpectedBundle }
            let kind = value.st_mode & S_IFMT
            guard kind == S_IFDIR || (kind == S_IFREG && value.st_nlink == 1) else { throw Failure.unexpectedBundle }
            device = value.st_dev
            inode = value.st_ino
            modified = value.st_mtimespec.tv_sec
            nanoseconds = value.st_mtimespec.tv_nsec
            changed = value.st_ctimespec.tv_sec
            changedNanoseconds = value.st_ctimespec.tv_nsec
            size = value.st_size
            directory = kind == S_IFDIR
        }
    }

    private static func inventory(_ root: URL) throws -> [String: Stamp] {
        let directories = ["", "Contents", "Contents/Resources", "Contents/MacOS", "Contents/_CodeSignature"]
        var items: [String: Stamp] = [:]
        for path in directories {
            let url = path.isEmpty ? root : root.appendingPathComponent(path)
            let dir = try Stamp(url)
            guard dir.directory else { throw Failure.unexpectedBundle }
            items[path] = dir
            let names = try FileManager.default.contentsOfDirectory(atPath: url.path)
            for name in names {
                let relative = path.isEmpty ? name : path + "/" + name
                let stamp = try Stamp(url.appendingPathComponent(name))
                if stamp.directory {
                    guard directories.contains(relative) else { throw Failure.unexpectedBundle }
                } else {
                    guard path == "Contents/Resources" || ["Contents/Info.plist", "Contents/MacOS/CodexBattery",
                                                          "Contents/_CodeSignature/CodeResources"].contains(relative)
                    else { throw Failure.unexpectedBundle }
                    items[relative] = stamp
                }
            }
        }
        guard items[iconPath] != nil else { throw Failure.unexpectedBundle }
        return items
    }

    private static func runningHash() throws -> Data {
        var code: SecCode?
        var staticCode: SecStaticCode?
        guard SecCodeCopySelf([], &code) == errSecSuccess, let code,
              SecCodeCheckValidity(code, [], nil) == errSecSuccess,
              SecCodeCopyStaticCode(code, [], &staticCode) == errSecSuccess, let staticCode
        else { throw Failure.signature }
        return try hash(staticCode)
    }

    private static func signedBundle(_ app: URL) throws -> Data {
        var code: SecStaticCode?
        guard SecStaticCodeCreateWithPath(app as CFURL, [], &code) == errSecSuccess, let code,
              SecStaticCodeCheckValidity(code, SecCSFlags(rawValue: kSecCSStrictValidate), nil) == errSecSuccess
        else { throw Failure.signature }
        return try hash(code)
    }

    private static func hash(_ code: SecStaticCode) throws -> Data {
        var info: CFDictionary?
        guard SecCodeCopySigningInformation(code, SecCSFlags(rawValue: kSecCSSigningInformation), &info) == errSecSuccess,
              let info = info as? [String: Any],
              (info[kSecCodeInfoFlags as String] as? NSNumber)?.uint32Value == 0x0002, // kSecCodeSignatureAdhoc
              info[kSecCodeInfoIdentifier as String] as? String == "app.codexbattery.macos",
              let plist = info[kSecCodeInfoPList as String] as? [String: Any],
              plist["CFBundleExecutable"] as? String == "CodexBattery",
              let iconName = plist["CFBundleIconFile"] as? String, ["AppIcon", "AppIcon.icns"].contains(iconName),
              let data = info[kSecCodeInfoUnique as String] as? Data else { throw Failure.signature }
        return data
    }

    private static func sign(_ url: URL) throws {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/codesign")
        p.arguments = ["--force", "--sign", "-", url.path]
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        try p.run()
        p.waitUntilExit()
        guard p.terminationStatus == 0 else { throw Failure.signature }
    }

    private static func exchange(_ a: URL, _ b: URL) throws {
        guard renamex_np(a.path, b.path, UInt32(RENAME_SWAP)) == 0 else { throw Failure.exchange }
    }
}
