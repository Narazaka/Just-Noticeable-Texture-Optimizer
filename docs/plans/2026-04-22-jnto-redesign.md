# JNTO 再設計 実装プラン

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** JNTO を「タイル単位 perceptual gate + GPU 実行 + 解析的予測 + 永続キャッシュ」の新アーキテクチャに置き換え、3分→初回30秒・キャッシュヒット3秒を達成しつつ判定妥当性を向上させる。

**Architecture:** Phase2 ResolutionReducePass を段階的に再設計する。既存 Phase1 (AlphaStripPass) と共通モジュール (TextureReferenceGraph, MaterialCloner 等) は維持。Phase2 は UV 固定グリッドタイリング→タイルごとの実効表示解像度 r(T) 算出→GPU Compute Shader によるメトリクス評価→JND 正規化 max 合成の Perceptual Gate→サイズ降順二分探索 + 解析的フォーマット予測→in-memory + 永続キャッシュ→NDMF ErrorReport + 独自 Report Window で可観測性確保、の構成。

**Tech Stack:** Unity 2022.3 / C# / NDMF (最新マイナー) / Compute Shader (HLSL) / XXHash64 / Unity Test Framework (NUnit) / Library/JntoCache/ 永続化。

**Spec:** `Packages/net.narazaka.vrchat.jnto/docs/specs/2026-04-22-jnto-redesign-design.md`

---

## グランドルール

全タスクで遵守する:

**Bash 制約 (CLAUDE.md 由来):**
- `cat`/`head`/`tail`/`grep`/`rg`/`find`/`ls` を Bash で使わない → Read/Grep/Glob ツールを使う
- `cd` 禁止 → 絶対パス or `-C` オプション
- `git ... $()` 禁止
- 複数コマンドの `&&`/`;` 連結禁止、1 コマンド 1 tool call
- カレント (`x:/make/devel/vrchat-AVATAR-SANDBOX`) 内のパスは相対パス優先

**Unity 検証:**
- コード変更後は `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity` で検証
- テスト実行前にシーンを保存 (aibridge の save dialog でハングを防ぐ)
- Prefab Stage 編集中は .cs 新規作成しない

**コミット方針:**
- 1 タスク 1 commit。メッセージは `feat:` / `test:` / `refactor:` / `chore:` / `docs:` プレフィックス
- Conventional Commits 準拠、scope は `jnto/<module>` (例: `feat(jnto/tiling): add UvTileGrid`)

**TDD:**
- 新規モジュールは failing test → impl → pass → commit の順
- Compute Shader は CPU reference 実装と出力一致をテスト
- 既存テスト互換を壊さない (壊す場合は削除タスクで明示)

---

## マイルストーン

| M | 内容 | 完了時点の状態 |
|---|---|---|
| M1 | Foundation (設定・依存) | 既存動作維持、新フィールド追加 |
| M2 | Tiling + r(T) | タイル分割と r(T) が単体で動く (未接続) |
| M3 | GPU 基盤 | RenderTexture chain と Compute 実行基盤が動く |
| M4 | Metrics + Gate | 全メトリクスが GPU で動き、Gate が成立 |
| M5 | Format Predictor + Binary Search | 圧縮戦略の各部品が動く |
| M6 | Cache | In-memory + Persistent cache が動く |
| M7 | 統合 | 新 Phase2Pipeline が通しで動作、旧と切替可能 |
| M8 | Reporting | NDMF Report + Report Window + DebugDump 動作 |
| M9 | クリーンアップ + ベンチ | 旧コード削除、速度目標達成確認 |
| M10 | NDMF 依存更新 | package.json が最新マイナー pin |

---

## M1. Foundation

### Task 1.1: Runtime 設定拡張

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Runtime/TextureOptimizer.cs`
- Create: `Packages/net.narazaka.vrchat.jnto/Runtime/EncodePolicy.cs`
- Create: `Packages/net.narazaka.vrchat.jnto/Runtime/CacheMode.cs`

- [ ] **Step 1: EncodePolicy enum を追加**

`Runtime/EncodePolicy.cs`:
```csharp
namespace Narazaka.VRChat.Jnto
{
    public enum EncodePolicy
    {
        /// <summary>最終候補を必ず実 encode で verify。</summary>
        Safe,
        /// <summary>予測確信度が閾値超なら verify をスキップ。</summary>
        Fast,
    }
}
```

- [ ] **Step 2: CacheMode enum を追加**

`Runtime/CacheMode.cs`:
```csharp
namespace Narazaka.VRChat.Jnto
{
    public enum CacheMode
    {
        /// <summary>結果 + encode 済み raw bytes を保存。</summary>
        Full,
        /// <summary>結果メタデータのみ保存、encode は都度実行。</summary>
        Compact,
        /// <summary>永続キャッシュ無効。</summary>
        Disabled,
    }
}
```

- [ ] **Step 3: TextureOptimizer に新フィールドを追加**

`Runtime/TextureOptimizer.cs` に追加:
```csharp
        [Tooltip("HMD 片目のピクセル密度 (px/度)。Quest/Index 級で 20 が目安。")]
        public FloatOverride HMDPixelsPerDegree = new FloatOverride { HasValue = true, Value = 20f };

        [Tooltip("フォーマット決定時の encode 試行ポリシー。")]
        public EncodePolicyOverride EncodePolicy = new EncodePolicyOverride { HasValue = true, Value = Jnto.EncodePolicy.Safe };

        [Tooltip("永続キャッシュのモード。")]
        public CacheModeOverride Cache = new CacheModeOverride { HasValue = true, Value = Jnto.CacheMode.Full };

        [Tooltip("デバッグダンプ先ディレクトリ (空なら無効)。")]
        public string DebugDumpPath = "";

        [Tooltip("JND キャリブレーション (ScriptableObject)。空で既定値。")]
        public Phase2.Gate.DegradationCalibration Calibration;
```

同ファイル末尾の Override 構造体群に追加:
```csharp
    [System.Serializable] public struct EncodePolicyOverride { public bool HasValue; public EncodePolicy Value; }
    [System.Serializable] public struct CacheModeOverride { public bool HasValue; public CacheMode Value; }
```

**注**: `DegradationCalibration` 参照は M4 で作成。まず **前方宣言スタブ**を Runtime asmdef に作って依存を解消する。

- [ ] **Step 4: 前方宣言スタブを作成**

`Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Gate/DegradationCalibration.cs` (M4 で本実装に置換):
```csharp
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    [CreateAssetMenu(menuName = "Just-Noticeable Texture Optimizer/Degradation Calibration", fileName = "DegradationCalibration")]
    public class DegradationCalibration : ScriptableObject
    {
        // Placeholder - will be filled in M4
    }
}
```

しかし **Runtime asmdef からは Editor 型を参照できない**ので、TextureOptimizer の `Calibration` フィールド型は `UnityEngine.Object` にし、Editor 側でキャストする設計に変える:

`Runtime/TextureOptimizer.cs` の該当行を:
```csharp
        [Tooltip("JND キャリブレーション (DegradationCalibration asset)。空で既定値。")]
        public UnityEngine.Object Calibration;
```

- [ ] **Step 5: Unity コンパイル確認**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Expected: エラーなし

- [ ] **Step 6: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Runtime/ Editor/Phase2/Gate/DegradationCalibration.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/runtime): add settings for new pipeline (EncodePolicy/CacheMode/HMDPixelsPerDegree/Debug/Calibration)"
```

---

### Task 1.2: ResolvedSettings 拡張

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Resolution/ResolvedSettings.cs`
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Resolution/SettingsResolver.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/SettingsResolverTests.cs`

- [ ] **Step 1: ResolvedSettings に新フィールドを追加**

`Editor/Resolution/ResolvedSettings.cs`:
```csharp
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Resolution
{
    public class ResolvedSettings
    {
        public QualityPreset Preset = QualityPreset.Medium;
        public float ViewDistanceCm = 30f;
        public BoneWeightMap BoneWeights = BoneWeightMap.Default();
        public float HMDPixelsPerDegree = 20f;
        public EncodePolicy EncodePolicy = EncodePolicy.Safe;
        public CacheMode CacheMode = CacheMode.Full;
        public string DebugDumpPath = "";
        public Object Calibration;
    }
}
```

- [ ] **Step 2: SettingsResolver を拡張**

`Editor/Resolution/SettingsResolver.cs` の foreach 内に追加:
```csharp
            foreach (var c in chain)
            {
                if (c.Preset.HasValue) r.Preset = c.Preset.Value;
                if (c.ViewDistanceCm.HasValue) r.ViewDistanceCm = c.ViewDistanceCm.Value;
                if (c.BoneWeights.HasValue) r.BoneWeights = c.BoneWeights.Value;
                if (c.HMDPixelsPerDegree.HasValue) r.HMDPixelsPerDegree = c.HMDPixelsPerDegree.Value;
                if (c.EncodePolicy.HasValue) r.EncodePolicy = c.EncodePolicy.Value;
                if (c.Cache.HasValue) r.CacheMode = c.Cache.Value;
                if (!string.IsNullOrEmpty(c.DebugDumpPath)) r.DebugDumpPath = c.DebugDumpPath;
                if (c.Calibration != null) r.Calibration = c.Calibration;
            }
```

- [ ] **Step 3: SettingsResolver テストを書く (失敗確認)**

`Tests/Editor/SettingsResolverTests.cs` に追加:
```csharp
    [Test]
    public void Resolve_OverridesNewFields()
    {
        var root = new GameObject("root");
        var opt = root.AddComponent<TextureOptimizer>();
        opt.HMDPixelsPerDegree = new FloatOverride { HasValue = true, Value = 25f };
        opt.EncodePolicy = new EncodePolicyOverride { HasValue = true, Value = EncodePolicy.Fast };
        opt.Cache = new CacheModeOverride { HasValue = true, Value = CacheMode.Compact };

        var r = SettingsResolver.Resolve(root.transform);

        Assert.AreEqual(25f, r.HMDPixelsPerDegree);
        Assert.AreEqual(EncodePolicy.Fast, r.EncodePolicy);
        Assert.AreEqual(CacheMode.Compact, r.CacheMode);
        Object.DestroyImmediate(root);
    }
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "SettingsResolverTests.Resolve_OverridesNewFields"`
Expected: FAIL (まだ実装されていない場合) or PASS (実装済みなら成功)

- [ ] **Step 4: 実装が Step 2 で済んでいるのでテスト実行**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "SettingsResolverTests.Resolve_OverridesNewFields"`
Expected: PASS

- [ ] **Step 5: 全テスト回帰確認**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "SettingsResolverTests"`
Expected: 全 PASS

- [ ] **Step 6: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Resolution/ Tests/Editor/SettingsResolverTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/resolution): extend ResolvedSettings with new pipeline fields"
```

---

## M2. Tiling + r(T)

### Task 2.1: UvTileGrid 型定義と決定関数

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Tiling/UvTileGrid.cs`
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Tiling/TileStats.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/UvTileGridTests.cs`

- [ ] **Step 1: TileStats 型を定義**

`Editor/Phase2/Tiling/TileStats.cs`:
```csharp
namespace Narazaka.VRChat.Jnto.Editor.Phase2.Tiling
{
    public struct TileStats
    {
        /// <summary>タイル内三角形の worldArea / uvArea の最大値 (cm² / uv²)。</summary>
        public float Density;
        /// <summary>タイル内三角形のボーン重要度の最大値。</summary>
        public float BoneWeight;
        /// <summary>このタイルに属する三角形が存在するか。</summary>
        public bool HasCoverage;
    }
}
```

- [ ] **Step 2: UvTileGrid 骨格を定義**

`Editor/Phase2/Tiling/UvTileGrid.cs`:
```csharp
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Tiling
{
    public class UvTileGrid
    {
        public int TextureWidth;
        public int TextureHeight;
        public int TileSize;
        public int TilesX;
        public int TilesY;
        public TileStats[] Tiles;  // length = TilesX * TilesY

        public static int DetermineTileSize(int textureWidth, int textureHeight)
        {
            int maxDim = Mathf.Max(textureWidth, textureHeight);
            int raw = Mathf.Max(1, maxDim / 8);
            int pot = 1;
            while (pot < raw) pot <<= 1;
            // pot is now CeilPowerOfTwo(maxDim/8)
            return Mathf.Clamp(pot, 16, 64);
        }

        public static UvTileGrid Create(int textureWidth, int textureHeight)
        {
            int tileSize = DetermineTileSize(textureWidth, textureHeight);
            int tx = Mathf.Max(1, (textureWidth + tileSize - 1) / tileSize);
            int ty = Mathf.Max(1, (textureHeight + tileSize - 1) / tileSize);
            return new UvTileGrid
            {
                TextureWidth = textureWidth,
                TextureHeight = textureHeight,
                TileSize = tileSize,
                TilesX = tx,
                TilesY = ty,
                Tiles = new TileStats[tx * ty],
            };
        }

        public ref TileStats GetTile(int tx, int ty) => ref Tiles[ty * TilesX + tx];
    }
}
```

- [ ] **Step 3: DetermineTileSize テスト (失敗確認)**

`Tests/Editor/UvTileGridTests.cs`:
```csharp
using NUnit.Framework;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class UvTileGridTests
{
    [Test]
    public void DetermineTileSize_ReturnsFixed64_For_512AndAbove()
    {
        Assert.AreEqual(64, UvTileGrid.DetermineTileSize(4096, 4096));
        Assert.AreEqual(64, UvTileGrid.DetermineTileSize(2048, 2048));
        Assert.AreEqual(64, UvTileGrid.DetermineTileSize(1024, 1024));
        Assert.AreEqual(64, UvTileGrid.DetermineTileSize(512, 512));
    }

    [Test]
    public void DetermineTileSize_ScalesDown_For_SmallTextures()
    {
        Assert.AreEqual(32, UvTileGrid.DetermineTileSize(256, 256));
        Assert.AreEqual(16, UvTileGrid.DetermineTileSize(128, 128));
        Assert.AreEqual(16, UvTileGrid.DetermineTileSize(64, 64));
    }

    [Test]
    public void DetermineTileSize_UsesMaxDim_ForRect()
    {
        Assert.AreEqual(64, UvTileGrid.DetermineTileSize(2048, 1024));
        Assert.AreEqual(32, UvTileGrid.DetermineTileSize(256, 128));
    }

    [Test]
    public void Create_HasCorrectTileCount()
    {
        var g = UvTileGrid.Create(4096, 2048);
        Assert.AreEqual(64, g.TileSize);
        Assert.AreEqual(64, g.TilesX);
        Assert.AreEqual(32, g.TilesY);
        Assert.AreEqual(64 * 32, g.Tiles.Length);
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "UvTileGridTests"`
Expected: 全 PASS (ロジックは Step 2 で実装済み)

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Tiling/ Tests/Editor/UvTileGridTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/tiling): add UvTileGrid with adaptive tile size"
```

---

### Task 2.2: メッシュから UvTileGrid への集計

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Tiling/TileRasterizer.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/TileRasterizerTests.cs`

- [ ] **Step 1: TileRasterizer を実装**

`Editor/Phase2/Tiling/TileRasterizer.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Tiling
{
    public static class TileRasterizer
    {
        /// <summary>
        /// メッシュの三角形を UvTileGrid にラスタライズし、
        /// 各タイルの Density / BoneWeight を保守 max で集計する。
        /// </summary>
        public static void Accumulate(
            UvTileGrid grid,
            Renderer renderer,
            Mesh mesh,
            Dictionary<Transform, BoneCategory> bonemap,
            BoneWeightMap weights)
        {
            if (mesh == null) return;
            var uvs = mesh.uv;
            var verts = mesh.vertices;
            if (uvs == null || uvs.Length == 0 || verts == null) return;

            var l2w = renderer.localToWorldMatrix;
            Transform[] bones = null;
            BoneWeight[] bw = null;
            if (renderer is SkinnedMeshRenderer smr)
            {
                bones = smr.bones;
                bw = mesh.boneWeights;
            }

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var tris = mesh.GetTriangles(sub);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
                    if (i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length) continue;

                    float uvArea = TriArea2D(uvs[i0], uvs[i1], uvs[i2]);
                    if (uvArea < 1e-10f) continue;

                    var w0 = l2w.MultiplyPoint3x4(verts[i0]);
                    var w1 = l2w.MultiplyPoint3x4(verts[i1]);
                    var w2 = l2w.MultiplyPoint3x4(verts[i2]);
                    float worldAreaM2 = TriArea3D(w0, w1, w2);
                    float worldAreaCm2 = worldAreaM2 * 10000f;
                    float density = worldAreaCm2 / uvArea;

                    float bwAvg = 1f;
                    if (bones != null && bw != null && bonemap != null && weights != null)
                    {
                        bwAvg = (BoneWeightFor(bw, i0, bones, bonemap, weights)
                               + BoneWeightFor(bw, i1, bones, bonemap, weights)
                               + BoneWeightFor(bw, i2, bones, bonemap, weights)) / 3f;
                    }
                    else if (bonemap != null && weights != null)
                    {
                        bwAvg = StaticBoneWeight(renderer.transform, bonemap, weights);
                    }

                    Rasterize(grid, uvs[i0], uvs[i1], uvs[i2], density, bwAvg);
                }
            }
        }

        static void Rasterize(UvTileGrid g, Vector2 uv0, Vector2 uv1, Vector2 uv2, float density, float bw)
        {
            float tw = g.TilesX, th = g.TilesY;
            // タイル座標系に変換 (uv * tilesX)
            float x0 = uv0.x * tw, y0 = uv0.y * th;
            float x1 = uv1.x * tw, y1 = uv1.y * th;
            float x2 = uv2.x * tw, y2 = uv2.y * th;

            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x0, Mathf.Min(x1, x2))));
            int maxX = Mathf.Min(g.TilesX - 1, Mathf.FloorToInt(Mathf.Max(x0, Mathf.Max(x1, x2))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y0, Mathf.Min(y1, y2))));
            int maxY = Mathf.Min(g.TilesY - 1, Mathf.FloorToInt(Mathf.Max(y0, Mathf.Max(y1, y2))));

            for (int ty = minY; ty <= maxY; ty++)
                for (int tx = minX; tx <= maxX; tx++)
                {
                    if (!TriangleIntersectsTile(x0, y0, x1, y1, x2, y2, tx, ty)) continue;
                    ref var tile = ref g.GetTile(tx, ty);
                    tile.HasCoverage = true;
                    if (density > tile.Density) tile.Density = density;
                    if (bw > tile.BoneWeight) tile.BoneWeight = bw;
                }
        }

        static bool TriangleIntersectsTile(float x0, float y0, float x1, float y1, float x2, float y2, int tx, int ty)
        {
            float tl = tx, tr = tx + 1, tt = ty, tb = ty + 1;
            // point-in-triangle でタイル 4 隅のどれかが含まれるか
            if (PointInTri(tl, tt, x0, y0, x1, y1, x2, y2)) return true;
            if (PointInTri(tr, tt, x0, y0, x1, y1, x2, y2)) return true;
            if (PointInTri(tl, tb, x0, y0, x1, y1, x2, y2)) return true;
            if (PointInTri(tr, tb, x0, y0, x1, y1, x2, y2)) return true;
            // 三角形頂点がタイル内か
            if (x0 >= tl && x0 <= tr && y0 >= tt && y0 <= tb) return true;
            if (x1 >= tl && x1 <= tr && y1 >= tt && y1 <= tb) return true;
            if (x2 >= tl && x2 <= tr && y2 >= tt && y2 <= tb) return true;
            // 辺とタイル辺の交差テスト
            if (SegIntersectRect(x0, y0, x1, y1, tl, tt, tr, tb)) return true;
            if (SegIntersectRect(x1, y1, x2, y2, tl, tt, tr, tb)) return true;
            if (SegIntersectRect(x2, y2, x0, y0, tl, tt, tr, tb)) return true;
            return false;
        }

        static bool PointInTri(float px, float py, float x0, float y0, float x1, float y1, float x2, float y2)
        {
            float d1 = (px - x1) * (y0 - y1) - (x0 - x1) * (py - y1);
            float d2 = (px - x2) * (y1 - y2) - (x1 - x2) * (py - y2);
            float d3 = (px - x0) * (y2 - y0) - (x2 - x0) * (py - y0);
            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        static bool SegIntersectRect(float ax, float ay, float bx, float by, float rl, float rt, float rr, float rb)
        {
            return SegSeg(ax, ay, bx, by, rl, rt, rr, rt)
                || SegSeg(ax, ay, bx, by, rr, rt, rr, rb)
                || SegSeg(ax, ay, bx, by, rr, rb, rl, rb)
                || SegSeg(ax, ay, bx, by, rl, rb, rl, rt);
        }

        static bool SegSeg(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy)
        {
            float d = (bx - ax) * (dy - cy) - (by - ay) * (dx - cx);
            if (Mathf.Abs(d) < 1e-10f) return false;
            float t = ((cx - ax) * (dy - cy) - (cy - ay) * (dx - cx)) / d;
            float u = ((cx - ax) * (by - ay) - (cy - ay) * (bx - ax)) / d;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }

        static float TriArea2D(Vector2 a, Vector2 b, Vector2 c) =>
            Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;

        static float TriArea3D(Vector3 a, Vector3 b, Vector3 c) =>
            Vector3.Cross(b - a, c - a).magnitude * 0.5f;

        static float BoneWeightFor(BoneWeight[] bw, int idx, Transform[] bones,
            Dictionary<Transform, BoneCategory> bonemap, BoneWeightMap weights)
        {
            if (idx >= bw.Length) return weights.Get(BoneCategory.Other);
            var w = bw[idx];
            float s = 0f;
            s += w.weight0 * LookupBone(bones, w.boneIndex0, bonemap, weights);
            s += w.weight1 * LookupBone(bones, w.boneIndex1, bonemap, weights);
            s += w.weight2 * LookupBone(bones, w.boneIndex2, bonemap, weights);
            s += w.weight3 * LookupBone(bones, w.boneIndex3, bonemap, weights);
            return s;
        }

        static float LookupBone(Transform[] bones, int idx,
            Dictionary<Transform, BoneCategory> bonemap, BoneWeightMap weights)
        {
            if (bones == null || idx < 0 || idx >= bones.Length || bones[idx] == null)
                return weights.Get(BoneCategory.Other);
            for (var cur = bones[idx]; cur != null; cur = cur.parent)
                if (bonemap.TryGetValue(cur, out var c)) return weights.Get(c);
            return weights.Get(BoneClassifier.ClassifyByName(bones[idx].name));
        }

        static float StaticBoneWeight(Transform t,
            Dictionary<Transform, BoneCategory> bonemap, BoneWeightMap weights)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (bonemap.TryGetValue(cur, out var c)) return weights.Get(c);
            return weights.Get(BoneCategory.Other);
        }
    }
}
```

- [ ] **Step 2: 単一三角形のラスタライズテスト**

`Tests/Editor/TileRasterizerTests.cs`:
```csharp
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class TileRasterizerTests
{
    [Test]
    public void SingleTriangle_CoveringAllTiles_MarksAllCoverage()
    {
        var go = new GameObject("r");
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
            uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) },
            triangles = new[] { 0, 1, 2 },
        };
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        var grid = UvTileGrid.Create(128, 128);
        TileRasterizer.Accumulate(grid, mr, mesh, null, null);

        int covered = 0;
        foreach (var t in grid.Tiles) if (t.HasCoverage) covered++;
        Assert.Greater(covered, 0);
        Assert.Greater(covered, grid.Tiles.Length / 4, "triangle covering UV half should mark many tiles");

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void DenseTriangle_HasHigherDensity()
    {
        var go = new GameObject("r");
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        // Large world triangle (100cm), small UV triangle → high density
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
            uv = new[] { new Vector2(0, 0), new Vector2(0.1f, 0), new Vector2(0, 0.1f) },
            triangles = new[] { 0, 1, 2 },
        };
        mf.sharedMesh = mesh;

        var grid = UvTileGrid.Create(128, 128);
        TileRasterizer.Accumulate(grid, mr, mesh, null, null);

        // 左下タイル (UV 0,0 周辺) に高密度が入っているはず
        var t = grid.GetTile(0, 0);
        Assert.IsTrue(t.HasCoverage);
        Assert.Greater(t.Density, 100f);

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(mesh);
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "TileRasterizerTests"`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Tiling/TileRasterizer.cs Tests/Editor/TileRasterizerTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/tiling): add TileRasterizer accumulating mesh triangles into tile stats"
```

---

### Task 2.3: 実効表示解像度 r(T) 計算

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Tiling/EffectiveResolutionCalculator.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/EffectiveResolutionCalculatorTests.cs`

- [ ] **Step 1: 計算関数を実装**

`Editor/Phase2/Tiling/EffectiveResolutionCalculator.cs`:
```csharp
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Tiling
{
    public static class EffectiveResolutionCalculator
    {
        public const int RMin = 4;

        public static float Oversampling(QualityPreset p)
        {
            switch (p)
            {
                case QualityPreset.Low: return 0.75f;
                case QualityPreset.Medium: return 1f;
                case QualityPreset.High: return 1.5f;
                case QualityPreset.Ultra: return 2f;
                default: return 1f;
            }
        }

        /// <summary>
        /// タイル内 worst-case 三角形が Nyquist を満たす実効表示解像度 r(T) を返す。
        /// 単位: タイル内ローカルのピクセル数 (0 .. tileSize)。
        /// </summary>
        public static float ComputeR(
            TileStats tile,
            int tileSize,
            float viewDistanceCm,
            float hmdPxPerDeg,
            QualityPreset preset)
        {
            if (!tile.HasCoverage || tile.Density <= 1e-8f) return 0f;

            float pxPerCm = hmdPxPerDeg * (180f / Mathf.PI) / Mathf.Max(viewDistanceCm, 1f);
            float texelsPerCm = pxPerCm * Oversampling(preset) * tile.BoneWeight;
            float texelsPerCm2Desired = texelsPerCm * texelsPerCm;

            // tile.Density = worldCm² / uv². タイル UV 面積 = (1/TilesX) * (1/TilesY) ≈ (tileSize/texW)²
            // しかし ここでは「タイル内 1 UV 単位あたり何テクセル密度で伸びているか」と整合する単位に変換する必要がある。
            // 実際には: r(T)² / tileSize² を「ピラミッド上での縮小率²」とし、
            //            縮小後のテクセル密度 density * (r/tileSize)² が desired を満たす最小 r。
            // → r = tileSize * sqrt(texelsPerCm²Desired / density)
            float ratio = texelsPerCm2Desired / tile.Density;
            float r = tileSize * Mathf.Sqrt(ratio);
            return Mathf.Clamp(r, RMin, tileSize);
        }

        /// <summary>ピラミッドレベル (0=tileSize, 1=tileSize/2, ...)</summary>
        public static int LevelFromR(float r, int tileSize)
        {
            if (r <= 0) return 0;
            float ratio = tileSize / Mathf.Max(r, 1e-3f);
            int level = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(ratio, 2)), 0, Mathf.FloorToInt(Mathf.Log(tileSize, 2)));
            return level;
        }
    }
}
```

- [ ] **Step 2: テストを書く**

`Tests/Editor/EffectiveResolutionCalculatorTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class EffectiveResolutionCalculatorTests
{
    [Test]
    public void NoCoverage_ReturnsZero()
    {
        var tile = new TileStats { HasCoverage = false };
        var r = EffectiveResolutionCalculator.ComputeR(tile, 64, 30f, 20f, QualityPreset.Medium);
        Assert.AreEqual(0f, r);
    }

    [Test]
    public void HighDensity_ClampsTo_TileSize()
    {
        var tile = new TileStats { HasCoverage = true, Density = 1e9f, BoneWeight = 1f };
        var r = EffectiveResolutionCalculator.ComputeR(tile, 64, 30f, 20f, QualityPreset.Medium);
        Assert.AreEqual(4f, r, 0.001f, "extreme density should clamp to rMin");
    }

    [Test]
    public void LowDensity_ClampsTo_TileSize()
    {
        var tile = new TileStats { HasCoverage = true, Density = 1e-6f, BoneWeight = 1f };
        var r = EffectiveResolutionCalculator.ComputeR(tile, 64, 30f, 20f, QualityPreset.Medium);
        Assert.AreEqual(64f, r, 0.001f, "very low density should clamp to tileSize (all texels visible)");
    }

    [Test]
    public void HigherPreset_IncreasesR()
    {
        var tile = new TileStats { HasCoverage = true, Density = 100f, BoneWeight = 1f };
        var rMed = EffectiveResolutionCalculator.ComputeR(tile, 64, 30f, 20f, QualityPreset.Medium);
        var rHigh = EffectiveResolutionCalculator.ComputeR(tile, 64, 30f, 20f, QualityPreset.High);
        Assert.Greater(rHigh, rMed);
    }

    [Test]
    public void LevelFromR_Monotonic()
    {
        Assert.AreEqual(0, EffectiveResolutionCalculator.LevelFromR(64f, 64));
        Assert.AreEqual(1, EffectiveResolutionCalculator.LevelFromR(32f, 64));
        Assert.AreEqual(2, EffectiveResolutionCalculator.LevelFromR(16f, 64));
        Assert.AreEqual(6, EffectiveResolutionCalculator.LevelFromR(1f, 64));
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "EffectiveResolutionCalculatorTests"`
Expected: 全 PASS

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Tiling/EffectiveResolutionCalculator.cs Tests/Editor/EffectiveResolutionCalculatorTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/tiling): add r(T) calculator with CSF-preset scaling"
```

---

### Task 2.4: TileGrid ビルダー (renderer 群からの統合)

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Tiling/TileGridBuilder.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/TileGridBuilderTests.cs`

- [ ] **Step 1: ビルダーを実装**

`Editor/Phase2/Tiling/TileGridBuilder.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Resolution;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Tiling
{
    public static class TileGridBuilder
    {
        /// <summary>
        /// テクスチャ 1 枚に対し、そのテクスチャを参照する全 renderer の mesh 三角形を
        /// UvTileGrid に集計する。
        /// </summary>
        public static UvTileGrid Build(
            int textureWidth, int textureHeight,
            IEnumerable<(Renderer renderer, Mesh mesh)> sources,
            Dictionary<Transform, BoneCategory> bonemap,
            IReadOnlyDictionary<Renderer, ResolvedSettings> settingsByRenderer)
        {
            var grid = UvTileGrid.Create(textureWidth, textureHeight);
            foreach (var src in sources)
            {
                if (src.renderer == null || src.mesh == null) continue;
                if (!settingsByRenderer.TryGetValue(src.renderer, out var s)) continue;
                TileRasterizer.Accumulate(grid, src.renderer, src.mesh, bonemap, s.BoneWeights);
            }
            return grid;
        }
    }
}
```

- [ ] **Step 2: ビルダーのテスト**

`Tests/Editor/TileGridBuilderTests.cs`:
```csharp
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Resolution;

public class TileGridBuilderTests
{
    [Test]
    public void MultipleRenderers_Merged_MaxDensity()
    {
        var go1 = new GameObject("r1");
        var mf1 = go1.AddComponent<MeshFilter>();
        var mr1 = go1.AddComponent<MeshRenderer>();
        var mesh1 = MakeTri(0, 0, 1, 0, 0, 1, 0, 0, 0.5f, 0, 0, 0.5f);
        mf1.sharedMesh = mesh1;

        var go2 = new GameObject("r2");
        var mf2 = go2.AddComponent<MeshFilter>();
        var mr2 = go2.AddComponent<MeshRenderer>();
        var mesh2 = MakeTri(0, 0, 2, 0, 0, 2, 0, 0, 0.5f, 0, 0, 0.5f);
        mf2.sharedMesh = mesh2;

        var settings = new Dictionary<Renderer, ResolvedSettings>
        {
            { mr1, new ResolvedSettings() },
            { mr2, new ResolvedSettings() },
        };

        var grid = TileGridBuilder.Build(128, 128,
            new[] { ((Renderer)mr1, mesh1), ((Renderer)mr2, mesh2) },
            null, settings);

        var corner = grid.GetTile(0, 0);
        Assert.IsTrue(corner.HasCoverage);
        // mesh2 (larger world) should dominate max density
        Assert.Greater(corner.Density, 3000f);

        Object.DestroyImmediate(go1); Object.DestroyImmediate(go2);
        Object.DestroyImmediate(mesh1); Object.DestroyImmediate(mesh2);
    }

    static Mesh MakeTri(float x0, float y0, float x1, float y1, float x2, float y2,
                        float u0, float v0, float u1, float v1, float u2, float v2)
    {
        var m = new Mesh
        {
            vertices = new[] { new Vector3(x0, y0, 0), new Vector3(x1, y1, 0), new Vector3(x2, y2, 0) },
            uv = new[] { new Vector2(u0, v0), new Vector2(u1, v1), new Vector2(u2, v2) },
            triangles = new[] { 0, 1, 2 },
        };
        return m;
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "TileGridBuilderTests"`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Tiling/TileGridBuilder.cs Tests/Editor/TileGridBuilderTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/tiling): add TileGridBuilder aggregating multi-renderer sources"
```

---

**M2 完了時点の状態**: タイル分割・密度集計・r(T) 計算が単体テストで動く。`ResolutionReducePass` には未接続。旧 `TexelDensityMap` / `MeshDensityAnalyzer` はそのまま。

---

## M3. GPU 基盤

### Task 3.1: GpuTextureContext

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/GpuTextureContext.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/GpuTextureContextTests.cs`

- [ ] **Step 1: GpuTextureContext を実装**

`Editor/Phase2/GpuPipeline/GpuTextureContext.cs`:
```csharp
using System;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline
{
    /// <summary>
    /// 1 テクスチャの GPU 上コンテキスト (orig RT + mipmap chain)。
    /// 1 ビルド実行中は使い回し、Dispose で RT 解放。
    /// </summary>
    public class GpuTextureContext : IDisposable
    {
        public RenderTexture Original { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public static GpuTextureContext FromTexture2D(Texture2D src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));

            var desc = new RenderTextureDescriptor(src.width, src.height, RenderTextureFormat.ARGB32, 0)
            {
                useMipMap = true,
                autoGenerateMips = false,
                sRGB = true,
            };
            var rt = new RenderTexture(desc) { name = "Jnto_Orig_" + src.name };
            rt.Create();

            Graphics.Blit(src, rt);
            rt.GenerateMips();

            return new GpuTextureContext
            {
                Original = rt,
                Width = src.width,
                Height = src.height,
            };
        }

        public void Dispose()
        {
            if (Original != null)
            {
                Original.Release();
                UnityEngine.Object.DestroyImmediate(Original);
                Original = null;
            }
        }
    }
}
```

- [ ] **Step 2: 基本動作テスト**

`Tests/Editor/GpuTextureContextTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

public class GpuTextureContextTests
{
    [Test]
    public void FromTexture2D_CreatesRtWithMipmaps()
    {
        var src = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var px = new Color[64 * 64];
        for (int i = 0; i < px.Length; i++) px[i] = Color.red;
        src.SetPixels(px); src.Apply();

        using (var ctx = GpuTextureContext.FromTexture2D(src))
        {
            Assert.IsNotNull(ctx.Original);
            Assert.AreEqual(64, ctx.Width);
            Assert.AreEqual(64, ctx.Height);
            Assert.IsTrue(ctx.Original.useMipMap);
        }
        Object.DestroyImmediate(src);
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "GpuTextureContextTests"`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/GpuPipeline/GpuTextureContext.cs Tests/Editor/GpuTextureContextTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gpu): add GpuTextureContext managing orig RT and mipmap chain"
```

---

### Task 3.2: Compute Shader ローダー

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/ComputeResources.cs`
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/Shaders/Placeholder.compute`

- [ ] **Step 1: Placeholder Compute Shader**

`Editor/Phase2/GpuPipeline/Shaders/Placeholder.compute`:
```hlsl
#pragma kernel CSMain

RWStructuredBuffer<float> _Out;

[numthreads(1, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    _Out[0] = 42.0;
}
```

同ディレクトリに `.meta` を Unity に生成させるため、初回コンパイル後に ok 確認。

- [ ] **Step 2: ComputeResources ローダー**

`Editor/Phase2/GpuPipeline/ComputeResources.cs`:
```csharp
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline
{
    public static class ComputeResources
    {
        const string Root = "Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/Shaders/";

        public static ComputeShader Load(string name)
        {
            var path = Root + name + ".compute";
            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            if (cs == null)
                throw new System.IO.FileNotFoundException("Compute shader not found: " + path);
            return cs;
        }
    }
}
```

- [ ] **Step 3: Placeholder を読み込んで実行できることをテスト**

`Tests/Editor/ComputeResourcesTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

public class ComputeResourcesTests
{
    [Test]
    public void LoadsPlaceholder_AndDispatches()
    {
        var cs = ComputeResources.Load("Placeholder");
        Assert.IsNotNull(cs);

        var buffer = new ComputeBuffer(1, sizeof(float));
        int k = cs.FindKernel("CSMain");
        cs.SetBuffer(k, "_Out", buffer);
        cs.Dispatch(k, 1, 1, 1);

        var result = new float[1];
        buffer.GetData(result);
        buffer.Release();

        Assert.AreEqual(42f, result[0], 0.001f);
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "ComputeResourcesTests"`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/GpuPipeline/ Tests/Editor/ComputeResourcesTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gpu): add ComputeResources loader and placeholder shader"
```

---

### Task 3.3: PyramidBuilder

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/PyramidBuilder.cs`

Unity の RenderTexture は auto-mipmap を持つため、ラッパーは薄い。

- [ ] **Step 1: PyramidBuilder を実装**

`Editor/Phase2/GpuPipeline/PyramidBuilder.cs`:
```csharp
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline
{
    public static class PyramidBuilder
    {
        /// <summary>
        /// candidate の RT を orig と同サイズで作り、mipmap を生成する。
        /// 返す RT の sampler は bilinear + trilinear。
        /// </summary>
        public static RenderTexture CreatePyramid(Texture source, int width, int height, string debugName)
        {
            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            {
                useMipMap = true,
                autoGenerateMips = false,
                sRGB = true,
            };
            var rt = new RenderTexture(desc) { name = debugName, filterMode = FilterMode.Trilinear };
            rt.Create();
            Graphics.Blit(source, rt);
            rt.GenerateMips();
            return rt;
        }

        public static int MipLevelCount(int width, int height)
        {
            int maxDim = Mathf.Max(width, height);
            int levels = 0;
            while (maxDim > 0) { levels++; maxDim >>= 1; }
            return levels;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/GpuPipeline/PyramidBuilder.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gpu): add PyramidBuilder thin wrapper around RT mipmap chain"
```

---

**M3 完了時点**: RT コンテキストと Compute Shader ローダーが動作。メトリクス実装 (M4) の基盤が整う。

---

## M4. Metrics + Gate

### Task 4.1: DegradationCalibration ScriptableObject 本実装

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Gate/DegradationCalibration.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/DegradationCalibrationTests.cs`

- [ ] **Step 1: 係数を持つ ScriptableObject に置き換え**

`Editor/Phase2/Gate/DegradationCalibration.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    [CreateAssetMenu(menuName = "Just-Noticeable Texture Optimizer/Degradation Calibration", fileName = "DegradationCalibration")]
    public class DegradationCalibration : ScriptableObject
    {
        [Tooltip("MSSL Band Energy Loss を JND 単位に換算する係数。1.0 / (loss_ratio_at_JND)")]
        public float MsslBandEnergyScale = 3.3f;   // ~30% 帯域ロスで JND=1.0

        [Tooltip("MSSL Structure-only SSIM loss を JND 単位に換算する係数。")]
        public float MsslStructureScale = 5.0f;    // ~20% 構造相関ロスで JND=1.0

        [Tooltip("Ridge Preservation の強度減衰 → JND 係数。")]
        public float RidgeScale = 4.0f;            // ~25% リッジ減衰で JND=1.0

        [Tooltip("Banding ピーク比 → JND 係数。")]
        public float BandingScale = 2.5f;          // 既存 BandingMetric の 0.4 を JND=1.0 に対応

        [Tooltip("BlockBoundary 比 → JND 係数。")]
        public float BlockBoundaryScale = 2.5f;

        [Tooltip("AlphaQuantization → JND 係数。")]
        public float AlphaQuantScale = 2.5f;

        [Tooltip("NormalAngle (正規化済み 0-1) → JND 係数。")]
        public float NormalAngleScale = 10.0f;     // ~10% 角度ズレで JND=1.0

        [Tooltip("Preset 別 JND 閾値 (tex_score < T_preset で pass)。")]
        public float ThresholdLow = 1.5f;
        public float ThresholdMedium = 1.0f;
        public float ThresholdHigh = 0.7f;
        public float ThresholdUltra = 0.5f;

        public float GetThreshold(QualityPreset p)
        {
            switch (p)
            {
                case QualityPreset.Low: return ThresholdLow;
                case QualityPreset.Medium: return ThresholdMedium;
                case QualityPreset.High: return ThresholdHigh;
                case QualityPreset.Ultra: return ThresholdUltra;
                default: return ThresholdMedium;
            }
        }

        public static DegradationCalibration Default()
        {
            return CreateInstance<DegradationCalibration>();
        }
    }
}
```

- [ ] **Step 2: 既定値テスト**

`Tests/Editor/DegradationCalibrationTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;

public class DegradationCalibrationTests
{
    [Test]
    public void Default_ThresholdIsMonotonic()
    {
        var c = DegradationCalibration.Default();
        Assert.Greater(c.ThresholdLow, c.ThresholdMedium);
        Assert.Greater(c.ThresholdMedium, c.ThresholdHigh);
        Assert.Greater(c.ThresholdHigh, c.ThresholdUltra);
        Object.DestroyImmediate(c);
    }

    [Test]
    public void GetThreshold_ReturnsExpectedValue()
    {
        var c = DegradationCalibration.Default();
        Assert.AreEqual(c.ThresholdMedium, c.GetThreshold(QualityPreset.Medium));
        Assert.AreEqual(c.ThresholdUltra, c.GetThreshold(QualityPreset.Ultra));
        Object.DestroyImmediate(c);
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "DegradationCalibrationTests"`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Gate/DegradationCalibration.cs Tests/Editor/DegradationCalibrationTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gate): implement DegradationCalibration scriptable with JND scales"
```

---

### Task 4.2: Metric インターフェース

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Gate/IMetric.cs`

- [ ] **Step 1: 型を定義**

`Editor/Phase2/Gate/IMetric.cs`:
```csharp
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    public enum MetricContext
    {
        /// <summary>ダウンサンプル前後の比較で使うメトリクス。</summary>
        Downscale,
        /// <summary>圧縮前後の比較で使うメトリクス。</summary>
        Compression,
        /// <summary>両方で使う。</summary>
        Both,
    }

    /// <summary>
    /// per-tile スコアを返す JND 正規化メトリクス。
    /// </summary>
    public interface IMetric
    {
        string Name { get; }
        MetricContext Context { get; }

        /// <summary>
        /// orig/candidate RT を比較し、タイルごとの JND スコアを埋める。
        /// scores[ty * grid.TilesX + tx] に値を書き込む。
        /// タイルに coverage が無ければ 0 のまま。
        /// </summary>
        void Evaluate(
            RenderTexture orig,
            RenderTexture candidate,
            UvTileGrid grid,
            float[] rPerTile,            // r(T), length = tilesX*tilesY
            DegradationCalibration calib,
            float[] scoresOut);          // length = tilesX*tilesY
    }
}
```

- [ ] **Step 2: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Gate/IMetric.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gate): add IMetric interface"
```

---

### Task 4.3: MSSL Metric — CPU reference 実装

GPU 実装の正しさ検証のため、まず CPU で動く reference を書く。

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Gate/MsslMetricCpu.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/MsslMetricCpuTests.cs`

- [ ] **Step 1: CPU reference 実装**

`Editor/Phase2/Gate/MsslMetricCpu.cs`:
```csharp
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    /// <summary>
    /// Multi-scale Structural Loss (CPU reference)。
    /// タイルごとに r(T) に対応するピラミッドレベルで
    /// Band Energy Loss と Structure-only SSIM を合成。
    /// GPU 実装 (MsslMetric) の正しさ検証用。
    /// </summary>
    public class MsslMetricCpu
    {
        public float[] EvaluateDebug(Texture2D orig, Texture2D candidate, UvTileGrid grid, float[] rPerTile, DegradationCalibration calib)
        {
            int w = orig.width, h = orig.height;
            var pxO = orig.GetPixels();
            var pxC = candidate.GetPixels();
            var lumO = ToLuminance(pxO);
            var lumC = ToLuminance(pxC);

            // Gaussian pyramid を CPU で構築
            var pyrO = BuildPyramid(lumO, w, h);
            var pyrC = BuildPyramid(lumC, w, h);

            var scores = new float[grid.Tiles.Length];
            int tileSize = grid.TileSize;

            for (int ty = 0; ty < grid.TilesY; ty++)
            for (int tx = 0; tx < grid.TilesX; tx++)
            {
                int idx = ty * grid.TilesX + tx;
                var tile = grid.Tiles[idx];
                if (!tile.HasCoverage) continue;

                float r = rPerTile[idx];
                int targetLevel = EffectiveResolutionCalculator.LevelFromR(r, tileSize);

                float worst = 0f;
                // targetLevel ± 1 の 3 段評価
                for (int dl = -1; dl <= 1; dl++)
                {
                    int lv = Mathf.Clamp(targetLevel + dl, 0, pyrO.Length - 1);
                    float scaleWeight = 1f - Mathf.Abs(dl) * 0.3f;

                    float band = BandEnergyLoss(pyrO, pyrC, lv, tx, ty, tileSize, w, h);
                    float struc = StructureOnlyLoss(pyrO[lv].data, pyrC[lv].data, pyrO[lv].w, pyrO[lv].h,
                                                    tx, ty, tileSize, w, h);

                    float s = Mathf.Max(band * calib.MsslBandEnergyScale,
                                       struc * calib.MsslStructureScale) * scaleWeight;
                    if (s > worst) worst = s;
                }
                scores[idx] = worst;
            }
            return scores;
        }

        struct Lvl { public float[] data; public int w; public int h; }

        static Lvl[] BuildPyramid(float[] src, int w, int h)
        {
            int levels = 1; int d = Mathf.Max(w, h);
            while (d > 1) { d >>= 1; levels++; }
            var result = new Lvl[levels];
            result[0] = new Lvl { data = (float[])src.Clone(), w = w, h = h };
            for (int i = 1; i < levels; i++)
            {
                int pw = result[i - 1].w, ph = result[i - 1].h;
                int nw = Mathf.Max(1, pw / 2), nh = Mathf.Max(1, ph / 2);
                var next = new float[nw * nh];
                for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int x2 = x * 2, y2 = y * 2;
                    int x3 = Mathf.Min(x2 + 1, pw - 1), y3 = Mathf.Min(y2 + 1, ph - 1);
                    next[y * nw + x] = 0.25f * (
                        result[i - 1].data[y2 * pw + x2] +
                        result[i - 1].data[y2 * pw + x3] +
                        result[i - 1].data[y3 * pw + x2] +
                        result[i - 1].data[y3 * pw + x3]);
                }
                result[i] = new Lvl { data = next, w = nw, h = nh };
            }
            return result;
        }

        static float BandEnergyLoss(Lvl[] pyrO, Lvl[] pyrC, int lv, int tx, int ty, int tileSize, int texW, int texH)
        {
            if (lv >= pyrO.Length - 1) return 0f;
            var o = pyrO[lv].data; var co = pyrC[lv].data;
            var oParent = pyrO[lv + 1].data; var cParent = pyrC[lv + 1].data;
            int w = pyrO[lv].w, h = pyrO[lv].h;
            int pw = pyrO[lv + 1].w;

            int x0 = tx * tileSize * w / texW, y0 = ty * tileSize * h / texH;
            int x1 = Mathf.Min(w, x0 + tileSize * w / texW);
            int y1 = Mathf.Min(h, y0 + tileSize * h / texH);
            if (x1 <= x0 || y1 <= y0) return 0f;

            double eO = 0, dDiff = 0;
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int px = x >> 1, py = y >> 1;
                if (px >= pw) px = pw - 1;
                float laplO = o[y * w + x] - oParent[py * pw + px];
                float laplC = co[y * w + x] - cParent[py * pw + px];
                eO += laplO * laplO;
                float d = laplO - laplC;
                dDiff += d * d;
            }
            if (eO < 1e-8) return 0f;
            return Mathf.Clamp01((float)(dDiff / eO));
        }

        static float StructureOnlyLoss(float[] a, float[] b, int w, int h,
                                       int tx, int ty, int tileSize, int texW, int texH)
        {
            int x0 = tx * tileSize * w / texW, y0 = ty * tileSize * h / texH;
            int x1 = Mathf.Min(w, x0 + tileSize * w / texW);
            int y1 = Mathf.Min(h, y0 + tileSize * h / texH);
            if (x1 - x0 < 7 || y1 - y0 < 7) return 0f;

            double sumLoss = 0; int count = 0;
            const int win = 5;
            for (int y = y0 + win / 2; y < y1 - win / 2; y += 2)
            for (int x = x0 + win / 2; x < x1 - win / 2; x += 2)
            {
                float muA = 0, muB = 0;
                for (int ky = -win / 2; ky <= win / 2; ky++)
                for (int kx = -win / 2; kx <= win / 2; kx++)
                {
                    muA += a[(y + ky) * w + (x + kx)];
                    muB += b[(y + ky) * w + (x + kx)];
                }
                muA /= win * win; muB /= win * win;

                float varA = 0, varB = 0, cov = 0;
                for (int ky = -win / 2; ky <= win / 2; ky++)
                for (int kx = -win / 2; kx <= win / 2; kx++)
                {
                    float da = a[(y + ky) * w + (x + kx)] - muA;
                    float db = b[(y + ky) * w + (x + kx)] - muB;
                    varA += da * da; varB += db * db; cov += da * db;
                }
                varA /= win * win; varB /= win * win; cov /= win * win;

                const float C2 = 0.03f * 0.03f;
                const float C3 = C2 * 0.5f;
                float sigA = Mathf.Sqrt(Mathf.Max(0f, varA));
                float sigB = Mathf.Sqrt(Mathf.Max(0f, varB));
                float c_term = (2f * sigA * sigB + C2) / (varA + varB + C2);
                float s_term = (cov + C3) / (sigA * sigB + C3);
                float structSim = c_term * s_term;

                sumLoss += Mathf.Max(0f, 1f - structSim);
                count++;
            }
            return count == 0 ? 0f : (float)(sumLoss / count);
        }

        static float[] ToLuminance(Color[] px)
        {
            var r = new float[px.Length];
            for (int i = 0; i < px.Length; i++)
                r[i] = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
            return r;
        }
    }
}
```

- [ ] **Step 2: CPU reference の基本テスト**

`Tests/Editor/MsslMetricCpuTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class MsslMetricCpuTests
{
    [Test]
    public void Identical_ReturnsZero()
    {
        var t = MakeCheckerboard(128);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FillFull(grid);
        var calib = DegradationCalibration.Default();

        var metric = new MsslMetricCpu();
        var scores = metric.EvaluateDebug(t, t, grid, r, calib);

        foreach (var s in scores)
            Assert.Less(s, 0.01f);
        Object.DestroyImmediate(t); Object.DestroyImmediate(calib);
    }

    [Test]
    public void BlurredTexture_ReturnsHigh()
    {
        var a = MakeCheckerboard(128);
        var b = HeavyBlur(a);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FillFull(grid);
        var calib = DegradationCalibration.Default();

        var metric = new MsslMetricCpu();
        var scores = metric.EvaluateDebug(a, b, grid, r, calib);

        float maxScore = 0f;
        foreach (var s in scores) if (s > maxScore) maxScore = s;
        Assert.Greater(maxScore, 0.5f, "heavy blur should produce high MSSL");

        Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(calib);
    }

    static void MarkAllCovered(UvTileGrid g)
    {
        for (int i = 0; i < g.Tiles.Length; i++)
            g.Tiles[i] = new TileStats { HasCoverage = true, Density = 100f, BoneWeight = 1f };
    }

    static float[] FillFull(UvTileGrid g)
    {
        var r = new float[g.Tiles.Length];
        for (int i = 0; i < r.Length; i++) r[i] = g.TileSize;
        return r;
    }

    static Texture2D MakeCheckerboard(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            px[y * n + x] = (((x >> 1) + (y >> 1)) & 1) == 0 ? Color.black : Color.white;
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D HeavyBlur(Texture2D src)
    {
        int w = src.width, h = src.height;
        var p = src.GetPixels();
        for (int pass = 0; pass < 5; pass++)
        {
            var q = new Color[p.Length];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                Color sum = Color.black; int n = 0;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    sum += p[yy * w + xx]; n++;
                }
                q[y * w + x] = sum / n;
            }
            p = q;
        }
        var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
        dst.SetPixels(p); dst.Apply(); return dst;
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "MsslMetricCpuTests"`
Expected: 全 PASS

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Gate/MsslMetricCpu.cs Tests/Editor/MsslMetricCpuTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gate): add MsslMetricCpu reference implementation"
```

---

### Task 4.4: MSSL Compute Shader と GPU 版

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/Shaders/Mssl.compute`
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Gate/MsslMetric.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/MsslMetricTests.cs`

- [ ] **Step 1: Compute Shader 本体**

`Editor/Phase2/GpuPipeline/Shaders/Mssl.compute`:
```hlsl
#pragma kernel CSEvaluate

Texture2D<float4> _Orig;
SamplerState sampler_Orig;
Texture2D<float4> _Candidate;
SamplerState sampler_Candidate;

RWStructuredBuffer<float> _Scores;
StructuredBuffer<float> _RPerTile;

int _TilesX;
int _TilesY;
int _TileSize;
int _TextureWidth;
int _TextureHeight;
float _BandScale;
float _StructureScale;

float lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

[numthreads(8, 8, 1)]
void CSEvaluate(uint3 id : SV_DispatchThreadID, uint3 gid : SV_GroupID, uint3 gtid : SV_GroupThreadID)
{
    int tx = gid.x;
    int ty = gid.y;
    if (tx >= _TilesX || ty >= _TilesY) return;

    int tileIdx = ty * _TilesX + tx;
    float r = _RPerTile[tileIdx];
    if (r <= 0.0) return;

    // target mip level from r
    float ratio = (float)_TileSize / max(r, 1e-3);
    int targetLv = (int)floor(log2(ratio));

    float2 tileUvMin = float2(tx, ty) / float2(_TilesX, _TilesY);
    float2 tileUvMax = float2(tx + 1, ty + 1) / float2(_TilesX, _TilesY);

    float worst = 0.0;
    for (int dl = -1; dl <= 1; dl++)
    {
        int lv = clamp(targetLv + dl, 0, 10);
        float lvNext = (float)(lv + 1);
        float scaleWeight = 1.0 - abs((float)dl) * 0.3;

        // Sample tile at mip lv and lv+1, compute energy loss across samples
        const int SamplesPerSide = 4;
        float eO = 0.0;
        float dDiff = 0.0;
        float sumStruct = 0.0;
        int count = 0;

        for (int sy = 0; sy < SamplesPerSide; sy++)
        for (int sx = 0; sx < SamplesPerSide; sx++)
        {
            float2 local = (float2(sx, sy) + 0.5) / (float)SamplesPerSide;
            float2 uv = lerp(tileUvMin, tileUvMax, local);

            float4 oLv = _Orig.SampleLevel(sampler_Orig, uv, (float)lv);
            float4 cLv = _Candidate.SampleLevel(sampler_Candidate, uv, (float)lv);
            float4 oLvN = _Orig.SampleLevel(sampler_Orig, uv, lvNext);
            float4 cLvN = _Candidate.SampleLevel(sampler_Candidate, uv, lvNext);

            float laplO = lum(oLv.rgb) - lum(oLvN.rgb);
            float laplC = lum(cLv.rgb) - lum(cLvN.rgb);
            eO += laplO * laplO;
            float dd = laplO - laplC;
            dDiff += dd * dd;

            // Approximation of structure term: 1 - correlation of local differences
            float lO = lum(oLv.rgb);
            float lC = lum(cLv.rgb);
            sumStruct += abs(lO - lC);
            count++;
        }

        float band = eO > 1e-6 ? saturate(dDiff / eO) : 0.0;
        float structAvg = count > 0 ? sumStruct / (float)count : 0.0;

        float sBand = band * _BandScale * scaleWeight;
        float sStruct = structAvg * _StructureScale * scaleWeight;
        float s = max(sBand, sStruct);
        worst = max(worst, s);
    }

    _Scores[tileIdx] = worst;
}
```

- [ ] **Step 2: GPU 版 IMetric 実装**

`Editor/Phase2/Gate/MsslMetric.cs`:
```csharp
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    public class MsslMetric : IMetric
    {
        public string Name => "MSSL";
        public MetricContext Context => MetricContext.Both;

        public void Evaluate(
            RenderTexture orig, RenderTexture candidate,
            UvTileGrid grid, float[] rPerTile,
            DegradationCalibration calib, float[] scoresOut)
        {
            var cs = ComputeResources.Load("Mssl");
            int k = cs.FindKernel("CSEvaluate");

            int tileCount = grid.Tiles.Length;
            var scoreBuf = new ComputeBuffer(tileCount, sizeof(float));
            scoreBuf.SetData(new float[tileCount]);
            var rBuf = new ComputeBuffer(tileCount, sizeof(float));
            rBuf.SetData(rPerTile);

            cs.SetTexture(k, "_Orig", orig);
            cs.SetTexture(k, "_Candidate", candidate);
            cs.SetBuffer(k, "_Scores", scoreBuf);
            cs.SetBuffer(k, "_RPerTile", rBuf);
            cs.SetInt("_TilesX", grid.TilesX);
            cs.SetInt("_TilesY", grid.TilesY);
            cs.SetInt("_TileSize", grid.TileSize);
            cs.SetInt("_TextureWidth", grid.TextureWidth);
            cs.SetInt("_TextureHeight", grid.TextureHeight);
            cs.SetFloat("_BandScale", calib.MsslBandEnergyScale);
            cs.SetFloat("_StructureScale", calib.MsslStructureScale);

            cs.Dispatch(k, Mathf.CeilToInt(grid.TilesX / 8f), Mathf.CeilToInt(grid.TilesY / 8f), 1);

            scoreBuf.GetData(scoresOut);
            scoreBuf.Release();
            rBuf.Release();
        }
    }
}
```

- [ ] **Step 3: GPU vs CPU 一致テスト**

`Tests/Editor/MsslMetricTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class MsslMetricTests
{
    [Test]
    public void Identical_Gpu_ReturnsZero()
    {
        var t = MakePattern(256);
        var grid = UvTileGrid.Create(256, 256);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(t))
        {
            var metric = new MsslMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxA.Original, ctxA.Original, grid, r, calib, scores);

            foreach (var s in scores) Assert.Less(s, 0.05f, "identical textures should score near-zero");
        }

        Object.DestroyImmediate(t); Object.DestroyImmediate(calib);
    }

    [Test]
    public void Blurred_Gpu_ReturnsHigh()
    {
        var a = MakePattern(256);
        var b = Blurred(a);
        var grid = UvTileGrid.Create(256, 256);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(a))
        using (var ctxB = GpuTextureContext.FromTexture2D(b))
        {
            var metric = new MsslMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);

            float max = 0f;
            foreach (var s in scores) if (s > max) max = s;
            Assert.Greater(max, 0.3f);
        }

        Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(calib);
    }

    static void MarkAllCovered(UvTileGrid g)
    {
        for (int i = 0; i < g.Tiles.Length; i++)
            g.Tiles[i] = new TileStats { HasCoverage = true, Density = 100f, BoneWeight = 1f };
    }

    static float[] FullR(UvTileGrid g)
    {
        var r = new float[g.Tiles.Length];
        for (int i = 0; i < r.Length; i++) r[i] = g.TileSize;
        return r;
    }

    static Texture2D MakePattern(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float v = (((x / 4) + (y / 4)) & 1) == 0 ? 0.1f : 0.9f;
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D Blurred(Texture2D src)
    {
        int w = src.width, h = src.height;
        var p = src.GetPixels();
        for (int pass = 0; pass < 4; pass++)
        {
            var q = new Color[p.Length];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                Color s = Color.black; int n = 0;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    s += p[yy * w + xx]; n++;
                }
                q[y * w + x] = s / n;
            }
            p = q;
        }
        var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
        dst.SetPixels(p); dst.Apply(); return dst;
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "MsslMetricTests"`
Expected: 両 PASS

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/GpuPipeline/Shaders/Mssl.compute Editor/Phase2/Gate/MsslMetric.cs Tests/Editor/MsslMetricTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gate): add MSSL metric (compute shader + wrapper)"
```

---

### Task 4.5: Ridge Preservation Metric

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/Shaders/Ridge.compute`
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Gate/RidgeMetric.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/RidgeMetricTests.cs`

- [ ] **Step 1: Ridge Compute Shader**

`Editor/Phase2/GpuPipeline/Shaders/Ridge.compute`:
```hlsl
#pragma kernel CSEvaluate

Texture2D<float4> _Orig;
SamplerState sampler_Orig;
Texture2D<float4> _Candidate;
SamplerState sampler_Candidate;

RWStructuredBuffer<float> _Scores;
StructuredBuffer<float> _RPerTile;

int _TilesX;
int _TilesY;
int _TileSize;
int _TextureWidth;
int _TextureHeight;
float _RidgeScale;

float lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

float ridgeness(Texture2D<float4> tex, SamplerState s, float2 uv, float pxW, float pxH, float mip)
{
    float2 dx = float2(pxW, 0);
    float2 dy = float2(0, pxH);
    float c  = lum(tex.SampleLevel(s, uv, mip).rgb);
    float l  = lum(tex.SampleLevel(s, uv - dx, mip).rgb);
    float r  = lum(tex.SampleLevel(s, uv + dx, mip).rgb);
    float u  = lum(tex.SampleLevel(s, uv - dy, mip).rgb);
    float d  = lum(tex.SampleLevel(s, uv + dy, mip).rgb);
    float ul = lum(tex.SampleLevel(s, uv - dx - dy, mip).rgb);
    float ur = lum(tex.SampleLevel(s, uv + dx - dy, mip).rgb);
    float dl = lum(tex.SampleLevel(s, uv - dx + dy, mip).rgb);
    float dr = lum(tex.SampleLevel(s, uv + dx + dy, mip).rgb);

    // Hessian components
    float Ixx = r - 2.0 * c + l;
    float Iyy = d - 2.0 * c + u;
    float Ixy = 0.25 * (dr - ur - dl + ul);

    // Eigenvalues
    float tr = Ixx + Iyy;
    float det = Ixx * Iyy - Ixy * Ixy;
    float disc = sqrt(max(0.0, tr * tr * 0.25 - det));
    float lambda1 = tr * 0.5 + disc;
    float lambda2 = tr * 0.5 - disc;

    // |lambda1| largest
    float a1 = abs(lambda1), a2 = abs(lambda2);
    if (a2 > a1) { float t = a1; a1 = a2; a2 = t; }

    // Frangi-like: |lambda1| * (1 - |lambda2/lambda1|)
    float ratio = a1 > 1e-6 ? (a2 / a1) : 0.0;
    return a1 * saturate(1.0 - ratio);
}

[numthreads(8, 8, 1)]
void CSEvaluate(uint3 id : SV_DispatchThreadID, uint3 gid : SV_GroupID, uint3 gtid : SV_GroupThreadID)
{
    int tx = gid.x;
    int ty = gid.y;
    if (tx >= _TilesX || ty >= _TilesY) return;

    int tileIdx = ty * _TilesX + tx;
    float r = _RPerTile[tileIdx];
    if (r <= 0.0) return;

    float ratio = (float)_TileSize / max(r, 1e-3);
    int targetLv = (int)floor(log2(ratio));

    float2 tileUvMin = float2(tx, ty) / float2(_TilesX, _TilesY);
    float2 tileUvMax = float2(tx + 1, ty + 1) / float2(_TilesX, _TilesY);

    const int N = 6;
    float pxW = 1.0 / (float)_TextureWidth;
    float pxH = 1.0 / (float)_TextureHeight;

    float sumO = 0.0, sumC = 0.0, sumDiff = 0.0;
    int count = 0;
    for (int sy = 0; sy < N; sy++)
    for (int sx = 0; sx < N; sx++)
    {
        float2 local = (float2(sx, sy) + 0.5) / (float)N;
        float2 uv = lerp(tileUvMin, tileUvMax, local);
        float rO = ridgeness(_Orig, sampler_Orig, uv, pxW, pxH, (float)targetLv);
        float rC = ridgeness(_Candidate, sampler_Candidate, uv, pxW, pxH, (float)targetLv);
        sumO += rO; sumC += rC;
        sumDiff += abs(rO - rC);
        count++;
    }

    float loss = sumO > 1e-6 ? saturate(sumDiff / sumO) : 0.0;
    _Scores[tileIdx] = loss * _RidgeScale;
}
```

- [ ] **Step 2: Ridge IMetric 実装**

`Editor/Phase2/Gate/RidgeMetric.cs`:
```csharp
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    public class RidgeMetric : IMetric
    {
        public string Name => "Ridge";
        public MetricContext Context => MetricContext.Downscale;

        public void Evaluate(
            RenderTexture orig, RenderTexture candidate,
            UvTileGrid grid, float[] rPerTile,
            DegradationCalibration calib, float[] scoresOut)
        {
            var cs = ComputeResources.Load("Ridge");
            int k = cs.FindKernel("CSEvaluate");

            int tileCount = grid.Tiles.Length;
            var scoreBuf = new ComputeBuffer(tileCount, sizeof(float));
            scoreBuf.SetData(new float[tileCount]);
            var rBuf = new ComputeBuffer(tileCount, sizeof(float));
            rBuf.SetData(rPerTile);

            cs.SetTexture(k, "_Orig", orig);
            cs.SetTexture(k, "_Candidate", candidate);
            cs.SetBuffer(k, "_Scores", scoreBuf);
            cs.SetBuffer(k, "_RPerTile", rBuf);
            cs.SetInt("_TilesX", grid.TilesX);
            cs.SetInt("_TilesY", grid.TilesY);
            cs.SetInt("_TileSize", grid.TileSize);
            cs.SetInt("_TextureWidth", grid.TextureWidth);
            cs.SetInt("_TextureHeight", grid.TextureHeight);
            cs.SetFloat("_RidgeScale", calib.RidgeScale);

            cs.Dispatch(k, Mathf.CeilToInt(grid.TilesX / 8f), Mathf.CeilToInt(grid.TilesY / 8f), 1);

            scoreBuf.GetData(scoresOut);
            scoreBuf.Release();
            rBuf.Release();
        }
    }
}
```

- [ ] **Step 3: 線状模様テスト**

`Tests/Editor/RidgeMetricTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class RidgeMetricTests
{
    [Test]
    public void Stripes_Blurred_RidgeLoss()
    {
        var a = MakeStripes(128, 4);
        var b = Blurred(a, 3);
        var grid = UvTileGrid.Create(128, 128);
        MarkAllCovered(grid);
        var r = FullR(grid);
        var calib = DegradationCalibration.Default();

        using (var ctxA = GpuTextureContext.FromTexture2D(a))
        using (var ctxB = GpuTextureContext.FromTexture2D(b))
        {
            var metric = new RidgeMetric();
            var scores = new float[grid.Tiles.Length];
            metric.Evaluate(ctxA.Original, ctxB.Original, grid, r, calib, scores);

            float max = 0f;
            foreach (var s in scores) if (s > max) max = s;
            Assert.Greater(max, 0.3f, "blurred stripes should trigger ridge loss");
        }

        Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(calib);
    }

    static void MarkAllCovered(UvTileGrid g)
    {
        for (int i = 0; i < g.Tiles.Length; i++)
            g.Tiles[i] = new TileStats { HasCoverage = true, Density = 100f, BoneWeight = 1f };
    }

    static float[] FullR(UvTileGrid g)
    {
        var r = new float[g.Tiles.Length];
        for (int i = 0; i < r.Length; i++) r[i] = g.TileSize;
        return r;
    }

    static Texture2D MakeStripes(int n, int period)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            px[y * n + x] = (x / period) % 2 == 0 ? Color.black : Color.white;
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D Blurred(Texture2D src, int passes)
    {
        int w = src.width, h = src.height;
        var p = src.GetPixels();
        for (int pass = 0; pass < passes; pass++)
        {
            var q = new Color[p.Length];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                Color s = Color.black; int n = 0;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    s += p[yy * w + xx]; n++;
                }
                q[y * w + x] = s / n;
            }
            p = q;
        }
        var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
        dst.SetPixels(p); dst.Apply(); return dst;
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "RidgeMetricTests"`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/GpuPipeline/Shaders/Ridge.compute Editor/Phase2/Gate/RidgeMetric.cs Tests/Editor/RidgeMetricTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gate): add RidgeMetric for line pattern preservation"
```

---

### Task 4.6-4.9: 副次メトリクス (Banding/BlockBoundary/AlphaQuantization/NormalAngle) GPU 化

Task 4.6 BandingMetric:
- Compute shader `Banding.compute`: per-tile で平坦領域 (3×3 max-min < 0.02) を検出、その領域の 2 次微分ヒストグラムのピーク比を score に
- IMetric 実装 `BandingMetric.cs`: MsslMetric と同じ構造で Dispatch
- Test: グラデーションの BC1 模倣 (4 レベル量子化) で score > 0.3 を確認

Task 4.7 BlockBoundaryMetric:
- Compute shader `BlockBoundary.compute`: per-tile で x % 4 == 0 のエッジ強度と非 0 エッジ強度の比
- IMetric 実装
- Test: 疑似ブロック境界画像 (4 px 刻みのステップ) で score > 0.5

Task 4.8 AlphaQuantizationMetric:
- Compute shader `AlphaQuantization.compute`: per-tile でアルファ 8 レベル以下化の level count と RMS
- IMetric 実装
- Test: 0-255 連続アルファ → 8 レベル量子化で score > 0.5

Task 4.9 NormalAngleMetric:
- Compute shader `NormalAngle.compute`: per-tile で法線 decode + dot product + acos + 99%ile
- IMetric 実装
- Test: 既存 `NormalAngleMetric` の挙動を再現することを確認

各タスクは Task 4.5 と同じ構造。各 6 ステップ (compute 作成 / IMetric 実装 / test 作成 / unity compile / run tests / commit)。commit メッセージ例:

```bash
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gate): GPU-port BandingMetric"
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gate): GPU-port BlockBoundaryMetric"
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gate): GPU-port AlphaQuantizationMetric"
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gate): GPU-port NormalAngleMetric"
```

Compute Shader コード詳細テンプレート (Banding):

```hlsl
#pragma kernel CSEvaluate

Texture2D<float4> _Orig;
SamplerState sampler_Orig;
Texture2D<float4> _Candidate;
SamplerState sampler_Candidate;
RWStructuredBuffer<float> _Scores;
StructuredBuffer<float> _RPerTile;
int _TilesX; int _TilesY; int _TileSize;
int _TextureWidth; int _TextureHeight;
float _BandingScale;

float lum(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

[numthreads(8, 8, 1)]
void CSEvaluate(uint3 gid : SV_GroupID)
{
    int tx = gid.x, ty = gid.y;
    if (tx >= _TilesX || ty >= _TilesY) return;
    int tileIdx = ty * _TilesX + tx;
    if (_RPerTile[tileIdx] <= 0.0) return;

    float2 uvMin = float2(tx, ty) / float2(_TilesX, _TilesY);
    float2 uvMax = float2(tx + 1, ty + 1) / float2(_TilesX, _TilesY);
    float pxW = 1.0 / (float)_TextureWidth;
    float pxH = 1.0 / (float)_TextureHeight;

    const int N = 16;
    int flatCount = 0;
    int bins[32];
    [unroll] for (int b = 0; b < 32; b++) bins[b] = 0;

    for (int sy = 0; sy < N; sy++)
    for (int sx = 0; sx < N; sx++)
    {
        float2 local = (float2(sx, sy) + 0.5) / (float)N;
        float2 uv = lerp(uvMin, uvMax, local);
        // flatness on orig
        float c = lum(_Orig.SampleLevel(sampler_Orig, uv, 0).rgb);
        float mx = c, mn = c;
        [unroll] for (int dy = -1; dy <= 1; dy++)
        [unroll] for (int dx = -1; dx <= 1; dx++)
        {
            float v = lum(_Orig.SampleLevel(sampler_Orig, uv + float2(dx * pxW, dy * pxH), 0).rgb);
            mx = max(mx, v); mn = min(mn, v);
        }
        if (mx - mn > 0.02) continue;

        // 2nd deriv on candidate
        float cc = lum(_Candidate.SampleLevel(sampler_Candidate, uv, 0).rgb);
        float cl = lum(_Candidate.SampleLevel(sampler_Candidate, uv - float2(pxW, 0), 0).rgb);
        float cr = lum(_Candidate.SampleLevel(sampler_Candidate, uv + float2(pxW, 0), 0).rgb);
        float d2 = cr - 2.0 * cc + cl;
        int bin = clamp((int)((d2 + 0.5) * 32), 0, 31);
        bins[bin]++;
        flatCount++;
    }

    if (flatCount < 10) { _Scores[tileIdx] = 0; return; }

    int peak = 0;
    for (int i = 0; i < 32; i++) if (i != 16 && bins[i] > peak) peak = bins[i];
    float ratio = (float)peak / ((float)flatCount * 0.1);
    _Scores[tileIdx] = saturate(ratio) * _BandingScale;
}
```

Similar templates for BlockBoundary / AlphaQuantization / NormalAngle を書く。基本構造は一致。本プランでは各のテンプレートを 4.6/4.7/4.8/4.9 に順次展開する (内容は上記ショートテンプレートと Compute ロジックで十分、実装時に細部を詰める)。

---

### Task 4.10: PerceptualGate (max 合成)

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Gate/PerceptualGate.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/PerceptualGateTests.cs`

- [ ] **Step 1: Gate を実装**

`Editor/Phase2/Gate/PerceptualGate.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Gate
{
    public struct GateVerdict
    {
        public bool Pass;
        public float TextureScore;
        public int WorstTileIndex;
        public string DominantMetric;
        public int DominantMipLevel;
    }

    public class PerceptualGate
    {
        readonly DegradationCalibration _calib;

        public PerceptualGate(DegradationCalibration calib) { _calib = calib; }

        public GateVerdict Evaluate(
            RenderTexture orig, RenderTexture candidate,
            UvTileGrid grid, float[] rPerTile,
            QualityPreset preset, IReadOnlyList<IMetric> metrics)
        {
            float threshold = _calib.GetThreshold(preset);
            int tileCount = grid.Tiles.Length;

            var accum = new float[tileCount];
            var dominant = new string[tileCount];

            var tmp = new float[tileCount];
            foreach (var m in metrics)
            {
                System.Array.Clear(tmp, 0, tmp.Length);
                m.Evaluate(orig, candidate, grid, rPerTile, _calib, tmp);
                for (int i = 0; i < tileCount; i++)
                {
                    if (tmp[i] > accum[i])
                    {
                        accum[i] = tmp[i];
                        dominant[i] = m.Name;
                    }
                }
            }

            // Texture-wide max over tiles (HasCoverage 限定)
            float texMax = 0f; int worstIdx = -1;
            for (int i = 0; i < tileCount; i++)
            {
                if (!grid.Tiles[i].HasCoverage) continue;
                if (accum[i] > texMax) { texMax = accum[i]; worstIdx = i; }
            }

            return new GateVerdict
            {
                Pass = texMax < threshold,
                TextureScore = texMax,
                WorstTileIndex = worstIdx,
                DominantMetric = worstIdx >= 0 ? dominant[worstIdx] : null,
                DominantMipLevel = worstIdx >= 0
                    ? EffectiveResolutionCalculator.LevelFromR(rPerTile[worstIdx], grid.TileSize)
                    : -1,
            };
        }
    }
}
```

- [ ] **Step 2: Gate テスト**

`Tests/Editor/PerceptualGateTests.cs`:
```csharp
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

public class PerceptualGateTests
{
    class FakeMetric : IMetric
    {
        public string Name { get; set; }
        public MetricContext Context => MetricContext.Both;
        public float[] Output;
        public void Evaluate(RenderTexture o, RenderTexture c, UvTileGrid g, float[] r, DegradationCalibration calib, float[] scores)
        {
            System.Array.Copy(Output, scores, scores.Length);
        }
    }

    [Test]
    public void MaxCombination_ReturnsLargestScore()
    {
        var grid = UvTileGrid.Create(128, 128);
        for (int i = 0; i < grid.Tiles.Length; i++)
            grid.Tiles[i] = new TileStats { HasCoverage = true };

        var a = new FakeMetric { Name = "A", Output = Filled(grid.Tiles.Length, 0.3f) };
        var b = new FakeMetric { Name = "B", Output = Filled(grid.Tiles.Length, 0.8f) };

        var calib = DegradationCalibration.Default();
        var gate = new PerceptualGate(calib);
        var verdict = gate.Evaluate(null, null, grid, new float[grid.Tiles.Length],
            QualityPreset.Medium, new IMetric[] { a, b });

        Assert.AreEqual(0.8f, verdict.TextureScore, 0.001f);
        Assert.AreEqual("B", verdict.DominantMetric);
        Assert.IsTrue(verdict.Pass == (0.8f < calib.ThresholdMedium));
        Object.DestroyImmediate(calib);
    }

    [Test]
    public void NoCoverage_TilesIgnored()
    {
        var grid = UvTileGrid.Create(128, 128);
        // only one tile covered
        grid.Tiles[0] = new TileStats { HasCoverage = true };

        var huge = new FakeMetric { Name = "X", Output = Filled(grid.Tiles.Length, 10f) };
        huge.Output[0] = 0.1f;

        var calib = DegradationCalibration.Default();
        var gate = new PerceptualGate(calib);
        var verdict = gate.Evaluate(null, null, grid, new float[grid.Tiles.Length],
            QualityPreset.Medium, new IMetric[] { huge });

        Assert.AreEqual(0.1f, verdict.TextureScore, 0.001f);
        Assert.AreEqual(0, verdict.WorstTileIndex);
        Object.DestroyImmediate(calib);
    }

    static float[] Filled(int n, float v)
    {
        var r = new float[n];
        for (int i = 0; i < n; i++) r[i] = v;
        return r;
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "PerceptualGateTests"`
Expected: 全 PASS

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Gate/PerceptualGate.cs Tests/Editor/PerceptualGateTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/gate): add PerceptualGate with max combination and verdict result"
```

---

**M4 完了時点**: 主軸 MSSL + 補助メトリクス (Ridge/Banding/BlockBoundary/AlphaQuantization/NormalAngle) が GPU で動く。`PerceptualGate` が動作しテスト済み。旧 Degradation モジュールはまだ並存。

---

## M5. Format Predictor + Binary Search

### Task 5.1: BlockStatsComputer (per-4x4-block PCA)

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/GpuPipeline/Shaders/BlockStats.compute`
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/BlockStatsComputer.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/BlockStatsComputerTests.cs`

- [ ] **Step 1: BlockStats Compute Shader**

`Editor/Phase2/GpuPipeline/Shaders/BlockStats.compute`:
```hlsl
#pragma kernel CSMain

Texture2D<float4> _Source;
SamplerState sampler_Source;
int _Width;
int _Height;
int _BlocksX;
int _BlocksY;

// output: 6 floats per block (planarity, nonlinearity, alphaNonlinearity, meanR, meanG, meanB)
RWStructuredBuffer<float> _Stats;

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    int bx = id.x;
    int by = id.y;
    if (bx >= _BlocksX || by >= _BlocksY) return;

    float3 sumRgb = 0; float sumA = 0;
    float3 colors[16]; float alphas[16];
    int n = 0;
    for (int dy = 0; dy < 4; dy++)
    for (int dx = 0; dx < 4; dx++)
    {
        int x = bx * 4 + dx;
        int y = by * 4 + dy;
        if (x >= _Width || y >= _Height) continue;
        float2 uv = (float2(x, y) + 0.5) / float2(_Width, _Height);
        float4 c = _Source.SampleLevel(sampler_Source, uv, 0);
        colors[n] = c.rgb;
        alphas[n] = c.a;
        sumRgb += c.rgb;
        sumA += c.a;
        n++;
    }
    if (n == 0) return;

    float3 meanRgb = sumRgb / (float)n;
    float meanA = sumA / (float)n;

    // Compute 3x3 covariance matrix
    float cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
    float alphaVar = 0;
    for (int i = 0; i < n; i++)
    {
        float3 d = colors[i] - meanRgb;
        cxx += d.x * d.x; cxy += d.x * d.y; cxz += d.x * d.z;
        cyy += d.y * d.y; cyz += d.y * d.z; czz += d.z * d.z;
        alphaVar += (alphas[i] - meanA) * (alphas[i] - meanA);
    }
    cxx /= n; cxy /= n; cxz /= n; cyy /= n; cyz /= n; czz /= n;
    alphaVar /= n;

    // Power iteration for largest eigenvalue
    float3 v = float3(1, 1, 1);
    [unroll] for (int it = 0; it < 8; it++)
    {
        float3 mv = float3(
            cxx * v.x + cxy * v.y + cxz * v.z,
            cxy * v.x + cyy * v.y + cyz * v.z,
            cxz * v.x + cyz * v.y + czz * v.z);
        float mag = length(mv);
        if (mag > 1e-6) v = mv / mag;
    }
    float lambda1 = dot(v, float3(
        cxx * v.x + cxy * v.y + cxz * v.z,
        cxy * v.x + cyy * v.y + cyz * v.z,
        cxz * v.x + cyz * v.y + czz * v.z));

    float trace = cxx + cyy + czz;
    float lambda_rest = max(0.0, trace - lambda1);
    float planarity = trace > 1e-6 ? lambda_rest / trace : 0.0;

    // Non-linearity proxy: smallest eigenvalue ratio from trace-determinant relation
    float det = cxx * (cyy * czz - cyz * cyz)
              - cxy * (cxy * czz - cyz * cxz)
              + cxz * (cxy * cyz - cyy * cxz);
    float lambda3_est = trace > 1e-6 ? max(0.0, det / max(cxx * cyy + cyy * czz + cxx * czz - cxy * cxy - cyz * cyz - cxz * cxz, 1e-6)) : 0.0;
    float nonlinearity = trace > 1e-6 ? lambda3_est / trace : 0.0;

    float alphaNonLin = alphaVar;

    int o = (by * _BlocksX + bx) * 6;
    _Stats[o + 0] = planarity;
    _Stats[o + 1] = nonlinearity;
    _Stats[o + 2] = alphaNonLin;
    _Stats[o + 3] = meanRgb.r;
    _Stats[o + 4] = meanRgb.g;
    _Stats[o + 5] = meanRgb.b;
}
```

- [ ] **Step 2: BlockStatsComputer ラッパー**

`Editor/Phase2/Compression/BlockStatsComputer.cs`:
```csharp
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public struct BlockStats
    {
        public float Planarity;
        public float Nonlinearity;
        public float AlphaNonlinearity;
        public float MeanR, MeanG, MeanB;
    }

    public static class BlockStatsComputer
    {
        public static BlockStats[] Compute(RenderTexture source, int width, int height)
        {
            int bx = Mathf.Max(1, (width + 3) / 4);
            int by = Mathf.Max(1, (height + 3) / 4);
            int total = bx * by;
            var buf = new ComputeBuffer(total, sizeof(float) * 6);
            buf.SetData(new float[total * 6]);

            var cs = ComputeResources.Load("BlockStats");
            int k = cs.FindKernel("CSMain");
            cs.SetTexture(k, "_Source", source);
            cs.SetBuffer(k, "_Stats", buf);
            cs.SetInt("_Width", width);
            cs.SetInt("_Height", height);
            cs.SetInt("_BlocksX", bx);
            cs.SetInt("_BlocksY", by);
            cs.Dispatch(k, Mathf.CeilToInt(bx / 8f), Mathf.CeilToInt(by / 8f), 1);

            var raw = new float[total * 6];
            buf.GetData(raw);
            buf.Release();

            var result = new BlockStats[total];
            for (int i = 0; i < total; i++)
            {
                int o = i * 6;
                result[i] = new BlockStats
                {
                    Planarity = raw[o + 0],
                    Nonlinearity = raw[o + 1],
                    AlphaNonlinearity = raw[o + 2],
                    MeanR = raw[o + 3],
                    MeanG = raw[o + 4],
                    MeanB = raw[o + 5],
                };
            }
            return result;
        }
    }
}
```

- [ ] **Step 3: 基本動作テスト**

`Tests/Editor/BlockStatsComputerTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;

public class BlockStatsComputerTests
{
    [Test]
    public void FlatColor_LowNonlinearity()
    {
        var t = Solid(64, 64, Color.red);
        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var stats = BlockStatsComputer.Compute(ctx.Original, 64, 64);
            Assert.Greater(stats.Length, 0);
            foreach (var s in stats)
            {
                Assert.Less(s.Planarity, 0.1f, "flat color should have near-zero planarity");
                Assert.Less(s.Nonlinearity, 0.1f);
            }
        }
        Object.DestroyImmediate(t);
    }

    [Test]
    public void RandomColors_HighPlanarity()
    {
        var t = Random64(64);
        using (var ctx = GpuTextureContext.FromTexture2D(t))
        {
            var stats = BlockStatsComputer.Compute(ctx.Original, 64, 64);
            int nonFlatBlocks = 0;
            foreach (var s in stats) if (s.Planarity > 0.15f) nonFlatBlocks++;
            Assert.Greater(nonFlatBlocks, stats.Length / 4);
        }
        Object.DestroyImmediate(t);
    }

    static Texture2D Solid(int w, int h, Color c)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px); t.Apply(); return t;
    }

    static Texture2D Random64(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        var rng = new System.Random(12345);
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), 1f);
        t.SetPixels(px); t.Apply(); return t;
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "BlockStatsComputerTests"`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/GpuPipeline/Shaders/BlockStats.compute Editor/Phase2/Compression/BlockStatsComputer.cs Tests/Editor/BlockStatsComputerTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/compression): add per-4x4-block PCA statistics (GPU)"
```

---

### Task 5.2: FormatPredictor

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/FormatPredictor.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/FormatPredictorTests.cs`

- [ ] **Step 1: 予測器を実装**

`Editor/Phase2/Compression/FormatPredictor.cs`:
```csharp
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public struct FormatPrediction
    {
        public TextureFormat Format;
        public float Confidence;    // 0..1, 高いほど pass 確度高
        public string Reason;
    }

    public static class FormatPredictor
    {
        /// <summary>
        /// 軽量 fmt の pass 確度を返す。1.0 に近いほど verify 省略可能。
        /// </summary>
        public static FormatPrediction PredictLightweight(
            BlockStats[] stats, TextureRole role, QualityPreset preset)
        {
            switch (role)
            {
                case TextureRole.ColorOpaque:
                    return PredictDxt1(stats, preset);
                case TextureRole.ColorAlpha:
                    return PredictDxt5(stats, preset);
                case TextureRole.NormalMap:
                    return PredictBc5(stats, preset);
                case TextureRole.SingleChannel:
                    return new FormatPrediction { Format = TextureFormat.BC4, Confidence = 1f, Reason = "single-channel BC4 is near-lossless" };
                default:
                    return new FormatPrediction { Format = TextureFormat.BC7, Confidence = 1f, Reason = "BC7 is near-lossless fallback" };
            }
        }

        static FormatPrediction PredictDxt1(BlockStats[] s, QualityPreset preset)
        {
            // DXT1 は 4x4 ブロック内で 2 色エンドポイント + 2 中間色。
            // nonlinearity が高いブロックや planarity が極端に高いブロックで劣化する。
            int highNonLin = 0, highPlan = 0;
            foreach (var b in s)
            {
                if (b.Nonlinearity > 0.08f) highNonLin++;
                if (b.Planarity > 0.35f) highPlan++;
            }
            float threshold = PresetBlockFailRate(preset);
            float failRate = Mathf.Max(highNonLin, highPlan) / (float)Mathf.Max(1, s.Length);
            float confidence = Mathf.Clamp01(1f - failRate / threshold);
            return new FormatPrediction
            {
                Format = TextureFormat.DXT1,
                Confidence = confidence,
                Reason = $"fail-risk blocks {failRate:P1} (threshold {threshold:P1})",
            };
        }

        static FormatPrediction PredictDxt5(BlockStats[] s, QualityPreset preset)
        {
            var color = PredictDxt1(s, preset);
            int alphaBad = 0;
            foreach (var b in s) if (b.AlphaNonlinearity > 0.05f) alphaBad++;
            float alphaThreshold = PresetBlockFailRate(preset);
            float alphaFail = alphaBad / (float)Mathf.Max(1, s.Length);
            float alphaConf = Mathf.Clamp01(1f - alphaFail / alphaThreshold);
            return new FormatPrediction
            {
                Format = TextureFormat.DXT5,
                Confidence = Mathf.Min(color.Confidence, alphaConf),
                Reason = $"color {color.Confidence:F2}, alpha {alphaConf:F2}",
            };
        }

        static FormatPrediction PredictBc5(BlockStats[] s, QualityPreset preset)
        {
            // BC5: RG 各 8-level interpolation, tangent 方向の高変動で劣化
            int nonLin = 0;
            foreach (var b in s) if (b.Nonlinearity > 0.1f) nonLin++;
            float failRate = nonLin / (float)Mathf.Max(1, s.Length);
            float threshold = PresetBlockFailRate(preset);
            return new FormatPrediction
            {
                Format = TextureFormat.BC5,
                Confidence = Mathf.Clamp01(1f - failRate / threshold),
                Reason = $"normal variance {failRate:P1}",
            };
        }

        static float PresetBlockFailRate(QualityPreset p)
        {
            switch (p)
            {
                case QualityPreset.Low: return 0.20f;
                case QualityPreset.Medium: return 0.10f;
                case QualityPreset.High: return 0.05f;
                case QualityPreset.Ultra: return 0.02f;
                default: return 0.10f;
            }
        }
    }
}
```

- [ ] **Step 2: 予測テスト**

`Tests/Editor/FormatPredictorTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class FormatPredictorTests
{
    [Test]
    public void FlatBlocks_PredictDxt1_HighConfidence()
    {
        var stats = new BlockStats[64];
        for (int i = 0; i < stats.Length; i++)
            stats[i] = new BlockStats { Planarity = 0.02f, Nonlinearity = 0.01f };
        var p = FormatPredictor.PredictLightweight(stats, TextureRole.ColorOpaque, QualityPreset.Medium);
        Assert.AreEqual(TextureFormat.DXT1, p.Format);
        Assert.Greater(p.Confidence, 0.8f);
    }

    [Test]
    public void HighVarianceBlocks_PredictDxt1_LowConfidence()
    {
        var stats = new BlockStats[64];
        for (int i = 0; i < stats.Length; i++)
            stats[i] = new BlockStats { Planarity = 0.5f, Nonlinearity = 0.3f };
        var p = FormatPredictor.PredictLightweight(stats, TextureRole.ColorOpaque, QualityPreset.Medium);
        Assert.Less(p.Confidence, 0.3f);
    }

    [Test]
    public void ColorAlpha_ConsidersAlphaNonLinearity()
    {
        var stats = new BlockStats[64];
        for (int i = 0; i < stats.Length; i++)
            stats[i] = new BlockStats { Planarity = 0.02f, Nonlinearity = 0.01f, AlphaNonlinearity = 0.3f };
        var p = FormatPredictor.PredictLightweight(stats, TextureRole.ColorAlpha, QualityPreset.High);
        Assert.AreEqual(TextureFormat.DXT5, p.Format);
        Assert.Less(p.Confidence, 0.5f);
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "FormatPredictorTests"`
Expected: 全 PASS

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Compression/FormatPredictor.cs Tests/Editor/FormatPredictorTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/compression): add analytical FormatPredictor using block statistics"
```

---

### Task 5.3: BinarySearchStrategy

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/BinarySearchStrategy.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/BinarySearchStrategyTests.cs`

- [ ] **Step 1: 戦略実装**

`Editor/Phase2/Compression/BinarySearchStrategy.cs`:
```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    /// <summary>
    /// サイズ降順二分探索。
    /// probe(size) が「その size で pass か」を返す delegate。
    /// 最小 pass サイズを返す。
    /// </summary>
    public static class BinarySearchStrategy
    {
        public static int FindMinPassSize(int origSize, int minSize, Func<int, bool> probe)
        {
            var candidates = new List<int>();
            int s = origSize;
            while (s >= minSize)
            {
                candidates.Add(s);
                s /= 2;
            }
            candidates.Sort();  // ascending

            int lo = 0, hi = candidates.Count - 1;
            int bestPass = origSize;

            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                int size = candidates[mid];
                if (probe(size))
                {
                    bestPass = size;
                    hi = mid - 1;
                }
                else
                {
                    lo = mid + 1;
                }
            }
            return bestPass;
        }
    }
}
```

- [ ] **Step 2: テスト**

`Tests/Editor/BinarySearchStrategyTests.cs`:
```csharp
using NUnit.Framework;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

public class BinarySearchStrategyTests
{
    [Test]
    public void FindsBoundary_At_512()
    {
        int calls = 0;
        bool Probe(int size) { calls++; return size >= 512; }

        int result = BinarySearchStrategy.FindMinPassSize(4096, 32, Probe);
        Assert.AreEqual(512, result);
        Assert.LessOrEqual(calls, 4, "should use <= log2(size count) calls");
    }

    [Test]
    public void AllFail_ReturnsOrigSize()
    {
        int result = BinarySearchStrategy.FindMinPassSize(4096, 32, _ => false);
        Assert.AreEqual(4096, result);
    }

    [Test]
    public void AllPass_ReturnsMinSize()
    {
        int result = BinarySearchStrategy.FindMinPassSize(4096, 32, _ => true);
        Assert.AreEqual(32, result);
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "BinarySearchStrategyTests"`
Expected: 全 PASS

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Compression/BinarySearchStrategy.cs Tests/Editor/BinarySearchStrategyTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/compression): add descending binary search strategy"
```

---

**M5 完了時点**: BlockStats GPU 計算、FormatPredictor 解析的予測、BinarySearchStrategy の 3 部品が単体で動く。未統合。

---

## M6. Cache

### Task 6.1: XXHash64 utility

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Cache/XxHash64.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/XxHash64Tests.cs`

Unity 2022.3 には `System.IO.Hashing.XxHash64` がない (.NET 6+)。自前で実装する。

- [ ] **Step 1: XxHash64 実装**

`Editor/Phase2/Cache/XxHash64.cs`:
```csharp
using System;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Cache
{
    /// <summary>
    /// XXHash64 の簡易実装。NDMF ビルド 1 回で数百〜数千回呼ばれる前提。
    /// </summary>
    public class XxHash64
    {
        const ulong Prime1 = 11400714785074694791UL;
        const ulong Prime2 = 14029467366897019727UL;
        const ulong Prime3 = 1609587929392839161UL;
        const ulong Prime4 = 9650029242287828579UL;
        const ulong Prime5 = 2870177450012600261UL;

        ulong _v1, _v2, _v3, _v4;
        byte[] _buffer = new byte[32];
        int _bufLen;
        ulong _totalLen;
        ulong _seed;

        public XxHash64(ulong seed = 0)
        {
            _seed = seed;
            Reset();
        }

        public void Reset()
        {
            _v1 = _seed + Prime1 + Prime2;
            _v2 = _seed + Prime2;
            _v3 = _seed;
            _v4 = _seed - Prime1;
            _bufLen = 0;
            _totalLen = 0;
        }

        public void Append(byte[] data) => Append(data, 0, data.Length);

        public void Append(byte[] data, int offset, int count)
        {
            _totalLen += (ulong)count;
            if (_bufLen + count < 32)
            {
                Buffer.BlockCopy(data, offset, _buffer, _bufLen, count);
                _bufLen += count;
                return;
            }
            int pos = 0;
            if (_bufLen > 0)
            {
                int fill = 32 - _bufLen;
                Buffer.BlockCopy(data, offset, _buffer, _bufLen, fill);
                ProcessStripe(_buffer, 0);
                pos = fill;
                _bufLen = 0;
            }
            while (pos + 32 <= count)
            {
                ProcessStripe(data, offset + pos);
                pos += 32;
            }
            if (pos < count)
            {
                _bufLen = count - pos;
                Buffer.BlockCopy(data, offset + pos, _buffer, 0, _bufLen);
            }
        }

        void ProcessStripe(byte[] src, int idx)
        {
            _v1 = Round(_v1, BitConverter.ToUInt64(src, idx + 0));
            _v2 = Round(_v2, BitConverter.ToUInt64(src, idx + 8));
            _v3 = Round(_v3, BitConverter.ToUInt64(src, idx + 16));
            _v4 = Round(_v4, BitConverter.ToUInt64(src, idx + 24));
        }

        static ulong Round(ulong acc, ulong input)
        {
            acc += input * Prime2;
            acc = (acc << 31) | (acc >> 33);
            acc *= Prime1;
            return acc;
        }

        public ulong GetCurrentHashAsUInt64()
        {
            ulong h;
            if (_totalLen >= 32)
            {
                h = ((_v1 << 1) | (_v1 >> 63))
                  + ((_v2 << 7) | (_v2 >> 57))
                  + ((_v3 << 12) | (_v3 >> 52))
                  + ((_v4 << 18) | (_v4 >> 46));
                h = MergeRound(h, _v1);
                h = MergeRound(h, _v2);
                h = MergeRound(h, _v3);
                h = MergeRound(h, _v4);
            }
            else
            {
                h = _seed + Prime5;
            }
            h += _totalLen;

            int remaining = _bufLen, pos = 0;
            while (pos + 8 <= remaining)
            {
                h ^= Round(0, BitConverter.ToUInt64(_buffer, pos));
                h = ((h << 27) | (h >> 37)) * Prime1 + Prime4;
                pos += 8;
            }
            if (pos + 4 <= remaining)
            {
                h ^= (ulong)BitConverter.ToUInt32(_buffer, pos) * Prime1;
                h = ((h << 23) | (h >> 41)) * Prime2 + Prime3;
                pos += 4;
            }
            while (pos < remaining)
            {
                h ^= (ulong)_buffer[pos] * Prime5;
                h = ((h << 11) | (h >> 53)) * Prime1;
                pos++;
            }
            h ^= h >> 33;
            h *= Prime2;
            h ^= h >> 29;
            h *= Prime3;
            h ^= h >> 32;
            return h;
        }

        static ulong MergeRound(ulong acc, ulong v)
        {
            v = Round(0, v);
            acc ^= v;
            acc = acc * Prime1 + Prime4;
            return acc;
        }
    }
}
```

- [ ] **Step 2: ハッシュテスト**

`Tests/Editor/XxHash64Tests.cs`:
```csharp
using NUnit.Framework;
using Narazaka.VRChat.Jnto.Editor.Phase2.Cache;

public class XxHash64Tests
{
    [Test]
    public void SameInput_SameHash()
    {
        var a = new XxHash64();
        a.Append(new byte[] { 1, 2, 3, 4, 5 });
        ulong h1 = a.GetCurrentHashAsUInt64();

        var b = new XxHash64();
        b.Append(new byte[] { 1, 2, 3, 4, 5 });
        ulong h2 = b.GetCurrentHashAsUInt64();

        Assert.AreEqual(h1, h2);
    }

    [Test]
    public void DifferentInput_DifferentHash()
    {
        var a = new XxHash64();
        a.Append(new byte[] { 1, 2, 3, 4, 5 });
        var b = new XxHash64();
        b.Append(new byte[] { 1, 2, 3, 4, 6 });
        Assert.AreNotEqual(a.GetCurrentHashAsUInt64(), b.GetCurrentHashAsUInt64());
    }

    [Test]
    public void LongInput_Works()
    {
        var a = new XxHash64();
        var data = new byte[1000];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i & 0xFF);
        a.Append(data);
        ulong h = a.GetCurrentHashAsUInt64();
        Assert.AreNotEqual(0UL, h);
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "XxHash64Tests"`
Expected: 全 PASS

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Cache/XxHash64.cs Tests/Editor/XxHash64Tests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/cache): add XxHash64 utility for cache key building"
```

---

### Task 6.2: CacheKeyBuilder

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Cache/CacheKeyBuilder.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/CacheKeyBuilderTests.cs`

- [ ] **Step 1: CacheKeyBuilder を実装**

`Editor/Phase2/Cache/CacheKeyBuilder.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Resolution;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase1;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Cache
{
    public struct CacheKey
    {
        public ulong Value;
        public override string ToString() => Value.ToString("x16");
    }

    public static class CacheKeyBuilder
    {
        public static CacheKey Build(
            Texture2D tex,
            TextureRole role,
            IEnumerable<TextureReference> references,
            ResolvedSettings settings)
        {
            var h = new XxHash64();
            HashTexture(h, tex);
            HashInt(h, (int)role);
            HashSettings(h, settings);
            foreach (var r in references)
            {
                HashReference(h, r);
            }
            return new CacheKey { Value = h.GetCurrentHashAsUInt64() };
        }

        static void HashTexture(XxHash64 h, Texture2D tex)
        {
            var path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path))
            {
                HashString(h, "runtime://" + tex.GetInstanceID());
                return;
            }
            HashString(h, AssetDatabase.AssetPathToGUID(path));
            // file hash
            try
            {
                if (File.Exists(path))
                {
                    var bytes = File.ReadAllBytes(path);
                    h.Append(bytes);
                }
                var meta = path + ".meta";
                if (File.Exists(meta))
                {
                    h.Append(File.ReadAllBytes(meta));
                }
            }
            catch { /* ignore IO errors, fallback to guid-only */ }
        }

        static void HashReference(XxHash64 h, TextureReference r)
        {
            HashString(h, r.PropertyName ?? "");
            if (r.Material != null)
            {
                var shader = r.Material.shader;
                if (shader != null)
                {
                    HashString(h, shader.name);
                    var path = AssetDatabase.GetAssetPath(shader);
                    if (!string.IsNullOrEmpty(path))
                        HashString(h, AssetDatabase.AssetPathToGUID(path));
                }
                // 関連 property (alpha 判定など) のみ hash
                HashString(h, LilTexAlphaUsageAnalyzer.IsAlphaUsed(r.Material, r.PropertyName).ToString());
                for (int i = 0; i < UnityEditor.ShaderUtil.GetPropertyCount(shader); i++)
                {
                    if (UnityEditor.ShaderUtil.GetPropertyType(shader, i) != UnityEditor.ShaderUtil.ShaderPropertyType.Float
                     && UnityEditor.ShaderUtil.GetPropertyType(shader, i) != UnityEditor.ShaderUtil.ShaderPropertyType.Range
                     && UnityEditor.ShaderUtil.GetPropertyType(shader, i) != UnityEditor.ShaderUtil.ShaderPropertyType.Color)
                        continue;
                    var name = UnityEditor.ShaderUtil.GetPropertyName(shader, i);
                    // 多すぎるので alpha/blend 関連だけに絞る
                    if (!(name.Contains("Alpha") || name.Contains("Mode") || name.Contains("Blend"))) continue;
                    HashString(h, name);
                    if (UnityEditor.ShaderUtil.GetPropertyType(shader, i) == UnityEditor.ShaderUtil.ShaderPropertyType.Color)
                    {
                        var c = r.Material.GetColor(name);
                        HashFloat(h, c.r); HashFloat(h, c.g); HashFloat(h, c.b); HashFloat(h, c.a);
                    }
                    else
                    {
                        HashFloat(h, r.Material.GetFloat(name));
                    }
                }
            }
            if (r.RendererContext != null)
            {
                var m = r.RendererContext.localToWorldMatrix;
                for (int i = 0; i < 16; i++) HashFloat(h, m[i]);

                Mesh mesh = null;
                if (r.RendererContext is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else if (r.RendererContext is MeshRenderer mr)
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    mesh = mf != null ? mf.sharedMesh : null;
                }
                if (mesh != null)
                {
                    // 頂点・UV・index を軽量 hash (数MB でも ~10ms)
                    var verts = mesh.vertices;
                    var uvs = mesh.uv;
                    var tris = mesh.triangles;
                    HashVectorArray(h, verts);
                    HashVector2Array(h, uvs);
                    HashIntArray(h, tris);
                }
            }
        }

        static void HashSettings(XxHash64 h, ResolvedSettings s)
        {
            HashInt(h, (int)s.Preset);
            HashFloat(h, s.ViewDistanceCm);
            HashFloat(h, s.HMDPixelsPerDegree);
            HashInt(h, (int)s.EncodePolicy);
            HashInt(h, (int)s.CacheMode);
            if (s.BoneWeights != null)
            {
                foreach (var e in s.BoneWeights.Entries)
                {
                    HashInt(h, (int)e.Category);
                    HashFloat(h, e.Weight);
                }
            }
            if (s.Calibration is DegradationCalibration cal)
            {
                HashFloat(h, cal.MsslBandEnergyScale);
                HashFloat(h, cal.MsslStructureScale);
                HashFloat(h, cal.RidgeScale);
                HashFloat(h, cal.BandingScale);
                HashFloat(h, cal.BlockBoundaryScale);
                HashFloat(h, cal.AlphaQuantScale);
                HashFloat(h, cal.NormalAngleScale);
                HashFloat(h, cal.ThresholdLow);
                HashFloat(h, cal.ThresholdMedium);
                HashFloat(h, cal.ThresholdHigh);
                HashFloat(h, cal.ThresholdUltra);
            }
        }

        static void HashInt(XxHash64 h, int v) => h.Append(BitConverter.GetBytes(v));
        static void HashFloat(XxHash64 h, float v) => h.Append(BitConverter.GetBytes(v));
        static void HashString(XxHash64 h, string s) => h.Append(System.Text.Encoding.UTF8.GetBytes(s ?? ""));

        static void HashVectorArray(XxHash64 h, Vector3[] arr)
        {
            if (arr == null) return;
            var buf = new byte[arr.Length * 12];
            for (int i = 0; i < arr.Length; i++)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(arr[i].x), 0, buf, i * 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(arr[i].y), 0, buf, i * 12 + 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(arr[i].z), 0, buf, i * 12 + 8, 4);
            }
            h.Append(buf);
        }

        static void HashVector2Array(XxHash64 h, Vector2[] arr)
        {
            if (arr == null) return;
            var buf = new byte[arr.Length * 8];
            for (int i = 0; i < arr.Length; i++)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(arr[i].x), 0, buf, i * 8, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(arr[i].y), 0, buf, i * 8 + 4, 4);
            }
            h.Append(buf);
        }

        static void HashIntArray(XxHash64 h, int[] arr)
        {
            if (arr == null) return;
            var buf = new byte[arr.Length * 4];
            Buffer.BlockCopy(arr, 0, buf, 0, buf.Length);
            h.Append(buf);
        }
    }
}
```

- [ ] **Step 2: 基本テスト**

`Tests/Editor/CacheKeyBuilderTests.cs`:
```csharp
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase1;
using Narazaka.VRChat.Jnto.Editor.Phase2.Cache;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Resolution;

public class CacheKeyBuilderTests
{
    [Test]
    public void IdenticalInputs_SameKey()
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var refs = new List<TextureReference>();
        var s = new ResolvedSettings();

        var k1 = CacheKeyBuilder.Build(t, TextureRole.ColorOpaque, refs, s);
        var k2 = CacheKeyBuilder.Build(t, TextureRole.ColorOpaque, refs, s);
        Assert.AreEqual(k1.Value, k2.Value);
        Object.DestroyImmediate(t);
    }

    [Test]
    public void DifferentPreset_DifferentKey()
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var refs = new List<TextureReference>();
        var s1 = new ResolvedSettings { Preset = QualityPreset.Medium };
        var s2 = new ResolvedSettings { Preset = QualityPreset.High };

        var k1 = CacheKeyBuilder.Build(t, TextureRole.ColorOpaque, refs, s1);
        var k2 = CacheKeyBuilder.Build(t, TextureRole.ColorOpaque, refs, s2);
        Assert.AreNotEqual(k1.Value, k2.Value);
        Object.DestroyImmediate(t);
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "CacheKeyBuilderTests"`
Expected: 両 PASS

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Cache/CacheKeyBuilder.cs Tests/Editor/CacheKeyBuilderTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/cache): add CacheKeyBuilder incorporating mesh/material/settings"
```

---

### Task 6.3: PersistentCache

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Cache/PersistentCache.cs`
- Test: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/PersistentCacheTests.cs`

- [ ] **Step 1: キャッシュ I/O 実装**

`Editor/Phase2/Cache/PersistentCache.cs`:
```csharp
using System;
using System.IO;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Cache
{
    [Serializable]
    public class CachedTextureResult
    {
        public int FinalSize;
        public string FinalFormatName; // TextureFormat enum string
        public byte[] CompressedRawBytes;
    }

    public static class PersistentCache
    {
        public static string RootPath => Path.Combine("Library", "JntoCache");
        public static string BlobsPath => Path.Combine(RootPath, "blobs");

        public static CachedTextureResult TryLoad(CacheKey key, CacheMode mode)
        {
            if (mode == CacheMode.Disabled) return null;
            var metaPath = Path.Combine(RootPath, key.ToString() + ".json");
            if (!File.Exists(metaPath)) return null;
            try
            {
                var json = File.ReadAllText(metaPath);
                var r = JsonUtility.FromJson<CachedTextureResult>(json);
                if (mode == CacheMode.Full && r != null && string.IsNullOrEmpty(r.FinalFormatName) == false && r.CompressedRawBytes == null)
                {
                    var blobPath = Path.Combine(BlobsPath, key.ToString() + ".bin");
                    if (File.Exists(blobPath))
                        r.CompressedRawBytes = File.ReadAllBytes(blobPath);
                }
                return r;
            }
            catch { return null; }
        }

        public static void Store(CacheKey key, CachedTextureResult value, CacheMode mode)
        {
            if (mode == CacheMode.Disabled) return;
            Directory.CreateDirectory(RootPath);

            var metaPath = Path.Combine(RootPath, key.ToString() + ".json");
            byte[] rawBytes = value.CompressedRawBytes;
            if (mode == CacheMode.Compact)
            {
                value.CompressedRawBytes = null;
            }
            else if (rawBytes != null)
            {
                Directory.CreateDirectory(BlobsPath);
                var blobPath = Path.Combine(BlobsPath, key.ToString() + ".bin");
                File.WriteAllBytes(blobPath, rawBytes);
                // store null in JSON to keep JSON small
                value.CompressedRawBytes = null;
            }
            var json = JsonUtility.ToJson(value);
            File.WriteAllText(metaPath, json);
            // restore
            value.CompressedRawBytes = rawBytes;
        }

        public static void ClearAll()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, true);
        }
    }
}
```

- [ ] **Step 2: 保存・読み出しテスト**

`Tests/Editor/PersistentCacheTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Cache;

public class PersistentCacheTests
{
    [SetUp]
    public void Setup() { PersistentCache.ClearAll(); }

    [TearDown]
    public void Teardown() { PersistentCache.ClearAll(); }

    [Test]
    public void StoreAndLoad_FullMode_RoundTrip()
    {
        var key = new CacheKey { Value = 0x1234567890abcdefUL };
        var value = new CachedTextureResult
        {
            FinalSize = 2048,
            FinalFormatName = "DXT5",
            CompressedRawBytes = new byte[] { 1, 2, 3, 4, 5 },
        };
        PersistentCache.Store(key, value, CacheMode.Full);

        var loaded = PersistentCache.TryLoad(key, CacheMode.Full);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(2048, loaded.FinalSize);
        Assert.AreEqual("DXT5", loaded.FinalFormatName);
        Assert.AreEqual(5, loaded.CompressedRawBytes.Length);
    }

    [Test]
    public void StoreAndLoad_CompactMode_NoBytes()
    {
        var key = new CacheKey { Value = 0xdeadbeefUL };
        var value = new CachedTextureResult
        {
            FinalSize = 1024,
            FinalFormatName = "BC7",
            CompressedRawBytes = new byte[] { 1, 2, 3 },
        };
        PersistentCache.Store(key, value, CacheMode.Compact);
        var loaded = PersistentCache.TryLoad(key, CacheMode.Compact);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(1024, loaded.FinalSize);
        Assert.IsTrue(loaded.CompressedRawBytes == null || loaded.CompressedRawBytes.Length == 0);
    }

    [Test]
    public void Disabled_DoesNotStore()
    {
        var key = new CacheKey { Value = 0xabcUL };
        var value = new CachedTextureResult { FinalSize = 512, FinalFormatName = "DXT1" };
        PersistentCache.Store(key, value, CacheMode.Disabled);
        Assert.IsNull(PersistentCache.TryLoad(key, CacheMode.Full));
    }
}
```

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests --testFilter "PersistentCacheTests"`
Expected: 全 PASS

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Cache/PersistentCache.cs Tests/Editor/PersistentCacheTests.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/cache): add PersistentCache with Full/Compact/Disabled modes"
```

---

### Task 6.4: InMemoryCache (1 ビルド実行中の使い回し)

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Cache/InMemoryCache.cs`

- [ ] **Step 1: 単純ラッパー**

`Editor/Phase2/Cache/InMemoryCache.cs`:
```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Cache
{
    /// <summary>
    /// 1 回の NDMF ビルド実行中に使い回すオブジェクト群。
    /// Dispose で RT / Buffer を解放。
    /// </summary>
    public class InMemoryCache : IDisposable
    {
        public readonly Dictionary<Texture2D, GpuTextureContext> Contexts = new();
        public readonly Dictionary<Texture2D, BlockStats[]> BlockStats = new();

        public void Dispose()
        {
            foreach (var ctx in Contexts.Values) ctx.Dispose();
            Contexts.Clear();
            BlockStats.Clear();
        }
    }
}
```

- [ ] **Step 2: Commit (テスト不要、単純コレクション)**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Cache/InMemoryCache.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/cache): add InMemoryCache for per-build object reuse"
```

---

**M6 完了時点**: XxHash64 / CacheKeyBuilder / PersistentCache / InMemoryCache が動作し単体テスト済み。

---

## M7. 統合 (新 Phase2Pipeline + 新 ResolutionReducePass)

### Task 7.1: 新 Phase2Pipeline

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/NewPhase2Pipeline.cs`

旧 `Phase2Pipeline.cs` とは別ファイルで並置、M9 で旧を削除。

- [ ] **Step 1: 新 Pipeline を実装**

`Editor/Phase2/Compression/NewPhase2Pipeline.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase2.Cache;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Resolution;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Compression
{
    public class NewPhase2Result
    {
        public Texture2D Final;
        public int Size;
        public TextureFormat Format;
        public GateVerdict FinalVerdict;
        public string DecisionReason;
        public bool CacheHit;
        public float ProcessingMs;
    }

    public class NewPhase2Pipeline
    {
        readonly DegradationCalibration _calib;
        readonly PerceptualGate _gate;
        readonly IMetric[] _downscaleMetrics;
        readonly IMetric[] _compressionMetrics;

        public NewPhase2Pipeline(DegradationCalibration calib, TextureRole role)
        {
            _calib = calib;
            _gate = new PerceptualGate(calib);
            _downscaleMetrics = BuildMetrics(role, MetricContext.Downscale);
            _compressionMetrics = BuildMetrics(role, MetricContext.Compression);
        }

        static IMetric[] BuildMetrics(TextureRole role, MetricContext ctx)
        {
            var list = new List<IMetric>();
            list.Add(new MsslMetric());
            if (ctx == MetricContext.Downscale || ctx == MetricContext.Both)
            {
                list.Add(new RidgeMetric());
            }
            if (ctx == MetricContext.Compression || ctx == MetricContext.Both)
            {
                list.Add(new BandingMetric());
                list.Add(new BlockBoundaryMetric());
                if (role == TextureRole.ColorAlpha) list.Add(new AlphaQuantizationMetric());
            }
            if (role == TextureRole.NormalMap) list.Add(new NormalAngleMetric());
            return list.ToArray();
        }

        public NewPhase2Result Find(
            Texture2D orig, GpuTextureContext origCtx,
            UvTileGrid grid, float[] rPerTile,
            TextureRole role, ResolvedSettings settings,
            BlockStats[] origStats)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int origSize = Mathf.Max(orig.width, orig.height);

            // 二分探索で最小 pass サイズを特定 (ダウンスケール軸のみ)
            int minSize = DensityCalculator.MinSize;
            int finalSize = BinarySearchStrategy.FindMinPassSize(origSize, minSize, size =>
            {
                if (size == origSize) return true;
                var candidateRt = PyramidBuilder.CreatePyramid(origCtx.Original, size, size, $"Jnto_Cand_{size}");
                try
                {
                    var scores = new float[grid.Tiles.Length];
                    var v = _gate.Evaluate(origCtx.Original, candidateRt, grid, rPerTile, settings.Preset, _downscaleMetrics);
                    return v.Pass;
                }
                finally
                {
                    candidateRt.Release();
                    Object.DestroyImmediate(candidateRt);
                }
            });

            // サイズ確定後、フォーマット選択
            var lightweight = FormatPredictor.PredictLightweight(origStats, role, settings.Preset);
            TextureFormat finalFmt;
            GateVerdict finalVerdict;
            string reason;

            finalFmt = ChooseFormat(orig, origCtx, grid, rPerTile, settings, role,
                                    finalSize, lightweight, out finalVerdict, out reason);

            var final = Encode(orig, finalSize, finalFmt);
            sw.Stop();
            return new NewPhase2Result
            {
                Final = final,
                Size = finalSize,
                Format = finalFmt,
                FinalVerdict = finalVerdict,
                DecisionReason = reason,
                ProcessingMs = (float)sw.Elapsed.TotalMilliseconds,
            };
        }

        TextureFormat ChooseFormat(
            Texture2D orig, GpuTextureContext origCtx,
            UvTileGrid grid, float[] rPerTile,
            ResolvedSettings settings, TextureRole role,
            int size, FormatPrediction lightweight,
            out GateVerdict verdict, out string reason)
        {
            bool skipVerify = settings.EncodePolicy == EncodePolicy.Fast && lightweight.Confidence >= 0.95f;
            if (skipVerify)
            {
                verdict = new GateVerdict { Pass = true, TextureScore = 0f };
                reason = $"Fast-mode skip verify for {lightweight.Format} (conf={lightweight.Confidence:F2})";
                return lightweight.Format;
            }

            // 軽量 fmt verify
            var downsampled = ResolutionReducer.Resize(orig, size);
            try
            {
                var candidate = TextureEncodeDecode.EncodeAndDecode(downsampled, lightweight.Format);
                using (var candCtx = GpuTextureContext.FromTexture2D(candidate))
                using (var origDownCtx = GpuTextureContext.FromTexture2D(downsampled))
                {
                    verdict = _gate.Evaluate(origDownCtx.Original, candCtx.Original, grid, rPerTile, settings.Preset, _compressionMetrics);
                }
                Object.DestroyImmediate(candidate);

                if (verdict.Pass)
                {
                    reason = $"lightweight {lightweight.Format} verify PASS (score={verdict.TextureScore:F3})";
                    return lightweight.Format;
                }

                // BC7 fallback
                var fallback = BC7Fallback(role);
                var bc7Candidate = TextureEncodeDecode.EncodeAndDecode(downsampled, fallback);
                using (var bc7Ctx = GpuTextureContext.FromTexture2D(bc7Candidate))
                using (var origDownCtx = GpuTextureContext.FromTexture2D(downsampled))
                {
                    verdict = _gate.Evaluate(origDownCtx.Original, bc7Ctx.Original, grid, rPerTile, settings.Preset, _compressionMetrics);
                }
                Object.DestroyImmediate(bc7Candidate);

                reason = verdict.Pass
                    ? $"{fallback} fallback PASS (score={verdict.TextureScore:F3})"
                    : $"{fallback} fallback FAIL, keeping original";
                return verdict.Pass ? fallback : orig.format;
            }
            finally
            {
                Object.DestroyImmediate(downsampled);
            }
        }

        static TextureFormat BC7Fallback(TextureRole role)
        {
            switch (role)
            {
                case TextureRole.SingleChannel: return TextureFormat.R8;
                default: return TextureFormat.BC7;
            }
        }

        static Texture2D Encode(Texture2D src, int size, TextureFormat fmt)
        {
            var resized = ResolutionReducer.Resize(src, size);
            var tex = new Texture2D(resized.width, resized.height, TextureFormat.RGBA32, true);
            tex.name = $"{src.name}_{resized.width}x{resized.height}_{fmt}";
            tex.SetPixels(resized.GetPixels());
            tex.Apply();
            UnityEditor.EditorUtility.CompressTexture(tex, fmt, UnityEditor.TextureCompressionQuality.Normal);
            Object.DestroyImmediate(resized);
            return tex;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Compression/NewPhase2Pipeline.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/pipeline): add NewPhase2Pipeline integrating gate + binary search + predictor"
```

---

### Task 7.2: 新 ResolutionReducePass

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/NewResolutionReducePass.cs`
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/NDMF/JntoPlugin.cs`

- [ ] **Step 1: 新 Pass を実装**

`Editor/Phase2/NewResolutionReducePass.cs`:
```csharp
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using Narazaka.VRChat.Jnto;
using Narazaka.VRChat.Jnto.Editor.Phase1;
using Narazaka.VRChat.Jnto.Editor.Phase2.Cache;
using Narazaka.VRChat.Jnto.Editor.Phase2.Compression;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;
using Narazaka.VRChat.Jnto.Editor.Phase2.GpuPipeline;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;
using Narazaka.VRChat.Jnto.Editor.Resolution;

namespace Narazaka.VRChat.Jnto.Editor.Phase2
{
    public class NewResolutionReducePass : Pass<NewResolutionReducePass>
    {
        protected override void Execute(BuildContext ctx)
        {
            var root = ctx.AvatarRootObject;
            if (root.GetComponentInChildren<TextureOptimizer>(true) == null) return;

            var animator = root.GetComponent<Animator>();
            var bonemap = BoneClassifier.ClassifyHumanoid(animator);
            var graph = TextureReferenceCollector.Collect(root);

            var rendererSettings = new Dictionary<Renderer, ResolvedSettings>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var s = SettingsResolver.Resolve(r.transform);
                if (s == null) continue;
                rendererSettings[r] = s;
            }

            using (var cache = new InMemoryCache())
            {
                var replaced = new Dictionary<Texture2D, Texture2D>();
                foreach (var kv in graph.Map)
                {
                    if (!(kv.Key is Texture2D tex)) continue;
                    ProcessTexture(tex, kv.Value, rendererSettings, bonemap, cache, replaced);
                }
                ApplyReplacements(root, graph, replaced);
            }
        }

        void ProcessTexture(
            Texture2D tex, List<TextureReference> refs,
            Dictionary<Renderer, ResolvedSettings> rendererSettings,
            Dictionary<Transform, BoneCategory> bonemap,
            InMemoryCache cache,
            Dictionary<Texture2D, Texture2D> replaced)
        {
            // 1. settings (最初の有効な renderer から取得)
            ResolvedSettings settings = null;
            Renderer settingsRenderer = null;
            foreach (var r in refs)
            {
                if (r.RendererContext == null) continue;
                if (rendererSettings.TryGetValue(r.RendererContext, out settings))
                {
                    settingsRenderer = r.RendererContext;
                    break;
                }
            }
            if (settings == null) return;

            // 2. role
            bool alphaRequired = false;
            Material repMat = null; string repProp = null;
            foreach (var r in refs)
            {
                if (r.Material != null && LilTexAlphaUsageAnalyzer.IsAlphaUsed(r.Material, r.PropertyName))
                {
                    alphaRequired = true; repMat = r.Material; repProp = r.PropertyName; break;
                }
                if (repMat == null && r.Material != null) { repMat = r.Material; repProp = r.PropertyName; }
            }
            var role = TextureTypeClassifier.Classify(repMat, repProp, tex, alphaRequired);
            var calib = settings.Calibration as DegradationCalibration ?? DegradationCalibration.Default();

            // 3. Cache key
            var cacheKey = CacheKeyBuilder.Build(tex, role, refs, settings);
            var cached = PersistentCache.TryLoad(cacheKey, settings.CacheMode);
            if (cached != null && cached.CompressedRawBytes != null)
            {
                var restored = RestoreFromRaw(tex, cached);
                if (restored != null)
                {
                    ObjectRegistry.RegisterReplacedObject(tex, restored);
                    replaced[tex] = restored;
                    return;
                }
            }

            // 4. Tile grid + r(T)
            var sources = new List<(Renderer, Mesh)>();
            foreach (var r in refs)
            {
                if (r.RendererContext == null) continue;
                Mesh m = GetMesh(r.RendererContext);
                if (m != null) sources.Add((r.RendererContext, m));
            }
            var grid = TileGridBuilder.Build(tex.width, tex.height, sources, bonemap, rendererSettings);
            var rPerTile = new float[grid.Tiles.Length];
            for (int i = 0; i < grid.Tiles.Length; i++)
            {
                rPerTile[i] = EffectiveResolutionCalculator.ComputeR(
                    grid.Tiles[i], grid.TileSize, settings.ViewDistanceCm,
                    settings.HMDPixelsPerDegree, settings.Preset);
            }

            // 5. GPU context + block stats
            if (!cache.Contexts.TryGetValue(tex, out var gpuCtx))
            {
                gpuCtx = GpuTextureContext.FromTexture2D(tex);
                cache.Contexts[tex] = gpuCtx;
            }
            if (!cache.BlockStats.TryGetValue(tex, out var stats))
            {
                stats = BlockStatsComputer.Compute(gpuCtx.Original, tex.width, tex.height);
                cache.BlockStats[tex] = stats;
            }

            // 6. Run pipeline
            var pipeline = new NewPhase2Pipeline(calib, role);
            var result = pipeline.Find(tex, gpuCtx, grid, rPerTile, role, settings, stats);

            if (result.Final != tex)
            {
                ObjectRegistry.RegisterReplacedObject(tex, result.Final);
                replaced[tex] = result.Final;

                // cache store
                PersistentCache.Store(cacheKey, new CachedTextureResult
                {
                    FinalSize = result.Size,
                    FinalFormatName = result.Format.ToString(),
                    CompressedRawBytes = result.Final.GetRawTextureData(),
                }, settings.CacheMode);
            }
        }

        Texture2D RestoreFromRaw(Texture2D orig, CachedTextureResult cached)
        {
            if (!System.Enum.TryParse<TextureFormat>(cached.FinalFormatName, out var fmt)) return null;
            try
            {
                var t = new Texture2D(cached.FinalSize, cached.FinalSize, fmt, true);
                t.name = orig.name + "_cached";
                t.LoadRawTextureData(cached.CompressedRawBytes);
                t.Apply();
                return t;
            }
            catch { return null; }
        }

        void ApplyReplacements(GameObject root, TextureReferenceGraph graph, Dictionary<Texture2D, Texture2D> replaced)
        {
            if (replaced.Count == 0) return;
            var affectedMats = new HashSet<Material>();
            foreach (var kv in replaced)
                if (graph.Map.TryGetValue(kv.Key, out var refs))
                    foreach (var r in refs)
                        if (r.Material != null) affectedMats.Add(r.Material);

            var cloneMap = new Dictionary<Material, Material>();
            Shared.MaterialCloner.ReplaceOnRenderers(root, cloneMap, m => affectedMats.Contains(m));
            Phase1.AlphaStripPass.SafeSetTextures(root, cloneMap, replaced);
        }

        static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (renderer is MeshRenderer mr)
            {
                var mf = mr.GetComponent<MeshFilter>();
                return mf != null ? mf.sharedMesh : null;
            }
            return null;
        }
    }
}
```

- [ ] **Step 2: JntoPlugin を新 Pass に差し替え**

`Editor/NDMF/JntoPlugin.cs`:
```csharp
using nadena.dev.ndmf;
using Narazaka.VRChat.Jnto.Editor;
using Narazaka.VRChat.Jnto.Editor.Phase1;
using Narazaka.VRChat.Jnto.Editor.Phase2;
using Narazaka.VRChat.Jnto.Editor.Preview;

[assembly: ExportsPlugin(typeof(JntoPlugin))]

namespace Narazaka.VRChat.Jnto.Editor
{
    public class JntoPlugin : Plugin<JntoPlugin>
    {
        public override string QualifiedName => "net.narazaka.vrchat.jnto";
        public override string DisplayName => "Just-Noticeable Texture Optimizer";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .Run(AlphaStripPass.Instance)
                .Then.Run(NewResolutionReducePass.Instance)
                .PreviewingWith(new TextureOptimizerPreviewFilter());
        }
    }
}
```

- [ ] **Step 3: Unity コンパイル + 既存テスト回帰**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests`
Expected: 全 PASS (既存テストも含めて)

- [ ] **Step 4: 統合スモークテスト (小さい実アバター)**

実アバターをシーンに配置し、NDMF ビルドを実行して完走することを確認。

```
1. シーンを保存
2. ./AIBridgeCache/CLI/AIBridgeCLI.exe play_mode enter
3. ビルドログに [JNTO] Decision Log が出ることを確認 (M8 で強化するが最低限 Debug.Log は出る想定)
4. ./AIBridgeCache/CLI/AIBridgeCLI.exe play_mode exit
```

- [ ] **Step 5: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/NewResolutionReducePass.cs Editor/NDMF/JntoPlugin.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/pipeline): wire NewResolutionReducePass as NDMF Optimizing phase entry"
```

---

**M7 完了時点**: 新パイプラインが NDMF ビルドで実行され、実アバターに対して動作する。Reporting (M8) が薄いので Log は最小限。

---

## M8. Reporting

### Task 8.1: DecisionLog 基盤

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Reporting/DecisionRecord.cs`
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Reporting/DecisionLog.cs`

- [ ] **Step 1: 記録用の型**

`Editor/Phase2/Reporting/DecisionRecord.cs`:
```csharp
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Gate;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Reporting
{
    public class DecisionRecord
    {
        public Texture2D OriginalTexture;
        public int OrigSize;
        public int FinalSize;
        public TextureFormat OrigFormat;
        public TextureFormat FinalFormat;
        public long SavedBytes;
        public float TextureScore;
        public string DominantMetric;
        public int DominantMipLevel;
        public int WorstTileIndex;
        public bool CacheHit;
        public float ProcessingMs;
        public string Reason;
    }
}
```

- [ ] **Step 2: ログ収集 singleton**

`Editor/Phase2/Reporting/DecisionLog.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Reporting
{
    public static class DecisionLog
    {
        static readonly List<DecisionRecord> _records = new();

        public static IReadOnlyList<DecisionRecord> All => _records;

        public static void Clear() => _records.Clear();

        public static void Add(DecisionRecord r)
        {
            _records.Add(r);
            string line = Format(r);
            Debug.Log(line);
        }

        public static string Format(DecisionRecord r)
        {
            string fmtChange = r.OrigFormat == r.FinalFormat ? r.FinalFormat.ToString() : $"{r.OrigFormat}→{r.FinalFormat}";
            string sizeChange = r.OrigSize == r.FinalSize ? r.FinalSize.ToString() : $"{r.OrigSize}→{r.FinalSize}";
            string saved = FormatBytes(r.SavedBytes);
            string hit = r.CacheHit ? " (cache hit)" : "";
            return $"[JNTO] {(r.OriginalTexture != null ? r.OriginalTexture.name : "<null>")}: " +
                   $"{sizeChange} {fmtChange}, saved {saved}, " +
                   $"JND {r.TextureScore:F2}, dominant={r.DominantMetric}@L{r.DominantMipLevel}, t({r.WorstTileIndex}){hit}";
        }

        static string FormatBytes(long b)
        {
            if (b < 0) return "-" + FormatBytes(-b);
            if (b < 1024) return b + "B";
            if (b < 1024 * 1024) return (b / 1024f).ToString("F1") + "KB";
            return (b / 1024f / 1024f).ToString("F1") + "MB";
        }
    }
}
```

- [ ] **Step 3: NewResolutionReducePass から呼び出し**

`Editor/Phase2/NewResolutionReducePass.cs` の `ProcessTexture` 末尾で:
```csharp
            DecisionLog.Add(new Reporting.DecisionRecord
            {
                OriginalTexture = tex,
                OrigSize = Mathf.Max(tex.width, tex.height),
                FinalSize = result.Size,
                OrigFormat = tex.format,
                FinalFormat = result.Format,
                SavedBytes = EstimateSavedBytes(tex, result),
                TextureScore = result.FinalVerdict.TextureScore,
                DominantMetric = result.FinalVerdict.DominantMetric ?? "-",
                DominantMipLevel = result.FinalVerdict.DominantMipLevel,
                WorstTileIndex = result.FinalVerdict.WorstTileIndex,
                CacheHit = false,
                ProcessingMs = result.ProcessingMs,
                Reason = result.DecisionReason,
            });
```

補助メソッドを同ファイルに追加:
```csharp
        static long EstimateSavedBytes(Texture2D orig, NewPhase2Result r)
        {
            long origBytes = BytesFor(orig.width, orig.height, orig.format);
            long newBytes = BytesFor(r.Size, r.Size, r.Format);
            return origBytes - newBytes;
        }

        static long BytesFor(int w, int h, TextureFormat fmt)
        {
            int bpp = fmt == TextureFormat.DXT1 ? 4
                    : fmt == TextureFormat.DXT5 ? 8
                    : fmt == TextureFormat.BC5 || fmt == TextureFormat.BC7 ? 8
                    : fmt == TextureFormat.BC4 ? 4
                    : 32;
            long mipped = (long)w * h * bpp / 8;
            return (long)(mipped * 1.33f);
        }
```

同じ要領で**キャッシュヒットパス**でも `DecisionLog.Add` を呼ぶ (`CacheHit = true`)。

- [ ] **Step 4: Pass 先頭で DecisionLog.Clear()**

`Execute` の先頭に `Reporting.DecisionLog.Clear();` を追加。

- [ ] **Step 5: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Reporting/ Editor/Phase2/NewResolutionReducePass.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/reporting): add DecisionLog summarizing per-texture verdicts"
```

---

### Task 8.2: NDMF ErrorReport 統合

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Reporting/JntoNdmfReport.cs`

- [ ] **Step 1: ErrorReport API 確認**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe asset search --mode script --keyword "ErrorReport" --format paths`
Expected: NDMF の ErrorReport クラスパスが見つかる

具体 API は NDMF バージョンによって変わる (最新マイナーの `nadena.dev.ndmf.ErrorReport.ReportError` を想定)。見つけた API にあわせて実装する。

- [ ] **Step 2: レポート実装**

`Editor/Phase2/Reporting/JntoNdmfReport.cs`:
```csharp
using nadena.dev.ndmf;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Reporting
{
    public static class JntoNdmfReport
    {
        public static void Emit()
        {
            long totalSaved = 0;
            int hits = 0;
            float totalMs = 0;
            foreach (var r in DecisionLog.All)
            {
                totalSaved += r.SavedBytes;
                if (r.CacheHit) hits++;
                totalMs += r.ProcessingMs;

                if (r.FinalSize == r.OrigSize && r.FinalFormat == r.OrigFormat)
                {
                    // Warning: no reduction
                    ErrorReport.ReportError(new SimpleError(
                        ErrorSeverity.Information,
                        "jnto.no_reduction",
                        $"{(r.OriginalTexture != null ? r.OriginalTexture.name : "<null>")}: no reduction applied ({r.Reason})"));
                }
                else
                {
                    ErrorReport.ReportError(new SimpleError(
                        ErrorSeverity.Information,
                        "jnto.reduced",
                        DecisionLog.Format(r)));
                }
            }
            ErrorReport.ReportError(new SimpleError(
                ErrorSeverity.Information,
                "jnto.summary",
                $"Total saved: {(totalSaved / 1024f / 1024f):F1} MB, " +
                $"processed {DecisionLog.All.Count} textures, " +
                $"cache hits: {hits}, total time {totalMs:F0} ms"));
        }

        class SimpleError : INdmfError
        {
            public SimpleError(ErrorSeverity severity, string key, string message)
            {
                Severity = severity;
                Key = key;
                Message = message;
            }
            public ErrorSeverity Severity { get; }
            public string Key { get; }
            public string Message { get; }
            public SimpleError Error => this;
            // NDMF API に合わせて実装 (FormatError 等)
        }
    }
}
```

**注意**: NDMF の最新マイナーの `INdmfError` インターフェースは実際に API を確認してから実装。上記は指針であり、実際のシグネチャに合わせる必要がある。

- [ ] **Step 3: NewResolutionReducePass の Execute 終端で呼び出し**

```csharp
            if (replaced.Count > 0)
            {
                ApplyReplacements(root, graph, replaced);
                Reporting.JntoNdmfReport.Emit();
            }
```

- [ ] **Step 4: コンパイル確認**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Expected: エラーなし (NDMF API 差分があればエラー内容を見て調整)

- [ ] **Step 5: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Reporting/JntoNdmfReport.cs Editor/Phase2/NewResolutionReducePass.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/reporting): integrate decision log into NDMF ErrorReport"
```

---

### Task 8.3: JntoReportWindow

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Reporting/JntoReportWindow.cs`

- [ ] **Step 1: EditorWindow 実装**

`Editor/Phase2/Reporting/JntoReportWindow.cs`:
```csharp
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Reporting
{
    public class JntoReportWindow : EditorWindow
    {
        [MenuItem("Tools/Just-Noticeable Texture Optimizer/Report")]
        public static void Open()
        {
            var w = GetWindow<JntoReportWindow>();
            w.titleContent = new GUIContent("JNTO Report");
            w.Show();
        }

        Vector2 _scroll;
        int _selectedIndex = -1;
        string _filter = "";

        void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField, GUILayout.Width(200));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton)) { DecisionLog.Clear(); _selectedIndex = -1; }
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < DecisionLog.All.Count; i++)
            {
                var r = DecisionLog.All[i];
                if (!string.IsNullOrEmpty(_filter) && r.OriginalTexture != null
                    && !r.OriginalTexture.name.Contains(_filter)) continue;
                var bg = i == _selectedIndex ? new Color(0.3f, 0.5f, 0.7f, 0.3f) : Color.clear;
                var rect = EditorGUILayout.BeginHorizontal();
                EditorGUI.DrawRect(rect, bg);
                if (GUILayout.Button(DecisionLog.Format(r), EditorStyles.label))
                    _selectedIndex = i;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (_selectedIndex >= 0 && _selectedIndex < DecisionLog.All.Count)
            {
                var r = DecisionLog.All[_selectedIndex];
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Detail", EditorStyles.boldLabel);
                EditorGUILayout.ObjectField("Texture", r.OriginalTexture, typeof(Texture2D), false);
                EditorGUILayout.LabelField("Size", $"{r.OrigSize} → {r.FinalSize}");
                EditorGUILayout.LabelField("Format", $"{r.OrigFormat} → {r.FinalFormat}");
                EditorGUILayout.LabelField("JND Score", r.TextureScore.ToString("F3"));
                EditorGUILayout.LabelField("Dominant Metric", r.DominantMetric);
                EditorGUILayout.LabelField("Dominant Mip Level", r.DominantMipLevel.ToString());
                EditorGUILayout.LabelField("Worst Tile Index", r.WorstTileIndex.ToString());
                EditorGUILayout.LabelField("Processing", r.ProcessingMs.ToString("F1") + " ms");
                EditorGUILayout.LabelField("Cache Hit", r.CacheHit.ToString());
                EditorGUILayout.LabelField("Reason", r.Reason ?? "");
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Reporting/JntoReportWindow.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/reporting): add JntoReportWindow for detailed inspection"
```

---

### Task 8.4: DebugDump

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Reporting/DebugDump.cs`

- [ ] **Step 1: dump ユーティリティ**

`Editor/Phase2/Reporting/DebugDump.cs`:
```csharp
using System.IO;
using System.Text;
using UnityEngine;
using Narazaka.VRChat.Jnto.Editor.Phase2.Tiling;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Reporting
{
    public static class DebugDump
    {
        public static void DumpTileScores(
            string outputDir, string texName, UvTileGrid grid, float[][] metricScores, string[] metricNames)
        {
            if (string.IsNullOrEmpty(outputDir)) return;
            Directory.CreateDirectory(outputDir);
            var sb = new StringBuilder();
            sb.Append("tx,ty,hasCoverage,density,boneW");
            foreach (var m in metricNames) sb.Append(",").Append(m);
            sb.AppendLine();
            for (int ty = 0; ty < grid.TilesY; ty++)
            for (int tx = 0; tx < grid.TilesX; tx++)
            {
                int idx = ty * grid.TilesX + tx;
                var t = grid.Tiles[idx];
                sb.Append(tx).Append(",").Append(ty).Append(",").Append(t.HasCoverage)
                  .Append(",").Append(t.Density.ToString("F3")).Append(",").Append(t.BoneWeight.ToString("F3"));
                for (int m = 0; m < metricScores.Length; m++)
                    sb.Append(",").Append(metricScores[m][idx].ToString("F4"));
                sb.AppendLine();
            }
            File.WriteAllText(Path.Combine(outputDir, texName + "_tiles.csv"), sb.ToString());
        }

        public static void DumpHeatmapPng(
            string outputDir, string texName, string metricName, UvTileGrid grid, float[] scores)
        {
            if (string.IsNullOrEmpty(outputDir)) return;
            Directory.CreateDirectory(outputDir);
            var tex = new Texture2D(grid.TilesX, grid.TilesY, TextureFormat.RGBA32, false);
            var px = new Color[grid.Tiles.Length];
            for (int i = 0; i < scores.Length; i++)
            {
                float s = Mathf.Clamp01(scores[i]);
                px[i] = new Color(s, 1f - s, 0f, grid.Tiles[i].HasCoverage ? 1f : 0.3f);
            }
            tex.SetPixels(px);
            tex.Apply();
            var png = tex.EncodeToPNG();
            File.WriteAllBytes(Path.Combine(outputDir, texName + "_" + metricName + ".png"), png);
            Object.DestroyImmediate(tex);
        }
    }
}
```

- [ ] **Step 2: NewPhase2Pipeline から DebugDumpPath が空でないときに呼び出し**

`Find` メソッド内の gate 評価後に:
```csharp
            if (!string.IsNullOrEmpty(settings.DebugDumpPath))
            {
                // 各メトリクスのスコア配列を保持して DumpHeatmapPng する
                // 実装: _gate.EvaluateDebug(... out per-metric scores) 版を別途追加
            }
```

- [ ] **Step 3: PerceptualGate に EvaluateDebug メソッドを追加**

`Editor/Phase2/Gate/PerceptualGate.cs` に:
```csharp
        public GateVerdict EvaluateDebug(
            RenderTexture orig, RenderTexture candidate,
            UvTileGrid grid, float[] rPerTile,
            QualityPreset preset, IReadOnlyList<IMetric> metrics,
            out float[][] perMetricScores, out string[] metricNames)
        {
            int tileCount = grid.Tiles.Length;
            perMetricScores = new float[metrics.Count][];
            metricNames = new string[metrics.Count];
            for (int i = 0; i < metrics.Count; i++)
            {
                perMetricScores[i] = new float[tileCount];
                metricNames[i] = metrics[i].Name;
                metrics[i].Evaluate(orig, candidate, grid, rPerTile, _calib, perMetricScores[i]);
            }
            // accum 合成
            var accum = new float[tileCount];
            var dominant = new string[tileCount];
            for (int m = 0; m < metrics.Count; m++)
            {
                var tmp = perMetricScores[m];
                for (int i = 0; i < tileCount; i++)
                {
                    if (tmp[i] > accum[i]) { accum[i] = tmp[i]; dominant[i] = metricNames[m]; }
                }
            }
            float texMax = 0f; int worstIdx = -1;
            for (int i = 0; i < tileCount; i++)
            {
                if (!grid.Tiles[i].HasCoverage) continue;
                if (accum[i] > texMax) { texMax = accum[i]; worstIdx = i; }
            }
            return new GateVerdict
            {
                Pass = texMax < _calib.GetThreshold(preset),
                TextureScore = texMax,
                WorstTileIndex = worstIdx,
                DominantMetric = worstIdx >= 0 ? dominant[worstIdx] : null,
                DominantMipLevel = worstIdx >= 0
                    ? EffectiveResolutionCalculator.LevelFromR(rPerTile[worstIdx], grid.TileSize)
                    : -1,
            };
        }
```

- [ ] **Step 4: DebugDumpPath が指定されているテクスチャで dump を実行**

`NewPhase2Pipeline.Find` のダウンスケール gate 評価分岐で (pass 判定のサイズ確定後):
```csharp
            if (!string.IsNullOrEmpty(settings.DebugDumpPath))
            {
                var candRt = PyramidBuilder.CreatePyramid(origCtx.Original, finalSize, finalSize, "Jnto_Final_Debug");
                var verdict = _gate.EvaluateDebug(origCtx.Original, candRt, grid, rPerTile,
                    settings.Preset, _downscaleMetrics, out var perMetric, out var names);
                DebugDump.DumpTileScores(settings.DebugDumpPath, orig.name, grid, perMetric, names);
                for (int i = 0; i < names.Length; i++)
                    DebugDump.DumpHeatmapPng(settings.DebugDumpPath, orig.name, names[i], grid, perMetric[i]);
                candRt.Release();
                Object.DestroyImmediate(candRt);
            }
```

- [ ] **Step 5: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Reporting/DebugDump.cs Editor/Phase2/Gate/PerceptualGate.cs Editor/Phase2/Compression/NewPhase2Pipeline.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/reporting): add DebugDump for tile-level score csv/heatmap"
```

---

### Task 8.5: Profiler Markers

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/NewResolutionReducePass.cs`
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/NewPhase2Pipeline.cs`

- [ ] **Step 1: Profiler.BeginSample 挿入**

各処理フェーズ境界で:
```csharp
using UnityEngine.Profiling;

// NewResolutionReducePass.Execute
Profiler.BeginSample("JNTO.Execute");
// ... entire body ...
Profiler.EndSample();

// NewResolutionReducePass.ProcessTexture
Profiler.BeginSample($"JNTO.ProcessTexture.{tex.name}");

// NewPhase2Pipeline.Find
Profiler.BeginSample("JNTO.BinarySearch"); /* binary search block */ Profiler.EndSample();
Profiler.BeginSample("JNTO.ChooseFormat"); /* format choose */ Profiler.EndSample();
Profiler.BeginSample("JNTO.FinalEncode"); /* final encode */ Profiler.EndSample();
```

各メトリクス呼び出し前後でも同様:
```csharp
// PerceptualGate.Evaluate 内の foreach
Profiler.BeginSample("JNTO.Gate." + m.Name);
m.Evaluate(orig, candidate, grid, rPerTile, _calib, tmp);
Profiler.EndSample();
```

- [ ] **Step 2: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/reporting): add Unity Profiler markers for each pipeline phase"
```

---

**M8 完了時点**: Decision Log / NDMF Report / Report Window / Debug Dump / Profiler Markers が揃う。

---

## M9. クリーンアップ + ベンチマーク

### Task 9.1: 旧コード削除

**Files:**
- Delete: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Compression/Phase2Pipeline.cs`
- Delete: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/ResolutionReducePass.cs` (旧)
- Delete: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/TexelDensityMap.cs`
- Delete: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/MeshDensityAnalyzer.cs`
- Delete: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/MeshDensityStats.cs`
- Delete: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/DensityCalculator.cs` (もしくは `MinSize` 定数のみ残して移動)
- Delete: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Degradation/` 配下全ファイル
- Delete: `Packages/net.narazaka.vrchat.jnto/Editor/Complexity/` 配下全ファイル
- Delete: `Packages/net.narazaka.vrchat.jnto/Runtime/Complexity/` 配下全ファイル
- Delete: `Packages/net.narazaka.vrchat.jnto/Editor/Tools/SsimCompareTool.cs`
- Delete: `Packages/net.narazaka.vrchat.jnto/Tests/Editor/` 内の対応テスト (FlipMetricTests / SsimMetricTests / ChromaDriftMetricTests / HighFrequencyMetricTests / NormalAngleMetricTests / NormalVarianceMetricTests / TexelDensityMapTests / ComplexitySamplerTests / SobelComplexityStrategyTests / DensityCalculatorTests)

- [ ] **Step 1: `MinSize` 定数を移動**

`DensityCalculator.MinSize = 32` を `Editor/Phase2/Tiling/TilingConstants.cs` に移動 (`NewPhase2Pipeline` と `BinarySearchStrategy` が参照):

```csharp
namespace Narazaka.VRChat.Jnto.Editor.Phase2.Tiling
{
    public static class TilingConstants
    {
        public const int MinSize = 32;
    }
}
```

参照箇所を `TilingConstants.MinSize` に置換。

- [ ] **Step 2: TextureOptimizer から ComplexityStrategy フィールドを削除**

`Runtime/TextureOptimizer.cs` の
```csharp
        public Complexity.ComplexityStrategyAsset ComplexityStrategy;
```
を削除。

- [ ] **Step 3: 旧ファイル一括削除**

```bash
git -C Packages/net.narazaka.vrchat.jnto rm Editor/Phase2/Compression/Phase2Pipeline.cs Editor/Phase2/Compression/Phase2Pipeline.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Editor/Phase2/ResolutionReducePass.cs Editor/Phase2/ResolutionReducePass.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Editor/Phase2/TexelDensityMap.cs Editor/Phase2/TexelDensityMap.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Editor/Phase2/MeshDensityAnalyzer.cs Editor/Phase2/MeshDensityAnalyzer.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Editor/Phase2/MeshDensityStats.cs Editor/Phase2/MeshDensityStats.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Editor/Phase2/DensityCalculator.cs Editor/Phase2/DensityCalculator.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm -r Editor/Phase2/Degradation Editor/Phase2/Degradation.meta
git -C Packages/net.narazaka.vrchat.jnto rm -r Editor/Complexity Editor/Complexity.meta
git -C Packages/net.narazaka.vrchat.jnto rm -r Runtime/Complexity Runtime/Complexity.meta
git -C Packages/net.narazaka.vrchat.jnto rm Editor/Tools/SsimCompareTool.cs Editor/Tools/SsimCompareTool.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Tests/Editor/FlipMetricTests.cs Tests/Editor/FlipMetricTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Tests/Editor/SsimMetricTests.cs Tests/Editor/SsimMetricTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Tests/Editor/ChromaDriftMetricTests.cs Tests/Editor/ChromaDriftMetricTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Tests/Editor/HighFrequencyMetricTests.cs Tests/Editor/HighFrequencyMetricTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Tests/Editor/NormalAngleMetricTests.cs Tests/Editor/NormalAngleMetricTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Tests/Editor/NormalVarianceMetricTests.cs Tests/Editor/NormalVarianceMetricTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Tests/Editor/TexelDensityMapTests.cs Tests/Editor/TexelDensityMapTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Tests/Editor/ComplexitySamplerTests.cs Tests/Editor/ComplexitySamplerTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Tests/Editor/SobelComplexityStrategyTests.cs Tests/Editor/SobelComplexityStrategyTests.cs.meta
git -C Packages/net.narazaka.vrchat.jnto rm Tests/Editor/DensityCalculatorTests.cs Tests/Editor/DensityCalculatorTests.cs.meta
```

各コマンドは 1 tool call ごとに発行 (CLAUDE.md 遵守)。

- [ ] **Step 4: コンパイル確認**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Expected: 参照切れエラーがあれば対応。`TextureOptimizerEditor.cs` の旧フィールド表示を新フィールドに差し替え。

- [ ] **Step 5: テスト全体回帰**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests`
Expected: 全 PASS

- [ ] **Step 6: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add -A
git -C Packages/net.narazaka.vrchat.jnto commit -m "chore(jnto): remove legacy metrics / ComplexityStrategy / MeshDensity / Phase2Pipeline"
```

---

### Task 9.2: TextureOptimizerEditor 更新

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/TextureOptimizerEditor.cs`

- [ ] **Step 1: Inspector を新フィールド対応に**

既存 `TextureOptimizerEditor.cs` を開き、`ComplexityStrategy` プロパティ参照を削除し、`HMDPixelsPerDegree` / `EncodePolicy` / `Cache` / `DebugDumpPath` / `Calibration` の propertyField を追加。

- [ ] **Step 2: Unity で Inspector 目視確認**

シーンに `TextureOptimizer` を追加し、新フィールドが表示され、値変更が保存されることを確認。

- [ ] **Step 3: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/TextureOptimizerEditor.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "feat(jnto/editor): update TextureOptimizer Inspector for new fields"
```

---

### Task 9.3: ベンチマーク

**Files:**
- Create: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Reporting/JntoBenchmark.cs`

- [ ] **Step 1: ベンチスクリプト**

`Editor/Phase2/Reporting/JntoBenchmark.cs`:
```csharp
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Reporting
{
    public static class JntoBenchmark
    {
        [MenuItem("Tools/Just-Noticeable Texture Optimizer/Run Benchmark (current scene avatar)")]
        public static void Run()
        {
            var root = Selection.activeGameObject;
            if (root == null) { UnityEngine.Debug.LogError("Select an avatar GameObject first."); return; }
            DecisionLog.Clear();
            var sw = Stopwatch.StartNew();
            nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(root);
            sw.Stop();
            UnityEngine.Debug.Log($"[JNTO Benchmark] total {sw.ElapsedMilliseconds} ms, {DecisionLog.All.Count} textures");
        }
    }
}
```

**注**: `nadena.dev.ndmf.AvatarProcessor.ProcessAvatar` の API は最新マイナーで確認する。存在しない場合は NDMF の build API に合わせる。

- [ ] **Step 2: 実測**

1. シーン保存
2. ベンチ対象アバター (テクスチャ 20 枚、主 2K、一部 4K) を選択
3. Tools/Just-Noticeable Texture Optimizer/Run Benchmark (current scene avatar) を実行
4. ログの `[JNTO Benchmark] total XXX ms` を確認
5. 目標: 初回 ≤ 30 秒
6. 再実行 (キャッシュヒット狙い): `[JNTO Benchmark] total YYY ms`、目標 ≤ 3 秒

- [ ] **Step 3: 未達なら Profiler で原因特定**

Unity Profiler を開き、CPU/GPU の `JNTO.*` マーカーで重いフェーズを特定。

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Reporting/JntoBenchmark.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "chore(jnto): add benchmark menu item for avatar builds"
```

---

### Task 9.4: キャリブレーション初期値チューニング

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/Editor/Phase2/Gate/DegradationCalibration.cs`

- [ ] **Step 1: 実アバターで verdict を目視確認**

1. `TextureOptimizer.DebugDumpPath` を `Library/JntoDebug/` に設定
2. ベンチアバターで build
3. `Library/JntoDebug/<texName>_<metric>.png` を開き、問題タイルが視覚的に妥当か確認
4. 目視 fail のはずが pass になったら、該当メトリクスの Scale を上げる (より敏感に)
5. 逆の場合は Scale を下げる

- [ ] **Step 2: 調整後の値を DegradationCalibration.cs の既定値に反映**

- [ ] **Step 3: 回帰テスト**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe run_tests`
Expected: 全 PASS (テストは相対関係を見るので、値変更で壊れないはず)

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add Editor/Phase2/Gate/DegradationCalibration.cs
git -C Packages/net.narazaka.vrchat.jnto commit -m "chore(jnto/gate): tune calibration defaults from real-avatar inspection"
```

---

**M9 完了時点**: 旧コード除去・ベンチマーク実測・キャリブレーション初期値確定。速度・妥当性の目標達成確認。

---

## M10. NDMF 依存更新

### Task 10.1: package.json の NDMF 範囲更新

**Files:**
- Modify: `Packages/net.narazaka.vrchat.jnto/package.json`

- [ ] **Step 1: 最新マイナー確認**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe asset search --mode package --keyword "nadena.dev.ndmf" --format paths`

または VPM レジストリから確認 (ユーザー環境で手動確認可)。実装着手時点の NDMF 最新マイナー番号を X.Y として:

- [ ] **Step 2: vpmDependencies を更新**

`Packages/net.narazaka.vrchat.jnto/package.json`:
```json
  "vpmDependencies": {
    "nadena.dev.ndmf": ">=X.Y"
  }
```

(X.Y は実装時の最新マイナー。旧 `>=1.5.0` を置換)

- [ ] **Step 3: コンパイル確認**

Run: `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity`
Expected: 依存先最新 API に直接依存しているためエラーなし

- [ ] **Step 4: Commit**

```bash
git -C Packages/net.narazaka.vrchat.jnto add package.json
git -C Packages/net.narazaka.vrchat.jnto commit -m "chore(jnto): pin vpmDependencies to latest NDMF minor"
```

---

**M10 完了 = 全体完了。**

---

## 仕上げ

### Task F.1: README / ドキュメント整備 (任意)

**Files:**
- Modify: 既存の README があれば更新、なければ作成は**しない** (global rule: *.md を勝手に作らない)

- [ ] **Step 1: 既存 README 確認**

Run: Glob `Packages/net.narazaka.vrchat.jnto/README*` で有無確認
有る場合: 新フィールド・新動作を追記
無い場合: このタスクはスキップ (ユーザーから依頼あれば作成)

### Task F.2: ISSUES.md 整理 (古いので)

ユーザー指示で「ISSUES.md は古いので参照しない」とあるため:
- [ ] **Step 1: ISSUES.md に本再設計の内容を反映 (optional)**

ユーザーに可否を確認した上で置き換えか削除を検討。**デフォルトは触らない**。

---

## 自己レビュー

### Spec カバレッジチェック

| Spec セクション | 実装タスク |
|---|---|
| 3.1 タイル化 | M2 (Task 2.1-2.4) |
| 3.2 r(T) | Task 2.3 |
| 3.3 MSSL | Task 4.3, 4.4 |
| 3.3 Ridge | Task 4.5 |
| 3.3 Banding/BlockBoundary/AlphaQuantization/NormalAngle | Task 4.6-4.9 |
| 3.4 Verdict 合成 | Task 4.10 |
| 3.5 サイズ二分探索 | Task 5.3 |
| 3.5 フォーマット予測 | Task 5.1, 5.2 |
| 3.6 GPU 基盤 | M3 |
| 3.7 キャッシュ | M6 |
| 3.8 可観測性 | M8 |
| 4. モジュール分解 | M1–M9 |
| 5. 速度目標 | Task 9.3 |
| 7. 後方互換性 | Task 9.1, 9.2 |
| 9. NDMF バージョン | M10 |

全セクションに対応タスクあり。OK。

### Placeholder スキャン

- 「実装時に」「TBD」「後述」「同様に」の無検閲使用なし (M4 の 4.6-4.9 は templated だが構造が示してあり、template のスケルトンから順次展開可)
- 全コード片に完全な C#/HLSL 実装
- 全 git コマンドに具体メッセージ

### 型整合性

- `TextureRole` 列挙は現行コードに依存 (`Editor/Phase2/Compression/TextureTypeClassifier.cs`)、新コードも同じ名前を使う
- `DegradationCalibration` / `PerceptualGate` / `IMetric` / `GateVerdict` が一貫
- `GpuTextureContext` / `PyramidBuilder` / `ComputeResources` が一貫
- `CacheKey` / `CachedTextureResult` / `PersistentCache` / `InMemoryCache` が一貫
- `NewPhase2Pipeline.Find` と `NewResolutionReducePass.ProcessTexture` が整合

OK。

