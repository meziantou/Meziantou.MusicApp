import SwiftUI

@main
struct MeziantouMusicApp: App {
    @State private var model = MobileAppModel()

    var body: some Scene {
        WindowGroup {
            MobileContentView(model: model)
        }
    }
}
