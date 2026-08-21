using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace ContentCheck.Acad.UI
{
    /// <summary>按 Handle 选中并高亮图纸中的实体（结果表双击定位文字及其关联实体）。</summary>
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
                // Handle.ToString() 是十六进制（如 "2F"），必须用 HexNumber 解析
                if (!long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h)) continue;
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

        /// <summary>
        /// 将实体颜色改为红色（ColorIndex=1）以实现选中高亮。
        /// 成功时返回 true 并通过 origColor 输出原始颜色；失败返回 false。
        /// </summary>
        public static bool SetHighlightColor(Document doc, string handle, out Color origColor)
        {
            origColor = default;
            if (string.IsNullOrWhiteSpace(handle)) return false;
            try
            {
                if (!long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h)) return false;
                var id = doc.Database.GetObjectId(false, new Handle(h), 0);
                if (id.IsNull || id.IsErased || !id.IsValid) return false;

                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite, true) as Entity;
                    if (ent == null) return false;
                    origColor = ent.Color;
                    ent.Color = Color.FromColorIndex(ColorMethod.ByColor, 1); // 红色
                    tr.Commit();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>恢复实体颜色（配合 SetHighlightColor 使用）。</summary>
        public static void RestoreEntityColor(Document doc, string handle, Color origColor)
        {
            if (string.IsNullOrWhiteSpace(handle)) return;
            try
            {
                if (!long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h)) return;
                var id = doc.Database.GetObjectId(false, new Handle(h), 0);
                if (id.IsNull || id.IsErased || !id.IsValid) return;

                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite, true) as Entity;
                    if (ent == null) return;
                    ent.Color = origColor;
                    tr.Commit();
                }
            }
            catch
            {
                // 恢复失败不阻塞
            }
        }
    }
}
