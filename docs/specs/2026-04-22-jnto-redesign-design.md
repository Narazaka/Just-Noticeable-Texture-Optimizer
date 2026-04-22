# JNTO 再設計 — タイル単位 perceptual gate による高速化と妥当性向上

**日付**: 2026-04-22
**対象パッケージ**: `net.narazaka.vrchat.jnto` (Just-Noticeable Texture Optimizer)
**種別**: アーキテクチャ再設計

## 1. 背景と目的

### 1.1 問題

現在の JNTO 実装は、VRChat アバター向けテクスチャを NDMF の Optimizing フェーズで解像度縮小・圧縮するプラグイン。以下の 2 つの深刻な問題がある:

- **速度**: 一般的なアバターで 3 分前後かかる。原因は Phase2 が常にフル origRes (4K 時に 16.7 Mpx) でメトリクス計算を繰り返すこと、実 encode→decode をサイズ×フォーマット全候補で実行すること、すべて CPU managed array で処理しており GC も頻発すること。
- **判定の妥当性**: 「各三角形の UV 領域に対して、縮小前後のテクスチャをその三角形の実効表示サイズで品質評価する」という本来要件に対し、現実装は per-pixel スコアの density-weighted 99%ile という間接的な近似に留まる。さらに実効表示解像度での比較そのものが行われていない。

### 1.2 要件

- 判定妥当性第一。速度はその次だが、一般的なアバターで初回 30 秒以下・キャッシュヒット時 3 秒以下を目標とする。
- 主に検出したい劣化軸は「**ぼやけ**」「**細密模様・線状模様のディテール消失**」「**ノーマルの布質感消失**」「**BC 系圧縮のバンディング・ブロッキング**」。色差は副次。
- 実装量・実装時間は制約としない。

## 2. アーキテクチャ概観

### 2.1 パイプライン

```
[Phase1] AlphaStripPass                    (現状維持)
[Phase2] ResolutionReducePass (再設計)
  ├─ 1. Reference / Statistics Collection
  │    - TextureReferenceGraph (現状維持)
  │    - Mesh Density Analyze
  │    - UV 固定グリッドタイルへの分割
  │    - タイル毎の実効表示解像度 r(T) 算出
  ├─ 2. GPU Pipeline Setup
  │    - Original texture を RenderTexture 化
  │    - Mipmap chain (ハードウェア) 構築
  │    - Per-block 統計 (PCA, color count, etc) を Compute Shader で事前計算
  ├─ 3. Binary Search over Size (降順)
  │    for size in binary_search:
  │        downsampled = resize(orig, size)
  │        predicted_fmt = analytical_format_predictor(downsampled_stats)
  │        candidate = encode_decode(downsampled, predicted_fmt or BC7)
  │        verdict = PerceptualGate.Evaluate(orig, candidate, tiles, r(T))
  │        if pass: record best (size, fmt)
  │    最小 pass サイズを確定
  ├─ 4. Final Encode (1 回)
  │    採用されたサイズ・フォーマットで最終 Texture2D を生成
  └─ 5. Material Clone + Texture Replacement (現状維持)
```

### 2.2 判定の核 — Perceptual Gate

タイル T ごとに、r(T) でダウンサンプルした orig と candidate を比較し、複数メトリクスの JND 正規化スコアを出す。タイル間・メトリクス間ともに **max 合成**し、テクスチャ全体のスカラー verdict を得る。

```
per_tile_score(T)    = max over metrics: s_m(T)
texture_score        = max over tiles:   per_tile_score(T)
verdict              = texture_score < T_preset
```

## 3. 設計詳細

### 3.1 タイル化

- **粒度**: UV 固定グリッドタイル (テクスチャをグリッド等分)
- **タイルサイズ**:
  - 基本: 64×64 texel
  - 入力最大辺が小さい場合のみ自動縮小:
    `tileSize = clamp(nearestPow2(max(W,H) / 8), 16, 64)`
  - 256 以下の入力で 32 や 16 に縮小、≥512 は固定 64
- **タイル統計**: そのタイルに一部でも属する三角形群のうち、
  - `density(T) = max worldArea/uvArea`
  - `boneW(T)   = max boneWeight`
  を保守側 max で採用

### 3.2 実効表示解像度 r(T)

タイル内 worst-case 三角形が Nyquist を満たす解像度を、視距離と HMD 解像度から逆算:

```
px_per_cm            = HMDPixelsPerDegree × (180/π) / viewDistanceCm
texelsPerCm_desired  = px_per_cm × oversampling(preset) × boneW(T)
r(T)                 = clamp(tileSize × sqrt(
                           texelsPerCm_desired² / density(T)
                       ), rMin, tileSize)
```

パラメータ既定値:
- `HMDPixelsPerDegree = 20` (Quest/Index 級の代表値。`TextureOptimizer` コンポーネントで上書き可)
- `oversampling`: Low=0.75, Medium=1.0, High=1.5, Ultra=2.0
- `viewDistanceCm`: 既存 `TextureOptimizer.ViewDistanceCm` (既定 30 cm)
- `rMin = 4` (これ以下のタイルは「見えない」扱いで評価スキップ)

r(T) はピラミッドのレベル選択に使う:
`level = log2(tileSize / r(T))`

### 3.3 メトリクス

#### 主軸: Multi-scale Structural Loss (MSSL)

`HighFrequencyMetric` を発展させたもの。タイル T では「r(T) に対応するピラミッドレベル」と「その下位 1–2 段」のみ評価する (r(T) より細かい帯域は Nyquist 外で見えないので評価不要)。

- **ピラミッド**: Gaussian pyramid (orig / candidate 各々)。Laplacian は隣接レベル差分。
- **軸1 (ぼやけ検出): Band Energy Loss**
  各スケール s で `||Laplacian_s||² on candidate / ||Laplacian_s||² on orig` の減衰率を計算。orig 側にエネルギーがあった帯域で candidate が失った割合が JND 閾値に対応。
- **軸2 (ディテール消失検出): Structure-only SSIM**
  SSIM の構造項 c(x,y) · s(x,y) のみ抽出 (luminance 項は捨てる)。局所コントラスト相関の保存を測る。5×5 or 7×7 separable window。
- **スケール重み**: CSF に準拠、中〜高周波帯域を重視、低周波帯域はほぼ無視。r(T) に対応するレベルに最大重みを置く。
- **JND キャリブレーション**: 各軸の数値を「1.0 = ちょうど気づく」に換算する係数は `DegradationCalibration.asset` に外出し。

#### 補助: Ridge Preservation (線状模様専用)

服のピンストライプ・刺繍輪郭など**細線・リッジ**の保存検出:
- 各ピクセルで Hessian 行列 (2 次偏微分) を計算し、固有値 λ1, λ2 (|λ1| ≥ |λ2|) を出す
- `ridgeness = |λ1| × (1 - |λ2/λ1|)` で「細線らしさ」を定量化 (Frangi filter 簡易版)
- orig / candidate 間で ridgeness の local max の強度比と位置シフトを測り、減衰率を JND スコア化
- `RingingMetric` (現状「エッジ近傍の振動」だけを見る) ではカバーできなかった軸

#### 補助: Banding (圧縮軸、平坦勾配の階段化)

- 平坦領域 (3×3 max-min < threshold) を検出
- その領域の 2 次微分ヒストグラムで階段化ピーク比を見る
- 既存 `BandingMetric` の思想を継承、JND 正規化に合わせた閾値キャリブレーション

#### 補助: BlockBoundary (圧縮軸、4×4 ブロック境界顕在化)

- 候補 texture でブロック境界 (x % 4 == 0) の水平エッジ強度と非境界エッジ強度の比
- 1.5 倍以上で JND 到達
- 既存 `BlockBoundaryMetric` の思想継承

#### 補助: AlphaQuantization (ColorAlpha 専用)

- `AlphaQuantizationMetric` 既存維持 (レベル数減少と RMS の max)

#### 補助: NormalAngle (NormalMap 専用)

- 既存 `NormalAngleMetric` 維持 (角度差)
- MSSL を normal vector field (デコード済み xyz) にも適用して「tangent 方向高周波の消失」も検出

#### 廃止メトリクス

現実装中の以下は再設計で廃止:
- `FlipMetric` (色差寄りでぼやけ検出に最適でない)
- `SsimMetric` フル (luminance 項を含むため、構造項のみ抽出した MSSL に吸収)
- `ChromaDriftMetric` (色差は副次要件)
- `RingingMetric` (Ridge Preservation に置換)
- `NormalVarianceMetric` (MSSL の normal 版に吸収)

### 3.4 Verdict 合成

```
per_tile_score(T)   = max over active_metrics: s_m(T)
texture_score       = max over tiles: per_tile_score(T)
verdict(preset)     = texture_score < T_preset
```

T_preset 初期値:
| Preset | T |
|---|---|
| Low | 1.5 |
| Medium | 1.0 |
| High | 0.7 |
| Ultra | 0.5 |

(JND = 1.0 が「ちょうど気づく」基準。Medium で JND 閾値、Low で許容、High/Ultra で控えめ)

### 3.5 サイズ × フォーマット探索

#### サイズ: 降順二分探索

- 候補: `{origRes, origRes/2, ..., minRes}` power-of-2 降順、`minRes = 32` (既存 `DensityCalculator.MinSize` 維持)
- origRes は常に pass (自己比較で JND=0) を前提に、二分探索で「最小 pass サイズ」を特定
- 4K → 32 なら 8 段階なので **3 回の gate 評価で確定**

#### フォーマット: 解析的予測 + 実測 verify

フォーマット候補は役割ごとに**軽量 fmt → BC7 fallback**の 2 段:

| TextureRole | 軽量候補 | Fallback |
|---|---|---|
| ColorOpaque | DXT1 | BC7 |
| ColorAlpha | DXT5 | BC7 |
| NormalMap | BC5 | BC7 |
| SingleChannel | BC4 | R8 |
| MatCap/LUT | BC7 | — |

ステップ:
1. **解析的予測** (encode なし):
   - 各 4×4 ブロックで RGB 共分散行列の固有値分解 → `planarity = λ2/λ1`, `nonlinearity = λ3/λ1`
   - RGB565 量子化誤差、アルファ非線形度、等を per-block で集計
   - DXT1/5/BC5 の原理的劣化モデルから「軽量 fmt pass 確信度」を出す
2. **実測 verify** (encode → decode → gate): 最終採用候補で 1 回のみ
3. 軽量 verify 不合格 → BC7 で再 verify → BC7 採用

**エンコード削減モード切替**: `TextureOptimizer` コンポーネントにフラグ (例: `EncodePolicy`) を設け、
- **Safe モード** (既定): 常に実測 verify を行う
- **Fast モード**: 予測確信度が閾値 (例 0.95) 超なら verify をスキップ

両モードを選択可能とする。

#### 試算 (4K 入力)

| 戦略 | size 試行 | encode 回数 |
|---|---|---|
| 現状 | 最悪 4 × 3 fmt | 12 |
| 新 (Safe モード) | 3 × ~1.5 fmt | ~5 |
| 新 (Fast モード) | 3 × ~1.2 fmt | ~4 |

### 3.6 GPU 実行基盤

#### 完全 GPU 実装 (Compute Shader 中心)

- **orig / candidate は RenderTexture で保持**
- **ピラミッド**: RenderTexture の hardware mipmap chain を利用
- **per-block 統計 (PCA 等)**: `Dispatch(W/4, H/4, 1)` で 4×4 ブロック並列
- **MSSL / Ridge / Banding / BlockBoundary / AlphaQuant / NormalAngle**:
  - `Dispatch(W/tileSize, H/tileSize, 1)` でタイル並列
  - 各スレッドグループがタイル内ピクセルを処理、group shared memory に集約、atomic max で global buffer に書く
- **GPU→CPU 読み戻しは per-tile max スコア配列のみ** (4K で 4096 float = 16 KB)
- **encode/decode 部分だけ CPU** (`EditorUtility.CompressTexture`)、直後に RenderTexture へ Blit で GPU に戻す

#### in-memory キャッシュ (1 ビルド実行中)

`Dictionary<Texture2D, TextureCache>` で以下を保持し、サイズ探索中に使い回す:
- orig のピラミッド RenderTexture
- orig の per-block 統計
- orig のデコード済みルミナンス/法線バッファ
- (size, fmt, score) の試行テーブル

### 3.7 永続キャッシュ

`Library/JntoCache/` に保存。

#### キャッシュキー

NDMF Optimizing phase 入り口時点の effective state を XXHash64 で束ねる:

```
CacheKey = XXHash64(
    texture.assetGuid,
    texture.contentHash,                    // AssetDatabase asset hash
    texture.importerSettings hash,
    for each reference (material-prop-renderer):
        mesh.vertices⊕uv⊕indices XXHash,    // ビルド時 effective mesh
        renderer.localToWorldMatrix,         // scale 反映
        material.shader GUID,
        relevant material property values,   // lilToon alpha 判定に使う値等
        submesh index,
    settings (preset, viewDistanceCm, boneWeights, calibration)
)
```

#### キャッシュ値

```
CacheValue = {
    finalSize: int,
    finalFormat: TextureFormat,
    compressedRawBytes?: byte[]     // 省容量モードでは省略
}
```

- **Full モード** (既定): encode 済み raw bytes を含む。ヒット時は `Texture2D.LoadRawTextureData()` 一発で再構築 (ms 単位)。
- **Compact モード**: raw bytes を省略、ヒット時も encode を再実行。ディスク容量を抑えたい場合に選択可能。

保存場所: `Library/JntoCache/` (VCS 対象外、プロジェクト単位)
無効化: キャッシュキー不一致で自動 miss、他 NDMF プラグインが mesh/material を変えても正しく miss する

### 3.8 可観測性

#### Decision Log (常時 ON)

NDMF `ErrorReport` への info 1 行サマリー/texture:

```
[JNTO] body_main.png: 4096→2048 BC7→DXT5, saved 16 MB, JND 0.82 (Medium:1.0), dominant=MSSL@scale2 t(342)
```

#### NDMF ErrorReport 統合

`INdmfError` 実装で「Just-Noticeable Texture Optimizer」セクションを持つ:
- **Info**: 各テクスチャの 1 行サマリー
- **Warning**: 想定外の結果 (gate 全 fail で orig 保持、BC7 fallback 強制、minRes 張り付き、特殊シェーダー未対応等)
- **Summary**: 合計削減 MB、処理時間、キャッシュヒット率

ビルド直後の NDMF 結果ダイアログで自動表示される。

#### JNTO Report Window (EditorWindow)

`Tools/Just-Noticeable Texture Optimizer/Report` から開く詳細ビュー:
- 最後のビルドで処理されたテクスチャの一覧、列: preview / 元→後サイズ / 元→後 fmt / 削減 bytes / JND score / 支配メトリクス / キャッシュヒット / 処理時間
- ソート・絞り込み可能
- 行クリックで: 全メトリクス×全サイズ試行の gate スコア matrix、支配タイルのスコアヒートマップ

#### Detail Log + Score Map Dump (debug flag)

`TextureOptimizer.DebugDumpPath` が設定されている場合のみ:
- per-tile × per-metric スコアを CSV/JSON に dump
- タイル空間スコア heatmap を PNG で dump
- サイズ探索の中間 verdict 全記録

#### Profiler Markers

`Profiler.BeginSample` を各フェーズに挿入:
- `JNTO.CollectReferences`
- `JNTO.AnalyzeDensity`
- `JNTO.BuildTiles`
- `JNTO.GpuPipeline.Setup`
- `JNTO.BinarySearch`
- `JNTO.Gate.MSSL` / `JNTO.Gate.Ridge` / `JNTO.Gate.Banding` / ...
- `JNTO.Encode.BC7` / `JNTO.Encode.DXT5` / ...
- `JNTO.Cache.Lookup` / `JNTO.Cache.Store`

#### NDMF Preview Overlay (オプション、debug flag)

既存 `TextureOptimizerPreviewFilter` を拡張して、Preview モード時にタイル境界と per-tile JND スコアを Scene View 上でヒートマップオーバーレイ表示。

## 4. モジュール分解

再設計後のディレクトリ構成案:

```
Editor/
  NDMF/
    JntoPlugin.cs                         (現状維持、Phase 配線のみ)
  Phase1/
    AlphaStripPass.cs                     (現状維持)
    ...
  Phase2/
    ResolutionReducePass.cs               (再設計)
    MeshDensityAnalyzer.cs                (現状維持)
    BoneClassifier.cs                     (現状維持)
    DensityCalculator.cs                  (再設計: r(T) 計算)
    Tiling/
      UvTileGrid.cs                       (新規: タイル分割 + 統計)
      TileStats.cs                        (新規)
    GpuPipeline/
      GpuTextureContext.cs                (新規: RenderTexture chain)
      PyramidBuilder.cs                   (新規: MipMap 生成)
      BlockStatsComputer.cs               (新規: PCA 等を Compute Shader で)
      Shaders/ (.compute)
        Pyramid.compute
        BlockStats.compute
        MSSL.compute
        Ridge.compute
        Banding.compute
        BlockBoundary.compute
        AlphaQuantization.compute
        NormalAngle.compute
    Gate/
      PerceptualGate.cs                   (旧 DegradationGate を置換)
      IMetric.cs                          (GPU + CPU 共通)
      MsslMetric.cs                       (新規)
      RidgeMetric.cs                      (新規)
      BandingMetric.cs                    (GPU 化)
      BlockBoundaryMetric.cs              (GPU 化)
      AlphaQuantizationMetric.cs          (GPU 化)
      NormalAngleMetric.cs                (GPU 化)
      DegradationCalibration.cs           (ScriptableObject: JND 係数)
      VerdictAggregator.cs                (max 合成)
    Compression/
      CompressionChain.cs                 (現状維持)
      TextureTypeClassifier.cs            (現状維持)
      FormatPredictor.cs                  (新規: 解析的予測)
      TextureEncodeDecode.cs              (現状維持、GPU 呼び出しラッパー化)
      BinarySearchStrategy.cs             (新規)
      Phase2Pipeline.cs                   (再設計)
    Cache/
      CacheKeyBuilder.cs                  (新規)
      PersistentCache.cs                  (新規)
      InMemoryCache.cs                    (新規)
    Reporting/
      NdmfReport.cs                       (新規)
      JntoReportWindow.cs                 (新規)
      DecisionLog.cs                      (新規)
      DebugDump.cs                        (新規)
  Resolution/
    ResolvedSettings.cs                   (拡張: HMDPixelsPerDegree, EncodePolicy 等)
    SettingsResolver.cs                   (現状維持)
  Preview/
    TextureOptimizerPreviewFilter.cs      (拡張: ヒートマップオーバーレイ)
Runtime/
  TextureOptimizer.cs                     (拡張: 新プロパティ)
  QualityPreset.cs                        (現状維持)
```

旧 `Degradation/` は新 `Gate/` に置き換え、廃止メトリクスは削除。

## 5. 速度目標

標準アバター (テクスチャ 20 枚、主に 2K、一部 4K) でのベンチマーク目標:

| シナリオ | 目標 | 現状 |
|---|---|---|
| 初回ビルド | ≤ 30 秒 | ~3 分 |
| 2 回目以降 (キャッシュヒット) | ≤ 3 秒 | ~3 分 |

**主要寄与**:
- GPU 化でメトリクス計算 ~10× 高速化
- 二分探索で size 試行回数削減
- 解析的予測で encode 回数 2–3× 削減
- タイル単位評価で低密度領域のコスト削減
- 永続キャッシュで再ビルド時ゼロコスト

## 6. 妥当性改善

| 改善項目 | 現状の問題 | 新設計 |
|---|---|---|
| 実視覚サイズでの評価 | フル origRes で比較 | タイル毎 r(T) スケールで比較 |
| ぼやけ直接検出 | 間接 (HighFrequency) | MSSL 軸1 Band Energy Loss |
| 細密模様の検出 | 99%ile で局所最悪を見逃し | per-tile max × tile-max 集約 |
| 線状模様保存 | 未検出 | Ridge Preservation |
| ノーマル質感 | 単一スケール variance | MSSL を normal field に適用 |
| 判定閾値管理 | per-metric 個別閾値 9 本 | JND 正規化 + 単一 T_preset |
| 再現性・デバッグ性 | ログ薄く追跡不能 | NDMF Report + Report Window + Score Dump |

## 7. 後方互換性

- `TextureOptimizer` コンポーネントの既存フィールド (`Preset`, `ViewDistanceCm`, `BoneWeights`, `ComplexityStrategy`) は維持
- `ComplexityStrategy` は新パイプラインでは使われないので deprecated 化 (削除は破壊変更なので別フェーズ)
- `DegradationThresholds` は `DegradationCalibration.asset` に移行、旧静的閾値テーブルは削除
- 新規追加フィールド: `HMDPixelsPerDegree`, `EncodePolicy (Safe/Fast)`, `CacheMode (Full/Compact/Disabled)`, `DebugDumpPath`

## 8. リスクと緩和策

| リスク | 緩和策 |
|---|---|
| Compute Shader が動かない環境 | minimum Unity Graphics Level 要件を明記、フォールバックは当面設けない (VRChat 向けなので環境は十分) |
| JND キャリブレーション初期値のズレ | `DegradationCalibration.asset` で外出し、ユーザー調整可、Detail Log で調整支援 |
| NDMF 他プラグインとの順序依存 | キャッシュキーに effective mesh hash を含めることで自動 miss する |
| タイル粒度で細密模様を見逃す | 将来的に Quadtree で細分化可能な設計、まず 64×64 で実地確認 |
| GPU メモリ不足 (4K × 複数同時) | テクスチャを 1 枚ずつシリアル処理、RenderTexture は使い回し |

## 9. NDMF バージョン方針

- 最新マイナーバージョンに対応する。それより古い NDMF との互換は維持しない。
- `package.json` の `vpmDependencies["nadena.dev.ndmf"]` は実装開始時点の最新マイナー以上に書き換える (現在の `>=1.5.0` は緩すぎるので引き上げ)。
- ErrorReport / Preview / Pass API は最新マイナーの API に直接依存し、互換 shim を挟まない。

## 10. 未決事項 (実装フェーズで決定)

- JND キャリブレーション初期値の具体数値 (MSSL band-energy loss → JND 変換定数等)
- Compute Shader のスレッドグループサイズ最適値 (タイル 64×64 なら 8×8 or 16×16)
- 永続キャッシュのスキーマバージョン管理方針
- NDMF 最新マイナーバージョンの具体番号確定
