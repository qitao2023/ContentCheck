using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace ContentCheck.Acad.Dwg
{
    /// <summary>一条文本行（来自 DBText 或 MText 的拆分行）。</summary>
    public class TextLine
    {
        public string Text { get; set; }
        public Point3d Position { get; set; }
        public double Height { get; set; }
        public string Layer { get; set; }

        /// <summary>AutoCAD 实体 Handle（用于结果表双击定位）。</summary>
        public string Handle { get; set; }

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
