import Foundation

class SettingsService {
    private static let settingsDir: URL = {
        let appSupport = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        let bundleName = (Bundle.main.object(forInfoDictionaryKey: "CFBundleName") as? String) ?? "Grimly"
        return appSupport.appendingPathComponent(bundleName)
    }()

    private static let settingsPath: URL = {
        settingsDir.appendingPathComponent("settings.json")
    }()

    func load() -> AppSettings {
        let path = Self.settingsPath
        guard FileManager.default.fileExists(atPath: path.path) else {
            return AppSettings()
        }

        do {
            let data = try Data(contentsOf: path)
            return try JSONDecoder().decode(AppSettings.self, from: data)
        } catch {
            return AppSettings()
        }
    }

    func save(_ settings: AppSettings) {
        do {
            try FileManager.default.createDirectory(at: Self.settingsDir, withIntermediateDirectories: true)
            let encoder = JSONEncoder()
            encoder.outputFormatting = .prettyPrinted
            let data = try encoder.encode(settings)
            try data.write(to: Self.settingsPath)
        } catch {
            print("Failed to save settings: \(error)")
        }
    }
}
