using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace ContentCheck.Acad.UI
{
    /// <summary>按 Handle 选中并高亮图纸中的文本实体（结果表双击定位）。</summary>
    public static class Highlighter
    {
        public static void HighlightHandle(Document doc, string handle)
        {
            if (string.IsNullOrWhiteSpace(handle)) return;
            HighlightHandles(doc, new[] { handle });
        }

        public static void HighlightHandles(Document doc, IEnumerable<string> handles)
        {
            var ids = new List<ObjectId>();
            foreach (var handle in handles)
            {
                if (string.IsNullOrWhiteSpace(handle)) continue;
                if (!long.TryParse(handle, out var h)) continue;
                var id = doc.Database.GetObjectId(false, new Handle(h), 0);
                if (!id.IsNull && !id.IsErased && id.IsValid) ids.Add(id);
            }
            if (ids.Count == 0) return;

            try
            {
                using (doc.LockDocument())
                {
                    doc.Editor.SetImpliedSelection(ids.ToArray());
                }
            }
            catch
            {
                // 高亮失败不影响主流程
            }
        }
    }
}
