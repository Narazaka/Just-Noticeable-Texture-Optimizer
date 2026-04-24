# JNTO 致命バグ先行修正 — sRGB/linear 整合と色メトリクス正常化

**日付**: 2026-04-23
**対象パッケージ**: `net.narazaka.vrchat.jnto`
**種別**: 局所バグ修正 (根本設計変更は別タスク)

## 1. 背景

2026-04-22 設計書 (`2026-04-22-jnto-redesign-design.md`) の実装は一通り済んでいるが、実装者が未経験だったため、perceptual gate まわりに以下の疑わしい挙動が残っている:

- sRGB / linear RT の扱いが `ShaderUsage` 由来で、`TextureImporter.sRGBTexture` と整合していない
- `ChromaDrift.compute` が sRGB RT のサンプル値 (自動デコード済み) にもう一度 `toLinear()` を適用している可能性
- 色差が ΔE76 で、色空間非均一性の補正が無い
- `coveredCount == 0` のとき `Pass = false` を返して「評価不能=不合格」と誤認している
- `ResolutionReducer.Resize` と `CompressionCandidateEnumerator.ComputeAspectSize` で size 丸めが別々に実装されている
- `PyramidBuilder` の `Graphics.Blit` による大幅縮小が aliasing を生んでいる疑い
- `Banding.compute` の 2 次微分ヒストグラムで bin clamp が両端に飽和している疑い
- `minRequiredDim` が `r98` パーセンタイルで critical area (顔細部等) を早期除外している疑い

根本的な設計変更 (per-texel LOD 化, JND empirical 校正, メトリクス再定義) は別タスクに回し、今回は**独立に直せる確定バグ**と**色空間整合性**を潰す。

## 2. ゴール

**判定妥当性を現実装レベルで正しく機能させる**。メトリクスの意味付けそのものは変えない (JND スケール定数やメトリクス挙動の大きな変更は行わない)。

## 3. スコープ

### 3.1 Tier 1 (確定修正)
| ID | 内容 |
|---|---|
| B2 | `sRGB` flag を `TextureImporter.sRGBTexture` から取得 |
| B1 + M2 | `ChromaDrift.compute` の sRGB/linear 整合 (`toLinear` 二重適用の解消) |
| M1 | 色差を ΔE76 → ΔE2000 に置換 |
| B5 | `coveredCount == 0` 時に `Pass = true` (no-op 扱い) を返す |
| B7 | `ResolutionReducer.Resize` と `ComputeAspectSize` の size 丸めロジック統一 |

### 3.2 Tier 2 (再現テストで実在確認 → 実在すれば修正)
| ID | 検証テスト |
|---|---|
| B3 | `PyramidBuilder` の Blit downsample が高周波で aliasing を起こすか合成チェッカー柄で確認 |
| B4 | `Banding.compute` の bin clamp が両端に飽和するか合成 sharp edge で確認 |
| B6 | `minRequiredDim` の `r98` が狭小高密度領域を除外するか合成 distribution で確認 |

### 3.3 スコープ外 (今回触らない)
- MSSL の `lum` を L* 化 (M3)
- 各メトリクスの JND スケール定数の empirical 再校正 (G4)
- per-tile → per-texel / quadtree 設計 (G1)
- 集約方式 (max × max) の見直し (G2, G3)
- `compression gate` の floor 根拠 (G5)
- Ridge の multi-scale 化 (M8)
- NormalAngle の specular-aware 化 (M6)
- AlphaQuantization の level-count scoring 見直し (M7)

これらは後続の根本設計タスクで扱う。

## 4. 各修正の設計詳細

### 4.1 B2: sRGB flag の由来を統一

**現状**: `GpuTextureContext.FromTexture2D`, `PyramidBuilder.CreatePyramid`, `TextureEncodeDecode.EncodeAndDecode`, `ResolutionReducer.Resize` で `sRGB = !isLinear` と書いており、`isLinear` は上流で `usage == Normal || usage == SingleChannel` から作られている。Color usage でも `sRGBTexture=false` (linear data として扱うべき) なテクスチャが存在するが、常に sRGB として扱われる。

**修正**: `Editor/Phase2/GpuPipeline/TextureColorSpace.cs` を新設し、以下のヘルパーを提供:

```csharp
public static class TextureColorSpace
{
    /// <summary>
    /// テクスチャが内部 RT 上で linear 値を保持すべきかを判定する。
    /// 優先順: TextureImporter.sRGBTexture → format → ShaderUsage fallback。
    /// </summary>
    public static bool IsLinear(Texture2D tex, ShaderUsage usageFallback);
}
```

判定ロジック:
1. `AssetImporter.GetAtPath` が `TextureImporter` として取得できれば `!imp.sRGBTexture` を返す
2. 取得不可なら format が「物理的に非 sRGB」(`RGBAHalf`/`RGBAFloat`/`R16`/`BC6H`) なら linear (BC4/BC5/BC7/DXT* は sRGB-flag が importer 設定次第なのでここで判定しない)
3. いずれも該当しなければ `usageFallback` で `Normal/SingleChannel` なら linear、それ以外 sRGB

**isLinear 算出の重複を排除**: 現状 `NewResolutionReducePass.ProcessTexture` (line 106) と `NewPhase2Pipeline.cs` (line 65) の 2 箇所で同じ式が書かれている。どちらも `TextureColorSpace.IsLinear(tex, usage)` の呼び出しに差し替え、`NewPhase2Pipeline` 内の derive は構築時に caller から受け取る `bool isLinear` をそのまま採用する。

対象ファイル:
- 新規: `Editor/Phase2/GpuPipeline/TextureColorSpace.cs`
- `NewResolutionReducePass.cs` — `isLinear` 算出を置換
- `NewPhase2Pipeline.cs` — コンストラクタに `bool isLinear` を追加 (ShaderUsage からの derive を削除)
- 既存呼び出し側 (`GpuTextureContext.FromTexture2D(tex, isLinear)`, `PyramidBuilder.CreatePyramid(..., isLinear)` 等) は引数を変えず、上流の `isLinear` 値だけが新経路になる
- テスト: `Tests/Editor/TextureColorSpaceTests.cs`

### 4.2 B1 + M2: ChromaDrift の sRGB/linear 整合

**現状**: Unity の sRGB RT (`sRGB=true`) を `SampleLevel` すると**自動で linear にデコードされた値が返る**。`ChromaDrift.compute:88-91` では受け取った値に `toLinear()` を適用しているため、二重線形化が起きている疑いが強い。

**B2 適用後の前提**: B2 により RT の sRGB flag が `TextureImporter.sRGBTexture` と整合するようになる。このとき:
- sRGB texture → RT sRGB=true → SampleLevel は linear 値を返す
- linear-data texture → RT sRGB=false → SampleLevel は raw 値 (すでに linear)

いずれの場合も**サンプル値は linear として扱うのが正しい**。よって shader 側の `toLinear()` は常に不要。

**修正**:
1. `ChromaDrift.compute` から `toLinear()` 呼び出しを削除 (`labO = toLab(toXYZ(rgbO))` に直変換)
2. `toLinear()` 関数本体は他で使わないので削除
3. `ChromaDriftMetric.cs` / binding 側には変更なし

**副次効果**: `DegradationCalibration.ChromaDriftScale` の既定値 (現 2.5) は、ΔE が正しく小さくなる分 (中〜高輝度で数倍) 、下方修正が必要になる可能性が高い。M1 (ΔE2000 置換) と連動して既存 E2E テストの挙動差分を見ながら default 値を再決定する。

### 4.3 M1: ΔE76 → ΔE2000 置換

**現状**: `ChromaDrift.compute:94-95` は `deltaE76(labO, labC) / 2.3` を使う。ΔE76 は色空間の非均一性 (青に過敏、灰に鈍感) を補正しない Euclidean 距離。

**修正**: ΔE2000 (CIE 2000) に置換。ΔE2000 は「1.0 = 1 JND」と規定済みなので `/2.3` は削除。

実装方針:
- `ChromaDrift.compute` に `float deltaE2000(float3 lab1, float3 lab2)` を追加
- 既存 `deltaE76` は削除
- ΔE2000 の式は標準 (Sharma 2005) を使用。sin/cos/atan2 を含むので HLSL で重いが、tile × N=8×8 スレッド ほどの負荷なので許容範囲

**JND 換算**: `de / 1.0 = JND`. `DegradationCalibration.ChromaDriftScale` は既存の "単位調整" としての役割を保つ (例えば top-3 平均 vs JND のキャリブレーション)。

テスト:
- `Tests/Editor/ChromaDriftDeltaE2000Tests.cs` を新設
- 既知 Lab ペアでの ΔE2000 値を literature (Sharma et al. 2005 の test pairs) と比較し誤差 < 0.1

### 4.4 B5: coveredCount == 0 時の返り値

**現状** (`NewPhase2Pipeline.cs:122-133`):
```csharp
return new NewPhase2Result {
    Final = orig, ...,
    FinalVerdict = new GateVerdict { Pass = false, ... },
    DecisionReason = "skipped: no tile coverage (cannot evaluate)",
};
```

**修正**:
- `Pass = true, TextureScore = 0f` に変更
- `DecisionReason = "kept original: no evaluable tile coverage"` に変更
- `DecisionLog.Add` を通じて報告されるとき「skipped」ではなく「kept as-is」として可視化される

影響: `JntoReportWindow` での表示が改善される。実際の処理 (orig を返す) は変わらない。

### 4.5 B7: Encode と Enumerate の size 丸め統一

**現状**:
- `CompressionCandidateEnumerator.ComputeAspectSize` で丸めた `(w, h)` を候補に格納
- `NewPhase2Pipeline.Encode` では `ResolutionReducer.Resize(src, targetMaxDim=max(cand.Width, cand.Height))` を呼ぶが、`Resize` 内部で**独自に `RoundToMultipleOf4` を掛けて** `tw, th` を計算する
- 両者の丸めロジックは**現時点では同じ**だが、片方だけ変更した場合に不整合が発生するリスクがある

**修正**:
- `ComputeAspectSize` を `CompressionCandidateEnumerator` から `Editor/Phase2/AspectSizeCalculator.cs` に切り出し、public static に
- `ResolutionReducer.Resize` に `public static Texture2D ResizeToSize(Texture2D src, int width, int height, bool isLinear)` を追加し、明示的な (w, h) で呼ぶ overload を提供
- `NewPhase2Pipeline.Encode` は `ResizeToSize(src, cand.Width, cand.Height, isLinear)` を呼ぶ
- `CompressionCandidateEnumerator` も `AspectSizeCalculator.Compute` を使う

テスト:
- `Tests/Editor/AspectSizeCalculatorTests.cs` を新設
- 非 POT × 非正方形 の全組み合わせで `(enumerator で計算した size) == (Encode で実際に作られる tex の size)` を assert

### 4.6 Tier 2 検証テスト

#### B3: PyramidBuilder aliasing
- `Tests/Editor/PyramidBuilderAliasingTests.cs` を新設
- 4K のチェッカーパターン (周期 2) を `PyramidBuilder.CreatePyramid(src, 1024, 1024, ...)` にかけて、期待値 (すべてのテクセル = 0.5 gray) との RMS を測定
- RMS > 閾値 (例: 0.05) なら "aliasing あり" を記録
- aliasing が確認された場合のみ、別 commit で box downsample compute shader に置換

#### B4: Banding bin clamp
- `Tests/Editor/BandingBinClampTests.cs` を新設
- 合成 "sharp edge in flat area" 画像 (左半分 0.1, 右半分 0.9) を作り、`d2c` ヒストグラムを CPU で再現して両端 bin 飽和率を測定
- 飽和率 > 閾値 (例: 10%) なら "飽和あり"
- 実在確認時: clamp 範囲を `[-0.5, 0.5]` から広げるか、両端 bin を peak 集計から除外する修正

#### B6: minRequiredDim percentile
- `Tests/Editor/MinRequiredDimPercentileTests.cs` を新設
- 合成 `rPerTile` 分布 (99% が 4, 1% が 64) を作り、現実装の `r98` と仮想 `r99.5` で `minRequiredDim` がどう変わるか測定
- `r98` が 1% の高密度領域を除外する挙動を再現できれば、`TextureOptimizer` に `CriticalAreaPercentile` (既定 99.5) を追加、または `max` を使う

## 5. 進め方

**1 バグ = 1 commit** で以下の順序で進める:

1. **B2 + TextureColorSpace 新設** — sRGB flag の由来統一 (影響範囲広、最初に通す)
2. **B1 + M2** — ChromaDrift の二重線形化修正 (B2 に依存)
3. **M1** — ΔE2000 置換 (B1/M2 に依存)
4. **B5** — coveredCount == 0 の返り値 (独立)
5. **B7** — Encode/Enumerate size 丸め統一 (独立)
6. **B3 検証テスト** — aliasing 実在確認
7. **B3 修正** (実在時のみ、別 commit)
8. **B4 検証テスト**
9. **B4 修正** (実在時のみ)
10. **B6 検証テスト**
11. **B6 修正** (実在時のみ)

各ステップで `test-driven-development` スキルに従い、**再現テスト → 修正 → リグレッション回避** の順で進める。

## 6. テスト拡充方針

### 6.1 既存テスト維持
既存テストは「旧挙動」を記録しているので、修正で落ちる可能性がある。落ちたテストは:
- **旧挙動が誤りだった場合**: テストを新挙動に更新し、コミットメッセージで理由を明記
- **旧挙動が正しかった場合**: 修正を見直し

### 6.2 新規テスト
| ファイル | 対応 | 内容 |
|---|---|---|
| `TextureColorSpaceTests.cs` | B2 | TextureImporter.sRGBTexture の各設定で IsLinear が期待値を返す |
| `ChromaDriftSRgbIntegrityTests.cs` | B1+M2 | 同一色の sRGB 入力に対して ΔE ≈ 0、二重線形化時の理論値との差分で検出 |
| `ChromaDriftDeltaE2000Tests.cs` | M1 | 既知 Lab ペアで ΔE2000 計算値検証 |
| `NoCoverageKeepsOriginalTests.cs` | B5 | coverage ゼロ時に `Pass=true`, `Final==orig` |
| `AspectSizeCalculatorTests.cs` | B7 | 非 POT × 非正方形組み合わせで Enumerate と Encode の size 一致 |
| `PyramidBuilderAliasingTests.cs` | B3 | チェッカー柄 aliasing 検出 |
| `BandingBinClampTests.cs` | B4 | sharp edge で bin clamp 飽和検出 |
| `MinRequiredDimPercentileTests.cs` | B6 | 高密度領域が `r98` で除外される挙動検出 |

## 7. リスク

| リスク | 緩和 |
|---|---|
| B2 の sRGB flag 変更で既存 E2E テスト挙動が変わる | 変更前に現挙動のベースラインを captured、変更後と diff を取り "期待される変化" を明示 |
| M1 の ΔE2000 置換で `ChromaDriftScale` の妥当値がズレる | 既存の NewPhase2PipelineE2E の baseline で相対比較、必要なら default 値を調整 |
| Tier 2 の実在確認で予想外に多くの修正が必要になる | 各 Tier 2 バグは独立 commit。影響を 1 つずつに閉じる |
| `TextureImporter` が取得不可な runtime-generated texture への対応 | fallback chain (format → ShaderUsage) で safe defaults |

## 8. 後方互換性

- `TextureOptimizer` コンポーネントの既存フィールドに変更なし
- `DegradationCalibration.asset` の既存値は維持 (ChromaDriftScale は default 値のみ調整)
- Pipeline / Metric 公開 API に変更なし (内部 field 追加のみ)

## 9. 未決事項 (実装時に決定)

- `ChromaDriftScale` 既定値の具体値 (現 2.5 から M1 (ΔE2000 置換) と B1+M2 (二重線形化解消) 適用後の baseline で決定)
- B3 修正時の box downsample 実装 (compute shader でフル実装するか `Graphics.Blit` のカスタムマテリアルで済ますか) — aliasing が実在確認できた時点で決める
- B6 修正時の percentile 値 (`r99.5` か max に近いか) — Tier 2 検証テストの結果を見て決める
