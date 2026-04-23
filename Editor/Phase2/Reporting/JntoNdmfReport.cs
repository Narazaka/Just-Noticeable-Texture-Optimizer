using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.UIElements;

namespace Narazaka.VRChat.Jnto.Editor.Phase2.Reporting
{
    /// <summary>
    /// Task 8.2: DecisionLog の内容を NDMF の ErrorReport に流し込む。
    /// 各 DecisionRecord は既に <see cref="DecisionLog.Add"/> で Debug.Log 済みのため、
    /// 本クラスでは aggregate summary + 各 record を NDMF ErrorReport に一覧として流す。
    /// <para>
    /// NDMF の <c>ErrorReport.ReportError</c> は <c>CurrentReport</c> が null でも例外を投げず
    /// Debug.LogWarning にフォールバックする (NDMF 内部仕様)。そのためテスト文脈でも安全。
    /// </para>
    /// </summary>
    public static class JntoNdmfReport
    {
        public static void Emit()
        {
            var records = DecisionLog.All;
            // debug: 常に file 出力 (records.Count が 0 でも痕跡を残す)。
            try
            {
                var debugPath0 = "AIBridgeCache/jnto_last_bake.txt";
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(debugPath0) ?? ".");
                var sb0 = new System.Text.StringBuilder();
                sb0.AppendLine($"[JNTO/emit] records.Count={(records == null ? -1 : records.Count)}");
                if (records != null) foreach (var r in records)
                {
                    sb0.AppendLine(DecisionLog.Format(r));
                    if (!string.IsNullOrEmpty(r.Reason)) sb0.AppendLine("    Reason: " + r.Reason);
                }
                System.IO.File.WriteAllText(debugPath0, sb0.ToString());
            }
            catch { /* best-effort */ }
            if (records == null || records.Count == 0) return;

            long totalSaved = 0;
            int cacheHits = 0;
            float totalMs = 0f;
            int reduced = 0;
            foreach (var r in records)
            {
                totalSaved += r.SavedBytes;
                if (r.CacheHit) cacheHits++;
                totalMs += r.ProcessingMs;
                if (r.OrigSize != r.FinalSize || r.OrigFormat != r.FinalFormat) reduced++;
            }

            string summary =
                $"[JNTO] Total saved: {(totalSaved / 1024f / 1024f):F1} MB, " +
                $"reduced {reduced}/{records.Count}, " +
                $"cache hits: {cacheHits}, total {totalMs:F0} ms";

            // summary は常に Debug.Log で出しておく (NDMF Error Report ウィンドウを開かなくても確認できる)。
            Debug.Log(summary);
            // debug: summary を既存 dump file に prepend する (Reason 含む行は上の try block で書いた)。
            try
            {
                var debugPath = "AIBridgeCache/jnto_last_bake.txt";
                var existing = System.IO.File.Exists(debugPath) ? System.IO.File.ReadAllText(debugPath) : "";
                System.IO.File.WriteAllText(debugPath, summary + "\n" + existing);
            }
            catch { /* best-effort */ }

            // ErrorReport へ投函: summary 1 件 + 各 record。
            // NDMF 側は CurrentReport が null なら Debug.LogWarning で代替する (追加の Warn ログが出る)。
            ErrorReport.ReportError(new JntoInfoError(summary, ErrorSeverity.Information));
            foreach (var r in records)
            {
                var error = new JntoInfoError(DecisionLog.Format(r), ErrorSeverity.Information);
                if (r.OriginalTexture != null)
                {
                    error.AddReference(ObjectRegistry.GetReference(r.OriginalTexture));
                }
                ErrorReport.ReportError(error);
            }
        }

        /// <summary>
        /// Localizer を持たないため <see cref="SimpleError"/> ではなく <see cref="IError"/> を直接実装する。
        /// NDMF のレポートウィンドウには単純な Label として表示される。
        /// </summary>
        internal sealed class JntoInfoError : IError
        {
            readonly string _message;
            readonly List<ObjectReference> _references = new List<ObjectReference>();

            public JntoInfoError(string message, ErrorSeverity severity)
            {
                _message = message ?? string.Empty;
                Severity = severity;
            }

            public ErrorSeverity Severity { get; }

            public VisualElement CreateVisualElement(ErrorReport report)
            {
                var root = new VisualElement();
                root.style.paddingLeft = 4;
                root.style.paddingRight = 4;
                root.style.paddingTop = 2;
                root.style.paddingBottom = 2;
                var label = new Label(_message)
                {
                    style = { whiteSpace = WhiteSpace.Normal }
                };
                root.Add(label);
                return root;
            }

            public string ToMessage() => _message;

            public void AddReference(ObjectReference obj)
            {
                if (obj != null) _references.Add(obj);
            }
        }
    }
}
