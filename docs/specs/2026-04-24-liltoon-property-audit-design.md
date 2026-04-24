# lilToon プロパティカタログ網羅監査 設計

- Date: 2026-04-24
- Scope: `Packages/net.narazaka.vrchat.jnto`
- lilToon 検証対象版: `jp.lilxyzw.liltoon` 2.3.2（`Packages/jp.lilxyzw.liltoon/package.json`）
- カスタムシェーダー同梱拡張対象: `jp.sigmal00.uzumore-shader` 1.0.16
- Precedent: 直近コミット `638b30d fix: EmissionBlendMask is Color not SingleChannel` で `_EmissionBlendMask` / `_Emission2ndBlendMask` の誤分類が判明。同種の取りこぼしを網羅的に洗い出すのが目的。

## 背景

現行 `LilToonTextureCatalog`（`Editor/Shared/LilToonTextureCatalog.cs`）は lilToon の texture プロパティごとに `(ShaderUsage, AlphaUsed)` のペアを返す辞書。この分類結果は Phase2 の `FormatCandidateSelector` で以下のように候補 `TextureFormat` を選ぶ基礎になる:

- `Normal` → BC5/BC7
- `SingleChannel` → BC4/BC7（R のみ保持、他 channel 破棄）
- `Color` + `AlphaUsed=false` → DXT1/BC7（α 0bit）
- `Color` + `AlphaUsed=true` → DXT5/BC7（α 保持）

誤分類すると channel を不可逆に失う（先日の `_EmissionBlendMask` は `SingleChannel` と誤認されていたため BC4 が選ばれ、GBA が破棄される経路に入っていた）。

現カタログは `_MainTex` 系など約 50 エントリを持つが、(a) lilToon 全 variant を網羅していない、(b) カスタムシェーダー (`.lilcontainer` ベース、例: Uzumore Shader) への汎用対応機構を持たない、(c) 検証の根拠 (hlsl path:line) が多くのエントリで残されていない、という問題がある。

## 目的

1. lilToon の全 texture プロパティについて、`ShaderUsage` と読み出し channel 集合を `.hlsl` 実ソースから確定する
2. カスタムシェーダー (`.lilcontainer` ベース) でも同じ variant 判定と property 分類が効く抽象を導入する
3. カスタムシェーダー固有プロパティ用の拡張点を設け、Uzumore Shader 用の同梱拡張を提供する
4. 検証結果を `.hlsl` の `path:line` エビデンス付きで catalog に記録する
5. 既存 `ShaderUsage` に当てはまらない sampling pattern が見つかった場合、既存値への丸めをせず新分類を追加する方針を spec 化する

## スコープ

### 対象

- `Packages/jp.lilxyzw.liltoon/Shader/*.shader` 全ファイルとそこから include される `.hlsl` 全て
- `Packages/jp.sigmal00.uzumore-shader/Runtime/Shaders/custom.hlsl` と関連 `.lilblock`
- jnto 内既存コンシューマのシグネチャ更新（`LilToonAlphaRules`, `ShaderUsageInferrer`, `LilTexAlphaUsageAnalyzer`）
- カタログロジック/同梱拡張/既存コンシューマの回帰テスト

### 対象外

- lilToon バージョン更新を検知して catalog を自動再検証する機構
- レンダリングに使われない shader（例: `ltspass_baker*`, `ltspass_proponly`, `ltspass_dummy`）の catalog 化
- `FormatCandidateSelector` の選択戦略自体の変更（新 `ShaderUsage` 値追加に伴う最小限の case 追加は含む）
- NDMF プラグインの preview / reporting パイプラインへの影響（データモデル変更への追従のみ）

## データモデル

```csharp
// ShaderUsage は既存の Phase2.Compression.ShaderUsage を再利用する。
// 新 sampling pattern が見つかった場合は既存値に丸めず、この spec 修正 +
// FormatCandidateSelector への case 追加をセットで行い、Phase2.Compression.ShaderUsage に
// 新値を追加する。enum の物理的な場所はレイヤー的に曖昧さがあるが、現状唯一の
// ShaderUsage 定義が Phase2.Compression 配下にあるため、重複定義を避けて再利用する判断。

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    [Flags]
    public enum ChannelMask
    {
        None = 0,
        R = 1 << 0,
        G = 1 << 1,
        B = 1 << 2,
        A = 1 << 3,
        RGB  = R | G | B,
        RGBA = R | G | B | A,
    }

    public readonly struct LilToonPropertyInfo
    {
        public readonly Phase2.Compression.ShaderUsage Usage;   // 既存 enum を再利用
        public readonly ChannelMask ReadChannels;
        public readonly string EvidenceRef;

        public bool AlphaUsed => (ReadChannels & ChannelMask.A) != 0;
    }
}
```

### `AlphaUsed` は派生プロパティ

`ReadChannels` から計算で導出する。格納フィールドとしては持たない。これは `_BumpMap` の DXT5nm (`UnpackNormal` が `.ag` を読む)、`_EmissionBlendMask` のような `.rgba` 乗算、alpha clip 専用サンプリングなど、思いつく全ケースで `AlphaUsed = (ReadChannels & A) != 0` が成立することから妥当。

### `ShaderUsage` 拡張ポリシー

audit 中に既存 3 値 (`Color` / `Normal` / `SingleChannel`) に収まらない pattern が見つかった場合:

1. audit 作業を一時停止し、この spec を更新（新値の意味論と根拠 hlsl の位置）
2. `FormatCandidateSelector` に対応 case を追加（どの `TextureFormat` 候補を出すか）
3. 関連テストを追加
4. 新値での catalog エントリを記述

既存値への丸めは行わない。

### `ChannelMask` 粒度

R/G/B/A 個別 bit。`.rg` のみ / `.rb` のみ といった packed mask の将来ケースに備える。

### `EvidenceRef` 書式

- 1 箇所参照: `"lil_common_frag.hlsl:1841"`
- 複数箇所: `"lil_common_frag.hlsl:1841 ; lil_common_frag2.hlsl:523"`
- 間接参照を要補足する場合: `"lil_fur_frag.hlsl:88 (calcIntrudePos 経由で _UzumoreMask.r のみ参照)"`
- path は lilToon パッケージルートからの相対（`Packages/jp.lilxyzw.liltoon/Shader/` prefix は省略）
- カスタムシェーダー拡張内は `uzumore/custom.hlsl:NN` のような prefix 付き

## コンポーネント構成

```
Editor/Shared/
├── LilToonPropertyInfo.cs              // struct, ShaderUsage, ChannelMask
├── LilToonShaderIdentifier.cs          // Shader → variantId 抽出
├── LilToonPropertyCatalog.cs           // 公開 API (TryGet / RegisterExtension / UnregisterExtension)
├── ICustomPropertyCatalogExtension.cs  // 拡張インターフェース
├── Catalog/
│   └── LilToonCoreCatalogData.cs       // (variantId, prop) → info の大型データ
└── Extensions/
    └── UzumoreTextureCatalogExtension.cs   // 同梱拡張
```

既存 `LilToonTextureCatalog` は `LilToonPropertyCatalog` にリネームし、内部データは `LilToonCoreCatalogData` に分離。

### `LilToonShaderIdentifier`

```csharp
public static class LilToonShaderIdentifier
{
    /// 与えられた Shader が lilToon 系なら variantId (= アセットファイル名 stem) を返す。
    /// それ以外は null。
    public static string TryGetVariantId(Shader shader);

    /// テスト用に path 入力で同ロジックを試行する overload。
    internal static string TryGetVariantIdFromPath(string assetPath);
}
```

#### variantId 抽出ルール

```csharp
public static string TryGetVariantId(Shader shader)
{
    if (shader == null) return null;
    var path = AssetDatabase.GetAssetPath(shader);
    if (string.IsNullOrEmpty(path)) return null;
    return TryGetVariantIdFromPath(path);
}

internal static string TryGetVariantIdFromPath(string assetPath)
{
    if (string.IsNullOrEmpty(assetPath)) return null;
    var normalized = assetPath.Replace('\\', '/');

    // カスタムシェーダー: .lilcontainer ScriptedImporter 経由
    if (normalized.EndsWith(".lilcontainer", StringComparison.Ordinal))
        return Path.GetFileNameWithoutExtension(normalized);

    // lilToon 本体: Packages/jp.lilxyzw.liltoon/Shader/*.shader
    if (normalized.EndsWith(".shader", StringComparison.Ordinal)
        && normalized.Contains("/jp.lilxyzw.liltoon/Shader/"))
        return Path.GetFileNameWithoutExtension(normalized);

    return null;
}
```

lilToon を VPM でなくローカル Assets/ にコピーしている場合は対象外（誤検出リスクを下げる方を優先、該当ケースはユーザーが自前 extension で対応可能）。

### `ICustomPropertyCatalogExtension`

```csharp
public interface ICustomPropertyCatalogExtension
{
    bool Matches(Shader shader);
    bool TryClassify(Shader shader, string variantId, string propName, out LilToonPropertyInfo info);
}
```

### `LilToonPropertyCatalog`

```csharp
public static class LilToonPropertyCatalog
{
    public static bool TryGet(Shader shader, string propName, out LilToonPropertyInfo info);

    public static void RegisterExtension(ICustomPropertyCatalogExtension ext);
    public static bool UnregisterExtension(ICustomPropertyCatalogExtension ext);
}
```

- 登録は冪等（同一インスタンス重複登録は noop）
- thread-safety: Editor 専用のため main thread 単一アクセス前提
- `RegisterExtension(null)` は `ArgumentNullException`

### `LilToonCoreCatalogData`

```csharp
internal static class LilToonCoreCatalogData
{
    // key1: variantId (例 "lts_fur") または null（全 variant 共通）
    // key2: propName
    // 解決順: (variantId, prop) → (null, prop) → miss
    internal static bool TryGet(string variantId, string propName, out LilToonPropertyInfo info);
}
```

大半の prop (`_MainTex`, `_BumpMap` 等) は `variantId=null` の共通エントリで表現。fur/gem/refraction 等 variant 固有 prop や、同一 prop で variant 挙動が異なるものだけ具体 variantId 指定エントリを持つ。

### `UzumoreTextureCatalogExtension`（同梱）

```csharp
internal sealed class UzumoreTextureCatalogExtension : ICustomPropertyCatalogExtension
{
    const string UZUMORE_PATH_MARKER = "jp.sigmal00.uzumore-shader";

    [InitializeOnLoadMethod]
    static void Register() =>
        LilToonPropertyCatalog.RegisterExtension(new UzumoreTextureCatalogExtension());

    public bool Matches(Shader s) { /* asset path に marker 含むか */ }
    public bool TryClassify(...) { /* _UzumoreMask 等の分類 */ }
}
```

- `jp.sigmal00.uzumore-shader` への asmdef 参照なし（未インストール環境では `Matches` 常時 false で noop）
- Uzumore の `custom.hlsl` を私（実装担当 agent）が実読して分類を導出、`EvidenceRef` に hlsl path:line を残す

## データフロー

```
Material → material.shader
  │
  ▼  LilToonShaderIdentifier.TryGetVariantId(shader)
variantId  (null → lilToon 系でない、既存 fallback へ)
  │
  ▼  LilToonPropertyCatalog.TryGet(shader, propName)
  ┌──────────────────────────────────────────────────────┐
  │ 1. Match する最初の extension を探す:                 │
  │      for each ext in _registered:                      │
  │        if ext.Matches(shader):                         │
  │          if ext.TryClassify(...): return info          │
  │          break   ← Matches したが classify 未対応      │
  │                    → core catalog にフォールバック     │
  │ 2. LilToonCoreCatalogData:                             │
  │      (variantId, prop) hit → return                    │
  │      (null, prop) hit       → return                   │
  │      miss                                              │
  │ 3. 未知プロパティ fallback                             │
  └──────────────────────────────────────────────────────┘
```

### 解決順の意味論

**extensions が core より優先**: カスタムシェーダーが本体 prop の sampling を差し替える可能性に備える。

**1 extension が `Matches=true` かつ `TryClassify=false`**:
- `Matches` は「この extension が shader の管轄を自称」
- `TryClassify=false` は「当該 prop は独自分類を持たない、core に委ねる」
- この組み合わせは **break して core catalog へフォールバック**。次の extension は試さない（自称した extension が責任放棄したのにさらに別 extension を探すと意味論が曖昧になる）

**複数 extension が同 shader に match**: 登録順で最初のもののみ使用。想定外状況だが挙動は決定的。

### 未知プロパティ fallback

`LilToonPropertyCatalog.TryGet` が `false` を返すのは以下:
- `TryGetVariantId(shader) == null`（lilToon 系でない）
- catalog miss

呼び出し側の現行挙動を維持:
- `LilToonAlphaRules.IsAlphaUsed(shader, propName)`: false 時は **安全側 `true`**（α 使用扱い、strip 禁止）
- `ShaderUsageInferrer.Infer(material, propName, tex)`: false 時は `TextureImporter.textureType` 推定へフォールバック

### キャッシュ

- `TryGetVariantId(shader)`: 初期実装ではキャッシュなし。ベンチして必要なら後続対応
- `LilToonPropertyCatalog.TryGet`: Dictionary 2 段 + extensions 走査。extensions は通常 0–数個、走査コスト negligible

## 既存コンシューマの更新

| 現行 | 変更後 |
|---|---|
| `LilToonTextureCatalog.TryGet(propName, out info)` | `LilToonPropertyCatalog.TryGet(shader, propName, out info)` |
| `LilToonAlphaRules.IsAlphaUsed(propName)` | `LilToonAlphaRules.IsAlphaUsed(shader, propName)` |
| `LilTexAlphaUsageAnalyzer.IsLilToon(material)` | 削除。`LilToonShaderIdentifier.TryGetVariantId(material.shader) != null` で置換 |
| `ShaderUsageInferrer.Infer(material, propName, tex)` | 第 1 分岐を `LilToonPropertyCatalog.TryGet(material.shader, propName, out info)` に置換 |

`TextureRole` 列挙は `CacheKeyBuilder` 互換のため残置。今回変更しない。

## エラー / エッジケース

| ケース | 挙動 |
|---|---|
| `shader == null` | `TryGetVariantId` は null、`TryGet` は false |
| `propName == null` / 空 | `TryGet` は false |
| `AssetDatabase.GetAssetPath(shader)` が空 | lilToon 系でない扱い（null） |
| path が `.shader` だが lilToon パッケージ外 | variantId null（誤検出防止） |
| Extension の `Matches` / `TryClassify` が例外 | **補足しない**。extension 側のバグを隠さない |
| `RegisterExtension(null)` | `ArgumentNullException` |
| 同一インスタンス重複登録 | noop（冪等） |
| `UnregisterExtension` 未登録指定 | false、例外なし |
| Domain reload 後 | `[InitializeOnLoadMethod]` で同梱拡張が再登録、冪等性で吸収 |

## テスト戦略

### A. `LilToonShaderIdentifier` 単体テスト

`TryGetVariantIdFromPath(string)` overload で shader 実体なしに以下を検証:

- stock lilToon 代表 variant (`lts`, `lts_fur`, `lts_gem`, `lts_ref`, `ltsmulti`, `ltspass_opaque`, `ltsl`, `ltsother_bakeramp`) で正しい stem
- `.lilcontainer` 拡張子の path（fake 可）で正しい stem
- lilToon パッケージ外の `.shader` path で null
- Windows 区切り `\` を含む path が正規化される
- 空文字 / null で null
- shader 実体経由の `TryGetVariantId(Shader)` は統合テストとして最小限 smoke 1 ケース

### B. `LilToonPropertyCatalog` 解決ロジック単体テスト

stub `ICustomPropertyCatalogExtension` と fake shader（path 差し替え手段を通じて）で:

- core catalog のみ登録での hit / miss
- `(variantId, prop)` hit が `(null, prop)` より優先される
- Extension が Matches=true, TryClassify=true → core より優先
- Extension が Matches=true, TryClassify=false → core fallback、他 extension 試さない
- Extension が Matches=false → スキップして次の extension
- `RegisterExtension` 重複登録の冪等性
- `UnregisterExtension` の返り値と登録状態
- `RegisterExtension(null)` で `ArgumentNullException`

### C. `LilToonCoreCatalogData` 全エントリ table-driven テスト

`[TestCaseSource]` で catalog 全エントリを一括検証:

- `(Usage, ReadChannels)` が期待値と一致
- `EvidenceRef != null && EvidenceRef.Length > 0`
- `ReadChannels != None`（サンプルされない prop は catalog に入れない原則）

期待値は spec + 各エントリの hlsl 根拠が source of truth。テスト失敗時は「.hlsl を読み直して catalog または test のどちらが正しいか確定」させる。

### D. `UzumoreTextureCatalogExtension` テスト

- **ロジック層**: `Matches` が fake asset path で true/false、`TryClassify` が stub shader + propName で期待分類を返す
- **統合層**（Uzumore インストール時のみ、未インストール時 `Assert.Ignore`）: 実 shader を `Shader.Find` で取得し `LilToonPropertyCatalog.TryGet` が期待値を返す

### E. 既存コンシューマの回帰テスト

- `LilToonAlphaRulesTests` を新シグネチャ用に更新
- `ShaderUsageInferrerTests` は既存 fallback 挙動を引き続き検証

## 監査ワークフロー（実装時の作業プロセス）

実装段階で `LilToonCoreCatalogData` を埋める手順。`.hlsl` を実際に読んで判定するのが必須。

1. **全 variant 列挙**: `Packages/jp.lilxyzw.liltoon/Shader/*.shader` の中からレンダリングに使われる variant（`lts*`, `ltsmulti*`, `ltsl*`, `ltspass_*` の main/outline 用）を拾う
2. **各 variant から include chain を追跡**: `.shader` → HLSLINCLUDE / lilSubShaderBRP / 参照 .hlsl を辿って sampling 呼び出しを含む hlsl ファイル集合を確定
3. **sampling 抽出**: 各 hlsl で `LIL_SAMPLE_2D(_X, ...)` / `LIL_SAMPLE_2D_ST(_X, ...)` / `UnpackNormal(...)` などのマクロ呼び出しを列挙。これ以後の channel 参照（`.r`, `.rgba`, `UnpackNormal` 経由等）を読む
4. **分類確定**: `(ShaderUsage, ReadChannels, EvidenceRef)` を確定。variant をまたいで sampling pattern が一致する prop は `variantId=null` の共通エントリとして統合、異なる variant 固有 prop / 例外 pattern だけ具体 variantId エントリを追加
5. **未分類 pattern の検出**: 既存 `ShaderUsage` 3 値に当てはまらないものが見つかったら本 spec を更新し `FormatCandidateSelector` に case を追加してから作業継続
6. **Uzumore 分も同じ手順で audit**: `custom.hlsl` とそこから macro 経由で参照される箇所を全て辿る

## 未分類 pattern 検出時プロシージャ (Phase 3 audit 作業用)

audit 中に既存 3 値 (Color / Normal / SingleChannel) のいずれにも当てはまらない sampling pattern が見つかった場合、作業を中断して以下を実行する:

1. 該当 hlsl の path:line と sampling pattern の簡易記述をこの spec の「未解決事項」欄に追記 (一時)
2. `ShaderUsage` 新値を追加: 意味論と representative な channel mask を決める
3. `FormatCandidateSelector` に新 case を追加 (どの TextureFormat 候補を出すか)
4. `FormatCandidateSelector` のテスト (`FormatCandidateSelectorTests`) を追加
5. この spec の「データモデル - ShaderUsage 拡張ポリシー」欄を更新し、新値の意味論を正式記述
6. audit 作業を再開し、当該 hlsl 由来のプロパティに新値を使用
7. 全 audit 完了後に「未解決事項」欄の一時メモを削除

## 未解決事項

なし。

## 代表サンプル（spec 設計の確認用、実装時の完全表は `LilToonCoreCatalogData.cs` 本体に移る）

```csharp
// 共通エントリ例: variantId=null で全 lilToon variant に適用
{ (null, "_MainTex"),              new(Color,    RGBA,       "lil_common_frag.hlsl:NN") },
{ (null, "_BumpMap"),              new(Normal,   A | G,      "lil_common.hlsl:NN (UnpackNormal が .ag のみ参照 → AlphaUsed=true が派生)") },
{ (null, "_ShadowStrengthMask"),   new(SingleChannel, R,     "lil_common_frag.hlsl:NN") },
{ (null, "_EmissionBlendMask"),    new(Color,    RGBA,       "lil_common_frag.hlsl:1841") },
{ (null, "_OutlineTex"),           new(Color,    RGB,        "lil_common_frag_outline.hlsl:NN") },

// variant 固有例
{ ("lts_fur", "_FurNoiseTex"),     new(Color,    RGB,        "lil_fur_frag.hlsl:NN") },
{ ("lts_gem", "_GemEnvTex"),       new(Color,    RGB,        "lil_gem_frag.hlsl:NN") },
```

（具体 line 番号は audit 作業で埋める）

## 参考資料

- lilToon 本体: `Packages/jp.lilxyzw.liltoon/Shader/`
- lilToon ScriptedImporter: `Packages/jp.lilxyzw.liltoon/Editor/lilShaderContainerImporter.cs:21` (`[ScriptedImporter(0, "lilcontainer")]`)
- lilToon 自身のカスタムシェーダー path 判定: `Packages/jp.lilxyzw.liltoon/Editor/lilMaterialUtils.cs:728`, `lilOptimizer.cs:335`
- Uzumore Shader: `Packages/jp.sigmal00.uzumore-shader/Runtime/Shaders/`
- 直近の誤分類 fix コミット: `638b30d fix: EmissionBlendMask is Color not SingleChannel (lilToon RGBA usage)`
