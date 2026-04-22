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
