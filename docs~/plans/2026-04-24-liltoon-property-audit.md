# lilToon プロパティカタログ網羅監査 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** lilToon 全 variant の texture property を `.hlsl` 実読で分類した catalog に刷新し、カスタムシェーダー拡張機構と Uzumore 同梱拡張を追加する。

**Architecture:** path ベースで variant ID を取得する `LilToonShaderIdentifier`、`(ShaderUsage, ReadChannels, EvidenceRef)` の事実データを持つ `LilToonPropertyInfo` 構造体、extension 優先 → core catalog → 未知 fallback の 3 段解決をする `LilToonPropertyCatalog` ファサード、内部データ保持する `LilToonCoreCatalogData`、`ICustomPropertyCatalogExtension` 拡張点で構成。既存 `LilToonTextureCatalog` はリネーム + 構造変更で置換、既存コンシューマ (`LilToonAlphaRules`, `LilTexAlphaUsageAnalyzer`, `ShaderUsageInferrer`) は新 API へ移行。

**Tech Stack:** C# (Unity 2022.3 Editor-only), NUnit (Unity Test Runner), NDMF, lilToon 2.3.2, aibridge CLI。

**Spec:** [docs/specs/2026-04-24-liltoon-property-audit-design.md](../specs/2026-04-24-liltoon-property-audit-design.md)

---

## Phase 1: インフラ構築

### Task 1: データモデル新設 + 旧 catalog の constructor 移行

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/LilToonPropertyInfo.cs`
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/LilToonTextureCatalog.cs` (既存 inline `LilToonPropertyInfo` 定義を削除し、entry を新 3-arg constructor へ seed 移行)

ChannelMask / LilToonPropertyInfo struct を新ファイルに定義。`ShaderUsage` は既存 `Phase2.Compression.ShaderUsage` を using で再利用する (重複定義回避)。

**重要**: 旧 `LilToonTextureCatalog.cs` 内部に `LilToonPropertyInfo` struct が inline 定義されている (`{ Usage, bool AlphaUsed }`, 2-arg constructor) ため、新ファイルを同じ名前空間に追加すると compile error になる。そのため Task 1 内で旧 inline 定義を削除し、Dictionary entry を新 3-arg constructor に移行する必要がある。移行は振る舞い保全ルール:

- `(Color, true)`          → `(Color, RGBA, SEED)`
- `(Color, false)`         → `(Color, RGB, SEED)`
- `(Normal, true, DXT5nm)` → `(Normal, A | G, SEED)`
- `(Normal, false)`        → `(Normal, RGB, SEED)`
- `(SingleChannel, false)` → `(SingleChannel, R, SEED)`

`SEED = "(seed from prev catalog)"`。Phase 3 audit で各 EvidenceRef を正式 `.hlsl path:line` に置き換える。

- [ ] **Step 1: 新ファイル作成**

Create `Packages/net.narazaka.vrchat.jnto/Editor/Shared/LilToonPropertyInfo.cs`:

```csharp
using System;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    /// <summary>
    /// シェーダーがテクスチャのどの channel を参照するか。
    /// 事実値 (.hlsl 読みで判定) として <see cref="LilToonPropertyInfo.ReadChannels"/> に格納される。
    /// </summary>
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

    /// <summary>
    /// lilToon プロパティ 1 エントリ分の分類情報。
    /// Usage は既存の <see cref="Phase2.Compression.ShaderUsage"/> を再利用する
    /// (重複定義を避けるため)。
    /// </summary>
    public readonly struct LilToonPropertyInfo
    {
        public readonly ShaderUsage Usage;
        public readonly ChannelMask ReadChannels;
        public readonly string EvidenceRef;

        public LilToonPropertyInfo(ShaderUsage usage, ChannelMask readChannels, string evidenceRef)
        {
            Usage = usage;
            ReadChannels = readChannels;
            EvidenceRef = evidenceRef;
        }

        public bool AlphaUsed => (ReadChannels & ChannelMask.A) != 0;
    }
}
```

**ShaderUsage の扱い**: 既存 `Editor/Phase2/Compression/ShaderUsage.cs` に同名 enum が定義されているため、新しく `Shared` 名前空間に再定義せず、`using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;` で取り込んで再利用する。レイヤー的には `Shared` が `Phase2` に依存する形になるが、重複定義を避ける判断。新 sampling pattern が見つかって新 `ShaderUsage` 値を追加する場合も `Phase2.Compression.ShaderUsage` に追記する。

- [ ] **Step 2: .meta は Unity が自動生成させる**

ファイル作成後 Unity ( aibridge 経由 ) を compile して .meta 生成を確認。

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Expected: 0 error。

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Shared/LilToonPropertyInfo.cs Editor/Shared/LilToonPropertyInfo.cs.meta
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto): introduce LilToonPropertyInfo + ChannelMask + ShaderUsage"
```

---

### Task 2: LilToonShaderIdentifier を TDD で実装

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/LilToonShaderIdentifier.cs`
- Create: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/LilToonShaderIdentifierTests.cs`

path ロジック単体テスト用 overload (`TryGetVariantIdFromPath(string)`) を内部に持ち、shader 実体テストは最小 smoke 1 件に留める。

- [ ] **Step 1: 失敗テストを書く**

Create `Packages/net.narazaka.vrchat.jnto/Tests/Editor/LilToonShaderIdentifierTests.cs`:

```csharp
using NUnit.Framework;
using Narazaka.VRChat.Jnto.Editor.Shared;

public class LilToonShaderIdentifierTests
{
    [TestCase("Packages/jp.lilxyzw.liltoon/Shader/lts.shader",                      "lts")]
    [TestCase("Packages/jp.lilxyzw.liltoon/Shader/lts_fur.shader",                  "lts_fur")]
    [TestCase("Packages/jp.lilxyzw.liltoon/Shader/ltsmulti_ref.shader",             "ltsmulti_ref")]
    [TestCase("Packages/jp.lilxyzw.liltoon/Shader/ltspass_opaque.shader",           "ltspass_opaque")]
    [TestCase("Packages/jp.lilxyzw.liltoon/Shader/ltsother_bakeramp.shader",        "ltsother_bakeramp")]
    [TestCase(@"Packages\jp.lilxyzw.liltoon\Shader\lts.shader",                     "lts")]
    [TestCase("Packages/jp.sigmal00.uzumore-shader/Runtime/Shaders/lts.lilcontainer","lts")]
    [TestCase("Packages/jp.sigmal00.uzumore-shader/Runtime/Shaders/lts_fur.lilcontainer","lts_fur")]
    [TestCase("Packages/com.example.other/Something/lts.lilcontainer",              "lts")]
    public void TryGetVariantIdFromPath_Recognized(string path, string expected)
    {
        Assert.AreEqual(expected, LilToonShaderIdentifier.TryGetVariantIdFromPath(path));
    }

    [TestCase("Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.shader")]
    [TestCase("Assets/MyShader.shader")]
    [TestCase("Packages/jp.lilxyzw.liltoon/Editor/lilToonSetting.cs")]
    [TestCase("")]
    [TestCase(null)]
    public void TryGetVariantIdFromPath_NotLilToonFamily_ReturnsNull(string path)
    {
        Assert.IsNull(LilToonShaderIdentifier.TryGetVariantIdFromPath(path));
    }

    [Test]
    public void TryGetVariantId_NullShader_ReturnsNull()
    {
        Assert.IsNull(LilToonShaderIdentifier.TryGetVariantId(null));
    }
}
```

- [ ] **Step 2: 失敗することを確認**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Expected: `LilToonShaderIdentifier` 未定義の compile error。

- [ ] **Step 3: 実装**

Create `Packages/net.narazaka.vrchat.jnto/Editor/Shared/LilToonShaderIdentifier.cs`:

```csharp
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    /// <summary>
    /// Shader アセットの path から lilToon 系 variant ID (= ファイル名 stem) を抽出する。
    /// カスタムシェーダー (.lilcontainer ScriptedImporter) にも対応。
    /// </summary>
    public static class LilToonShaderIdentifier
    {
        /// <summary>
        /// 与えられた Shader が lilToon 系なら variantId (アセットファイル名 stem) を返す。
        /// 以下のいずれにもマッチしない場合は null:
        ///   - .lilcontainer 拡張子のアセット (カスタムシェーダー)
        ///   - Packages/jp.lilxyzw.liltoon/Shader/ 配下の .shader
        /// </summary>
        public static string TryGetVariantId(Shader shader)
        {
            if (shader == null) return null;
            var path = AssetDatabase.GetAssetPath(shader);
            return TryGetVariantIdFromPath(path);
        }

        internal static string TryGetVariantIdFromPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var normalized = assetPath.Replace('\\', '/');

            if (normalized.EndsWith(".lilcontainer", StringComparison.Ordinal))
                return Path.GetFileNameWithoutExtension(normalized);

            if (normalized.EndsWith(".shader", StringComparison.Ordinal)
                && normalized.Contains("/jp.lilxyzw.liltoon/Shader/"))
                return Path.GetFileNameWithoutExtension(normalized);

            return null;
        }
    }
}
```

- [ ] **Step 4: テスト実行**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity` → 0 error。
Run: editor tests via aibridge for `LilToonShaderIdentifierTests`。
Expected: 全テスト PASS。

- [ ] **Step 5: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Shared/LilToonShaderIdentifier.cs Editor/Shared/LilToonShaderIdentifier.cs.meta Tests/Editor/LilToonShaderIdentifierTests.cs Tests/Editor/LilToonShaderIdentifierTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto): add LilToonShaderIdentifier for path-based variant resolution"
```

---

### Task 3: ICustomPropertyCatalogExtension インターフェース

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/ICustomPropertyCatalogExtension.cs`

- [ ] **Step 1: 新ファイル作成**

Create `Packages/net.narazaka.vrchat.jnto/Editor/Shared/ICustomPropertyCatalogExtension.cs`:

```csharp
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    /// <summary>
    /// 第三者 / 同梱のカスタムシェーダー用プロパティ分類の拡張点。
    /// <see cref="LilToonPropertyCatalog.RegisterExtension"/> で登録する。
    /// </summary>
    public interface ICustomPropertyCatalogExtension
    {
        /// <summary>この extension が当該 shader の分類を管轄するか。</summary>
        bool Matches(Shader shader);

        /// <summary>
        /// 管轄下のプロパティ分類を返す。
        /// Matches=true でも当該 prop について独自分類を持たない場合は false を返して良い
        /// (core catalog へフォールバックする)。
        /// </summary>
        bool TryClassify(Shader shader, string variantId, string propName, out LilToonPropertyInfo info);
    }
}
```

- [ ] **Step 2: Compile 確認**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity` → 0 error。

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Shared/ICustomPropertyCatalogExtension.cs Editor/Shared/ICustomPropertyCatalogExtension.cs.meta
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto): add ICustomPropertyCatalogExtension"
```

---

### Task 4: LilToonPropertyCatalog を TDD で実装

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/LilToonPropertyCatalog.cs`
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/AssemblyInfo.cs` (新設、`InternalsVisibleTo` 宣言)
- Create: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/LilToonPropertyCatalogTests.cs`

core catalog は Task 5 まで空のまま。lookup / registry ロジックのみを組み込む。テストから internal メンバ (`ResetExtensionsForTests`, `LilToonCoreCatalogData.EnumerateAll`) を呼べるよう `InternalsVisibleTo` を Editor assembly に設定する。

- [ ] **Step 0: InternalsVisibleTo を設定**

Create `Packages/net.narazaka.vrchat.jnto/Editor/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("net.narazaka.vrchat.jnto.Tests.Editor")]
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity` → 0 error (.meta は Unity が生成)。

- [ ] **Step 1: 失敗テストを書く**

Create `Packages/net.narazaka.vrchat.jnto/Tests/Editor/LilToonPropertyCatalogTests.cs`:

```csharp
using System;
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;

public class LilToonPropertyCatalogTests
{
    // Shader 実体を組み立てずに Matches / TryClassify を直接叩ける stub
    sealed class StubExt : ICustomPropertyCatalogExtension
    {
        public Func<Shader, bool> OnMatches;
        public TryClassifyFn OnClassify;
        public delegate bool TryClassifyFn(Shader s, string variant, string prop, out LilToonPropertyInfo info);

        public bool Matches(Shader shader) => OnMatches?.Invoke(shader) ?? false;
        public bool TryClassify(Shader shader, string variantId, string propName, out LilToonPropertyInfo info)
        {
            info = default;
            return OnClassify != null && OnClassify(shader, variantId, propName, out info);
        }
    }

    [TearDown] public void TearDown() => LilToonPropertyCatalog.ResetExtensionsForTests();

    [Test]
    public void RegisterExtension_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LilToonPropertyCatalog.RegisterExtension(null));
    }

    [Test]
    public void RegisterExtension_IsIdempotent_ForSameInstance()
    {
        var ext = new StubExt();
        LilToonPropertyCatalog.RegisterExtension(ext);
        LilToonPropertyCatalog.RegisterExtension(ext);
        Assert.IsTrue(LilToonPropertyCatalog.UnregisterExtension(ext));
        Assert.IsFalse(LilToonPropertyCatalog.UnregisterExtension(ext));
    }

    [Test]
    public void UnregisterExtension_NotRegistered_ReturnsFalse()
    {
        Assert.IsFalse(LilToonPropertyCatalog.UnregisterExtension(new StubExt()));
    }

    [Test]
    public void TryGet_NullShader_ReturnsFalse()
    {
        Assert.IsFalse(LilToonPropertyCatalog.TryGet(null, "_MainTex", out _));
    }

    [Test]
    public void TryGet_ExtensionClassifies_CoreIsSkipped()
    {
        var expected = new LilToonPropertyInfo(ShaderUsage.SingleChannel, ChannelMask.R, "stub/evidence:1");
        var ext = new StubExt {
            OnMatches = _ => true,
            OnClassify = (Shader s, string v, string p, out LilToonPropertyInfo info) => { info = expected; return true; }
        };
        LilToonPropertyCatalog.RegisterExtension(ext);

        var shader = Shader.Find("lilToon");
        if (shader == null) Assert.Ignore("lilToon shader not available");

        Assert.IsTrue(LilToonPropertyCatalog.TryGet(shader, "_AnyProp", out var info));
        Assert.AreEqual(expected.Usage, info.Usage);
        Assert.AreEqual(expected.ReadChannels, info.ReadChannels);
        Assert.AreEqual(expected.EvidenceRef, info.EvidenceRef);
    }

    [Test]
    public void TryGet_ExtensionMatchesButTryClassifyFalse_FallsBackToCoreWithoutTryingOtherExt()
    {
        int extBCalled = 0;
        var extA = new StubExt { OnMatches = _ => true, OnClassify = (Shader s, string v, string p, out LilToonPropertyInfo i) => { i = default; return false; } };
        var extB = new StubExt { OnMatches = _ => { extBCalled++; return true; }, OnClassify = (Shader s, string v, string p, out LilToonPropertyInfo i) => { i = new(ShaderUsage.Color, ChannelMask.RGBA, "B"); return true; } };
        LilToonPropertyCatalog.RegisterExtension(extA);
        LilToonPropertyCatalog.RegisterExtension(extB);

        var shader = Shader.Find("lilToon");
        if (shader == null) Assert.Ignore("lilToon shader not available");

        // core catalog が空 (Task 4 段階) なので最終的に false が返る
        Assert.IsFalse(LilToonPropertyCatalog.TryGet(shader, "_AnyProp", out _));
        Assert.AreEqual(0, extBCalled, "extA が Matches=true かつ classify=false なら extB は呼ばれない");
    }

    [Test]
    public void TryGet_ExtensionMatchesFalse_TriesNextExtension()
    {
        var calledB = 0;
        var extA = new StubExt { OnMatches = _ => false };
        var extB = new StubExt { OnMatches = _ => { calledB++; return true; }, OnClassify = (Shader s, string v, string p, out LilToonPropertyInfo i) => { i = new(ShaderUsage.Color, ChannelMask.RGBA, "B"); return true; } };
        LilToonPropertyCatalog.RegisterExtension(extA);
        LilToonPropertyCatalog.RegisterExtension(extB);

        var shader = Shader.Find("lilToon");
        if (shader == null) Assert.Ignore("lilToon shader not available");

        Assert.IsTrue(LilToonPropertyCatalog.TryGet(shader, "_AnyProp", out var info));
        Assert.AreEqual("B", info.EvidenceRef);
        Assert.AreEqual(1, calledB);
    }
}
```

- [ ] **Step 2: 失敗することを確認**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Expected: `LilToonPropertyCatalog` 未定義 + `ResetExtensionsForTests` 未定義の compile error。

- [ ] **Step 3: 実装**

Create `Packages/net.narazaka.vrchat.jnto/Editor/Shared/LilToonPropertyCatalog.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    /// <summary>
    /// lilToon / lilToon 派生シェーダーのプロパティ分類を解決する公開ファサード。
    /// 解決順: registered extensions → core catalog → miss。
    /// </summary>
    public static class LilToonPropertyCatalog
    {
        static readonly List<ICustomPropertyCatalogExtension> _extensions = new List<ICustomPropertyCatalogExtension>();

        /// <summary>
        /// (shader, propName) → 分類情報。以下の順で解決する:
        /// 1. Matches=true を返した最初の extension の TryClassify 結果 (true なら即確定、false なら core へ)
        /// 2. LilToonCoreCatalogData.TryGet(variantId, propName)
        /// 3. miss → false
        /// </summary>
        public static bool TryGet(Shader shader, string propName, out LilToonPropertyInfo info)
        {
            info = default;
            var variantId = LilToonShaderIdentifier.TryGetVariantId(shader);
            if (variantId == null) return false;

            foreach (var ext in _extensions)
            {
                if (!ext.Matches(shader)) continue;
                return ext.TryClassify(shader, variantId, propName, out info)
                    || LilToonCoreCatalogData.TryGet(variantId, propName, out info);
            }

            return LilToonCoreCatalogData.TryGet(variantId, propName, out info);
        }

        public static void RegisterExtension(ICustomPropertyCatalogExtension ext)
        {
            if (ext == null) throw new ArgumentNullException(nameof(ext));
            if (!_extensions.Contains(ext)) _extensions.Add(ext);
        }

        public static bool UnregisterExtension(ICustomPropertyCatalogExtension ext)
        {
            if (ext == null) return false;
            return _extensions.Remove(ext);
        }

        /// <summary>テスト用。production code では使用禁止。</summary>
        internal static void ResetExtensionsForTests() => _extensions.Clear();
    }
}
```

この時点では `LilToonCoreCatalogData` はまだ存在しない → Task 5 で追加するまで compile error が残ることを許容する。Step 4 では compile error を発見 → Task 5 まで進む。

- [ ] **Step 4: Compile で LilToonCoreCatalogData 未定義エラーを確認**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Expected: `LilToonCoreCatalogData` 未定義エラー。Task 5 に進む前提で残す。

- [ ] **Step 5: (Task 5 完了後) テスト実行**

Task 5 で core catalog skeleton を入れた後にこのテストを走らせて PASS を確認する。commit は Task 5 の後ろで合わせて行う。

---

### Task 5: LilToonCoreCatalogData skeleton

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Catalog/LilToonCoreCatalogData.cs`

Task 4 で残った compile error を解消する空 catalog。データ充填は Phase 2 / Phase 3 で行う。

- [ ] **Step 1: 新ファイル作成**

Create `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Catalog/LilToonCoreCatalogData.cs`:

```csharp
using System.Collections.Generic;

namespace Narazaka.VRChat.Jnto.Editor.Shared
{
    /// <summary>
    /// lilToon 本体 (stock) シェーダーの (variantId, propName) → 分類情報 データ。
    /// key の variantId が null のエントリは「全 variant 共通」。
    /// 解決順: (variantId, prop) hit → (null, prop) hit → miss。
    ///
    /// データ充填は spec の監査ワークフロー (Phase 3) で行う。
    /// </summary>
    internal static class LilToonCoreCatalogData
    {
        // (variantId, propName) → info。variantId=null が共通エントリ。
        static readonly Dictionary<(string, string), LilToonPropertyInfo> Table = new Dictionary<(string, string), LilToonPropertyInfo>();

        internal static bool TryGet(string variantId, string propName, out LilToonPropertyInfo info)
        {
            if (variantId != null && Table.TryGetValue((variantId, propName), out info)) return true;
            return Table.TryGetValue((null, propName), out info);
        }

        /// <summary>
        /// Phase 2 / Phase 3 で entry を追加する。同一 key 重複は例外 (catalog の静的検証)。
        /// </summary>
        internal static void Add(string variantId, string propName, LilToonPropertyInfo info)
        {
            Table.Add((variantId, propName), info);
        }

        /// <summary>全エントリ列挙 (table-driven テスト用)。</summary>
        internal static IEnumerable<KeyValuePair<(string variantId, string propName), LilToonPropertyInfo>> EnumerateAll() => Table;
    }
}
```

- [ ] **Step 2: Compile 確認**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity` → 0 error。

- [ ] **Step 3: Task 4 のテストを実行**

Run: editor tests via aibridge for `LilToonPropertyCatalogTests`。
Expected: 全 PASS。`Shader.Find("lilToon")` は lilToon がインストールされていれば成功、失敗なら `Assert.Ignore`。

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/AssemblyInfo.cs Editor/AssemblyInfo.cs.meta Editor/Shared/LilToonPropertyCatalog.cs Editor/Shared/LilToonPropertyCatalog.cs.meta Editor/Shared/Catalog Tests/Editor/LilToonPropertyCatalogTests.cs Tests/Editor/LilToonPropertyCatalogTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto): add LilToonPropertyCatalog facade + core catalog skeleton"
```

---

## Phase 2: 既存コンシューマ移行 (振る舞い保全)

### Task 6: 既存カタログデータを新 core catalog に seed 移植

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Catalog/LilToonCoreCatalogData.cs`

既存 `LilToonTextureCatalog` の 50 エントリを 1 対 1 で新 core catalog に移植する。`(Usage, AlphaUsed)` → `(Usage, ReadChannels)` の変換ルール:

| 既存 | 変換後 `ReadChannels` |
|---|---|
| `(Color, true)`         | `RGBA` |
| `(Color, false)`        | `RGB` |
| `(Normal, true)` (= DXT5nm 系) | `A \| G` (UnpackNormal が .ag のみ参照) |
| `(Normal, false)` (_OutlineVectorTex 等 RGB 参照) | `RGB` |
| `(SingleChannel, false)` | `R` |

`EvidenceRef` は seed 段階では `"(seed from prev catalog)"` 固定。Phase 3 audit で正式根拠に書き換える。variantId は全エントリ null (従来と同じく全 variant 共通扱い)。

- [ ] **Step 1: seed migration 失敗テストを書く**

Create `Packages/net.narazaka.vrchat.jnto/Tests/Editor/LilToonCoreCatalogSeedTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;

public class LilToonCoreCatalogSeedTests
{
    // 従来 LilToonTextureCatalogTests と同じ key を新 API で引けることを確認する移行検証。
    // Shader 実体に依存するので lilToon が無ければ skip。

    static Shader GetLilToonShader()
    {
        return Shader.Find("lilToon");
    }

    [Test] public void MainTex_ColorAlpha()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsTrue(LilToonPropertyCatalog.TryGet(s, "_MainTex", out var i));
        Assert.AreEqual(ShaderUsage.Color, i.Usage);
        Assert.IsTrue(i.AlphaUsed);
    }

    [Test] public void BumpMap_NormalAlpha()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsTrue(LilToonPropertyCatalog.TryGet(s, "_BumpMap", out var i));
        Assert.AreEqual(ShaderUsage.Normal, i.Usage);
        Assert.IsTrue(i.AlphaUsed, "DXT5nm packs normal X into alpha");
    }

    [Test] public void OutlineVectorTex_NormalNoAlpha()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsTrue(LilToonPropertyCatalog.TryGet(s, "_OutlineVectorTex", out var i));
        Assert.AreEqual(ShaderUsage.Normal, i.Usage);
        Assert.IsFalse(i.AlphaUsed);
    }

    [Test] public void ShadowStrengthMask_SingleChannel()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsTrue(LilToonPropertyCatalog.TryGet(s, "_ShadowStrengthMask", out var i));
        Assert.AreEqual(ShaderUsage.SingleChannel, i.Usage);
        Assert.IsFalse(i.AlphaUsed);
    }

    [Test] public void OutlineTex_ColorNoAlpha()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsTrue(LilToonPropertyCatalog.TryGet(s, "_OutlineTex", out var i));
        Assert.AreEqual(ShaderUsage.Color, i.Usage);
        Assert.IsFalse(i.AlphaUsed);
    }

    [Test] public void EmissionBlendMask_ColorAlpha_RegressionGuard()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsTrue(LilToonPropertyCatalog.TryGet(s, "_EmissionBlendMask", out var i));
        Assert.AreEqual(ShaderUsage.Color, i.Usage);
        Assert.IsTrue(i.AlphaUsed);
    }

    [Test] public void UnknownProperty_ReturnsFalse()
    {
        var s = GetLilToonShader();
        if (s == null) Assert.Ignore();
        Assert.IsFalse(LilToonPropertyCatalog.TryGet(s, "_MadeUpTex", out _));
    }
}
```

- [ ] **Step 2: 失敗することを確認**

Run: test execution → 全 Assert 失敗 (catalog が空なので `TryGet` が false を返す)。

- [ ] **Step 3: core catalog に seed データを追加する**

Edit `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Catalog/LilToonCoreCatalogData.cs` の static constructor を追加し、以下の全エントリを投入。既存 `LilToonTextureCatalog.cs` の 50 エントリ * 変換ルール適用:

```csharp
static LilToonCoreCatalogData()
{
    const string SEED = "(seed from prev catalog)";

    // ---- Color + alpha 使用 ----
    void ColorA(string prop) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.Color, ChannelMask.RGBA, SEED));
    ColorA("_MainTex");
    ColorA("_Main2ndTex");
    ColorA("_Main3rdTex");
    ColorA("_Main2ndBlendMask");
    ColorA("_Main3rdBlendMask");
    ColorA("_ShadowColorTex");
    ColorA("_Shadow2ndColorTex");
    ColorA("_Shadow3rdColorTex");
    ColorA("_MatCapTex");
    ColorA("_MatCap2ndTex");
    ColorA("_RimColorTex");
    ColorA("_ReflectionColorTex");
    ColorA("_EmissionMap");
    ColorA("_EmissionGradTex");
    ColorA("_Emission2ndMap");
    ColorA("_Emission2ndGradTex");
    ColorA("_BacklightColorTex");
    ColorA("_GlitterColorTex");
    ColorA("_MainGradationTex");
    ColorA("_AudioLinkMask");
    ColorA("_RimShadeMask");
    ColorA("_EmissionBlendMask");
    ColorA("_Emission2ndBlendMask");

    // ---- Color + alpha 不使用 ----
    void ColorRgb(string prop) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.Color, ChannelMask.RGB, SEED));
    ColorRgb("_OutlineTex");
    ColorRgb("_MatCapBlendMask");
    ColorRgb("_MatCap2ndBlendMask");
    ColorRgb("_GlitterShapeTex");

    // ---- Normal + DXT5nm (.ag 参照) ----
    void NormalAG(string prop) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.Normal, ChannelMask.A | ChannelMask.G, SEED));
    NormalAG("_BumpMap");
    NormalAG("_Bump2ndMap");
    NormalAG("_MatCapBumpMap");
    NormalAG("_MatCap2ndBumpMap");

    // ---- Normal + RGB 参照 (alpha 不使用) ----
    void NormalRgb(string prop) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.Normal, ChannelMask.RGB, SEED));
    NormalRgb("_OutlineBumpMap");
    NormalRgb("_OutlineVectorTex");

    // ---- SingleChannel (R のみ) ----
    void SingleR(string prop) => Table.Add((null, prop), new LilToonPropertyInfo(ShaderUsage.SingleChannel, ChannelMask.R, SEED));
    SingleR("_ShadowStrengthMask");
    SingleR("_ShadowBorderMask");
    SingleR("_ShadowBlurMask");
    SingleR("_OutlineWidthMask");
    SingleR("_MainColorAdjustMask");
    SingleR("_SmoothnessTex");
    SingleR("_MetallicGlossMap");
    SingleR("_AlphaMask");
    SingleR("_Bump2ndScaleMask");
    SingleR("_ParallaxMap");
    SingleR("_DissolveMask");
    SingleR("_DissolveNoiseMask");
    SingleR("_Main2ndDissolveMask");
    SingleR("_Main2ndDissolveNoiseMask");
    SingleR("_Main3rdDissolveMask");
    SingleR("_Main3rdDissolveNoiseMask");
    SingleR("_AudioLinkLocalMap");
}
```

- [ ] **Step 4: テストが通ることを確認**

Run: compile + test via aibridge。
Expected: `LilToonCoreCatalogSeedTests` 全 PASS。

- [ ] **Step 5: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Shared/Catalog/LilToonCoreCatalogData.cs Tests/Editor/LilToonCoreCatalogSeedTests.cs Tests/Editor/LilToonCoreCatalogSeedTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto): seed LilToonCoreCatalogData from existing LilToonTextureCatalog"
```

---

### Task 7: LilToonAlphaRules を新 API に移行 + テスト更新

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase1/LilToonAlphaRules.cs`
- Modify: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/LilToonAlphaRulesTests.cs`

シグネチャ変更: `IsAlphaUsed(propertyName)` → `IsAlphaUsed(shader, propertyName)`。テストは `Shader.Find("lilToon")` で shader を取得 → propname で引く形に変更。

- [ ] **Step 1: テストを新シグネチャに書き換える**

Overwrite `Packages/net.narazaka.vrchat.jnto/Tests/Editor/LilToonAlphaRulesTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase1;

public class LilToonAlphaRulesTests
{
    static Shader L => Shader.Find("lilToon");

    static bool IsAlpha(string prop)
    {
        var s = L;
        if (s == null) Assert.Ignore("lilToon shader not available");
        return LilToonAlphaRules.IsAlphaUsed(s, prop);
    }

    // ノーマルマップ: DXT5nm (.ag) → alpha 使用 (strip禁止)
    [Test] public void BumpMap_UsesAlpha() => Assert.IsTrue(IsAlpha("_BumpMap"));
    [Test] public void Bump2ndMap_UsesAlpha() => Assert.IsTrue(IsAlpha("_Bump2ndMap"));

    [Test] public void MainTex_AlwaysUsesAlpha() => Assert.IsTrue(IsAlpha("_MainTex"));

    [Test] public void AlphaMask_RChannelOnly_NoAlpha() => Assert.IsFalse(IsAlpha("_AlphaMask"));
    [Test] public void ShadowStrengthMask_NoAlpha() => Assert.IsFalse(IsAlpha("_ShadowStrengthMask"));
    [Test] public void SmoothnessTex_NoAlpha() => Assert.IsFalse(IsAlpha("_SmoothnessTex"));
    [Test] public void OutlineWidthMask_NoAlpha() => Assert.IsFalse(IsAlpha("_OutlineWidthMask"));
    [Test] public void MainColorAdjustMask_NoAlpha() => Assert.IsFalse(IsAlpha("_MainColorAdjustMask"));

    [Test] public void OutlineTex_NoAlpha() => Assert.IsFalse(IsAlpha("_OutlineTex"));
    [Test] public void MatCapBlendMask_NoAlpha() => Assert.IsFalse(IsAlpha("_MatCapBlendMask"));
    [Test] public void GlitterShapeTex_NoAlpha() => Assert.IsFalse(IsAlpha("_GlitterShapeTex"));

    [Test] public void MatCapTex_UsesAlpha() => Assert.IsTrue(IsAlpha("_MatCapTex"));
    [Test] public void ShadowColorTex_UsesAlpha() => Assert.IsTrue(IsAlpha("_ShadowColorTex"));
    [Test] public void EmissionMap_UsesAlpha() => Assert.IsTrue(IsAlpha("_EmissionMap"));
    [Test] public void RimColorTex_UsesAlpha() => Assert.IsTrue(IsAlpha("_RimColorTex"));
    [Test] public void BacklightColorTex_UsesAlpha() => Assert.IsTrue(IsAlpha("_BacklightColorTex"));

    // 未知プロパティ: 安全側 true
    [Test] public void UnknownProperty_ConservativeTrue() => Assert.IsTrue(IsAlpha("_MadeUpTex"));
}
```

- [ ] **Step 2: 失敗を確認**

Run: compile → `LilToonAlphaRules.IsAlphaUsed(Shader, string)` 未定義の compile error。

- [ ] **Step 3: LilToonAlphaRules を新 API に書き換える**

Overwrite `Packages/net.narazaka.vrchat.jnto/Editor/Phase1/LilToonAlphaRules.cs`:

```csharp
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;

namespace Narazaka.VRChat.Jnto.Editor.Phase1
{
    /// <summary>
    /// lilToon テクスチャプロパティのアルファ使用判定。
    /// 分類の実体は <see cref="LilToonPropertyCatalog"/>。
    /// </summary>
    public static class LilToonAlphaRules
    {
        public static bool IsAlphaUsed(Shader shader, string propertyName)
        {
            // 未知プロパティは安全側 true (strip 禁止)
            if (!LilToonPropertyCatalog.TryGet(shader, propertyName, out var info)) return true;
            return info.AlphaUsed;
        }
    }
}
```

- [ ] **Step 4: テスト実行**

Run: compile + test。
Expected: `LilToonAlphaRulesTests` 全 PASS。

- [ ] **Step 5: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase1/LilToonAlphaRules.cs Tests/Editor/LilToonAlphaRulesTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "refactor(jnto): migrate LilToonAlphaRules to shader-based new catalog API"
```

---

### Task 8: LilTexAlphaUsageAnalyzer の IsLilToon 削除と呼び出し更新

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase1/LilTexAlphaUsageAnalyzer.cs`

`IsLilToon` を削除し、`LilToonShaderIdentifier.TryGetVariantId(mat.shader) != null` で置換する。`IsAlphaUsed` のシグネチャは保つ (外部からは同じ)。

- [ ] **Step 1: 実装書き換え**

Overwrite `Packages/net.narazaka.vrchat.jnto/Editor/Phase1/LilTexAlphaUsageAnalyzer.cs`:

```csharp
using UnityEditor;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;

namespace Narazaka.VRChat.Jnto.Editor.Phase1
{
    public static class LilTexAlphaUsageAnalyzer
    {
        public static bool IsAlphaUsed(Material mat, string propertyName)
        {
            if (mat == null) return ConservativeNonLilFallback(mat, propertyName);
            var variantId = LilToonShaderIdentifier.TryGetVariantId(mat.shader);
            if (variantId != null) return LilToonAlphaRules.IsAlphaUsed(mat.shader, propertyName);
            return ConservativeNonLilFallback(mat, propertyName);
        }

        /// <summary>
        /// 旧 API 互換: 既存の lilToon 判定呼び出し向け。<see cref="LilToonShaderIdentifier.TryGetVariantId"/> に置換された。
        /// </summary>
        public static bool IsLilToon(Material mat)
            => mat != null && LilToonShaderIdentifier.TryGetVariantId(mat.shader) != null;

        static bool ConservativeNonLilFallback(Material mat, string propertyName)
        {
            if (mat == null) return true;   // null material は安全側
            var tex = mat.GetTexture(propertyName);
            if (tex == null) return false;
            var path = AssetDatabase.GetAssetPath(tex);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return true;
            switch (imp.textureType)
            {
                case TextureImporterType.NormalMap:
                case TextureImporterType.SingleChannel:
                    return false;
                default:
                    return true;
            }
        }
    }
}
```

- [ ] **Step 2: 既存呼び出し側の整合を確認**

Run: `grep -r "IsLilToon\|LilTexAlphaUsageAnalyzer" Packages/net.narazaka.vrchat.jnto --include="*.cs"` で全参照を拾い、新実装と整合するか確認。

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity` → 0 error。

- [ ] **Step 3: テスト実行**

Run: editor test。
Expected: 既存の `AlphaStripperTests` 等が全 PASS。

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase1/LilTexAlphaUsageAnalyzer.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "refactor(jnto): route LilTexAlphaUsageAnalyzer through LilToonShaderIdentifier"
```

---

### Task 9: ShaderUsageInferrer を新 API に移行 + テスト更新

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/ShaderUsageInferrer.cs`
- Modify: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/ShaderUsageInferrerTests.cs`

- [ ] **Step 1: 実装を新 API へ**

Overwrite `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/ShaderUsageInferrer.cs`:

```csharp
using UnityEditor;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    /// <summary>
    /// material/shader prop name または TextureImporter から ShaderUsage を推定する。
    /// lilToon プロパティの分類は <see cref="LilToonPropertyCatalog"/> を参照。
    /// </summary>
    public static class ShaderUsageInferrer
    {
        public static ShaderUsage Infer(Material material, string propName, Texture2D tex)
        {
            // 1. lilToon prop semantics
            if (material != null
                && LilToonPropertyCatalog.TryGet(material.shader, propName, out var info))
            {
                return info.Usage;
            }

            // 2. TextureImporter.textureType
            if (tex != null)
            {
                var path = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(path))
                {
                    var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (imp != null)
                    {
                        if (imp.textureType == TextureImporterType.NormalMap) return ShaderUsage.Normal;
                        if (imp.textureType == TextureImporterType.SingleChannel) return ShaderUsage.SingleChannel;
                    }
                }
            }

            // 3. デフォルト
            return ShaderUsage.Color;
        }
    }
}
```

- [ ] **Step 2: 既存テストが引き続き PASS することを確認**

Run: test `ShaderUsageInferrerTests`。既存の 2 テスト (`Infer_NullMaterial_NullTex_ReturnsColor`, `Infer_NullMaterial_NullTex_ReturnsColorEvenForBumpMapName`) は新実装でも通るはず。

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Compression/ShaderUsageInferrer.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "refactor(jnto): route ShaderUsageInferrer through LilToonPropertyCatalog"
```

---

### Task 10: 旧 LilToonTextureCatalog を削除

**Files:**
- Delete: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/LilToonTextureCatalog.cs`
- Delete: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/LilToonTextureCatalog.cs.meta`
- Delete: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/LilToonTextureCatalogTests.cs`
- Delete: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/LilToonTextureCatalogTests.cs.meta`

- [ ] **Step 1: 旧カタログへの参照が残っていないか grep**

Run: `grep -r "LilToonTextureCatalog" Packages/net.narazaka.vrchat.jnto --include="*.cs"`
Expected: 結果 0 件。

- [ ] **Step 2: 削除**

```bash
git -C Packages/net.narazaka.vrchat.jnto rm Editor/Shared/LilToonTextureCatalog.cs Editor/Shared/LilToonTextureCatalog.cs.meta Tests/Editor/LilToonTextureCatalogTests.cs Tests/Editor/LilToonTextureCatalogTests.cs.meta
```

- [ ] **Step 3: Compile + test 全走行**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity` → 0 error。
Run: 全 editor tests。
Expected: 全 PASS。

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto commit -m "refactor(jnto): remove obsolete LilToonTextureCatalog (migrated to LilToonPropertyCatalog)"
```

---

## Phase 3: .hlsl 実読による全 variant 監査

各 Phase 3 task は `.hlsl` を実読して catalog エントリを書き加える監査作業。作業手順は spec の「監査ワークフロー」に従い、各エントリに対し以下を行う:

1. 対象 hlsl で `LIL_SAMPLE_2D` / `LIL_SAMPLE_2D_ST` / `UnpackNormal` 呼び出しを拾う (Grep)
2. sample 結果の channel 参照 (`.r`, `.rgba` 等) を読み、ReadChannels を確定
3. `ShaderUsage` 意味論ラベルを決定 (Normal / SingleChannel / Color)
4. 既存 3 値に収まらない pattern が見つかった場合: **task を一時停止し spec と FormatCandidateSelector を先に更新 (Task 11 の「未分類 pattern 検出時プロシージャ」を参照)**
5. `EvidenceRef` に hlsl path:line を書き込み
6. エントリを `LilToonCoreCatalogData` の static constructor に追加 (同 key 重複は `Table.Add` が例外を投げて検知)

audit task 完了の判定は「その hlsl で sample されるプロパティが全て catalog 入りし、seed の `"(seed from prev catalog)"` が正式 EvidenceRef に置換されている」こと。

### Task 11: 監査品質テスト + 未分類 pattern 検出時プロシージャ

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/LilToonCoreCatalogQualityTests.cs`

全エントリに対する data-quality 不変条件を table-driven で守る。Task 4 Step 0 で設定済みの `InternalsVisibleTo` により `LilToonCoreCatalogData.EnumerateAll()` にテストから直接アクセスできる。

- [ ] **Step 1: 品質テストを書く**

Create `Packages/net.narazaka.vrchat.jnto/Tests/Editor/LilToonCoreCatalogQualityTests.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Narazaka.VRChat.Jnto.Editor.Shared;

public class LilToonCoreCatalogQualityTests
{
    static IEnumerable AllEntries()
    {
        foreach (var kvp in LilToonCoreCatalogData.EnumerateAll())
            yield return new TestCaseData(kvp.Key.variantId, kvp.Key.propName, kvp.Value).SetName($"{kvp.Key.variantId ?? "*"} / {kvp.Key.propName}");
    }

    [TestCaseSource(nameof(AllEntries))]
    public void Entry_HasEvidenceRef(string variantId, string propName, LilToonPropertyInfo info)
    {
        Assert.IsFalse(string.IsNullOrEmpty(info.EvidenceRef), $"{variantId ?? "*"}/{propName} の EvidenceRef が空");
    }

    [TestCaseSource(nameof(AllEntries))]
    public void Entry_HasNonEmptyReadChannels(string variantId, string propName, LilToonPropertyInfo info)
    {
        Assert.AreNotEqual(ChannelMask.None, info.ReadChannels, $"{variantId ?? "*"}/{propName} の ReadChannels が None (サンプルされない prop は catalog から除外)");
    }

    [TestCaseSource(nameof(AllEntries))]
    public void Entry_UsageIsValidEnumValue(string variantId, string propName, LilToonPropertyInfo info)
    {
        Assert.IsTrue(System.Enum.IsDefined(typeof(ShaderUsage), info.Usage));
    }

    // audit 進行中: seed entry が残っているものを検知。Phase 3 全部終了まで XFAIL 想定、終了時に green 化。
    [TestCaseSource(nameof(AllEntries))]
    public void Entry_EvidenceRef_NotSeedPlaceholder(string variantId, string propName, LilToonPropertyInfo info)
    {
        Assert.AreNotEqual("(seed from prev catalog)", info.EvidenceRef,
            $"{variantId ?? "*"}/{propName} が seed placeholder のまま。audit が未完了。");
    }
}
```

- [ ] **Step 2: compile + test 実行**

Run: compile。0 error。
Run: test。
Expected: `Entry_HasEvidenceRef` / `Entry_HasNonEmptyReadChannels` / `Entry_UsageIsValidEnumValue` は 全 PASS、`Entry_EvidenceRef_NotSeedPlaceholder` は 全エントリ FAIL (Phase 3 未完了の証拠)。**このタスクでは Entry_EvidenceRef_NotSeedPlaceholder が失敗することが期待値**。以後の audit task ごとに FAIL 数が減っていき、Phase 3 完了時に全 PASS となる。

- [ ] **Step 3: 未分類 pattern 検出時プロシージャをドキュメント化**

Append to `Packages/net.narazaka.vrchat.jnto/docs/specs/2026-04-24-liltoon-property-audit-design.md` (末尾に以下を追記):

```markdown
## 未分類 pattern 検出時プロシージャ (Phase 3 audit 作業用)

audit 中に既存 3 値 (Color / Normal / SingleChannel) のいずれにも当てはまらない sampling pattern が見つかった場合、作業を中断して以下を実行する:

1. 該当 hlsl の path:line と sampling pattern の簡易記述をこの spec の「未解決事項」欄に追記 (一時)
2. `ShaderUsage` 新値を追加: 意味論と representative な channel mask を決める
3. `FormatCandidateSelector` に新 case を追加 (どの TextureFormat 候補を出すか)
4. `FormatCandidateSelector` のテスト (`FormatCandidateSelectorTests`) を追加
5. この spec の「データモデル - ShaderUsage 拡張ポリシー」欄を更新し、新値の意味論を正式記述
6. audit 作業を再開し、当該 hlsl 由来のプロパティに新値を使用
7. 全 audit 完了後に「未解決事項」欄の一時メモを削除
```

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Tests/Editor/LilToonCoreCatalogQualityTests.cs Tests/Editor/LilToonCoreCatalogQualityTests.cs.meta docs/specs/2026-04-24-liltoon-property-audit-design.md
git -C Packages/net.narazaka.vrchat.jnto commit -m "test(jnto): add catalog quality invariants + audit procedure for new patterns"
```

---

### Task 12: lil_common* hlsl (universal 共通プロパティ) audit

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Catalog/LilToonCoreCatalogData.cs`

対象 hlsl (Packages/jp.lilxyzw.liltoon/Shader/ 配下):
- `lil_common.hlsl`
- `lil_common_appdata.hlsl`
- `lil_common_frag.hlsl`
- `lil_common_frag_alpha.hlsl`
- `lil_common_frag_normal.hlsl`
- `lil_common_frag_shadow.hlsl`
- `lil_common_input.hlsl`
- `lil_common_macro.hlsl`

（実ファイル名は lilToon 2.3.2 に合わせ、audit 実行時に `ls Packages/jp.lilxyzw.liltoon/Shader/*.hlsl` で確認すること）

- [ ] **Step 1: 対象 hlsl のサンプリング全列挙**

Run: `Grep` で `LIL_SAMPLE_2D` / `LIL_SAMPLE_2D_ST` / `UnpackNormal` を全行取得。
Expected: 各プロパティが sample されている行がリストアップされる。

- [ ] **Step 2: 各 sampling の channel 読みを追跡し ReadChannels を確定**

For each matched sampling site, read ±15 行のコンテキストで `.r`/`.g`/`.b`/`.a`/`.rgb`/`.rgba` を拾う。`UnpackNormal(tex2D(...))` は `.ag` のみ参照と判定。`*=` / `+=` / `lerp` 等の full-vector operator は全 channel 参照と判定。

- [ ] **Step 3: seed エントリを正式値に上書き**

`LilToonCoreCatalogData.cs` の static constructor 内で、seed 行を正式値に書き換える。例:

```csharp
// seed:  ColorA("_MainTex");
// 正式:  Table[(null, "_MainTex")] = new(Color, RGBA, "lil_common_frag.hlsl:523");
```

- [ ] **Step 4: seed に無かったが sample されていた新規プロパティを追加**

audit 中に `_NewProperty` (仮) のような lilToon 2.3.2 新規プロパティが見つかれば、同じ static constructor に追加する。

- [ ] **Step 5: 未分類 pattern の有無を確認**

既存 3 値 (Color/Normal/SingleChannel) に収まらないものが出たら Task 11 Step 4 のプロシージャに従う。

- [ ] **Step 6: compile + quality test 実行**

Run: compile。0 error。
Run: `LilToonCoreCatalogQualityTests.Entry_EvidenceRef_NotSeedPlaceholder` の FAIL 数が、本タスクで audit したプロパティ分減ることを確認。`Entry_HasEvidenceRef` / `Entry_HasNonEmptyReadChannels` / `Entry_UsageIsValidEnumValue` は全 PASS。

Run: `LilToonCoreCatalogSeedTests` / `LilToonAlphaRulesTests` が依然 PASS することを確認 (振る舞い保全)。

- [ ] **Step 7: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Shared/Catalog/LilToonCoreCatalogData.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "audit(jnto): verify lil_common* hlsl sampling for base lilToon properties"
```

---

### Task 13: outline / lil_*_outline.hlsl audit

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Catalog/LilToonCoreCatalogData.cs`

対象 hlsl:
- `lil_common_frag_outline.hlsl`
- `lil_pass_outline_*.hlsl`
- outline 系 sampling を含む他 hlsl

- [ ] **Step 1: 対象 hlsl 列挙 + Grep**

Run: `ls Packages/jp.lilxyzw.liltoon/Shader/*.hlsl` で `outline` / `pass_outline` を含むファイルを特定。

Run: `Grep` で `LIL_SAMPLE_2D*` と `UnpackNormal` を全行取得。

- [ ] **Step 2: 各 sampling の channel 読みを追跡、ReadChannels を確定**

特に注意: `_OutlineTex` (`.rgb` のみ)、`_OutlineBumpMap` / `_OutlineVectorTex` (RGB 参照の Normal)、`_OutlineWidthMask` (R のみ SingleChannel) 等。outline 独自の sampling も漏らさない。

- [ ] **Step 3: catalog に反映**

outline 固有 prop (他 variant で使われないもの) は `variantId=null` のままで OK (全 variant 共通扱いで問題ない、outline を持つ variant のみ sample するだけなので catalog 検索で害がない)。ただし **outline と main で挙動が変わる同名 prop がある場合のみ、outline 用 variantId 指定エントリを別途追加**。

- [ ] **Step 4: compile + test + commit**

Run: compile + quality test。振る舞い保全確認。

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Shared/Catalog/LilToonCoreCatalogData.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "audit(jnto): verify outline hlsl sampling"
```

---

### Task 14: fur / furonly variants audit

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Catalog/LilToonCoreCatalogData.cs`

対象:
- `lil_pass_fur_*.hlsl`
- `lil_common_fur*.hlsl`
- `lts_fur.shader` / `lts_fur_two.shader` / `lts_fur_cutout.shader` / `lts_furonly*.shader` の include chain

- [ ] **Step 1: 対象 hlsl + include chain 特定**

Run: `lts_fur.shader` 等の `.shader` ファイルから HLSLINCLUDE / #include を grep、更に遷移先 hlsl を追跡。

- [ ] **Step 2: Fur 固有 sampling 列挙**

候補 prop: `_FurNoiseTex`, `_FurMask`, `_FurLengthMask`, `_FurVectorTex`, `_FurGravityMask`, `_FurAOTex` 等。各 sampling を読む。

- [ ] **Step 3: catalog に反映**

Fur 固有 prop は `variantId` を具体値 (`"lts_fur"`, `"lts_fur_two"`, `"lts_fur_cutout"`, `"lts_furonly"`, `"lts_furonly_two"`, `"lts_furonly_cutout"`) で追加。同じ sampling pattern なら複数 variantId 分のエントリを追加する (seed 互換の観点で `variantId=null` に統合も検討可能だが、fur prop は fur shader にしか出ないので variant 固有指定が清潔)。

- [ ] **Step 4: compile + test + commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Shared/Catalog/LilToonCoreCatalogData.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "audit(jnto): verify fur / furonly variants sampling"
```

---

### Task 15: gem / refraction variants audit

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Catalog/LilToonCoreCatalogData.cs`

対象:
- `lts_gem.shader`, `ltsmulti_gem.shader` + 関連 hlsl
- `lts_ref.shader`, `lts_ref_blur.shader`, `ltsmulti_ref.shader` + 関連 hlsl

- [ ] **Step 1: 対象 hlsl 特定 + sampling 列挙**

- [ ] **Step 2: Gem/Refraction 固有 prop の channel 読み確定**

候補: `_GemEnvTex`, `_GemEnvCubemap` (cube map は 2D catalog 対象外)、`_SmoothnessTex` (re-use from common)、`_RefractionStrength` 関連 2D。

- [ ] **Step 3: catalog に反映**

固有 prop は variantId=(`lts_gem`, `ltsmulti_gem`, `lts_ref`, `lts_ref_blur`, `ltsmulti_ref`) 付きで追加。

- [ ] **Step 4: compile + test + commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Shared/Catalog/LilToonCoreCatalogData.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "audit(jnto): verify gem / refraction variants sampling"
```

---

### Task 16: ltsmulti-specific audit (Multi 共通 prop)

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Catalog/LilToonCoreCatalogData.cs`

対象:
- `ltsmulti.shader`, `ltsmulti_o.shader`, `ltsmulti_fur.shader`, `ltsmulti_gem.shader`, `ltsmulti_ref.shader` + 関連 hlsl
- `lil_pass_forward_multi.hlsl` 等

- [ ] **Step 1: Multi 版 hlsl 特定 + sampling 列挙**

- [ ] **Step 2: ltsmulti 固有 prop (あれば) と、既存 prop が ltsmulti で挙動が違うケースを特定**

挙動同一なら `variantId=null` のままで済む。異なる場合のみ `"ltsmulti*"` 指定エントリを追加。

- [ ] **Step 3: catalog に反映 + compile + test + commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto commit -m "audit(jnto): verify ltsmulti variants sampling"
```

---

### Task 17: ltsl (lite) variants audit

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Catalog/LilToonCoreCatalogData.cs`

対象:
- `ltsl*.shader` + `lil_common_lite.hlsl` / `lil_pass_forward_lite.hlsl` 等
- lite は `_TriMask` など固有 prop を持つ

- [ ] **Step 1〜3: 他 audit task と同手順**

Lite は property set が削減版のため、common 系プロパティの sampling 箇所が異なるケースがある。`_TriMask` 等 lite 固有 prop も拾う。

- [ ] **Step 4: commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto commit -m "audit(jnto): verify ltsl (lite) variants sampling"
```

---

### Task 18: tess variants + 残り audit、Phase 3 完了

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Catalog/LilToonCoreCatalogData.cs`

対象:
- `lts_tess*.shader` / `ltspass_tess_*.shader` + 関連 hlsl
- 他、ここまでで拾いきれていないプロパティ全て

- [ ] **Step 1: Tess 系 hlsl audit**

- [ ] **Step 2: 全 seed エントリが正式 EvidenceRef に置き換わっているか最終確認**

Run: `LilToonCoreCatalogQualityTests.Entry_EvidenceRef_NotSeedPlaceholder` を実行。
Expected: **全 PASS (FAIL 0)**。

- [ ] **Step 3: sample されているが catalog 未登録の prop を grep 確認**

Run: `Grep` で `LIL_SAMPLE_2D(_` パターンを全 hlsl 横断検索し、catalog に無い prop を列挙。あれば追加。

- [ ] **Step 4: 振る舞い保全 regression 確認**

Run: 全 editor test。
Expected: 全 PASS。特に `AlphaStripperTests`, `CompressionCandidateEnumeratorTests`, `Phase2PipelineE2ETests` が壊れていないこと。

- [ ] **Step 5: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto commit -m "audit(jnto): complete lilToon 2.3.2 property catalog audit"
```

---

## Phase 4: Uzumore Shader 同梱 extension

### Task 19: UzumoreTextureCatalogExtension を TDD で実装

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Extensions/UzumoreTextureCatalogExtension.cs`
- Create: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/UzumoreTextureCatalogExtensionTests.cs`

- [ ] **Step 1: Uzumore custom.hlsl を読んで対象 prop を特定**

Read `Packages/jp.sigmal00.uzumore-shader/Runtime/Shaders/custom.hlsl` と `lilCustomShaderProperties.lilblock`、`lilCustomShaderInsert.lilblock`、`lilCustomShaderDatas.lilblock`、`custom_insert.hlsl`。

Uzumore 固有 prop (現時点で確定): `_UzumoreAmount` (float、2D でないので catalog 対象外)、`_UzumoreBias` (float、catalog 対象外)、`_UzumoreMask` (2D、catalog 対象)。

`_UzumoreMask` の sampling 箇所と channel 読みを特定 → `ReadChannels` を確定。

- [ ] **Step 2: 失敗テストを書く**

Create `Packages/net.narazaka.vrchat.jnto/Tests/Editor/UzumoreTextureCatalogExtensionTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Shared;
using Narazaka.VRChat.Jnto.Editor.Shared.Extensions;

public class UzumoreTextureCatalogExtensionTests
{
    // Shader モック不可のため、Matches のロジックは path 直接判定 helper に切り出されていることを前提としたテストにする。
    // 拡張側に `MatchesPath(string)` internal helper を設置する (Step 4 で追加)。

    [TestCase("Packages/jp.sigmal00.uzumore-shader/Runtime/Shaders/lts.lilcontainer", true)]
    [TestCase("Packages/jp.sigmal00.uzumore-shader/Runtime/Shaders/lts_fur.lilcontainer", true)]
    [TestCase("Packages/jp.lilxyzw.liltoon/Shader/lts.shader", false)]
    [TestCase("Packages/com.example.other/foo.shader", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void MatchesPath(string path, bool expected)
    {
        Assert.AreEqual(expected, UzumoreTextureCatalogExtension.MatchesPath(path));
    }

    [Test]
    public void TryClassify_UzumoreMask_ReturnsExpected()
    {
        var ext = new UzumoreTextureCatalogExtension();
        Assert.IsTrue(ext.TryClassify(shader: null, variantId: "lts", propName: "_UzumoreMask", out var info));
        // audit で確定した値を記述。実値は Step 1 の .hlsl 読みで決定。
        Assert.AreNotEqual(ChannelMask.None, info.ReadChannels);
        Assert.IsFalse(string.IsNullOrEmpty(info.EvidenceRef));
    }

    [Test]
    public void TryClassify_UnknownProperty_ReturnsFalse()
    {
        var ext = new UzumoreTextureCatalogExtension();
        Assert.IsFalse(ext.TryClassify(shader: null, variantId: "lts", propName: "_MainTex", out _));
    }
}
```

- [ ] **Step 3: 失敗確認**

Run: compile → `UzumoreTextureCatalogExtension` 未定義の error。

- [ ] **Step 4: 実装**

Create `Packages/net.narazaka.vrchat.jnto/Editor/Shared/Extensions/UzumoreTextureCatalogExtension.cs`:

```csharp
using System;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Shared.Extensions
{
    /// <summary>
    /// jp.sigmal00.uzumore-shader 用 (jnto 同梱) の分類拡張。
    /// Uzumore 未インストール環境では Matches が常時 false になり noop。
    /// </summary>
    public sealed class UzumoreTextureCatalogExtension : ICustomPropertyCatalogExtension
    {
        const string UZUMORE_PATH_MARKER = "jp.sigmal00.uzumore-shader";

        [InitializeOnLoadMethod]
        static void AutoRegister() =>
            LilToonPropertyCatalog.RegisterExtension(new UzumoreTextureCatalogExtension());

        public bool Matches(Shader shader)
        {
            if (shader == null) return false;
            var path = AssetDatabase.GetAssetPath(shader);
            return MatchesPath(path);
        }

        internal static bool MatchesPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.Replace('\\', '/').Contains("/" + UZUMORE_PATH_MARKER + "/");
        }

        public bool TryClassify(Shader shader, string variantId, string propName, out LilToonPropertyInfo info)
        {
            switch (propName)
            {
                case "_UzumoreMask":
                    // Step 1 audit 結果に基づいて確定した値。実値は audit 時に記述する。
                    info = new LilToonPropertyInfo(
                        usage: ShaderUsage.SingleChannel,           // audit 結果で確定
                        readChannels: ChannelMask.R,                // audit 結果で確定
                        evidenceRef: "uzumore/custom.hlsl:NN (calcIntrudePos 経由で _UzumoreMask.r のみ参照)"
                    );
                    return true;
                default:
                    info = default;
                    return false;
            }
        }
    }
}
```

※ Step 1 の audit 結果に応じて `Usage` / `ReadChannels` / `EvidenceRef` の正式値に差し替えること。現状の値は「Step 1 でそう確定した前提」で書かれている placeholder ではなく、「現時点で最も蓋然性が高い推定」であり、Step 1 で .hlsl を実読した結果と齟齬があれば差し替える。

- [ ] **Step 5: テスト実行**

Run: compile + test。
Expected: `UzumoreTextureCatalogExtensionTests` 全 PASS。

- [ ] **Step 6: 統合確認**

Unity Editor を起動してドメインリロード後、`LilToonPropertyCatalog` に `UzumoreTextureCatalogExtension` が登録済みであることを smoke テストで確認。

Run: aibridge から既存 shader 試験: Uzumore インストール済み環境で `Shader.Find("*/lilToon")` 相当の query で Uzumore shader を取得し、`LilToonPropertyCatalog.TryGet(shader, "_UzumoreMask", out _)` が true を返すか確認。

- [ ] **Step 7: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Shared/Extensions/ Tests/Editor/UzumoreTextureCatalogExtensionTests.cs Tests/Editor/UzumoreTextureCatalogExtensionTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto): bundle UzumoreTextureCatalogExtension for jp.sigmal00.uzumore-shader"
```

---

### Task 20: 最終統合テストと spec の監査完了マーキング

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/docs/specs/2026-04-24-liltoon-property-audit-design.md`

- [ ] **Step 1: 全 editor test を通し実行**

Run: 全 editor test via aibridge。
Expected: 全 PASS。`LilToonCoreCatalogQualityTests.Entry_EvidenceRef_NotSeedPlaceholder` も全 PASS であること。

- [ ] **Step 2: spec の「未解決事項」欄を確認**

audit 中に仮追記した item があれば全て解消済みであることを確認。新 `ShaderUsage` 値を追加した場合はその記録が spec 本文に反映済みであることを確認。

- [ ] **Step 3: spec 冒頭に audit 完了情報を追記**

```markdown
- Audit 完了日: 2026-MM-DD
- 最終 catalog エントリ数: 約 NN 件 (core) + NN 件 (Uzumore 同梱拡張)
- 同梱拡張: UzumoreTextureCatalogExtension
```

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add docs/specs/2026-04-24-liltoon-property-audit-design.md
git -C Packages/net.narazaka.vrchat.jnto commit -m "docs(jnto): mark lilToon property audit as complete"
```

---

## 完了判定

以下を全て満たせば本計画は完了:

- [ ] `LilToonPropertyCatalog.TryGet(shader, propName)` が public 公開されている
- [ ] 旧 `LilToonTextureCatalog` が削除されている
- [ ] `LilToonCoreCatalogData` の全エントリが seed placeholder ではない正式 `EvidenceRef` を持つ
- [ ] `LilToonCoreCatalogQualityTests` が全 PASS
- [ ] `LilToonAlphaRulesTests` / `LilToonCoreCatalogSeedTests` / `LilToonShaderIdentifierTests` / `LilToonPropertyCatalogTests` / `UzumoreTextureCatalogExtensionTests` が全 PASS
- [ ] 既存 regression (`AlphaStripperTests`, `CompressionCandidateEnumeratorTests`, `Phase2PipelineE2ETests` 等) が全 PASS
- [ ] spec の「未解決事項」欄が空 (新分類追加がある場合は本文に統合済み)
- [ ] `UzumoreTextureCatalogExtension` が `jp.sigmal00.uzumore-shader` へ asmdef 参照を持たない
- [ ] Uzumore 未インストール環境で compile が通る (試せる場合)

## 備考 / 想定リスク

- **lilToon 2.3.2 の hlsl 構造に依存**: 将来 lilToon が include chain を大幅に再編すると EvidenceRef の line 番号が陳腐化する。再検証タスクを別途立てる前提
- **Uzumore 固有 prop が 1 つしかない**: 同梱拡張を作る費用対効果は低いが、拡張機構の「実在する第三者シェーダー対応例」として設計検証を兼ねる
- **audit 作業量**: lil_common* の hlsl だけで `LIL_SAMPLE_2D` 呼び出しが 60-80 件程度、各 site での channel 追跡に時間がかかる。Task 12-18 は 1 task あたり 30 分-1 時間を想定
- **`ShaderUsage` 新値が出た場合**: Task 11 Step 4 のプロシージャに従う。`FormatCandidateSelector` への case 追加は最小限 (existing format 候補群の一部を新 usage でも再利用するケースが大半) を想定
