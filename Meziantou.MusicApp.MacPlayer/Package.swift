// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "MeziantouMusicMacPlayer",
    platforms: [
        .macOS(.v15),
    ],
    products: [
        .executable(name: "MeziantouMusic", targets: ["MusicPlayerMac"]),
        .library(name: "MusicPlayerCore", targets: ["MusicPlayerCore"]),
    ],
    targets: [
        .target(
            name: "MusicPlayerCore"
        ),
        .executableTarget(
            name: "MusicPlayerMac",
            dependencies: ["MusicPlayerCore"],
            exclude: ["Resources/Info.plist", "Resources/AppIcon.icns"],
            linkerSettings: [
                // Embed the Info.plist in the binary so `swift run` gets the same
                // bundle identifier and App Transport Security settings as the .app bundle.
                .unsafeFlags([
                    "-Xlinker", "-sectcreate",
                    "-Xlinker", "__TEXT",
                    "-Xlinker", "__info_plist",
                    "-Xlinker", "Sources/MusicPlayerMac/Resources/Info.plist",
                ]),
            ]
        ),
        .testTarget(
            name: "MusicPlayerCoreTests",
            dependencies: ["MusicPlayerCore"]
        ),
    ]
)
