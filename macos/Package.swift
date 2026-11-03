// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "AnyDownloaderCore",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "AnyDownloaderCore", targets: ["AnyDownloaderCore"]),
    ],
    dependencies: [
        .package(url: "https://github.com/groue/GRDB.swift.git", from: "7.0.0"),
    ],
    targets: [
        .target(
            name: "AnyDownloaderCore",
            dependencies: [.product(name: "GRDB", package: "GRDB.swift")],
            swiftSettings: [.enableExperimentalFeature("StrictConcurrency")]
        ),
        .testTarget(
            name: "AnyDownloaderCoreTests",
            dependencies: ["AnyDownloaderCore"]
        ),
    ]
)
