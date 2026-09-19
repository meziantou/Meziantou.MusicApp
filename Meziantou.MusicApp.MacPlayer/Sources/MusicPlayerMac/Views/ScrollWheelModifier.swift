import AppKit
import MusicPlayerCore
import SwiftUI

extension View {
    /// Calls `action` with the number of steps (positive when scrolling up or right) when the mouse wheel
    /// or trackpad scrolls over the view. Clicks and drags still reach the view underneath.
    func onScrollWheel(perform action: @escaping (Double) -> Void) -> some View {
        overlay(ScrollWheelCatcher(action: action))
    }
}

private struct ScrollWheelCatcher: NSViewRepresentable {
    let action: (Double) -> Void

    func makeNSView(context: Context) -> CatcherView {
        let view = CatcherView()
        view.action = action
        return view
    }

    func updateNSView(_ nsView: CatcherView, context: Context) {
        nsView.action = action
    }

    final class CatcherView: NSView {
        var action: ((Double) -> Void)?

        /// Only claim scroll events: mouse clicks and drags go through to the control below.
        override func hitTest(_ point: NSPoint) -> NSView? {
            NSApp.currentEvent?.type == .scrollWheel ? super.hitTest(point) : nil
        }

        override func scrollWheel(with event: NSEvent) {
            let steps = ScrollWheel.steps(
                deltaX: event.scrollingDeltaX,
                deltaY: event.scrollingDeltaY,
                hasPreciseDeltas: event.hasPreciseScrollingDeltas,
                isDirectionInverted: event.isDirectionInvertedFromDevice)
            if steps != 0 {
                action?(steps)
            }
        }
    }
}
