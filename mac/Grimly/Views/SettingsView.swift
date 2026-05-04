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
}
