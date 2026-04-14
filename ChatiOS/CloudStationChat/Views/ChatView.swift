import SwiftUI
import PhotosUI

struct ChatView: View {
    @EnvironmentObject var chatService: ChatService
    @EnvironmentObject var authService: AuthService
    @State private var messageText = ""
    @State private var showPhotoPicker = false
    @State private var showDocPicker = false
    @State private var selectedPhoto: PhotosPickerItem?
    @FocusState private var isInputFocused: Bool
    
    var body: some View {
        VStack(spacing: 0) {
            // Messages
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(spacing: 8) {
                        ForEach(chatService.messages) { msg in
                            MessageBubble(message: msg, isOwnMessage: msg.userName == authService.userName)
                                .id(msg.id)
                        }
                    }
                    .padding(.horizontal, 12)
                    .padding(.vertical, 8)
                }
                .onChange(of: chatService.messages.count) { _ in
                    if let lastId = chatService.messages.last?.id {
                        withAnimation(.easeOut(duration: 0.2)) {
                            proxy.scrollTo(lastId, anchor: .bottom)
                        }
                    }
                }
            }
            
            Divider()
            
            // Input bar
            HStack(spacing: 8) {
                // Attach button
                Menu {
                    Button(action: { showPhotoPicker = true }) {
                        Label("Foto", systemImage: "photo")
                    }
                    Button(action: { showDocPicker = true }) {
                        Label("Archivo", systemImage: "doc")
                    }
                } label: {
                    Image(systemName: "paperclip")
                        .font(.title3)
                        .foregroundColor(.purple)
                }
                
                // Text field
                TextField("Escribe un mensaje...", text: $messageText, axis: .vertical)
                    .lineLimit(1...4)
                    .textFieldStyle(.plain)
                    .padding(.horizontal, 12)
                    .padding(.vertical, 8)
                    .background(Color.white.opacity(0.08))
                    .cornerRadius(20)
                    .focused($isInputFocused)
                
                // Send button
                Button(action: send) {
                    Image(systemName: "arrow.up.circle.fill")
                        .font(.title2)
                        .foregroundColor(messageText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? .gray : .purple)
                }
                .disabled(messageText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
            .background(Color(red: 0.1, green: 0.1, blue: 0.14))
        }
        .photosPicker(isPresented: $showPhotoPicker, selection: $selectedPhoto, matching: .images)
        .onChange(of: selectedPhoto) { newItem in
            Task {
                if let data = try? await newItem?.loadTransferable(type: Data.self) {
                    chatService.uploadFile(data: data, fileName: "photo.jpg", mimeType: "image/jpeg") { _ in }
                }
            }
        }
        .sheet(isPresented: $showDocPicker) {
            DocumentPickerView { url in
                if let data = try? Data(contentsOf: url) {
                    let name = url.lastPathComponent
                    let mime = mimeType(for: url.pathExtension)
                    chatService.uploadFile(data: data, fileName: name, mimeType: mime) { _ in }
                }
            }
        }
    }
    
    private func send() {
        let text = messageText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return }
        chatService.sendMessage(text)
        messageText = ""
    }
    
    private func mimeType(for ext: String) -> String {
        switch ext.lowercased() {
        case "pdf": return "application/pdf"
        case "doc", "docx": return "application/msword"
        case "xls", "xlsx": return "application/vnd.ms-excel"
        case "png": return "image/png"
        case "jpg", "jpeg": return "image/jpeg"
        case "gif": return "image/gif"
        case "zip": return "application/zip"
        case "txt": return "text/plain"
        default: return "application/octet-stream"
        }
    }
}

// MARK: - Message Bubble

struct MessageBubble: View {
    let message: ChatMessage
    let isOwnMessage: Bool
    @EnvironmentObject var authService: AuthService
    
    var body: some View {
        HStack(alignment: .top, spacing: 8) {
            if isOwnMessage { Spacer(minLength: 50) }
            
            VStack(alignment: isOwnMessage ? .trailing : .leading, spacing: 4) {
                if !isOwnMessage {
                    Text((message.isBot ? "🤖 " : "") + message.displayName)
                        .font(.caption.bold())
                        .foregroundColor(message.isBot ? .cyan : .purple)
                }
                
                // File attachment
                if message.hasFile {
                    filePreview
                }
                
                // Message text
                if !message.message.isEmpty {
                    Text(message.message)
                        .foregroundColor(.white)
                        .font(.body)
                }
                
                Text(message.timeString)
                    .font(.caption2)
                    .foregroundColor(.gray)
            }
            .padding(10)
            .background(
                message.isBot ? Color.purple.opacity(0.15) :
                isOwnMessage ? Color.purple.opacity(0.3) : Color.white.opacity(0.08)
            )
            .overlay(
                message.isBot ?
                    RoundedRectangle(cornerRadius: 16)
                        .stroke(Color.purple.opacity(0.3), lineWidth: 1) : nil
            )
            .cornerRadius(16)
            
            if !isOwnMessage { Spacer(minLength: 50) }
        }
    }
    
    @ViewBuilder
    var filePreview: some View {
        if message.isImage, let urlStr = message.fileUrl,
           let imageUrl = urlStr.hasPrefix("http") ? URL(string: urlStr) : URL(string: urlStr, relativeTo: URL(string: authService.serverUrl)) {
            AsyncImage(url: imageUrl) { image in
                image.resizable().scaledToFit()
            } placeholder: {
                ProgressView()
            }
            .frame(maxWidth: 200, maxHeight: 200)
            .cornerRadius(8)
        } else if let fileName = message.fileName {
            HStack {
                Image(systemName: fileIcon(for: message.fileType ?? ""))
                    .font(.title2)
                    .foregroundColor(.purple)
                VStack(alignment: .leading) {
                    Text(fileName)
                        .font(.caption)
                        .foregroundColor(.white)
                        .lineLimit(1)
                    Text(message.fileSizeFormatted)
                        .font(.caption2)
                        .foregroundColor(.gray)
                }
            }
            .padding(8)
            .background(Color.white.opacity(0.05))
            .cornerRadius(8)
        }
    }
    
    func fileIcon(for type: String) -> String {
        if type.contains("pdf") { return "doc.richtext" }
        if type.contains("word") || type.contains("doc") { return "doc.text" }
        if type.contains("excel") || type.contains("sheet") { return "tablecells" }
        if type.contains("zip") || type.contains("rar") { return "doc.zipper" }
        return "doc"
    }
}

// MARK: - Document Picker

struct DocumentPickerView: UIViewControllerRepresentable {
    let onPick: (URL) -> Void
    
    func makeUIViewController(context: Context) -> UIDocumentPickerViewController {
        let picker = UIDocumentPickerViewController(forOpeningContentTypes: [.data])
        picker.delegate = context.coordinator
        return picker
    }
    
    func updateUIViewController(_ uiViewController: UIDocumentPickerViewController, context: Context) {}
    
    func makeCoordinator() -> Coordinator { Coordinator(onPick: onPick) }
    
    class Coordinator: NSObject, UIDocumentPickerDelegate {
        let onPick: (URL) -> Void
        init(onPick: @escaping (URL) -> Void) { self.onPick = onPick }
        
        func documentPicker(_ controller: UIDocumentPickerViewController, didPickDocumentsAt urls: [URL]) {
            guard let url = urls.first else { return }
            guard url.startAccessingSecurityScopedResource() else { return }
            defer { url.stopAccessingSecurityScopedResource() }
            onPick(url)
        }
    }
}
