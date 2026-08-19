using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ContentCheck.Core.Config;
using ContentCheck.Core.Excel;
using ContentCheck.Core.Models;
using ContentCheck.Core.Storage;

namespace ContentCheck.Import
{
    /// <summary>
    /// 一次性导入工具：读《电网工程土建设计规范条文.xlsx》→ 写入 provisions.db。
    /// 用法：ContentCheck.Import.exe [--config=config.json] [--excel=…] [--db=…]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                string configPath = GetArg(args, "--config") ?? FindConfigUpwards();
                var cfg = ConfigLoader.Load(configPath);

                var excelOverride = GetArg(args, "--excel");
                var dbOverride = GetArg(args, "--db");
                if (excelOverride != null) cfg.ExcelPath = Path.GetFullPath(excelOverride);
                if (dbOverride != null) cfg.DbPath = Path.GetFullPath(dbOverride);

                Console.WriteLine($"配置：{Path.GetFullPath(configPath)}");
                Console.WriteLine($"Excel：{cfg.ExcelPath}");
                Console.WriteLine($"数据库：{cfg.DbPath}");
                Console.WriteLine();

                if (string.IsNullOrEmpty(cfg.ExcelPath) || !File.Exists(cfg.ExcelPath))
                {
                    Console.Error.WriteLine("错误：未找到 Excel 文件（config.json 中 excel_path 或 --excel 参数）。");
                    return 1;
                }

                Console.WriteLine("正在解析 Excel …");
                var provisions = ExcelParser.ParseAll(cfg.ExcelPath);
                Console.WriteLine($"解析完成：共 {provisions.Count} 条条文。");
                Console.WriteLine();

                var store = new SqliteProvisionStore(cfg.DbPath);
                store.Init();
                store.ReplaceAll(provisions, cfg.ExcelPath);

                // 汇总
                Console.WriteLine("各专业条文数（含「设计说明」图纸类型）：");
                foreach (var g in provisions.GroupBy(p => p.Discipline).OrderBy(g => g.Key))
                {
                    int total = g.Count();
                    int design = g.Count(p => p.DrawingTypesRaw.Contains("设计说明"));
                    Console.WriteLine($"  {g.Key,-8} 总数 {total,-5} 设计说明 {design}");
                }

                var designTotal = provisions.Count(p => p.DrawingTypesRaw.Contains("设计说明"));
                Console.WriteLine();
                Console.WriteLine($"导入成功：{provisions.Count} 条条文，其中「设计说明」适用 {designTotal} 条。");
                Console.WriteLine($"上次导入时间：{store.GetLastImport():yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine();
                Console.WriteLine("在 AutoCAD 中 NETLOAD 插件后输入 CHECK 即可开始校核。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("导入失败：" + ex.Message);
                if (Environment.GetCommandLineArgs().Contains("--verbose"))
                    Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        static string GetArg(string[] args, string name)
        {
            foreach (var a in args)
            {
                if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return a.Substring(name.Length + 1).Trim('"');
            }
            return null;
        }

        /// <summary>从可执行文件目录向上查找 config.json（默认位置为项目根目录）。</summary>
        static string FindConfigUpwards()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "config.json");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }
    }
}
