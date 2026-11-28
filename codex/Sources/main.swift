// GPT 开机动画 —— 外置浮层小程序（Claude 开机动画的姊妹版）
// 检测 ChatGPT 桌面端（含 Codex，bundle id com.openai.codex）启动 → 盖在它的主窗口上播放开屏动画 → 进入循环帧后半透明、点击穿透，作为循环背景一直播放，点「跳过」关掉。
// 不修改 ChatGPT.app 的任何文件。
//
// Copyright (C) 2026 wynneceles
// 本程序以 GNU GPL v3 发布，见仓库根目录 LICENSE。
// This program is free software under the GNU General Public License v3.

import AppKit
import AVFoundation
import CoreImage

let targetBundleID = "com.openai.codex"   // ChatGPT 桌面端（和 Codex 是同一个应用）
let showSettingsNote = Notification.Name("io.github.celestewynne0506-pixel.gpt-boot-animation.show-settings")

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
    // 老版本的设置文件缺字段时，用默认值补上，不会整个作废
    init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        let def = Config()
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
        if let i = subtitles.firstIndex(where: { $0.text == "Now tell me yours." && $0.start == 8.9 && $0.end == 11.3 }) {
            subtitles[i] = def.subtitles[1]
        }
    }
    private enum Legacy: String, CodingKey { case enabled }

    static var dir: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/GPTBootAnimation")
    }
    static var file: URL { dir.appendingPathComponent("config.json") }

    static func load() -> Config {
        guard let data = try? Data(contentsOf: file),
              let c = try? JSONDecoder().decode(Config.self, from: data) else {
            let c = Config(); c.save(); return c
        }
        c.save()   // 补齐新字段
        return c
    }
    func save() {
        try? FileManager.default.createDirectory(at: Config.dir, withIntermediateDirectories: true)
        let enc = JSONEncoder(); enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        try? enc.encode(self).write(to: Config.file)
    }
}

struct Subtitle: Codable { var text: String; var start: Double; var end: Double }

// MARK: - 日志

let logURL = FileManager.default.homeDirectoryForCurrentUser
    .appendingPathComponent("Library/Logs/GPTBootAnimation.log")

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

/// overlayFrame：传了浮层窗口编号时，浮层在同一份窗口列表里的位置（Cocoa 坐标）；两边从同一份列表取，滑动中才不会各取到不同的一帧
struct TargetWindow { let number: Int; let cocoaFrame: NSRect; let coveredByOtherApp: Bool; let overlayFrame: NSRect? }

func findTargetWindow(pid: pid_t, overlay: Int? = nil) -> TargetWindow? {
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
    for w in list {
        if (w[kCGWindowNumber as String] as? Int) == num { break }
        guard let owner = w[kCGWindowOwnerPID as String] as? Int32, owner != pid, owner != me,
              (w[kCGWindowLayer as String] as? Int) == 0,
              let b = w[kCGWindowBounds as String] as? [String: CGFloat] else { continue }
        if let a = w[kCGWindowAlpha as String] as? Double, a <= 0 { continue }
        if CGRect(x: b["X"] ?? 0, y: b["Y"] ?? 0, width: b["Width"] ?? 0, height: b["Height"] ?? 0).intersects(r) {
            covered = true; break
        }
    }
    // CG 坐标原点在主屏左上角，Cocoa 在主屏左下角
    func cocoa(_ r: CGRect) -> NSRect { NSRect(x: r.origin.x, y: primary.frame.height - r.origin.y - r.height, width: r.width, height: r.height) }
    var overlayFrame: NSRect?
    if let overlay, let w = list.first(where: { ($0[kCGWindowNumber as String] as? Int) == overlay }),
       let b = w[kCGWindowBounds as String] as? [String: CGFloat] {
        overlayFrame = cocoa(CGRect(x: b["X"] ?? 0, y: b["Y"] ?? 0, width: b["Width"] ?? 0, height: b["Height"] ?? 0))
    }
    return TargetWindow(number: num, cocoaFrame: cocoa(r), coveredByOtherApp: covered, overlayFrame: overlayFrame)
}

// MARK: - 把成片 + 循环拼成一整条（约 8 小时），播放器全程只放一个文件，接缝不会卡；放到头再跳回循环开头

struct Timeline { let asset: AVAsset; let loopStart: CMTime }

func buildTimeline(intro: URL?, loop: URL, repeats: Int = 5000) -> Timeline {   // 5000 段 ≈ 8 小时
    let comp = AVMutableComposition()
    let vT = comp.addMutableTrack(withMediaType: .video, preferredTrackID: kCMPersistentTrackID_Invalid)!
    let aT = comp.addMutableTrack(withMediaType: .audio, preferredTrackID: kCMPersistentTrackID_Invalid)!
    var cursor = CMTime.zero

    func append(_ url: URL) {
        let asset = AVURLAsset(url: url, options: [AVURLAssetPreferPreciseDurationAndTimingKey: true])
        guard let sv = asset.tracks(withMediaType: .video).first else { return }
        let vDur = sv.timeRange.duration
        try? vT.insertTimeRange(CMTimeRange(start: sv.timeRange.start, duration: vDur), of: sv, at: cursor)
        if vT.preferredTransform != sv.preferredTransform { vT.preferredTransform = sv.preferredTransform }
        if let sa = asset.tracks(withMediaType: .audio).first {
            let aDur = CMTimeMinimum(sa.timeRange.duration, vDur)
            try? aT.insertTimeRange(CMTimeRange(start: sa.timeRange.start, duration: aDur), of: sa, at: cursor)
        }
        cursor = cursor + vDur
    }

    if let intro { append(intro) }
    let loopStart = cursor
    for _ in 0..<repeats { append(loop) }
    return Timeline(asset: comp.copy() as! AVComposition, loopStart: loopStart)
}

/// 8 小时的时间线拼起来要好几秒，所以目标应用一启动就在后台拼（应用没开时不拼、不占内存，关掉后就释放）。
/// 先拼一条短的（300 段，不到 1 秒）应急，再拼长的；放到头照样跳回循环开头继续。
final class TimelineCache {
    let introURL: URL, loopURL: URL, repeats: Int
    private var ready: [Bool: Timeline] = [:]
    private var short: [Bool: Timeline] = [:]
    init(introURL: URL, loopURL: URL, repeats: Int, variants: [Bool]) {
        self.introURL = introURL; self.loopURL = loopURL; self.repeats = repeats
        for withIntro in variants {
            DispatchQueue.global(qos: .userInitiated).async { [weak self] in
                let s = buildTimeline(intro: withIntro ? introURL : nil, loop: loopURL, repeats: min(repeats, 300))
                DispatchQueue.main.async { self?.short[withIntro] = s }
                guard self != nil else { return }
                let t0 = Date()
                let t = buildTimeline(intro: withIntro ? introURL : nil, loop: loopURL, repeats: repeats)
                DispatchQueue.main.async {
                    guard let self else { return }   // 中途已经释放（应用关了）就丢掉
                    self.ready[withIntro] = t; self.short[withIntro] = nil
                    log("后台时间线准备好（\(withIntro ? "开屏+循环" : "只循环")，\(repeats) 段，\(Int(Date().timeIntervalSince(t0) * 1000))ms）")
                }
            }
        }
    }
    var isReady: Bool { !ready.isEmpty }
    func get(withIntro: Bool) -> Timeline {
        if let t = ready[withIntro] { return t }
        if let t = short[withIntro] { log("长时间线还没好，先用短的"); return t }
        log("后台时间线还没好，现场拼短的顶上")
        return buildTimeline(intro: withIntro ? introURL : nil, loop: loopURL, repeats: min(repeats, 300))
    }
}

// MARK: - 视图

final class PlayerView: NSView {
    let playerLayer = AVPlayerLayer()
    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer = CALayer()
        layer!.backgroundColor = NSColor.black.cgColor
        layer!.cornerRadius = 17          // 跟 ChatGPT 窗口圆角对齐（macOS 26 上实测约 17pt）
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
    let onDone: () -> Void
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
    var activateObs: NSObjectProtocol?
    var inLoopAudio = false
    var endObserver: NSObjectProtocol?
    var lastTarget = -1
    var lastFrame = NSRect.zero
    var sysAnim = false          // 窗口在做系统动画（最小化、还原、缩放、分屏）时浮层先藏起来
    var stableSince = Date()
    enum SpaceState { case none, sliding, away }
    var spaceState = SpaceState.none   // 浮层正跟着桌面一起滑（sliding），或 ChatGPT 已经随桌面滑走、浮层留在它的桌面上（away）
    private(set) var paused = false   // 菜单「暂停循环背景」：画面停在当前这一帧，背景照样盖着

    init(pid: pid_t, target: TargetWindow, config: Config, withIntro: Bool,
         timeline: Timeline, onDone: @escaping () -> Void) {
        self.pid = pid; self.config = config; self.withIntro = withIntro; self.onDone = onDone
        self.timeline = timeline
        item = AVPlayerItem(asset: timeline.asset)
        item.add(videoOut)   // 点「跳过」时用来截当前这一帧做淡出

        overlay = makeBorderlessWindow(target.cocoaFrame)
        playerView = PlayerView(frame: NSRect(origin: .zero, size: target.cocoaFrame.size))
        playerView.autoresizingMask = [.width, .height]
        overlay.contentView = playerView

        skipWin = makeBorderlessWindow(NSRect(x: 0, y: 0, width: 72, height: 28))
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

        // 一整条（约 8 小时）放完后跳回循环段开头继续，背景不会掉
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
                guard let self, self.phase == .revealed else { return }
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
        log(withIntro ? "开始播放开屏动画，ChatGPT 窗口 \(target.number) \(target.cocoaFrame)"
                      : "直接开启循环背景，ChatGPT 窗口 \(target.number)")
        if !withIntro {
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = config.fadeDurationSeconds
                overlay.animator().alphaValue = config.residualAlpha
            }
        }

        tick = Timer.scheduledTimer(withTimeInterval: 1.0 / 60, repeats: true) { [weak self] _ in self?.follow() }
        RunLoop.main.add(tick!, forMode: .common)   // 拖动菜单、窗口时也继续跟随
        activateObs = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main) { [weak self] n in
            let app = n.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
            // 别的 App 到前台：它的窗口可能马上盖到 ChatGPT 上，窗口列表要晚几十毫秒才反映出来，先立刻降回普通层级，免得浮层压在它上面
            if let self, let app, app.processIdentifier != self.pid, app.processIdentifier != getpid() {
                self.lastOtherActivation = Date()
                if self.overlay.level != .normal {
                    for w in [self.overlay, self.subWin, self.skipWin] { w.level = .normal }
                    // 切桌面时别的 App 会在新桌面上到前台，这时浮层不在当前桌面，重新排序会把它带到新桌面上
                    if self.lastTarget >= 0 && self.spaceState == .none && self.overlay.isOnActiveSpace {
                        self.overlay.order(.above, relativeTo: self.lastTarget)
                    }
                }
            }
            self?.follow()
        }
    }
    var lastOtherActivation = Date.distantPast

    func place(_ t: TargetWindow) {
        if t.cocoaFrame != lastFrame {
            overlay.setFrame(t.cocoaFrame, display: true)
            subWin.setFrame(t.cocoaFrame, display: true)
            let f = t.cocoaFrame
            skipWin.setFrameOrigin(NSPoint(x: f.maxX - 72 - 18, y: f.maxY - 28 - 44))
            lastFrame = f
        }
    }

    /// 跟着 ChatGPT 窗口移动/缩放，并保持正好压在它上面（不会盖到别的 App 的窗口）
    func follow() {
        guard phase != .done else { return }
        guard let t = findTargetWindow(pid: pid, overlay: overlay.windowNumber) else {
            // 刚才正跟着桌面滑，或浮层不在当前桌面 = 用户切到别的桌面了：浮层留在 ChatGPT 那个桌面上，切回来时跟着桌面一起滑进来，不用等。
            // 滑走的那一刻 isOnActiveSpace 可能还没更新，不能单靠它
            if overlay.isVisible && (spaceState != .none || !overlay.isOnActiveSpace) {
                if spaceState != .away { spaceState = .away; log("ChatGPT 在别的桌面，浮层留在它的桌面上") }
            } else {
                if overlay.isVisible { overlay.orderOut(nil) }
                sysAnim = true; stableSince = Date()   // 再出现时多半是从 Dock 还原的动画，等停稳再露面
            }
            if NSRunningApplication(processIdentifier: pid) == nil { finish(fast: true) }
            return
        }
        // 位置大小变了、鼠标却没按着 = 系统在做动画（最小化、还原、缩放、分屏）。
        // 最小化的神灯效果会把窗口拧成漏斗形，矩形视频怎么跟都对不上，干脆先藏起来；
        // 用户自己拖动、拉伸窗口时鼠标是按着的，照常跟随
        let mouseDown = NSEvent.pressedMouseButtons & 1 != 0
        // 大小没变、鼠标没按着，而且浮层被挪离了 place() 放的位置、挪的距离和 ChatGPT 窗口一样 = 系统正在平移整个桌面（切桌面，含滑到一半停住）：
        // 浮层和 ChatGPT 窗口会被一起平移，不藏也不动它；滑动中回报的位置不能拿来 place，否则会被移两次。
        // 浮层明明显示着、却不在屏幕上的窗口列表里 = 它所在的桌面已经滑出画面，ChatGPT 窗口这时还可能以偏移一整屏的位置留在列表里，一样当成切桌面。
        // 两边挪得不一样多（例如拔掉外接显示器、改分辨率，系统各自挪窗口）就不是切桌面，照下面原本的做法藏起来、停稳再贴回去
        let movedWithDesktop: Bool
        if let o = t.overlayFrame {
            movedWithDesktop = (abs(o.minX - lastFrame.minX) >= 1 || abs(o.minY - lastFrame.minY) >= 1)
                && abs(o.minX - t.cocoaFrame.minX) < 1 && abs(o.minY - t.cocoaFrame.minY) < 1
        } else {
            movedWithDesktop = true
        }
        if t.cocoaFrame != lastFrame && !sysAnim && overlay.isVisible && !mouseDown
            && abs(t.cocoaFrame.width - lastFrame.width) < 1 && abs(t.cocoaFrame.height - lastFrame.height) < 1
            && movedWithDesktop {
            if spaceState != .sliding { spaceState = .sliding; log("切桌面：浮层跟着桌面一起平移，不藏也不动它") }
            return
        }
        let wasFollowingSpace = spaceState != .none
        spaceState = .none
        if t.cocoaFrame != lastFrame {
            if !mouseDown && !sysAnim && overlay.isVisible { log("窗口在做系统动画，浮层先藏起来"); sysAnim = true }
            if sysAnim { stableSince = Date(); lastFrame = t.cocoaFrame }
        }
        if sysAnim {
            if overlay.isVisible { overlay.orderOut(nil) }
            if Date().timeIntervalSince(stableSince) < 0.18 { return }   // 停稳 0.18 秒再回来
            sysAnim = false
            lastFrame = .zero   // 让 place 一定按最新位置重设一次
            log("窗口停稳，浮层回来")
        }
        place(t)
        // 目标窗口被挪到别的桌面：重新显示一次，浮层才会跟过去。刚跟着桌面滑回来时 isOnActiveSpace 可能还没更新，不能当成被挪走
        if !wasFollowingSpace && overlay.isVisible && !overlay.isOnActiveSpace { overlay.orderOut(nil) }
        // ChatGPT 窗口没被别的 App 压着（不管 ChatGPT 在不在前台）：浮层放到浮动层级，点 ChatGPT 时它把自己窗口提上来也盖不过浮层
        //   （以前只在 ChatGPT 在前台时才升，别的 App 最小化后再点 ChatGPT，ChatGPT 会先盖过浮层一两帧，看着就是闪黑）
        // 被别的 App 压着：降回普通层级，紧贴在 ChatGPT 窗口上方，不会盖住别的 App（这种情况点 ChatGPT 仍可能轻微闪一下：ChatGPT 提窗口比外部程序快，抢不过）
        let frontPID = NSWorkspace.shared.frontmostApplication?.processIdentifier ?? -1
        // 别的 App 刚到前台的 0.3 秒内先不升（它的窗口可能正要盖上来，窗口列表还没反映）；ChatGPT 自己在前台不受限
        let otherJustActivated = frontPID != pid && Date().timeIntervalSince(lastOtherActivation) < 0.3
        let level: NSWindow.Level = !t.coveredByOtherApp && !otherJustActivated ? .floating : .normal
        // 先把层级定好再显示，直接排到最终位置，不经过「先到最前面、再退回 ChatGPT 上方」的中间状态
        if overlay.level != level {
            for w in [overlay, subWin, skipWin] { w.level = level }
        }
        let wasVisible = overlay.isVisible
        if level == .normal { overlay.order(.above, relativeTo: t.number) }
        else if !wasVisible { overlay.orderFront(nil) }
        if t.number != lastTarget { lastTarget = t.number; log("跟随窗口 \(t.number)") }
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
        if let o = timeObserver { player.removeTimeObserver(o) }
        if let o = volObserver { player.removeTimeObserver(o) }
        if let o = subObserver { player.removeTimeObserver(o) }
        if let o = endObserver { NotificationCenter.default.removeObserver(o) }
        if let o = activateObs { NSWorkspace.shared.notificationCenter.removeObserver(o) }
        player.pause(); player.replaceCurrentItem(with: nil)
        overlay.removeChildWindow(skipWin); overlay.removeChildWindow(subWin)
        skipWin.orderOut(nil); subWin.orderOut(nil); overlay.orderOut(nil)
        log("播放结束")
        onDone()
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

final class SettingsWindow: NSWindowController {
    let intro = NSButton(checkboxWithTitle: "打开 ChatGPT 时播放开屏动画", target: nil, action: nil)
    let loop = NSButton(checkboxWithTitle: "保留循环背景", target: nil, action: nil)
    var rows: [SliderRow] = []
    weak var app: AppDelegate?

    init(app: AppDelegate) {
        self.app = app
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 300, height: 360),
                         styleMask: [.titled, .closable], backing: .buffered, defer: false)
        w.title = "GPT 开机动画"
        w.isReleasedWhenClosed = false
        super.init(window: w)
        intro.target = self; intro.action = #selector(toggled)
        loop.target = self; loop.action = #selector(toggled)
        let c = app.config
        let r1 = SliderRow(title: "背景不透明度", value: c.residualAlpha, max: 0.8)
        r1.onChange = { [weak app] v in app?.update { $0.residualAlpha = v } }
        let r2 = SliderRow(title: "开屏音量", value: c.introVolume, max: 1)
        r2.onChange = { [weak app] v in app?.update { $0.introVolume = v } }
        rows = [r1, r2]
        let b1 = NSButton(title: "播放一次开屏动画", target: app, action: #selector(AppDelegate.previewIntro))
        let b2 = NSButton(title: "开启循环背景", target: app, action: #selector(AppDelegate.previewLoop))
        let b3 = NSButton(title: "关掉动画 / 背景", target: app, action: #selector(AppDelegate.stopShow))
        let tip = NSTextField(wrappingLabelWithString: "以后想打开这个窗口：在聚焦搜索（⌘空格）里搜 GPTBootAnimation 打开。")
        tip.font = .systemFont(ofSize: 11); tip.textColor = .secondaryLabelColor
        for r in rows { r.widthAnchor.constraint(equalToConstant: 260).isActive = true
                        r.heightAnchor.constraint(equalToConstant: 46).isActive = true }
        let stack = NSStackView(views: [intro, loop] + rows + [b1, b2, b3, tip])
        stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 10
        stack.edgeInsets = NSEdgeInsets(top: 18, left: 20, bottom: 18, right: 20)
        stack.setCustomSpacing(16, after: loop); stack.setCustomSpacing(16, after: r2)
        tip.preferredMaxLayoutWidth = 260
        w.contentView = stack
        refresh()
    }
    required init?(coder: NSCoder) { fatalError() }

    func refresh() {
        guard let c = app?.config else { return }
        intro.state = c.playIntro ? .on : .off
        loop.state = c.loopBackground ? .on : .off
        rows[0].slider.doubleValue = c.residualAlpha
        rows[1].slider.doubleValue = c.introVolume
        rows.forEach { $0.refreshLabel() }
    }
    @objc func toggled() {
        app?.update { $0.playIntro = intro.state == .on; $0.loopBackground = loop.state == .on }
        app?.refreshMenu()
    }
}

// MARK: - App

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    var config = Config.load()
    var statusItem: NSStatusItem!
    var introItem: NSMenuItem!
    var loopItem: NSMenuItem!
    var loopNowItem: NSMenuItem!   // 开着背景时是「关掉循环背景」，没开时是「开启循环背景」
    var pauseItem: NSMenuItem!
    var alphaRow: SliderRow!
    var introVolRow: SliderRow!
    var show: Show?
    var waiting: Timer?
    var settings: SettingsWindow?
    var cache: TimelineCache?
    /// 只拼这次要放的那一条（开屏+循环 或 只循环），另一条用到时再现场拼短的
    func ensureCache(withIntro: Bool) -> TimelineCache {
        if let c = cache { return c }
        let args = CommandLine.arguments   // 测试用：--repeats N 缩短时间线
        let reps = args.firstIndex(of: "--repeats").flatMap { i in i + 1 < args.count ? Int(args[i + 1]) : nil } ?? 5000
        let c = TimelineCache(introURL: introURL, loopURL: loopURL, repeats: reps, variants: [withIntro])
        cache = c; log("开始在后台准备视频")
        return c
    }
    var targetRunning: Bool { !NSRunningApplication.runningApplications(withBundleIdentifier: targetBundleID).isEmpty }
    /// 没在播（播完、跳过关掉、或目标应用已关）→ 放掉视频时间线，不占内存
    func releaseIfIdle() {
        guard show == nil, waiting == nil || waiting?.isValid == false, cache != nil else { return }
        cache = nil; log("没在播放，释放视频")
        // 拼时间线用掉的几百 MB 释放后，分配器默认先攒着；1 秒后（播放器收尾完）主动还给系统
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) { malloc_zone_pressure_relief(nil, 0) }
    }
    /// 菜单栏图标只在目标应用开着时出现
    func syncIcon() {
        let v = targetRunning || show != nil
        guard let si = statusItem, si.isVisible != v else { return }
        log("菜单栏图标 → \(v ? "显示" : "隐藏")")
        // macOS 在图标隐藏时会把记住的位置删掉，再显示就被排到最左边（刘海后面）。
        // 所以隐藏前先把位置抄一份，显示前再写回去。
        let d = UserDefaults.standard
        if !v, let pos = d.object(forKey: Self.posKey) { d.set(pos, forKey: Self.savedPosKey) }
        if v { d.set(d.object(forKey: Self.savedPosKey) ?? Self.defaultPos, forKey: Self.posKey) }
        si.isVisible = v
        if v { logIconFrame() }
    }
    static let posKey = "NSStatusItem Preferred Position main"
    static let savedPosKey = "iconPosition"   // 我们自己存的副本，系统不会删
    static let defaultPos = 262   // 比 Claude 版（230）往左错开一格，两个都装时不抢位置
    let previewOnly = CommandLine.arguments.contains("--preview")
    let loopOnlyPreview = CommandLine.arguments.contains("--loop-only")

    var introURL: URL { Bundle.main.url(forResource: "intro", withExtension: "mp4")! }
    var loopURL: URL { Bundle.main.url(forResource: "loop", withExtension: "mp4")! }

    func applicationDidFinishLaunching(_ n: Notification) {
        if let i = CommandLine.arguments.firstIndex(of: "--render-subtitle"), i + 3 < CommandLine.arguments.count {
            // 测试用：离屏把字幕画到一张背景图上（不需要亮屏）：--render-subtitle 背景.png 输出.png 第几句
            let args = CommandLine.arguments
            let bg = NSImage(contentsOfFile: args[i + 1])!
            let size = NSSize(width: 1512, height: 876)
            let root = NSView(frame: NSRect(origin: .zero, size: size))
            let iv = NSImageView(frame: root.bounds); iv.image = bg; iv.imageScaling = .scaleAxesIndependently
            root.addSubview(iv)
            let sv = SubtitleView(frame: root.bounds); sv.widthRatio = config.subtitleWidthRatio; root.addSubview(sv)
            sv.setText(config.subtitles[Int(args[i + 3])! - 1].text); sv.label.alphaValue = 1
            root.layoutSubtreeIfNeeded()
            let rep = root.bitmapImageRepForCachingDisplay(in: root.bounds)!
            root.cacheDisplay(in: root.bounds, to: rep)
            try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: args[i + 2]))
            exit(0)
        }
        if CommandLine.arguments.contains("--wait-cache") {
            // 测试用：等后台时间线拼好再开始，量出跟点菜单一样的响应速度
            _ = ensureCache(withIntro: !loopOnlyPreview)
            Timer.scheduledTimer(withTimeInterval: 0.2, repeats: true) { [weak self] t in
                guard let self, self.cache?.isReady == true else { return }
                t.invalidate()
                if !self.startOnCurrentWindow(withIntro: !self.loopOnlyPreview, exitWhenDone: true) { exit(1) }
            }
            return
        }
        if previewOnly || loopOnlyPreview {
            log(loopOnlyPreview ? "预览模式（只循环背景）" : "预览模式")
            guard startOnCurrentWindow(withIntro: !loopOnlyPreview, exitWhenDone: true) else {
                log("没找到正在运行的 ChatGPT 窗口"); exit(1)
            }
            return
        }
        // 只留一个常驻：用文件锁判断，抢不到锁说明已经有一个在跑，通知它弹设置窗口后退出（避免菜单栏出现两个图标）
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
        NSWorkspace.shared.notificationCenter.addObserver(
            self, selector: #selector(appLaunched(_:)),
            name: NSWorkspace.didLaunchApplicationNotification, object: nil)
        NSWorkspace.shared.notificationCenter.addObserver(
            self, selector: #selector(appTerminated(_:)),
            name: NSWorkspace.didTerminateApplicationNotification, object: nil)
        syncIcon()
        log("常驻启动，开屏=\(config.playIntro) 循环背景=\(config.loopBackground)")
        if CommandLine.arguments.contains("--settings") { openSettings() }
        if CommandLine.arguments.contains("--show-menu") {   // 测试/截图用：自动弹出菜单
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self] in self?.statusItem.button?.performClick(nil) }
        }
    }

    func setupMenu() {
        // 带刘海的 MacBook 上，新图标默认会被排到刘海后面看不见。
        // 第一次启动时把它的位置固定到电池图标左边（数值 = 离屏幕右边的距离）；以后按住 ⌘ 拖动会自己记住新位置。
        let d = UserDefaults.standard
        if CommandLine.arguments.contains("--reset-icon") { d.removeObject(forKey: Self.savedPosKey) }
        if d.object(forKey: Self.savedPosKey) == nil { d.set(d.object(forKey: Self.posKey) ?? Self.defaultPos, forKey: Self.savedPosKey) }
        d.set(d.object(forKey: Self.savedPosKey)!, forKey: Self.posKey)
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.autosaveName = "main"
        statusItem.behavior = []   // 不允许被拖出菜单栏删掉
        statusItem.isVisible = targetRunning
        statusItem.button?.image = NSImage(systemSymbolName: "moon.stars", accessibilityDescription: "GPT 开机动画")   // 不用 ✨，和 Claude 版区分开
        let m = NSMenu(); m.delegate = self
        m.autoenablesItems = false   // 「暂停」在没开背景时要变灰，由 refreshMenu 自己定

        let h1 = NSMenuItem(title: "打开 ChatGPT 时", action: nil, keyEquivalent: ""); h1.isEnabled = false
        m.addItem(h1)
        introItem = item("播放开屏动画", #selector(toggleIntro), m)
        loopItem = item("保留循环背景", #selector(toggleLoop), m)
        m.addItem(.separator())

        alphaRow = SliderRow(title: "背景不透明度", value: config.residualAlpha, max: 0.8)
        alphaRow.onChange = { [weak self] v in self?.update { $0.residualAlpha = v } }
        addView(alphaRow, m)
        introVolRow = SliderRow(title: "开屏音量", value: config.introVolume, max: 1)
        introVolRow.onChange = { [weak self] v in self?.update { $0.introVolume = v } }
        addView(introVolRow, m)
        m.addItem(.separator())

        let h2 = NSMenuItem(title: "现在，在 ChatGPT 窗口上", action: nil, keyEquivalent: ""); h2.isEnabled = false
        m.addItem(h2)
        item("播放一次开屏动画", #selector(previewIntro), m)
        loopNowItem = item("开启循环背景", #selector(toggleLoopNow), m)
        pauseItem = item("暂停循环背景", #selector(togglePause), m)
        m.addItem(.separator())
        item("设置窗口…", #selector(openSettings), m)
        item("打开设置文件…", #selector(openConfig), m)
        item("卸载 GPT 开机动画…", #selector(uninstall), m)
        statusItem.menu = m
        refreshMenu()
        logIconFrame()
    }
    func logIconFrame() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) { [weak self] in
            guard let si = self?.statusItem, si.isVisible else { return }
            let w = si.button?.window
            log("菜单栏图标诊断：isVisible=\(si.isVisible) 窗口=\(w.map { "\($0.frame)" } ?? "无") 可见=\(w?.occlusionState.contains(.visible) ?? false) 屏幕=\(w?.screen?.localizedName ?? "无")")
        }
    }

    @discardableResult
    func item(_ title: String, _ sel: Selector, _ m: NSMenu) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: sel, keyEquivalent: ""); i.target = self; m.addItem(i); return i
    }
    func addView(_ v: NSView, _ m: NSMenu) { let i = NSMenuItem(); i.view = v; m.addItem(i) }

    func menuWillOpen(_ menu: NSMenu) { config = Config.load(); refreshMenu() }

    func refreshMenu() {
        settings?.refresh()
        introItem.state = config.playIntro ? .on : .off
        loopItem.state = config.loopBackground ? .on : .off
        // 正在放（开屏或循环背景）就显示「关掉…」，没在放就显示「开启循环背景」
        let s = show.flatMap { $0.phase == .intro || $0.phase == .revealed ? $0 : nil }
        loopNowItem.title = s == nil ? "开启循环背景" : (s!.phase == .intro ? "关掉开屏动画" : "关掉循环背景")
        pauseItem.title = s?.paused == true ? "继续播放循环背景" : "暂停循环背景"
        pauseItem.isEnabled = s?.phase == .revealed
        alphaRow.slider.doubleValue = config.residualAlpha; alphaRow.refreshLabel()
        introVolRow.slider.doubleValue = config.introVolume; introVolRow.refreshLabel()
    }

    func update(_ change: (inout Config) -> Void) {
        change(&config); config.save(); show?.apply(config)
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
    @objc func openConfig() { NSWorkspace.shared.open(Config.file) }

    /// 彻底卸载：删登录项、小程序、设置、日志，然后退出。ChatGPT.app 从来没被改过，不用还原。
    @objc func uninstall() {
        NSApp.activate(ignoringOtherApps: true)
        let a = NSAlert()
        a.messageText = "卸载 GPT 开机动画？"
        a.informativeText = "会删除这个小程序、开机自启、设置和日志。ChatGPT 本身没有被改过，卸载后就是原装的样子。"
        a.addButton(withTitle: "卸载"); a.addButton(withTitle: "取消")
        a.buttons[0].hasDestructiveAction = true
        guard a.runModal() == .alertFirstButtonReturn else { return }
        show?.teardown()
        let fm = FileManager.default, home = fm.homeDirectoryForCurrentUser
        let label = "io.github.celestewynne0506-pixel.gpt-boot-animation"
        try? fm.removeItem(at: home.appendingPathComponent("Library/LaunchAgents/\(label).plist"))
        try? fm.removeItem(at: Config.dir)
        try? fm.removeItem(at: logURL)
        try? fm.removeItem(at: home.appendingPathComponent("Library/Preferences/\(label).plist"))
        let bundle = Bundle.main.bundleURL
        if bundle.pathExtension == "app" { try? fm.removeItem(at: bundle) }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/launchctl")
        p.arguments = ["bootout", "gui/\(getuid())/\(label)"]
        try? p.run()
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) { exit(0) }
    }
    @objc func openSettings() {
        config = Config.load()
        if settings == nil { settings = SettingsWindow(app: self) }
        settings!.refresh()
        settings!.window?.center()
        NSApp.activate(ignoringOtherApps: true)
        settings!.showWindow(nil)
        settings!.window?.makeKeyAndOrderFront(nil)
    }
    /// 已经在后台运行时再次打开 App（启动台 / 聚焦搜索 / 双击）→ 弹出设置窗口
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        openSettings(); return false
    }

    @discardableResult
    func startOnCurrentWindow(withIntro: Bool, exitWhenDone: Bool) -> Bool {
        guard show == nil,
              let app = NSRunningApplication.runningApplications(withBundleIdentifier: targetBundleID).first,
              let t = findTargetWindow(pid: app.processIdentifier) else { return false }
        start(pid: app.processIdentifier, target: t, withIntro: withIntro, exitWhenDone: exitWhenDone)
        return true
    }

    @objc func appLaunched(_ n: Notification) {
        guard let app = n.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication,
              app.bundleIdentifier == targetBundleID else { return }
        syncIcon()
        config = Config.load()
        log("检测到 ChatGPT 启动 pid=\(app.processIdentifier)，开屏=\(config.playIntro) 循环背景=\(config.loopBackground)")
        guard config.playIntro || config.loopBackground else { return }
        show?.teardown()
        let withIntro = config.playIntro
        _ = ensureCache(withIntro: withIntro)   // 趁等窗口的这几秒在后台拼
        // 等主窗口出现，最多 30 秒
        let pid = app.processIdentifier, deadline = Date().addingTimeInterval(30)
        waiting?.invalidate()
        waiting = Timer.scheduledTimer(withTimeInterval: 0.05, repeats: true) { [weak self] timer in
            guard let self else { timer.invalidate(); return }
            if let t = findTargetWindow(pid: pid) {
                timer.invalidate(); self.start(pid: pid, target: t, withIntro: withIntro, exitWhenDone: false)
            } else if Date() > deadline || app.isTerminated {
                timer.invalidate(); self.releaseIfIdle(); log("等不到 ChatGPT 主窗口，放弃这次")
            }
        }
    }

    @objc func appTerminated(_ n: Notification) {
        guard let app = n.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication,
              app.bundleIdentifier == targetBundleID else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in
            guard let self, !self.targetRunning else { return }
            self.waiting?.invalidate()
            self.show?.teardown()
            self.syncIcon(); self.releaseIfIdle()
        }
    }

    func start(pid: pid_t, target: TargetWindow, withIntro: Bool, exitWhenDone: Bool) {
        var s: Show!
        let t0 = Date()
        s = Show(pid: pid, target: target, config: config, withIntro: withIntro,
                 timeline: ensureCache(withIntro: withIntro).get(withIntro: withIntro)) { [weak self] in
            if self?.show === s { self?.show = nil }
            self?.refreshMenu(); self?.syncIcon(); self?.releaseIfIdle()
            if exitWhenDone { DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) { exit(0) } }
        }
        show = s
        if let i = CommandLine.arguments.firstIndex(of: "--test-skip"), i + 1 < CommandLine.arguments.count,
           let sec = Double(CommandLine.arguments[i + 1]) {   // 测试用：N 秒后模拟点「跳过」
            DispatchQueue.main.asyncAfter(deadline: .now() + sec) { [weak s] in s?.skipIntro() }
        }
        log("从点击到浮层出现 \(Int(Date().timeIntervalSince(t0) * 1000))ms")
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
