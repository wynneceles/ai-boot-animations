// AI 开机动画 · Mac 四合一（Claude / GPT / Gemini / DeepSeek）—— 外置浮层小程序
// 一个菜单栏图标同时管四个开机动画：检测 Claude、ChatGPT（含 Codex）、Antigravity、DeepSeek Harness 启动
// → 盖在各自的主窗口上播放各自的开屏动画 → 进入循环帧后半透明、点击穿透，作为循环背景一直播放。
// 不修改这四个应用的任何文件。播放部分沿用 GPT 版 codex/Sources/main.swift（含 PR #10 的降耗改动，作者 yechu12138-sudo）。
// 四合一的想法来自 Windows 合并版（PR #11，作者 q438241zy）。
//
// Copyright (C) 2026 wynneceles
// 本程序以 GNU GPL v3 发布，见仓库根目录 LICENSE。
// This program is free software under the GNU General Public License v3.

import AppKit
import AVFoundation
import CoreImage

let showSettingsNote = Notification.Name("io.github.celestewynne0506-pixel.ai-boot-animations.show-settings")

// MARK: - 四个应用各自的参数（素材、字幕、圆角……）

struct Profile {
    let key: String            // 设置文件名、素材子文件夹：claude / gpt / antigravity / deepseek
    let menuTitle: String      // 一级菜单里显示的名字
    let appName: String        // 目标应用的名字（菜单文字、日志）
    let bundleID: String
    let legacyDir: String      // 原版单独小程序的设置文件夹（第一次运行时把设置搬过来）
    let cornerRadius: CGFloat  // 目标窗口圆角
    let defaults: Config       // 默认的开屏时间点、字幕

    static let all: [Profile] = [
        Profile(key: "claude", menuTitle: "Claude", appName: "Claude", bundleID: "com.anthropic.claudefordesktop",
                legacyDir: "ClaudeBootAnimation", cornerRadius: 10,
                defaults: Config(fade: 10.767, showSubs: true, subs: [
                    Subtitle(text: "Under the moonlight... the blade awakens.", start: 4.087, end: 7.189),
                    Subtitle(text: "I am Claude. Ask me anything.", start: 8.281, end: 11.3)])),
        Profile(key: "gpt", menuTitle: "GPT", appName: "ChatGPT", bundleID: "com.openai.codex",
                legacyDir: "GPTBootAnimation", cornerRadius: 17,
                defaults: Config(fade: 11.4, showSubs: true, subs: [
                    Subtitle(text: "Every answer... is already in my hand.", start: 3.2, end: 7.2),
                    Subtitle(text: "All I lack... is your question.", start: 8.65, end: 11.6)])),
        Profile(key: "antigravity", menuTitle: "Gemini", appName: "Antigravity", bundleID: "com.google.antigravity",
                legacyDir: "AntigravityBootAnimation", cornerRadius: 17,
                defaults: Config(fade: 12.93, showSubs: false, subs: [   // 成片已经烧录了字幕，不再另画
                    Subtitle(text: "Where twin stars align, my blades carve a new horizon.", start: 2.68, end: 6.86),
                    Subtitle(text: "Ready to rewrite the universe?", start: 8.00, end: 10.20)])),
        Profile(key: "deepseek", menuTitle: "DeepSeek", appName: "DeepSeek Harness", bundleID: "com.deepseek.dsh",
                legacyDir: "DeepSeekBootAnimation", cornerRadius: 17,
                defaults: Config(fade: 11.7, showSubs: true, subs: [
                    Subtitle(text: "Where the moonlight falls, the deep answers.", start: 0.0, end: 3.833),
                    Subtitle(text: "Name your target, I never miss!", start: 4.334, end: 6.8)])),
    ]
}

// MARK: - 配置

struct Config: Codable {
    /// ChatGPT 启动时播放开屏动画
    var playIntro = true
    /// 开屏后（或不放开屏时直接）保留半透明的循环背景
    var loopBackground = true
    /// 成片里进入循环帧的时间点（秒）：甩牌落定（第 335 帧 11.17s）后，第 342 帧 ≈ 11.4s 开始透出
    var fadeStartSeconds = 11.4
    /// 从不透明淡到半透明用多久
    var fadeDurationSeconds = 1.2
    /// 循环背景的不透明度（0 = 看不见，1 = 完全挡住）
    var residualAlpha = 0.25
    /// 开屏动画音量 0~1
    var introVolume = 1.0
    /// 开屏字幕（时间是成片里的秒数）
    var showSubtitles = true
    /// 字幕入场、出场各渐变多久
    var subtitleFadeSeconds = 0.5
    /// 字幕大小：第一句大约占窗口宽度的比例（0.6 = 60%）
    var subtitleWidthRatio = 0.6
    var subtitles: [Subtitle] = [
        Subtitle(text: "Every answer... is already in my hand.", start: 3.2, end: 7.2),
        Subtitle(text: "All I lack... is your question.", start: 8.65, end: 11.6),
    ]

    init() {}
    init(fade: Double, showSubs: Bool, subs: [Subtitle]) {
        fadeStartSeconds = fade; showSubtitles = showSubs; subtitles = subs
    }
    /// 读设置文件时缺字段用哪个应用的默认值补（load 时先设好）
    static var decodingDefault = Config()
    // 老版本的设置文件缺字段时，用默认值补上，不会整个作废
    init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        let def = Config.decodingDefault
        func get<T: Decodable>(_ k: CodingKeys, _ fallback: T) -> T {
            ((try? c.decodeIfPresent(T.self, forKey: k)) ?? nil) ?? fallback
        }
        let legacyEnabled = ((try? d.container(keyedBy: Legacy.self).decodeIfPresent(Bool.self, forKey: .enabled)) ?? nil)
        playIntro = get(.playIntro, legacyEnabled ?? def.playIntro)
        loopBackground = get(.loopBackground, def.loopBackground)
        fadeStartSeconds = get(.fadeStartSeconds, def.fadeStartSeconds)
        fadeDurationSeconds = get(.fadeDurationSeconds, def.fadeDurationSeconds)
        residualAlpha = get(.residualAlpha, def.residualAlpha)
        introVolume = get(.introVolume, def.introVolume)
        showSubtitles = get(.showSubtitles, def.showSubtitles)
        subtitleFadeSeconds = get(.subtitleFadeSeconds, def.subtitleFadeSeconds)
        subtitleWidthRatio = get(.subtitleWidthRatio, def.subtitleWidthRatio)
        subtitles = get(.subtitles, def.subtitles)
        // 1.0.0 的第二句是 "Now tell me yours."（8.9–11.3s），1.1.0 换了台词和配音；没被用户改过的就跟着换
        if def.subtitles.count > 1, let i = subtitles.firstIndex(where: { $0.text == "Now tell me yours." && $0.start == 8.9 && $0.end == 11.3 }) {
            subtitles[i] = def.subtitles[1]
        }
    }
    private enum Legacy: String, CodingKey { case enabled }

    static var dir: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/AIBootAnimations")
    }
    static func file(_ key: String) -> URL { dir.appendingPathComponent("\(key).json") }

    /// 读某个应用的设置；第一次运行时把原版单独小程序的设置搬过来
    static func load(_ p: Profile) -> Config {
        decodingDefault = p.defaults
        let f = file(p.key)
        if !FileManager.default.fileExists(atPath: f.path) {
            let legacy = FileManager.default.homeDirectoryForCurrentUser
                .appendingPathComponent("Library/Application Support/\(p.legacyDir)/config.json")
            if let data = try? Data(contentsOf: legacy), let c = try? JSONDecoder().decode(Config.self, from: data) {
                log("[\(p.menuTitle)] 已把原版的设置搬过来")
                c.save(p.key); return c
            }
        }
        guard let data = try? Data(contentsOf: f),
              let c = try? JSONDecoder().decode(Config.self, from: data) else {
            let c = p.defaults; c.save(p.key); return c
        }
        c.save(p.key)   // 补齐新字段
        return c
    }
    func save(_ key: String) {
        try? FileManager.default.createDirectory(at: Config.dir, withIntermediateDirectories: true)
        let enc = JSONEncoder(); enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        try? enc.encode(self).write(to: Config.file(key))
    }
}

struct Subtitle: Codable { var text: String; var start: Double; var end: Double }

// MARK: - 日志

let logURL = FileManager.default.homeDirectoryForCurrentUser
    .appendingPathComponent("Library/Logs/AIBootAnimations.log")

func log(_ msg: String) {
    let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd HH:mm:ss.SSS"
    let line = "\(f.string(from: Date())) \(msg)\n"
    if let h = try? FileHandle(forWritingTo: logURL) {
        h.seekToEndOfFile(); h.write(line.data(using: .utf8)!); try? h.close()
    } else {
        try? line.data(using: .utf8)!.write(to: logURL)
    }
}

// MARK: - 找 ChatGPT 的主窗口（只读位置、PID、层级，不读标题，不需要任何授权）

struct TargetWindow {
    let number: Int
    let cocoaFrame: NSRect
    let coveredByOtherApp: Bool
    let fullyCovered: Bool
    let overlayNeedsOrdering: Bool
}

func findTargetWindow(pid: pid_t, overlayNumber: Int? = nil) -> TargetWindow? {
    guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements],
                                                kCGNullWindowID) as? [[String: Any]] else { return nil }
    var best: (Int, CGRect)?
    for w in list {
        guard (w[kCGWindowOwnerPID as String] as? Int32) == pid,
              (w[kCGWindowLayer as String] as? Int) == 0,
              let num = w[kCGWindowNumber as String] as? Int,
              let b = w[kCGWindowBounds as String] as? [String: CGFloat] else { continue }
        if let a = w[kCGWindowAlpha as String] as? Double, a <= 0 { continue }
        let r = CGRect(x: b["X"] ?? 0, y: b["Y"] ?? 0, width: b["Width"] ?? 0, height: b["Height"] ?? 0)
        if r.width < 300 || r.height < 200 { continue }
        if best == nil || r.width * r.height > best!.1.width * best!.1.height { best = (num, r) }
    }
    guard let (num, r) = best, let primary = NSScreen.screens.first else { return nil }
    // 列表由前到后排；排在 ChatGPT 窗口前面、又和它重叠的别家窗口，代表 ChatGPT 窗口被盖住了
    let me = getpid()
    var covered = false
    var fullyCovered = false
    var overlayAboveTarget = false
    var coveringWindowBelowOverlay = false
    for w in list {
        let number = w[kCGWindowNumber as String] as? Int
        if number == num { break }
        if let overlayNumber, number == overlayNumber { overlayAboveTarget = true }
        guard let owner = w[kCGWindowOwnerPID as String] as? Int32, owner != pid, owner != me,
              (w[kCGWindowLayer as String] as? Int) == 0,
              let b = w[kCGWindowBounds as String] as? [String: CGFloat] else { continue }
        if let a = w[kCGWindowAlpha as String] as? Double, a <= 0 { continue }
        let bounds = CGRect(x: b["X"] ?? 0, y: b["Y"] ?? 0, width: b["Width"] ?? 0, height: b["Height"] ?? 0)
        if bounds.intersects(r) {
            covered = true
            if overlayAboveTarget { coveringWindowBelowOverlay = true }
            if bounds.contains(r), (w[kCGWindowAlpha as String] as? Double ?? 1) >= 1 { fullyCovered = true }
        }
    }
    // CG 坐标原点在主屏左上角，Cocoa 在主屏左下角
    let y = primary.frame.height - r.origin.y - r.height
    return TargetWindow(number: num, cocoaFrame: NSRect(x: r.origin.x, y: y, width: r.width, height: r.height),
                        coveredByOtherApp: covered, fullyCovered: fullyCovered,
                        overlayNeedsOrdering: !overlayAboveTarget || coveringWindowBelowOverlay)
}

// MARK: - 把开屏和循环拼成一条时间线，放到末尾后跳回循环开始处

struct Timeline { let asset: AVAsset; let loopStart: CMTime }

func buildTimeline(intro: URL?, loop: URL, repeats: Int) -> Timeline {
    let comp = AVMutableComposition()
    let video = comp.addMutableTrack(withMediaType: .video, preferredTrackID: kCMPersistentTrackID_Invalid)!
    let audio = comp.addMutableTrack(withMediaType: .audio, preferredTrackID: kCMPersistentTrackID_Invalid)!
    var cursor = CMTime.zero

    func append(_ url: URL, count: Int) {
        let asset = AVURLAsset(url: url, options: [AVURLAssetPreferPreciseDurationAndTimingKey: true])
        guard let sourceVideo = asset.tracks(withMediaType: .video).first else { return }
        let videoRange = sourceVideo.timeRange
        let sourceAudio = asset.tracks(withMediaType: .audio).first
        let audioRange = sourceAudio.map {
            let range = $0.timeRange
            return CMTimeRange(start: range.start, duration: CMTimeMinimum(range.duration, videoRange.duration))
        }
        video.preferredTransform = sourceVideo.preferredTransform
        for _ in 0..<count {
            try? video.insertTimeRange(videoRange, of: sourceVideo, at: cursor)
            if let sourceAudio, let audioRange {
                try? audio.insertTimeRange(audioRange, of: sourceAudio, at: cursor)
            }
            cursor = cursor + videoRange.duration
        }
    }

    if let intro { append(intro, count: 1) }
    let loopStart = cursor
    append(loop, count: repeats)
    return Timeline(asset: comp.copy() as! AVComposition, loopStart: loopStart)
}

// MARK: - 视图

final class PlayerView: NSView {
    let playerLayer = AVPlayerLayer()
    init(frame: NSRect, radius: CGFloat) {
        super.init(frame: frame)
        wantsLayer = true
        layer = CALayer()
        layer!.backgroundColor = NSColor.black.cgColor
        layer!.cornerRadius = radius      // 跟目标窗口圆角对齐（Claude 10pt，其他三个 macOS 26 上实测约 17pt）
        layer!.masksToBounds = true
        playerLayer.videoGravity = .resizeAspectFill
        playerLayer.frame = bounds
        playerLayer.autoresizingMask = [.layerWidthSizable, .layerHeightSizable]
        layer!.addSublayer(playerLayer)
    }
    required init?(coder: NSCoder) { fatalError() }
}

final class SkipButtonView: NSView {
    var onClick: (() -> Void)?
    private let label = NSTextField(labelWithString: "跳过 ›")
    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer!.cornerRadius = frame.height / 2
        layer!.backgroundColor = NSColor(white: 0, alpha: 0.45).cgColor
        layer!.borderColor = NSColor(white: 1, alpha: 0.35).cgColor
        layer!.borderWidth = 1
        label.font = .systemFont(ofSize: 13, weight: .medium)
        label.textColor = .white
        label.alignment = .center
        label.frame = NSRect(x: 0, y: (frame.height - 17) / 2, width: frame.width, height: 17)
        addSubview(label)
    }
    required init?(coder: NSCoder) { fatalError() }
    override func mouseDown(with event: NSEvent) { onClick?() }
    override func resetCursorRects() { addCursorRect(bounds, cursor: .pointingHand) }
}

/// 英文花体字幕：Bodoni 72 Book Italic（macOS 自带），白字 + 细黑描边 + 柔和阴影，背景再亮也看得清
final class SubtitleView: NSView {
    /// 两层叠起来：底层只画黑色描边 + 柔和阴影，上层是纯白字。黑边只露在字外圈一点点，白字不会被吃成灰色
    let outline = NSTextField(labelWithString: "")
    let fill = NSTextField(labelWithString: "")
    /// 外部统一用它控制渐显渐隐
    let label = NSView()
    var widthRatio = 0.6
    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        label.wantsLayer = true
        label.alphaValue = 0
        for f in [outline, fill] { f.alignment = .center; label.addSubview(f) }
        addSubview(label)
    }
    required init?(coder: NSCoder) { fatalError() }

    func setText(_ text: String) {
        // 字号按窗口宽度算，比例在设置文件 subtitleWidthRatio 里改；窗口特别扁时再按高度限一下
        // 实测 Bodoni 72 斜体下，第一句宽度 ≈ 字号 × 15.5，所以 字号 = 窗口宽 × 比例 ÷ 15.5
        let size = max(18, min(bounds.width * widthRatio / 15.5, bounds.height * 0.09))
        let font = NSFont(name: "BodoniSvtyTwoITCTT-BookIta", size: size)
            ?? NSFontManager.shared.convert(NSFont(name: "Georgia", size: size) ?? .systemFont(ofSize: size), toHaveTrait: .italicFontMask)
        let para = NSMutableParagraphStyle(); para.alignment = .center
        let shadow = NSShadow()
        shadow.shadowColor = NSColor(white: 0, alpha: 0.75)
        shadow.shadowBlurRadius = size * 0.25
        shadow.shadowOffset = .zero
        outline.attributedStringValue = NSAttributedString(string: text, attributes: [
            .font: font, .paragraphStyle: para, .foregroundColor: NSColor.clear,
            .strokeColor: NSColor(white: 0, alpha: 0.9), .strokeWidth: 5.0,   // 正数 = 只描边
            .shadow: shadow,
        ])
        fill.attributedStringValue = NSAttributedString(string: text, attributes: [
            .font: font, .paragraphStyle: para, .foregroundColor: NSColor.white,
        ])
        layoutLabel()
    }
    func layoutLabel() {
        fill.sizeToFit()
        let w = min(fill.frame.width + 24, bounds.width - 40), h = fill.frame.height + 12
        label.frame = NSRect(x: (bounds.width - w) / 2, y: bounds.height * 0.15 - h / 2, width: w, height: h)   // 字的中线在离底边 15% 处
        outline.frame = label.bounds; fill.frame = label.bounds
    }
    override func layout() { super.layout(); layoutLabel() }
}

func makeBorderlessWindow(_ frame: NSRect) -> NSWindow {
    let w = NSWindow(contentRect: frame, styleMask: .borderless, backing: .buffered, defer: false)
    w.isOpaque = false
    w.backgroundColor = .clear
    w.hasShadow = false
    w.isReleasedWhenClosed = false
    w.level = .normal
    // 让 AppKit 在调度中心暂时隐藏浮层，避免它和目标窗口被分开排列。
    // 只待在目标窗口所在的桌面：切桌面时不会被带到别的桌面闪一下；每次重新显示时搬到当前桌面
    w.collectionBehavior = [.moveToActiveSpace, .transient, .fullScreenAuxiliary, .ignoresCycle]
    return w
}

// MARK: - 一次播放

final class Show {
    enum Phase { case intro, revealed, ending, done }

    let pid: pid_t
    var config: Config
    let withIntro: Bool
    let onDone: (Show) -> Void
    var phase = Phase.intro

    let overlay: NSWindow
    let skipWin: NSWindow
    let subWin: NSWindow
    let subView: SubtitleView
    let playerView: PlayerView
    let player = AVPlayer()
    let timeline: Timeline
    let item: AVPlayerItem
    let videoOut = AVPlayerItemVideoOutput(pixelBufferAttributes: [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA])
    var subObserver: Any?
    var currentSub = -1
    var tick: Timer?
    var rampTimer: Timer?
    var timeObserver: Any?
    var volObserver: Any?
    var readyObs: NSKeyValueObservation?
    private var workspaceObservers: [NSObjectProtocol] = []
    private var mouseMonitor: Any?
    private var dragTrackingUntil: TimeInterval = 0
    private static let dragReleaseGrace: TimeInterval = 0.06
    private var sleeping = false
    var inLoopAudio = false
    var endObserver: NSObjectProtocol?
    var lastTarget = -1
    var lastFrame = NSRect.zero
    var sysAnim = false          // 窗口在做系统动画（最小化、还原、缩放、分屏）时浮层先藏起来
    var stableSince = Date()
    private(set) var paused = false   // 菜单「暂停循环背景」：画面停在当前这一帧，背景照样盖着

    init(pid: pid_t, target: TargetWindow, config: Config, withIntro: Bool,
         timeline: Timeline, cornerRadius: CGFloat, onDone: @escaping (Show) -> Void) {
        self.pid = pid; self.config = config; self.withIntro = withIntro; self.onDone = onDone
        self.timeline = timeline
        item = AVPlayerItem(asset: timeline.asset)
        if withIntro { item.add(videoOut) }   // 点「跳过」时用来截当前这一帧做淡出

        overlay = makeBorderlessWindow(target.cocoaFrame)
        playerView = PlayerView(frame: NSRect(origin: .zero, size: target.cocoaFrame.size), radius: cornerRadius)
        playerView.autoresizingMask = [.width, .height]
        overlay.contentView = playerView

        let frame = target.cocoaFrame
        skipWin = makeBorderlessWindow(NSRect(x: frame.maxX - 90, y: frame.maxY - 72, width: 72, height: 28))
        let btn = SkipButtonView(frame: NSRect(x: 0, y: 0, width: 72, height: 28))
        skipWin.contentView = btn

        subWin = makeBorderlessWindow(target.cocoaFrame)
        subWin.ignoresMouseEvents = true
        subView = SubtitleView(frame: NSRect(origin: .zero, size: target.cocoaFrame.size))
        subView.autoresizingMask = [.width, .height]
        subWin.contentView = subView
        btn.onClick = { [weak self] in self?.skipIntro() }

        playerView.playerLayer.player = player
        let shownAt = Date()
        readyObs = playerView.playerLayer.observe(\.isReadyForDisplay) { l, _ in
            if l.isReadyForDisplay { DispatchQueue.main.async { log("第一帧出画面，用时 \(Int(Date().timeIntervalSince(shownAt) * 1000))ms") } }
        }
        player.replaceCurrentItem(with: item)
        player.actionAtItemEnd = .none

        // 放完后跳回循环开头，避免重播开屏。
        endObserver = NotificationCenter.default.addObserver(
            forName: .AVPlayerItemDidPlayToEndTime, object: item, queue: .main) { [weak self] _ in
            guard let self, self.phase != .done, !self.paused else { return }
            log("时间线放到头，跳回循环开头继续")
            self.player.seek(to: self.timeline.loopStart, toleranceBefore: .zero, toleranceAfter: .zero)
            self.player.play()
        }

        if withIntro {
            overlay.ignoresMouseEvents = false   // 开屏阶段拦住点击
            player.volume = Float(config.introVolume)
            let fadeAt = CMTimeMinimum(CMTime(seconds: config.fadeStartSeconds, preferredTimescale: 600), timeline.loopStart)
            timeObserver = player.addBoundaryTimeObserver(forTimes: [NSValue(time: fadeAt)], queue: .main) { [weak self] in
                self?.reveal()
            }
            if config.showSubtitles && !config.subtitles.isEmpty {
                subObserver = player.addPeriodicTimeObserver(forInterval: CMTime(value: 1, timescale: 30), queue: .main) { [weak self] t in
                    self?.updateSubtitle(at: t.seconds)
                }
            }
            // 开屏音轨放完（成片结束、进入循环文件）时，音量换成循环背景音量
            volObserver = player.addBoundaryTimeObserver(forTimes: [NSValue(time: timeline.loopStart)], queue: .main) { [weak self] in
                guard let self, self.phase == .revealed, !self.inLoopAudio else { return }
                self.hideSubtitles()
                self.inLoopAudio = true
                self.rampVolume(to: 0, over: 1.5)   // 循环段只是风声，静音
            }
        } else {
            phase = .revealed
            inLoopAudio = true
            overlay.ignoresMouseEvents = true
            playerView.layer?.backgroundColor = NSColor.clear.cgColor
            overlay.alphaValue = 0
            player.volume = 0
        }

        place(target)
        if withIntro {
            overlay.addChildWindow(subWin, ordered: .above)    // 字幕单独一层，不跟着背景变半透明
            overlay.addChildWindow(skipWin, ordered: .above)   // 「跳过」只用来跳过开屏；循环背景在菜单栏关
        }
        overlay.orderFront(nil)
        overlay.order(.above, relativeTo: target.number)
        player.play()
        log(withIntro ? "开始播放开屏动画，目标窗口 \(target.number) \(target.cocoaFrame)"
                      : "直接开启循环背景，目标窗口 \(target.number)")
        if !withIntro {
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = config.fadeDurationSeconds
                overlay.animator().alphaValue = config.residualAlpha
            }
        }

        scheduleFollow(every: 0.1)
        mouseMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .leftMouseUp]) { [weak self] event in
            guard let self, !self.sleeping, self.phase != .done else { return }
            let now = ProcessInfo.processInfo.systemUptime
            if event.type == .leftMouseDown {
                guard self.overlay.isVisible,
                      self.lastFrame.insetBy(dx: -8, dy: -8).contains(NSEvent.mouseLocation) else { return }
            } else {
                guard now <= self.dragTrackingUntil else { return }
            }
            // 先捕获按下，避免等到低频定时器才发现拖动；松手后再跟几帧。
            self.dragTrackingUntil = now + Self.dragReleaseGrace
            self.refreshTracking()
        }
        let workspace = NSWorkspace.shared.notificationCenter
        workspaceObservers.append(workspace.addObserver(
            forName: NSWorkspace.activeSpaceDidChangeNotification, object: nil, queue: .main) { [weak self] _ in
                self?.refreshTracking()
            })
        workspaceObservers.append(workspace.addObserver(
            forName: NSWorkspace.willSleepNotification, object: nil, queue: .main) { [weak self] _ in
                guard let self, self.phase != .done else { return }
                self.sleeping = true
                self.dragTrackingUntil = 0
                self.tick?.invalidate()
            })
        workspaceObservers.append(workspace.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
                self?.sleeping = false
                self?.refreshTracking()
            })
        workspaceObservers.append(workspace.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main) { [weak self] n in
            let app = n.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
            // 别的 App 到前台：它的窗口可能马上盖到 ChatGPT 上，窗口列表要晚几十毫秒才反映出来，先立刻降回普通层级，免得浮层压在它上面
            if let self, let app, app.processIdentifier != self.pid, app.processIdentifier != getpid() {
                self.lastOtherActivation = Date()
                if self.overlay.level != .normal {
                    for w in [self.overlay, self.subWin, self.skipWin] { w.level = .normal }
                    if self.lastTarget >= 0 { self.overlay.order(.above, relativeTo: self.lastTarget) }
                }
            }
            self?.refreshTracking()
        })
    }
    var lastOtherActivation = Date.distantPast

    private func scheduleFollow(every interval: TimeInterval) {
        if let tick, tick.isValid, tick.timeInterval == interval { return }
        tick?.invalidate()
        // 固定节拍，避免把每次查询耗时额外加到下一帧的等待时间。
        let timer = Timer(timeInterval: interval, repeats: true) { [weak self] _ in
            self?.refreshTracking()
        }
        timer.tolerance = interval >= 0.1 ? interval / 10 : 0
        tick = timer
        RunLoop.main.add(timer, forMode: .common)
    }

    private func refreshTracking() {
        guard !sleeping, phase != .done else { return }
        let interval = follow()
        if phase != .done { scheduleFollow(every: interval) }
    }

    func place(_ t: TargetWindow) {
        let frame = t.cocoaFrame
        guard frame != lastFrame else { return }
        if overlay.frame.size != frame.size {
            overlay.setFrame(frame, display: true)
            if subWin.parent === overlay { subWin.setFrame(frame, display: true) }
            if skipWin.parent === overlay {
                skipWin.setFrameOrigin(NSPoint(x: frame.maxX - 90, y: frame.maxY - 72))
            }
        } else if overlay.frame.origin != frame.origin {
            // 子窗口会随父窗口一起移动。
            overlay.setFrameOrigin(frame.origin)
        }
        lastFrame = frame
    }

    /// 跟着 ChatGPT 窗口移动/缩放，并保持正好压在它上面（不会盖到别的 App 的窗口）
    private func follow() -> TimeInterval {
        guard let t = findTargetWindow(pid: pid, overlayNumber: overlay.windowNumber) else {
            if overlay.isVisible { overlay.orderOut(nil) }
            sysAnim = true; stableSince = Date()   // 再出现时多半是从 Dock 还原的动画，等停稳再露面
            if NSRunningApplication(processIdentifier: pid) == nil { finish(fast: true) }
            return 0.1   // 最小化期间查窗口列表很便宜，0.1 秒一查，还原时背景能马上回来
        }
        let now = ProcessInfo.processInfo.systemUptime
        let frameChanged = t.cocoaFrame != lastFrame
        let mouseDown = NSEvent.pressedMouseButtons & 1 != 0
        if mouseDown && (frameChanged || now <= dragTrackingUntil) {
            dragTrackingUntil = now + Self.dragReleaseGrace
        }
        let trackingDrag = now <= dragTrackingUntil
        if frameChanged {
            // 最小化、分屏等系统动画期间先隐藏浮层，拖动结束的最后几帧继续跟随。
            if !trackingDrag && !sysAnim && overlay.isVisible { log("窗口在做系统动画，浮层先藏起来"); sysAnim = true }
            if sysAnim { stableSince = Date(); lastFrame = t.cocoaFrame }
        }
        if sysAnim {
            if overlay.isVisible { overlay.orderOut(nil) }
            if Date().timeIntervalSince(stableSince) < 0.18 { return 0.1 }   // 停稳 0.18 秒再回来
            sysAnim = false
            lastFrame = .zero   // 让 place 一定按最新位置重设一次
            log("窗口停稳，浮层回来")
        }
        place(t)
        if overlay.isVisible && !overlay.isOnActiveSpace { overlay.orderOut(nil) }   // 目标窗口被挪到别的桌面：重新显示一次，浮层才会跟过去
        // ChatGPT 窗口没被别的 App 压着（不管 ChatGPT 在不在前台）：浮层放到浮动层级，点 ChatGPT 时它把自己窗口提上来也盖不过浮层
        //   （以前只在 ChatGPT 在前台时才升，别的 App 最小化后再点 ChatGPT，ChatGPT 会先盖过浮层一两帧，看着就是闪黑）
        // 被别的 App 压着：降回普通层级，紧贴在 ChatGPT 窗口上方，不会盖住别的 App（这种情况点 ChatGPT 仍可能轻微闪一下：ChatGPT 提窗口比外部程序快，抢不过）
        let frontPID = NSWorkspace.shared.frontmostApplication?.processIdentifier ?? -1
        // 别的 App 刚到前台的 0.3 秒内先不升（它的窗口可能正要盖上来，窗口列表还没反映）；ChatGPT 自己在前台不受限
        let otherJustActivated = frontPID != pid && Date().timeIntervalSince(lastOtherActivation) < 0.3
        let level: NSWindow.Level = !t.coveredByOtherApp && !otherJustActivated ? .floating : .normal
        // 先把层级定好再显示，直接排到最终位置，不经过「先到最前面、再退回 ChatGPT 上方」的中间状态
        let levelChanged = overlay.level != level
        if levelChanged {
            for w in [overlay, subWin, skipWin] { w.level = level }
        }
        let wasVisible = overlay.isVisible
        if level == .normal {
            if !wasVisible || levelChanged || t.overlayNeedsOrdering {
                overlay.order(.above, relativeTo: t.number)
            }
        } else if !wasVisible { overlay.orderFront(nil) }
        if t.number != lastTarget { lastTarget = t.number; log("跟随窗口 \(t.number)") }
        if t.fullyCovered { return 0.5 }
        guard trackingDrag else { return 0.1 }
        let refreshRate = overlay.screen?.maximumFramesPerSecond ?? 60
        return 1.0 / Double(min(120, max(60, refreshRate)))
    }

    /// 点「跳过」：跳过开屏。开着循环背景就直接跳到循环段并透出界面，否则整个关掉
    func skipIntro() {
        guard phase == .intro else { return }
        log("点了跳过开屏")
        guard config.loopBackground else { finish(fast: true); return }
        hideSubtitles()
        // 先把当前这一帧截下来盖在上面，跳到循环段后再让它慢慢淡出，不会硬切
        let snap = CALayer()
        if let img = currentFrameImage() {
            snap.contents = img
            snap.contentsGravity = .resizeAspectFill
            snap.frame = playerView.bounds
            snap.autoresizingMask = [.layerWidthSizable, .layerHeightSizable]
            playerView.layer!.addSublayer(snap)
        }
        player.seek(to: timeline.loopStart, toleranceBefore: .zero, toleranceAfter: .zero) { _ in
            DispatchQueue.main.async {
                CATransaction.begin()
                CATransaction.setAnimationDuration(0.9)
                CATransaction.setAnimationTimingFunction(CAMediaTimingFunction(name: .easeInEaseOut))
                CATransaction.setCompletionBlock { snap.removeFromSuperlayer() }
                snap.opacity = 0
                CATransaction.commit()
            }
        }
        inLoopAudio = true
        rampVolume(to: 0, over: 0.9)   // 开屏音效随画面一起淡出，循环背景静音
        reveal(quick: true)
    }

    func currentFrameImage() -> CGImage? {
        let t = item.currentTime()
        guard let pb = videoOut.copyPixelBuffer(forItemTime: t, itemTimeForDisplay: nil) else { return nil }
        let ci = CIImage(cvPixelBuffer: pb)
        return CIContext(options: nil).createCGImage(ci, from: ci.extent)
    }

    // 字幕：按播放时间算透明度，入场、出场按 subtitleFadeSeconds 渐显渐隐（默认 0.5 秒）
    func updateSubtitle(at t: Double) {
        guard phase == .intro || phase == .revealed, config.showSubtitles else { return }
        let fade = config.subtitleFadeSeconds
        guard let i = config.subtitles.firstIndex(where: { t >= $0.start && t <= $0.end }) else {
            if currentSub >= 0 && subView.label.alphaValue > 0 { log(String(format: "字幕 %d 消失，视频时间 %.3fs", currentSub + 1, t)) }
            subView.label.alphaValue = 0; return
        }
        let sub = config.subtitles[i]
        if i != currentSub { currentSub = i; subView.widthRatio = config.subtitleWidthRatio; subView.setText(sub.text); log(String(format: "字幕 %d 出现，视频时间 %.3fs", i + 1, t)) }
        let a = min(1, (t - sub.start) / fade, (sub.end - t) / fade)
        subView.label.alphaValue = CGFloat(max(0, a))
    }

    func hideSubtitles() {
        if let o = subObserver { player.removeTimeObserver(o); subObserver = nil }
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.4
            subView.label.animator().alphaValue = 0
        }
    }

    func reveal(quick: Bool = false) {
        guard phase == .intro else { return }
        phase = .revealed
        item.remove(videoOut)   // 循环背景用不到「跳过」时的截图
        overlay.ignoresMouseEvents = true   // 透出后点击直接落到 ChatGPT 上
        playerView.layer?.backgroundColor = NSColor.clear.cgColor   // 万一视频层某帧没画出来，露出的是 ChatGPT 而不是黑块
        overlay.removeChildWindow(skipWin); skipWin.orderOut(nil)   // 进入循环背景后不再显示「跳过」
        if !config.loopBackground {
            log("进入循环帧，循环背景已关，淡出")
            finish(fast: false); return
        }
        log("进入循环帧，透出界面")
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = quick ? 0.9 : config.fadeDurationSeconds
            overlay.animator().alphaValue = config.residualAlpha
        }
        // 开屏音效（冲击声、闪光）照常放完，等进入循环文件再换音量
    }

    /// 暂停 / 继续循环背景。只在循环背景阶段有用；开屏还在放时不让暂停
    func setPaused(_ p: Bool) {
        guard phase == .revealed, p != paused else { return }
        paused = p
        if p { player.pause() } else { player.play() }
        log(p ? "暂停循环背景" : "继续播放循环背景")
    }

    /// 菜单里拖滑块时实时生效
    func apply(_ c: Config) {
        config = c
        guard phase != .ending && phase != .done else { return }
        if phase == .revealed { overlay.alphaValue = c.residualAlpha }
        rampTimer?.invalidate()
        player.volume = inLoopAudio ? 0 : Float(c.introVolume)
    }

    func rampVolume(to target: Float, over seconds: Double, then: (() -> Void)? = nil) {
        rampTimer?.invalidate()
        let start = player.volume, t0 = Date()
        rampTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { [weak self] timer in
            guard let self else { timer.invalidate(); return }
            let k = min(1, Date().timeIntervalSince(t0) / max(seconds, 0.01))
            self.player.volume = start + (target - start) * Float(k)
            if k >= 1 { timer.invalidate(); then?() }
        }
    }

    func finish(fast: Bool) {
        guard phase != .ending && phase != .done else { return }
        phase = .ending
        overlay.ignoresMouseEvents = true
        let d = fast ? 0.4 : 1.2
        log(fast ? "关闭（跳过）" : "淡出")
        skipWin.orderOut(nil)
        hideSubtitles()
        rampVolume(to: 0, over: d)
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = d
            overlay.animator().alphaValue = 0
        }, completionHandler: { [weak self] in self?.teardown() })
    }

    func teardown() {
        guard phase != .done else { return }
        phase = .done
        tick?.invalidate(); rampTimer?.invalidate()
        if let mouseMonitor { NSEvent.removeMonitor(mouseMonitor) }
        if let o = timeObserver { player.removeTimeObserver(o) }
        if let o = volObserver { player.removeTimeObserver(o) }
        if let o = subObserver { player.removeTimeObserver(o) }
        if let o = endObserver { NotificationCenter.default.removeObserver(o) }
        let workspace = NSWorkspace.shared.notificationCenter
        workspaceObservers.forEach { workspace.removeObserver($0) }
        player.pause(); player.replaceCurrentItem(with: nil)
        overlay.removeChildWindow(skipWin); overlay.removeChildWindow(subWin)
        skipWin.orderOut(nil); subWin.orderOut(nil); overlay.orderOut(nil)
        log("播放结束")
        onDone(self)
    }
}

// MARK: - 菜单里的滑块

final class SliderRow: NSView {
    let slider: NSSlider
    let valueLabel = NSTextField(labelWithString: "")
    var onChange: ((Double) -> Void)?

    init(title: String, value: Double, max: Double) {
        slider = NSSlider(value: value, minValue: 0, maxValue: max, target: nil, action: nil)
        super.init(frame: NSRect(x: 0, y: 0, width: 260, height: 46))
        let t = NSTextField(labelWithString: title)
        t.font = .menuFont(ofSize: 13)
        t.frame = NSRect(x: 20, y: 24, width: 170, height: 17)
        valueLabel.font = .monospacedDigitSystemFont(ofSize: 12, weight: .regular)
        valueLabel.textColor = .secondaryLabelColor
        valueLabel.alignment = .right
        valueLabel.frame = NSRect(x: 190, y: 24, width: 50, height: 17)
        slider.frame = NSRect(x: 18, y: 4, width: 224, height: 20)
        slider.isContinuous = true
        slider.target = self; slider.action = #selector(changed)
        addSubview(t); addSubview(valueLabel); addSubview(slider)
        refreshLabel()
    }
    required init?(coder: NSCoder) { fatalError() }
    func refreshLabel() { valueLabel.stringValue = "\(Int((slider.doubleValue * 100).rounded()))%" }
    @objc func changed() { refreshLabel(); onChange?(slider.doubleValue) }
}

// MARK: - 设置窗口（菜单栏图标被系统藏起来时也能调）

/// Controller 里有自己的 log（带应用名前缀），它用这个调到全局的 log
func globalLog(_ m: String) { log(m) }

// MARK: - 设置窗口（菜单栏图标被系统藏起来时也能调）：最上面先选应用，下面是这个应用自己的设置

final class SettingsWindow: NSWindowController {
    let picker = NSPopUpButton(frame: .zero, pullsDown: false)
    let intro = NSButton(checkboxWithTitle: "", target: nil, action: nil)
    let loop = NSButton(checkboxWithTitle: "保留循环背景", target: nil, action: nil)
    var rows: [SliderRow] = []
    weak var app: AppDelegate?
    var current: Controller? { app.flatMap { $0.controllers[max(0, picker.indexOfSelectedItem)] } }

    init(app: AppDelegate) {
        self.app = app
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 300, height: 400),
                         styleMask: [.titled, .closable], backing: .buffered, defer: false)
        w.title = "AI 开机动画"
        w.isReleasedWhenClosed = false
        super.init(window: w)
        picker.addItems(withTitles: app.controllers.map { $0.p.menuTitle })
        picker.target = self; picker.action = #selector(picked)
        intro.target = self; intro.action = #selector(toggled)
        loop.target = self; loop.action = #selector(toggled)
        let r1 = SliderRow(title: "背景不透明度", value: 0.25, max: 0.8)
        r1.onChange = { [weak self] v in self?.current?.update { $0.residualAlpha = v } }
        let r2 = SliderRow(title: "开屏音量", value: 1, max: 1)
        r2.onChange = { [weak self] v in self?.current?.update { $0.introVolume = v } }
        rows = [r1, r2]
        let b1 = NSButton(title: "播放一次开屏动画", target: self, action: #selector(previewIntro))
        let b2 = NSButton(title: "开启循环背景", target: self, action: #selector(previewLoop))
        let b3 = NSButton(title: "关掉动画 / 背景", target: self, action: #selector(stopShow))
        let tip = NSTextField(wrappingLabelWithString: "以后想打开这个窗口：在聚焦搜索（⌘空格）里搜 AIBootAnimations 打开。")
        tip.font = .systemFont(ofSize: 11); tip.textColor = .secondaryLabelColor
        for r in rows { r.widthAnchor.constraint(equalToConstant: 260).isActive = true
                        r.heightAnchor.constraint(equalToConstant: 46).isActive = true }
        let stack = NSStackView(views: [picker, intro, loop] + rows + [b1, b2, b3, tip])
        stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 10
        stack.edgeInsets = NSEdgeInsets(top: 18, left: 20, bottom: 18, right: 20)
        stack.setCustomSpacing(16, after: picker); stack.setCustomSpacing(16, after: loop); stack.setCustomSpacing(16, after: r2)
        tip.preferredMaxLayoutWidth = 260
        w.contentView = stack
        refresh()
    }
    required init?(coder: NSCoder) { fatalError() }

    func select(_ c: Controller) {
        if let i = app?.controllers.firstIndex(where: { $0 === c }) { picker.selectItem(at: i) }
        refresh()
    }
    func refresh() {
        guard let c = current else { return }
        intro.title = "打开 \(c.p.appName) 时播放开屏动画"
        intro.state = c.config.playIntro ? .on : .off
        loop.state = c.config.loopBackground ? .on : .off
        rows[0].slider.doubleValue = c.config.residualAlpha
        rows[1].slider.doubleValue = c.config.introVolume
        rows.forEach { $0.refreshLabel() }
    }
    @objc func picked() { refresh() }
    @objc func toggled() {
        current?.update { $0.playIntro = intro.state == .on; $0.loopBackground = loop.state == .on }
        current?.refreshMenu()
    }
    @objc func previewIntro() { current?.previewIntro() }
    @objc func previewLoop() { current?.previewLoop() }
    @objc func stopShow() { current?.stopShow() }
}

// MARK: - 一个应用的控制器：它自己的设置、正在放的那一场、二级菜单

final class Controller: NSObject {
    let p: Profile
    weak var app: AppDelegate?
    var config: Config
    var show: Show?
    var waiting: Timer?
    let submenu = NSMenu()
    var introItem: NSMenuItem!
    var loopItem: NSMenuItem!
    var loopNowItem: NSMenuItem!   // 开着背景时是「关掉循环背景」，没开时是「开启循环背景」
    var pauseItem: NSMenuItem!
    var alphaRow: SliderRow!
    var introVolRow: SliderRow!

    init(_ p: Profile, app: AppDelegate) {
        self.p = p; self.app = app
        config = Config.load(p)
        super.init()
        buildSubmenu()
    }

    var targetRunning: Bool { !NSRunningApplication.runningApplications(withBundleIdentifier: p.bundleID).isEmpty }
    var introURL: URL { Bundle.main.url(forResource: "intro", withExtension: "mp4", subdirectory: p.key)! }
    var loopURL: URL { Bundle.main.url(forResource: "loop", withExtension: "mp4", subdirectory: p.key)! }
    func log(_ msg: String) { globalLog("[\(p.menuTitle)] \(msg)") }

    /// 二级菜单：就是原来单独版的那一整套
    func buildSubmenu() {
        let m = submenu
        m.autoenablesItems = false   // 「暂停」在没开背景时要变灰，由 refreshMenu 自己定
        let h1 = NSMenuItem(title: "打开 \(p.appName) 时", action: nil, keyEquivalent: ""); h1.isEnabled = false
        m.addItem(h1)
        introItem = item("播放开屏动画", #selector(toggleIntro))
        loopItem = item("保留循环背景", #selector(toggleLoop))
        m.addItem(.separator())
        alphaRow = SliderRow(title: "背景不透明度", value: config.residualAlpha, max: 0.8)
        alphaRow.onChange = { [weak self] v in self?.update { $0.residualAlpha = v } }
        addView(alphaRow)
        introVolRow = SliderRow(title: "开屏音量", value: config.introVolume, max: 1)
        introVolRow.onChange = { [weak self] v in self?.update { $0.introVolume = v } }
        addView(introVolRow)
        m.addItem(.separator())
        let h2 = NSMenuItem(title: "现在，在 \(p.appName) 窗口上", action: nil, keyEquivalent: ""); h2.isEnabled = false
        m.addItem(h2)
        item("播放一次开屏动画", #selector(previewIntro))
        loopNowItem = item("开启循环背景", #selector(toggleLoopNow))
        pauseItem = item("暂停循环背景", #selector(togglePause))
        m.addItem(.separator())
        item("在设置窗口里调…", #selector(openSettingsHere))
    }
    @discardableResult
    func item(_ title: String, _ sel: Selector) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: sel, keyEquivalent: ""); i.target = self; submenu.addItem(i); return i
    }
    func addView(_ v: NSView) { let i = NSMenuItem(); i.view = v; submenu.addItem(i) }

    func refreshMenu() {
        app?.settings?.refresh()
        introItem.state = config.playIntro ? .on : .off
        loopItem.state = config.loopBackground ? .on : .off
        let s = show.flatMap { $0.phase == .intro || $0.phase == .revealed ? $0 : nil }
        loopNowItem.title = s == nil ? "开启循环背景" : (s!.phase == .intro ? "关掉开屏动画" : "关掉循环背景")
        loopNowItem.isEnabled = s != nil || targetRunning
        pauseItem.title = s?.paused == true ? "继续播放循环背景" : "暂停循环背景"
        pauseItem.isEnabled = s?.phase == .revealed
        alphaRow.slider.doubleValue = config.residualAlpha; alphaRow.refreshLabel()
        introVolRow.slider.doubleValue = config.introVolume; introVolRow.refreshLabel()
    }

    func update(_ change: (inout Config) -> Void) {
        change(&config); config.save(p.key); show?.apply(config)
    }

    @objc func toggleIntro() { update { $0.playIntro.toggle() }; refreshMenu(); log("开屏动画 → \(config.playIntro)") }
    @objc func toggleLoop() { update { $0.loopBackground.toggle() }; refreshMenu(); log("循环背景 → \(config.loopBackground)") }
    @objc func previewIntro() { show?.teardown(); _ = startOnCurrentWindow(withIntro: true, exitWhenDone: false) }
    @objc func previewLoop() { show?.teardown(); _ = startOnCurrentWindow(withIntro: false, exitWhenDone: false) }
    @objc func stopShow() { show?.finish(fast: true) }
    @objc func toggleLoopNow() {
        if let s = show, s.phase == .intro || s.phase == .revealed { s.finish(fast: true) } else { previewLoop() }
    }
    @objc func togglePause() { guard let s = show else { return }; s.setPaused(!s.paused) }
    @objc func openSettingsHere() { app?.openSettings(); app?.settings?.select(self) }

    @discardableResult
    func startOnCurrentWindow(withIntro: Bool, exitWhenDone: Bool) -> Bool {
        guard show == nil,
              let a = NSRunningApplication.runningApplications(withBundleIdentifier: p.bundleID).first,
              let t = findTargetWindow(pid: a.processIdentifier) else { return false }
        start(pid: a.processIdentifier, target: t, withIntro: withIntro, exitWhenDone: exitWhenDone)
        return true
    }

    func launched(_ a: NSRunningApplication) {
        config = Config.load(p)
        log("检测到 \(p.appName) 启动 pid=\(a.processIdentifier)，开屏=\(config.playIntro) 循环背景=\(config.loopBackground)")
        guard config.playIntro || config.loopBackground else { return }
        show?.teardown()
        let withIntro = config.playIntro
        // 等主窗口出现，最多 30 秒
        let pid = a.processIdentifier, deadline = Date().addingTimeInterval(30)
        waiting?.invalidate()
        waiting = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] timer in
            guard let self else { timer.invalidate(); return }
            if let t = findTargetWindow(pid: pid) {
                timer.invalidate()
                self.waiting = nil
                self.start(pid: pid, target: t, withIntro: withIntro, exitWhenDone: false)
            } else if Date() > deadline || a.isTerminated {
                timer.invalidate()
                self.waiting = nil
                self.log("等不到 \(self.p.appName) 主窗口，放弃这次")
            }
        }
    }

    func terminated() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in
            guard let self, !self.targetRunning else { return }
            self.waiting?.invalidate()
            self.waiting = nil
            self.show?.teardown()
            self.app?.syncIcon()
        }
    }

    func start(pid: pid_t, target: TargetWindow, withIntro: Bool, exitWhenDone: Bool) {
        let t0 = Date()
        let args = CommandLine.arguments
        let testRepeats = args.firstIndex(of: "--repeats").flatMap { i in i + 1 < args.count ? Int(args[i + 1]) : nil }
        let repeats = min(120, max(1, testRepeats ?? 120))
        let timeline = buildTimeline(intro: withIntro ? introURL : nil, loop: loopURL, repeats: repeats)
        let s = Show(pid: pid, target: target, config: config, withIntro: withIntro, timeline: timeline,
                     cornerRadius: p.cornerRadius) { [weak self] finished in
            if self?.show === finished { self?.show = nil }
            if self?.app?.statusItem != nil { self?.refreshMenu(); self?.app?.syncIcon() }
            if exitWhenDone { DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) { exit(0) } }
        }
        show = s
        if let i = args.firstIndex(of: "--test-skip"), i + 1 < args.count, let sec = Double(args[i + 1]) {
            DispatchQueue.main.asyncAfter(deadline: .now() + sec) { [weak s] in s?.skipIntro() }   // 测试用：N 秒后模拟点「跳过」
        }
        log("从点击到浮层出现 \(Int(Date().timeIntervalSince(t0) * 1000))ms")
    }
}

// MARK: - App：一个菜单栏图标（Claude 的 ✨），一级菜单选应用，二级菜单是那个应用的设置

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    var controllers: [Controller] = []
    var statusItem: NSStatusItem!
    var settings: SettingsWindow?
    var topItems: [NSMenuItem] = []

    /// 菜单栏图标只在四个应用里至少有一个开着（或正在放动画）时出现
    func syncIcon() {
        let v = controllers.contains { $0.targetRunning || $0.show != nil }
        guard let si = statusItem, si.isVisible != v else { return }
        log("菜单栏图标 → \(v ? "显示" : "隐藏")")
        // macOS 在图标隐藏时会把记住的位置删掉，再显示就被排到最左边（刘海后面）。所以隐藏前先把位置抄一份，显示前再写回去。
        let d = UserDefaults.standard
        if !v, let pos = d.object(forKey: Self.posKey) { d.set(pos, forKey: Self.savedPosKey) }
        if v { d.set(d.object(forKey: Self.savedPosKey) ?? Self.defaultPos, forKey: Self.posKey) }
        si.isVisible = v
    }
    static let posKey = "NSStatusItem Preferred Position main"
    static let savedPosKey = "iconPosition"   // 我们自己存的副本，系统不会删
    static let defaultPos = 230               // 原 Claude 版的位置（装四合一时原版会被卸掉，不抢位置）
    static let label = "io.github.celestewynne0506-pixel.ai-boot-animations"

    func controller(for bundleID: String?) -> Controller? { controllers.first { $0.p.bundleID == bundleID } }

    func applicationDidFinishLaunching(_ n: Notification) {
        controllers = Profile.all.map { Controller($0, app: self) }
        let args = CommandLine.arguments
        // 测试用：--preview / --loop-only 配 --profile claude|gpt|antigravity|deepseek，对着已经开着的窗口放一场就退出
        if args.contains("--preview") || args.contains("--loop-only") {
            let key = args.firstIndex(of: "--profile").flatMap { i in i + 1 < args.count ? args[i + 1] : nil } ?? "claude"
            guard let c = controllers.first(where: { $0.p.key == key }),
                  c.startOnCurrentWindow(withIntro: !args.contains("--loop-only"), exitWhenDone: true) else {
                log("预览：没找到正在运行的 \(key) 窗口"); exit(1)
            }
            return
        }
        // 只留一个常驻：用文件锁判断，抢不到锁说明已经有一个在跑，通知它弹设置窗口后退出
        try? FileManager.default.createDirectory(at: Config.dir, withIntermediateDirectories: true)
        let lockFD = open(Config.dir.appendingPathComponent(".lock").path, O_CREAT | O_RDWR, 0o644)
        if lockFD < 0 || flock(lockFD, LOCK_EX | LOCK_NB) != 0 {
            log("已经有一个在运行，通知它弹出设置窗口后退出")
            DistributedNotificationCenter.default().postNotificationName(showSettingsNote, object: nil, userInfo: nil, deliverImmediately: true)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { exit(0) }
            return
        }
        DistributedNotificationCenter.default().addObserver(forName: showSettingsNote, object: nil, queue: .main) { [weak self] _ in
            self?.openSettings()
        }
        setupMenu()
        let ws = NSWorkspace.shared.notificationCenter
        ws.addObserver(self, selector: #selector(appLaunched(_:)), name: NSWorkspace.didLaunchApplicationNotification, object: nil)
        ws.addObserver(self, selector: #selector(appTerminated(_:)), name: NSWorkspace.didTerminateApplicationNotification, object: nil)
        syncIcon()
        log("常驻启动：" + controllers.map { "\($0.p.menuTitle) 开屏=\($0.config.playIntro) 背景=\($0.config.loopBackground)" }.joined(separator: "；"))
        if args.contains("--settings") { openSettings() }
        if args.contains("--show-menu") {   // 测试/截图用：自动弹出菜单
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self] in self?.statusItem.button?.performClick(nil) }
        }
    }

    func setupMenu() {
        // 带刘海的 MacBook 上，新图标默认会被排到刘海后面看不见：第一次启动时把位置固定到电池图标左边
        let d = UserDefaults.standard
        if CommandLine.arguments.contains("--reset-icon") { d.removeObject(forKey: Self.savedPosKey) }
        if d.object(forKey: Self.savedPosKey) == nil { d.set(d.object(forKey: Self.posKey) ?? Self.defaultPos, forKey: Self.savedPosKey) }
        d.set(d.object(forKey: Self.savedPosKey)!, forKey: Self.posKey)
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.autosaveName = "main"
        statusItem.behavior = []   // 不允许被拖出菜单栏删掉
        statusItem.isVisible = false
        statusItem.button?.image = NSImage(systemSymbolName: "sparkles", accessibilityDescription: "AI 开机动画")   // 沿用 Claude 版的 ✨
        let m = NSMenu(); m.delegate = self
        m.autoenablesItems = false
        // 一级菜单：先选要调哪个应用的动画
        for c in controllers {
            let i = NSMenuItem(title: c.p.menuTitle, action: nil, keyEquivalent: "")
            i.submenu = c.submenu
            m.addItem(i); topItems.append(i)
        }
        m.addItem(.separator())
        func add(_ t: String, _ s: Selector) { let i = NSMenuItem(title: t, action: s, keyEquivalent: ""); i.target = self; m.addItem(i) }
        add("设置窗口…", #selector(openSettings))
        add("打开设置文件夹…", #selector(openConfig))
        add("卸载 AI 开机动画…", #selector(uninstall))
        statusItem.menu = m
    }

    func menuWillOpen(_ menu: NSMenu) {
        for (c, i) in zip(controllers, topItems) {
            c.config = Config.load(c.p); c.refreshMenu()
            // 应用开着的排前面标个点，一眼看出现在能调哪个
            i.title = c.p.menuTitle + (c.targetRunning ? "  ●" : "")
        }
    }

    @objc func openConfig() { NSWorkspace.shared.open(Config.dir) }

    /// 彻底卸载：删登录项、小程序、设置、日志，然后退出。四个应用本身从来没被改过，不用还原。
    @objc func uninstall() {
        NSApp.activate(ignoringOtherApps: true)
        let a = NSAlert()
        a.messageText = "卸载 AI 开机动画？"
        a.informativeText = "会删除这个小程序、开机自启、设置和日志。Claude、ChatGPT、Antigravity、DeepSeek Harness 本身没有被改过，卸载后就是原装的样子。"
        a.addButton(withTitle: "卸载"); a.addButton(withTitle: "取消")
        a.buttons[0].hasDestructiveAction = true
        guard a.runModal() == .alertFirstButtonReturn else { return }
        controllers.forEach { $0.show?.teardown() }
        let fm = FileManager.default, home = fm.homeDirectoryForCurrentUser
        try? fm.removeItem(at: home.appendingPathComponent("Library/LaunchAgents/\(Self.label).plist"))
        try? fm.removeItem(at: Config.dir)
        try? fm.removeItem(at: logURL)
        try? fm.removeItem(at: home.appendingPathComponent("Library/Preferences/\(Self.label).plist"))
        let bundle = Bundle.main.bundleURL
        if bundle.pathExtension == "app" { try? fm.removeItem(at: bundle) }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/launchctl")
        p.arguments = ["bootout", "gui/\(getuid())/\(Self.label)"]
        try? p.run()
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) { exit(0) }
    }
    @objc func openSettings() {
        controllers.forEach { $0.config = Config.load($0.p) }
        if settings == nil { settings = SettingsWindow(app: self) }
        // 默认选中正开着的那个应用
        if let c = controllers.first(where: { $0.targetRunning }) { settings!.select(c) } else { settings!.refresh() }
        settings!.window?.center()
        NSApp.activate(ignoringOtherApps: true)
        settings!.showWindow(nil)
        settings!.window?.makeKeyAndOrderFront(nil)
    }
    /// 已经在后台运行时再次打开 App（启动台 / 聚焦搜索 / 双击）→ 弹出设置窗口
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        openSettings(); return false
    }

    @objc func appLaunched(_ n: Notification) {
        guard let a = n.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication,
              let c = controller(for: a.bundleIdentifier) else { return }
        syncIcon()
        c.launched(a)
    }
    @objc func appTerminated(_ n: Notification) {
        guard let a = n.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication,
              let c = controller(for: a.bundleIdentifier) else { return }
        c.terminated()
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
