using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace ContentCheck.Acad.Dwg
{
    /// <summary>
    /// 与一条文字空间关联的图面实体（自动空间绑定）。
    /// 提取文字时按「距文字距离 ≤ 容差（≈字高倍数）」规则扫描邻近非文字实体并记录，
    /// 供结果表双击定位时把文字与其描述/标注的图面元素一起选中。
    /// </summary>
    public class BoundEntity
    {
        /// <summary>关联实体的 AutoCAD Handle（可直接用于 GetObjectId 定位）。</summary>
        public string Handle { get; set; }

        /// <summary>实体 DXF 类型名（LINE / LWPOLYLINE / INSERT / HATCH / DIMENSION …）。</summary>
        public string DxfName { get; set; }

        /// <summary>实体所在图层。</summary>
        public string Layer { get; set; }

        /// <summary>文字插入点到实体几何的最近距离（图纸单位）。</summary>
        public double Distance { get; set; }
    }

    /// <summary>一条文本行（来自 DBText 或 MText 的拆分行）。</summary>
    public class TextLine
    {
        public string Text { get; set; }
        public Point3d Position { get; set; }
        public double Height { get; set; }
        public string Layer { get; set; }

        /// <summary>AutoCAD 实体 Handle（用于结果表双击定位）。</summary>
        public string Handle { get; set; }

        /// <summary>自动空间绑定的邻近图面实体（按距离近者优先，最多 MaxBoundEntitiesPerLine 个）。</summary>
        public List<BoundEntity> BoundEntities { get; } = new List<BoundEntity>();

        public TextLine(string text, Point3d position, double height, string layer, string handle)
        {
            Text = text;
            Position = position;
            Height = height;
            Layer = layer;
            Handle = handle;
        }
    }

    /// <summary>聚合后的文本段（若干相邻 TextLine 组成，用于校核/定位）。</summary>
    public class TextSegment
    {
        public int Index { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public List<TextLine> Lines { get; } = new List<TextLine>();
    }

    /// <summary>图纸中的一个布局（或 Model），含其全部文本。</summary>
    public class DrawingSheet
    {
        public string Name { get; set; }
        public List<TextLine> TextLines { get; } = new List<TextLine>();
        public string FullText { get; set; }

        /// <summary>按规则聚合后的文本段（空行/序号/标题/缩进切分）。</summary>
        public List<TextSegment> Segments { get; } = new List<TextSegment>();
        public string SegmentedText { get; set; }
    }
}
