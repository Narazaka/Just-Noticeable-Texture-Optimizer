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

        [MenuItem("Tools/Just-Noticeable Texture Optimizer/Clear Cache")]
        public static void ClearCache()
        {
            Cache.PersistentCache.ClearAll();
            Debug.Log("[JNTO] Persistent cache cleared.");
        }

        Vector2 _scroll;
        int _selectedIndex = -1;
        string _filter = "";
        SortKey _sortKey = SortKey.Index;
        bool _sortDesc = false;

        enum SortKey { Index, Name, OrigSize, FinalSize, Score, ProcessingMs, SavedBytes }

        void OnGUI()
        {
            DrawToolbar();
            DrawSummary();
            DrawList();
            DrawDetail();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField, GUILayout.Width(200));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
                Repaint();
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                DecisionLog.Clear();
                _selectedIndex = -1;
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawSummary()
        {
            int total = DecisionLog.All.Count;
            if (total == 0)
            {
                EditorGUILayout.HelpBox("No JNTO build results yet. Trigger an NDMF build first.", MessageType.Info);
                return;
            }

            long savedSum = 0;
            int reduced = 0, hits = 0;
            float totalMs = 0;
            foreach (var r in DecisionLog.All)
            {
                savedSum += r.SavedBytes;
                if (r.OrigSize != r.FinalSize || r.OrigFormat != r.FinalFormat) reduced++;
                if (r.CacheHit) hits++;
                totalMs += r.ProcessingMs;
            }
            EditorGUILayout.HelpBox(
                $"{total} textures, {reduced} reduced, {hits} cache hit, " +
                $"saved {(savedSum / 1024f / 1024f):F1} MB, total {totalMs:F0} ms",
                MessageType.None);
        }

        void DrawList()
        {
            DrawHeader();
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(200));

            var sorted = GetSortedFiltered();
            for (int i = 0; i < sorted.Count; i++)
            {
                var (origIdx, r) = sorted[i];
                bool isSelected = origIdx == _selectedIndex;
                var rect = EditorGUILayout.BeginHorizontal();
                if (isSelected) EditorGUI.DrawRect(rect, new Color(0.3f, 0.5f, 0.7f, 0.3f));
                if (GUILayout.Button(BuildRowText(r, origIdx), EditorStyles.label))
                    _selectedIndex = origIdx;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            DrawSortHeader("#", SortKey.Index, 30);
            DrawSortHeader("Texture", SortKey.Name, 200);
            DrawSortHeader("Size", SortKey.OrigSize, 100);
            DrawSortHeader("→ Final", SortKey.FinalSize, 60);
            DrawSortHeader("Format", SortKey.Index, 100);
            DrawSortHeader("JND", SortKey.Score, 60);
            DrawSortHeader("Saved", SortKey.SavedBytes, 80);
            DrawSortHeader("ms", SortKey.ProcessingMs, 60);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        void DrawSortHeader(string label, SortKey key, float width)
        {
            string arrow = _sortKey == key ? (_sortDesc ? " ▼" : " ▲") : "";
            if (GUILayout.Button(label + arrow, EditorStyles.toolbarButton, GUILayout.Width(width)))
            {
                if (_sortKey == key) _sortDesc = !_sortDesc;
                else { _sortKey = key; _sortDesc = false; }
            }
        }

        System.Collections.Generic.List<(int origIdx, DecisionRecord r)> GetSortedFiltered()
        {
            var list = new System.Collections.Generic.List<(int, DecisionRecord)>();
            for (int i = 0; i < DecisionLog.All.Count; i++)
            {
                var r = DecisionLog.All[i];
                if (!string.IsNullOrEmpty(_filter)
                    && r.OriginalTexture != null
                    && !r.OriginalTexture.name.ToLower().Contains(_filter.ToLower()))
                    continue;
                list.Add((i, r));
            }

            int sign = _sortDesc ? -1 : 1;
            list.Sort((a, b) =>
            {
                switch (_sortKey)
                {
                    case SortKey.Index: return sign * a.Item1.CompareTo(b.Item1);
                    case SortKey.Name: return sign * (a.Item2.OriginalTexture?.name ?? "").CompareTo(b.Item2.OriginalTexture?.name ?? "");
                    case SortKey.OrigSize: return sign * a.Item2.OrigSize.CompareTo(b.Item2.OrigSize);
                    case SortKey.FinalSize: return sign * a.Item2.FinalSize.CompareTo(b.Item2.FinalSize);
                    case SortKey.Score: return sign * a.Item2.TextureScore.CompareTo(b.Item2.TextureScore);
                    case SortKey.SavedBytes: return sign * a.Item2.SavedBytes.CompareTo(b.Item2.SavedBytes);
                    case SortKey.ProcessingMs: return sign * a.Item2.ProcessingMs.CompareTo(b.Item2.ProcessingMs);
                }
                return 0;
            });
            return list;
        }

        static string BuildRowText(DecisionRecord r, int idx)
        {
            string name = r.OriginalTexture != null ? r.OriginalTexture.name : "<null>";
            string fmt = r.OrigFormat == r.FinalFormat ? r.FinalFormat.ToString() : $"{r.OrigFormat}→{r.FinalFormat}";
            string size = $"{r.OrigSize}→{r.FinalSize}";
            string saved = FormatBytes(r.SavedBytes);
            string hit = r.CacheHit ? " [cache]" : "";
            return $"{idx}: {name}  {size}  {fmt}  JND {r.TextureScore:F2}  {saved}  {r.ProcessingMs:F0}ms{hit}";
        }

        static string FormatBytes(long b)
        {
            if (b < 0) return "-" + FormatBytes(-b);
            if (b < 1024) return b + "B";
            if (b < 1024 * 1024) return (b / 1024f).ToString("F1") + "KB";
            return (b / 1024f / 1024f).ToString("F1") + "MB";
        }

        void DrawDetail()
        {
            if (_selectedIndex < 0 || _selectedIndex >= DecisionLog.All.Count) return;
            var r = DecisionLog.All[_selectedIndex];
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Detail", EditorStyles.boldLabel);
            EditorGUILayout.ObjectField("Texture", r.OriginalTexture, typeof(Texture2D), false);
            EditorGUILayout.LabelField("Size", $"{r.OrigSize} → {r.FinalSize}");
            EditorGUILayout.LabelField("Format", $"{r.OrigFormat} → {r.FinalFormat}");
            EditorGUILayout.LabelField("Saved", FormatBytes(r.SavedBytes));
            EditorGUILayout.LabelField("JND Score", r.TextureScore.ToString("F3"));
            EditorGUILayout.LabelField("Dominant Metric", r.DominantMetric ?? "-");
            EditorGUILayout.LabelField("Dominant Mip Level", r.DominantMipLevel.ToString());
            EditorGUILayout.LabelField("Worst Tile Index", r.WorstTileIndex.ToString());
            EditorGUILayout.LabelField("Processing", r.ProcessingMs.ToString("F1") + " ms");
            EditorGUILayout.LabelField("Cache Hit", r.CacheHit.ToString());
            EditorGUILayout.LabelField("Reason", r.Reason ?? "");
        }
    }
}
