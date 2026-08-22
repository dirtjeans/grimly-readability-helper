import SwiftUI

struct SettingsView: View {
    @ObservedObject var viewModel: SettingsViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            // Foundry Status
            HStack {
                Text("Foundry Local:")
                    .fontWeight(.semibold)
                Text(viewModel.foundryStatus)
                    .foregroundColor(Color(white: 0.53))
            }

            // Endpoint
            VStack(alignment: .leading, spacing: 4) {
                Text("Endpoint (auto-detected)")
                    .fontWeight(.semibold)
                TextField("", text: .constant(viewModel.foundryEndpoint))
                    .textFieldStyle(.roundedBorder)
                    .disabled(true)
                    .foregroundColor(Color(white: 0.6))
            }

            // Model selector
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Model")
                        .fontWeight(.semibold)
                    Spacer()
                    Button("Refresh") {
                        viewModel.refreshModels()
                    }
                    .font(.system(size: 11))
                }

                TextField("Model name", text: $viewModel.modelName)
                    .textFieldStyle(.roundedBorder)

                if !viewModel.availableModels.isEmpty {
                    List(viewModel.availableModels, id: \.self, selection: Binding(
                        get: { viewModel.modelName },
                        set: { if let v = $0 { viewModel.modelName = v } }
                    )) { model in
                        Text(model)
                            .font(.system(size: 12))
                    }
                    .frame(maxHeight: 100)
                    .cornerRadius(4)
                }

                if viewModel.isLoadingModels {
                    ProgressView()
                        .scaleEffect(0.7)
                }

                // Blurb — models already on this Mac switch instantly;
                // Foundry downloads can take minutes on first fetch.
                Text("Pick a model. Foundry Local downloads can take several minutes on the first fetch; models already on this Mac — cached, or served by Ollama or LM Studio — switch instantly.")
                    .font(.system(size: 10))
                    .foregroundColor(Color(white: 0.55))
                    .fixedSize(horizontal: false, vertical: true)

                Text(viewModel.providerHint)
                    .font(.system(size: 10))
                    .italic()
                    .foregroundColor(Color(white: 0.5))
                    .fixedSize(horizontal: false, vertical: true)

                // Pull-by-name row — only when at least one provider CLI is
                // installed. Provider dropdown + name box + Pull + browse link.
                if !viewModel.installedProviderPrefixes.isEmpty {
                    HStack(spacing: 6) {
                        Text("Get more:")
                            .font(.system(size: 11))
                            .foregroundColor(Color(white: 0.55))

                        Picker("", selection: $viewModel.pullProviderPrefix) {
                            ForEach(viewModel.installedProviderPrefixes, id: \.self) { p in
                                Text(p).tag(p)
                            }
                        }
                        .labelsHidden()
                        .frame(width: 100)

                        TextField("model name", text: $viewModel.pullModelName)
                            .textFieldStyle(.roundedBorder)
                            .frame(minWidth: 120)

                        Button("Pull") { viewModel.pullModel() }
                            .font(.system(size: 11))
                            .disabled(viewModel.isPulling || viewModel.pullModelName.trimmingCharacters(in: .whitespaces).isEmpty)

                        if let url = viewModel.pullCatalogURL {
                            Link("Browse ↗", destination: url)
                                .font(.system(size: 11))
                        }
                    }

                    if !viewModel.pullStatus.isEmpty {
                        HStack(spacing: 6) {
                            if viewModel.isPulling {
                                ProgressView().scaleEffect(0.5).frame(width: 12, height: 12)
                            }
                            Text(viewModel.pullStatus)
                                .font(.system(size: 10))
                                .foregroundColor(Color(white: 0.55))
                                .lineLimit(1)
                                .truncationMode(.middle)
                        }
                    }
                }
            }

            // Hotkey
            VStack(alignment: .leading, spacing: 4) {
                Text("Hotkey")
                    .fontWeight(.semibold)
                HStack {
                    TextField("Modifiers", text: $viewModel.hotkeyModifiers)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 120)
                    Text("+")
                    TextField("Key", text: $viewModel.hotkeyKey)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 60)
                }
            }

            // Floating icon toggle
            Toggle(isOn: $viewModel.showFloatingIcon) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Show Grimly icon when text is selected")
                        .fontWeight(.semibold)
                    Text("A small icon appears near selected text. Click it to open Grimly.")
                        .font(.system(size: 11))
                        .foregroundColor(Color(white: 0.55))
                }
            }

            // Creativity slider
            VStack(alignment: .leading, spacing: 4) {
                Text("Creativity: \(viewModel.creativity, specifier: "%.1f") \(viewModel.creativityLabel)")
                    .fontWeight(.semibold)
                Slider(value: $viewModel.creativity, in: 0...1, step: 0.1)
            }

            // Max Tokens
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Max Tokens")
                        .fontWeight(.semibold)
                    if !viewModel.maxTokensInfo.isEmpty {
                        Text(viewModel.maxTokensInfo)
                            .font(.system(size: 11))
                            .foregroundColor(Color(white: 0.53))
                    }
                }
                TextField("Max tokens", value: $viewModel.maxTokens, format: .number)
                    .textFieldStyle(.roundedBorder)
            }

            // Popup Opacity
            VStack(alignment: .leading, spacing: 4) {
                Text("Popup Opacity: \(Int(viewModel.popupOpacity * 100))%")
                    .fontWeight(.semibold)
                Slider(value: $viewModel.popupOpacity, in: 0.5...1, step: 0.05)
            }

            // About — small footer with app name, version, and build date.
            // Same info as the menu-bar "About Grimly" panel, kept here so
            // users can confirm what version they're on without leaving the
            // pane they're already in when they went looking for it.
            Divider()
                .padding(.top, 8)
            HStack(spacing: 4) {
                Text(Self.aboutLine)
                    .font(.system(size: 11))
                    .foregroundColor(Color(white: 0.53))
                Spacer()
            }

            // Buttons
            HStack {
                Spacer()
                Button("Reset Defaults") {
                    viewModel.resetDefaults()
                }
                Button("Save") {
                    viewModel.save()
                }
                .keyboardShortcut(.defaultAction)
                Button("Cancel") {
                    viewModel.cancel()
                }
                .keyboardShortcut(.cancelAction)
            }
            .padding(.top, 8)
        }
        .padding(20)
        .frame(width: 460)
    }

    /// "Grimly 1.2.0 · built Aug 5, 2026" — sourced from the bundle so a
    /// rebuild refreshes it automatically. Static so the View doesn't re-
    /// read the bundle on every SwiftUI redraw.
    private static let aboutLine: String = {
        let name = (Bundle.main.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String)
            ?? (Bundle.main.object(forInfoDictionaryKey: "CFBundleName") as? String)
            ?? "Grimly"
        let version = (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? ""
        let build = buildDate()
        return "\(name) \(version) · built \(build)"
    }()

    /// App bundle executable's modification date — a stable stand-in for
    /// "build date" without needing a separate INFOPLIST_KEY that would
    /// have to be bumped on every release.
    private static func buildDate() -> String {
        guard let url = Bundle.main.executableURL,
              let attrs = try? FileManager.default.attributesOfItem(atPath: url.path),
              let date = attrs[.modificationDate] as? Date else {
            return "unknown"
        }
        let formatter = DateFormatter()
        formatter.dateStyle = .medium
        formatter.timeStyle = .none
        return formatter.string(from: date)
    }
}
