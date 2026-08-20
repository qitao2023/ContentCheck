using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ContentCheck.Core.AI;
using ContentCheck.Core.Config;
using ContentCheck.Core.Excel;
using ContentCheck.Core.Models;
using ContentCheck.Core.Storage;
using Newtonsoft.Json.Linq;

namespace ContentCheck.Tests
{
    /// <summary>
    /// 离线自测（不引用 AutoCAD）：Excel 解析 / SQLite 存储 / AI JSON 解析 / 提示词。
    /// 用法：ContentCheck.Tests.exe [--excel=路径]
    /// </summary>
    static class Program
    {
        static int _passed;
        static int _failed;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            bool live = args.Any(a => a == "--live");
            string excelPath = null;
            foreach (var a in args)
                if (a.StartsWith("--excel=", StringComparison.OrdinalIgnoreCase))
                    excelPath = a.Substring("--excel=".Length).Trim('"');
            if (excelPath == null) excelPath = Path.Combine(FindRoot(), "00-资料", "电网工程土建设计规范条文.xlsx");

            if (!File.Exists(excelPath))
            {
                Console.Error.WriteLine("找不到 Excel：" + excelPath);
                return 1;
            }

            TestExcelParser(excelPath);
            TestSqliteStore();
            TestAiJsonParser();
            TestPromptBuilder();
            TestConfig();
            if (live) TestLiveAi();

            Console.WriteLine();
            Console.WriteLine($"通过 {_passed} 项，失败 {_failed} 项。");
            return _failed == 0 ? 0 : 1;
        }

        /// <summary>实时 AI 冒烟测试：一次真实的 DeepSeek 调用（需联网 + API key）。</summary>
        static void TestLiveAi()
        {
            Console.WriteLine("[5] DeepSeek 实时调用（--live）");
            Test("单批条文校核返回有效结论", () =>
            {
                var configPath = Path.Combine(FindRoot(), "config.json");
                var cfg = ContentCheck.Core.Config.ConfigLoader.Load(configPath);
                if (string.IsNullOrWhiteSpace(cfg.ApiKey))
                    throw new Exception("无 API key（config.json api_key 未配置）");

                var batch = new CheckBatch
                {
                    Discipline = "建筑",
                    CodeName = "《测试规范》",
                    Items =
                    {
                        new CheckBatch.BatchItem { ClauseNumber = "1.0.1", ClauseText = "工业建筑外墙应设置便于消防救援人员出入的消防救援口。" },
                        new CheckBatch.BatchItem { ClauseNumber = "2.0.2", ClauseText = "建筑应沿两条长边设置消防车道。" },
                    },
                };
                var client = new DeepSeekClient(cfg);
                var vs = client.CheckBatchAsync(batch, "总说明",
                    "本工程为单层工业厂房，建筑分类为丙类。\n外墙设置消防救援口，尺寸满足规范要求。\n厂区四周设消防车道。",
                    CancellationToken.None).GetAwaiter().GetResult();

                Assert(vs.Count == 2, $"应返回 2 条，实际 {vs.Count}");
                foreach (var v in vs)
                {
                    Console.WriteLine($"      {v.ClauseNumber} → {v.Verdict}（证据：{(((v.Evidence ?? "").Length > 24) ? v.Evidence.Substring(0, 24) + "…" : v.Evidence)}）");
                    Assert(v.Verdict == "符合" || v.Verdict == "不符合" || v.Verdict == "未涉及" || v.Verdict == "无法判断",
                        $"非法结论：{v.Verdict}");
                }
            });
        }

        static void Test(string name, Action body)
        {
            try
            {
                body();
                _passed++;
                Console.WriteLine($"  [通过] {name}");
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine($"  [失败] {name}：{ex.Message}");
            }
        }

        static void Assert(bool cond, string msg)
        {
            if (!cond) throw new Exception(msg);
        }

        // ---------- ExcelParser ----------

        static void TestExcelParser(string excelPath)
        {
            Console.WriteLine("[1] ExcelParser");
            var provisions = ExcelParser.ParseAll(excelPath);
            var total = provisions.Count;
            Console.WriteLine($"      共解析 {total} 条条文");

            Test("总条数 > 0 且 4 个专业都存在", () =>
            {
                var discs = provisions.Select(p => p.Discipline).Distinct().OrderBy(x => x).ToList();
                Console.WriteLine($"      专业：{string.Join("/", discs)}");
                Assert(discs.Contains("建筑") && discs.Contains("给排水") && discs.Contains("暖通") && discs.Contains("国网"),
                    "应包含 建筑/给排水/暖通/国网");
            });

            Test("「设计说明」过滤计数合理（>0 且 < 总数）", () =>
            {
                int design = provisions.Count(p => p.DrawingTypesRaw.Contains("设计说明"));
                Console.WriteLine($"      设计说明适用：{design} 条 / {total} 条");
                Assert(design > 100 && design < total, $"设计说明计数异常：{design}");
            });

            Test("各专业均有「设计说明」适用条文", () =>
            {
                foreach (var g in provisions.GroupBy(p => p.Discipline))
                {
                    int design = g.Count(p => p.DrawingTypesRaw.Contains("设计说明"));
                    Assert(design > 0, $"专业 {g.Key} 无设计说明条文");
                    Console.WriteLine($"      {g.Key,-8} 总数 {g.Count(),-5} 设计说明 {design}");
                }
            });

            Test("国网 sheet 空行被跳过（无空白条文）", () =>
            {
                var bad = provisions.Where(p => p.Discipline == "国网" && string.IsNullOrWhiteSpace(p.ClauseText));
                Assert(!bad.Any(), "国网存在空白条文");
            });

            Test("SplitClause 常见格式", () =>
            {
                var (n1, t1) = ExcelParser.SplitClause("3.4.1 工业与民用建筑周围应设置消防车道");
                Assert(n1 == "3.4.1", $"n1={n1}");
                Assert(t1 == "工业与民用建筑周围应设置消防车道", $"t1={t1}");

                var (n2, _) = ExcelParser.SplitClause("3. 1. 4自建危险品库房");
                Assert(n2 == "3.1.4", $"n2={n2}");

                var (n3, _) = ExcelParser.SplitClause("6.3.4. 电气线路和各类管道");
                Assert(n3 == "6.3.4", $"n3={n3}");

                var (n4, t4) = ExcelParser.SplitClause("3.4.2 下列建筑应设置消防车道：\n1 高层厂房\n2 仓库");
                Assert(n4 == "3.4.2", $"n4={n4}");
                Assert(t4.Contains("高层厂房") && t4.Contains("\n1 高层厂房"), "多行子条应保留");

                var (n5, _) = ExcelParser.SplitClause("第八条 站址应避开");
                Assert(n5 == null, "国网「第八条」不应拆出数字条文号");
            });

            Test("图纸类型规范化", () =>
            {
                var t = ExcelParser.NormalizeTypes("平面，设计说明、详图");
                Assert(t == "平面、设计说明、详图", $"t={t}");
                var parts = ExcelParser.SplitTypes("平面,设计说明");
                Assert(parts.Count == 2 && parts.Contains("设计说明"), "应拆出设计说明");
            });
        }

        // ---------- SqliteProvisionStore ----------

        static void TestSqliteStore()
        {
            Console.WriteLine("[2] SqliteProvisionStore");
            var tmpDb = Path.Combine(Path.GetTempPath(), $"cc_test_{Guid.NewGuid():N}.db");
            try
            {
                var store = new SqliteProvisionStore(tmpDb);
                Test("ReplaceAll → QueryByDisciplines 回环 + LIKE 过滤", () =>
                {
                    var provs = new List<Provision>
                    {
                        NewProv("建筑", "《A》", "1.0.1", "条文甲", "设计说明"),
                        NewProv("建筑", "《A》", "1.0.2", "条文乙", "平面"),
                        NewProv("暖通", "《B》", "2.0.1", "条文丙", "平面,设计说明"),
                    };
                    store.Init();
                    store.ReplaceAll(provs, "test.xlsx");

                    var byArch = store.QueryByDisciplines(new[] { "建筑" }, "设计说明");
                    Assert(byArch.Count == 1 && byArch[0].ClauseNumber == "1.0.1", $"建筑设计说明应1条，实际{byArch.Count}");
                    Assert(byArch[0].Discipline == "建筑" && byArch[0].DrawingTypesRaw == "设计说明", "字段回环");

                    var all = store.QueryByDisciplines(new[] { "建筑", "暖通" }, "设计说明");
                    Assert(all.Count == 2, $"两专业设计说明应2条，实际{all.Count}");

                    var discs = store.GetDistinctDisciplines();
                    Assert(discs.Contains("建筑") && discs.Contains("暖通"), "DISTINCT 专业");

                    Assert(store.GetLastImport() != null, "应记录导入时间");
                    Assert(store.CheckIntegrity() == "ok", "完整性应为 ok");
                });

                Test("重导幂等（替换而非追加）", () =>
                {
                    store.ReplaceAll(new[] { NewProv("建筑", "《A》", "1.0.1", "新", "设计说明") }, "b.xlsx");
                    var again = store.QueryByDisciplines(new[] { "建筑" }, "设计说明");
                    Assert(again.Count == 1, "重导后应只剩 1 条");
                });
            }
            finally
            {
                try { if (File.Exists(tmpDb)) File.Delete(tmpDb); } catch { }
            }
        }

        static Provision NewProv(string disc, string code, string num, string text, string types) => new Provision
        {
            Discipline = disc,
            CodeName = code,
            ClauseNumber = num,
            ClauseText = text,
            DrawingTypesRaw = ExcelParser.NormalizeTypes(types),
        };

        // ---------- AiJsonParser ----------

        static void TestAiJsonParser()
        {
            Console.WriteLine("[3] AiJsonParser");

            Test("CleanJson：剥 Markdown 围栏", () =>
            {
                var raw = "```json\n{\"results\":[]}\n```";
                Assert(AiJsonParser.CleanJson(raw) == "{\"results\":[]}", AiJsonParser.CleanJson(raw));
            });

            Test("CleanJson：剥离前缀说明文字", () =>
            {
                var raw = "好的，以下是分析结果：\n{\"results\":[{\"clause_number\":\"1\"}]}";
                var clean = AiJsonParser.CleanJson(raw);
                Assert(clean.StartsWith("{") && clean.Contains("clause_number"), clean);
            });

            Test("CleanJson：括号失配尽量保留", () =>
            {
                var clean = AiJsonParser.CleanJson("{\"a\":1");
                Assert(clean == "{\"a\":1", clean);
            });

            Test("ParseVerdicts：results 数组", () =>
            {
                var json = "{\"results\":[{\"clause_number\":\"3.4.1\",\"verdict\":\"符合\",\"evidence\":\"x\",\"analysis\":\"y\",\"suggestion\":\"\"}]}";
                var vs = AiJsonParser.ParseVerdicts(json);
                Assert(vs.Count == 1 && vs[0].ClauseNumber == "3.4.1" && vs[0].Verdict == "符合", "results 解析");
            });

            Test("ParseVerdicts：裸数组 + 单对象", () =>
            {
                var arr = AiJsonParser.ParseVerdicts("[{\"clause_number\":\"1\",\"verdict\":\"未涉及\"}]");
                Assert(arr.Count == 1 && arr[0].Verdict == "未涉及", "裸数组");
                var single = AiJsonParser.ParseVerdicts("{\"clause_number\":\"1\",\"verdict\":\"不符合\"}");
                Assert(single.Count == 1 && single[0].Verdict == "不符合", "单对象");
            });

            Test("未知 verdict 强制归为 无法判断", () =>
            {
                var vs = AiJsonParser.ParseVerdicts("[{\"clause_number\":\"1\",\"verdict\":\"xxx\"}]");
                Assert(vs[0].Verdict == "无法判断", vs[0].Verdict);
                Assert(AiJsonParser.CoerceVerdict("符合要求") == "符合", "变体符合");
                Assert(AiJsonParser.CoerceVerdict(null) == "无法判断", "空值");
            });

            Test("非法 JSON 返回空列表", () =>
            {
                Assert(AiJsonParser.ParseVerdicts("完全不是JSON").Count == 0, "非法输入");
                Assert(AiJsonParser.ParseVerdicts("").Count == 0, "空输入");
            });
        }

        // ---------- PromptBuilder ----------

        static void TestPromptBuilder()
        {
            Console.WriteLine("[4] PromptBuilder");
            Test("提示词包含布局名、条文全文与条文号", () =>
            {
                var batch = new CheckBatch
                {
                    Discipline = "建筑",
                    CodeName = "《A》",
                    Items = { new CheckBatch.BatchItem { ClauseNumber = "3.4.1", ClauseText = "工业与民用建筑周围应设置消防车道" } },
                };
                var user = PromptBuilder.UserPrompt("总说明-01", "设计依据：xxx", batch);
                Assert(user.Contains("总说明-01"), "布局名");
                Assert(user.Contains("3.4.1"), "条文号");
                Assert(user.Contains("消防车道"), "条文全文");
                Assert(user.Contains("《A》"), "规范名");
            });

            Test("总说明截断：保留头尾", () =>
            {
                var text = new string('A', 100) + new string('B', 100);
                var t = PromptBuilder.TruncateSheetText(text, 120);
                Assert(t.Contains("……"), "应含截断标记");
                Assert(t.StartsWith("AAA") && t.EndsWith("BBB"), "头尾保留");
            });
        }

        static void TestConfig()
        {
            Console.WriteLine("[6] Config（provider 预设 / 写回 / 回退）");

            Test("AiProviderPresets 查表与兜底", () =>
            {
                Assert(AiProviderPresets.Find("deepseek").Model == "deepseek-v4-flash", "deepseek 模型");
                Assert(AiProviderPresets.Find("unknown").Key == "deepseek", "未知 key 兜底 deepseek");
                Assert(AiProviderPresets.Find("mimo").RequiresKey, "mimo 需要 API key");
                Assert(!AiProviderPresets.Find("ollama").RequiresKey, "本地服务无需 API key");
            });

            Test("ConfigWriter 保留相对路径 + AI 字段写回", () =>
            {
                var file = Path.Combine(Path.GetTempPath(), $"cc_cfg_{Guid.NewGuid():N}.json");
                try
                {
                    File.WriteAllText(file, "{\"api_key\":\"sk-old\",\"base_url\":\"https://api.deepseek.com/v1\","
                        + "\"model\":\"m1\",\"temperature\":0.3,\"max_tokens\":8192,"
                        + "\"max_sheet_chars\":12000,\"batch_size\":20,"
                        + "\"excel_path\":\"00-资料/a.xlsx\",\"db_path\":\"provisions.db\",\"log_dir\":\"logs\"}", new UTF8Encoding(false));

                    var saved = ConfigWriter.SaveAiSettings(file, new AiSettings
                    {
                        Provider = "ollama", ApiKey = "", BaseUrl = "http://localhost:11434/v1",
                        Model = "qwen2.5:7b",
                        Temperature = 0.3, MaxTokens = 8192, MaxSheetChars = 12000, BatchSize = 20,
                    });

                    var raw = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
                    Assert(raw["excel_path"]?.Value<string>() == "00-资料/a.xlsx", "相对 excel_path 保留");
                    Assert(raw["db_path"]?.Value<string>() == "provisions.db", "相对 db_path 保留");
                    Assert(raw["provider"]?.Value<string>() == "ollama", "provider 已写入");
                    Assert(raw["api_key"]?.Value<string>() == "", "空 api_key 原样写回");
                    Assert(raw["temperature"]?.Value<double>() == 0.3, "temperature 0.3 序列化");
                    Assert(saved.Provider == "ollama" && saved.BaseUrl == "http://localhost:11434/v1", "Load 回读");
                    Assert(Path.IsPathRooted(saved.DbPath), "Load 后路径已解析为绝对");
                }
                finally { try { if (File.Exists(file)) File.Delete(file); } catch { } }
            });

            Test("ConfigLoader 按 provider 填默认（空 base_url/model）", () =>
            {
                var file = Path.Combine(Path.GetTempPath(), $"cc_cfg2_{Guid.NewGuid():N}.json");
                try
                {
                    File.WriteAllText(file, "{\"provider\":\"ollama\",\"api_key\":\"sk-test\","
                        + "\"base_url\":\"\",\"model\":\"\",\"excel_path\":\"\"}", new UTF8Encoding(false));
                    var cfg = ConfigLoader.Load(file);
                    Assert(cfg.Provider == "ollama", "provider 保留");
                    Assert(cfg.BaseUrl == "http://localhost:11434/v1", "base_url 用 ollama 预设");
                    Assert(cfg.Model == "qwen2.5:7b", "model 用 ollama 预设");
                    Assert(cfg.ApiKey == "sk-test", "api_key 文件值优先");
                }
                finally { try { if (File.Exists(file)) File.Delete(file); } catch { } }
            });

            Test("向后兼容：无 provider 的旧 config 仍走 deepseek 默认", () =>
            {
                var file = Path.Combine(Path.GetTempPath(), $"cc_cfg3_{Guid.NewGuid():N}.json");
                try
                {
                    File.WriteAllText(file, "{\"base_url\":\"https://api.deepseek.com/v1\",\"model\":\"deepseek-v4-flash\","
                        + "\"excel_path\":\"\"}", new UTF8Encoding(false));
                    var cfg = ConfigLoader.Load(file);
                    Assert(cfg.Provider == "deepseek", "provider 默认 deepseek");
                    Assert(cfg.Model == "deepseek-v4-flash", "模型不变");
                }
                finally { try { if (File.Exists(file)) File.Delete(file); } catch { } }
            });

            Test("API key 环境变量回退：config 为空时取 DEEPSEEK_API_KEY", () =>
            {
                var file = Path.Combine(Path.GetTempPath(), $"cc_cfg_env1_{Guid.NewGuid():N}.json");
                var oldEnv = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
                try
                {
                    File.WriteAllText(file, "{\"api_key\":\"\"}", new UTF8Encoding(false));
                    Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "sk-from-env");
                    var cfg = ConfigLoader.Load(file);
                    Assert(cfg.ApiKey == "sk-from-env", "回退到环境变量");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", oldEnv);
                    try { if (File.Exists(file)) File.Delete(file); } catch { }
                }
            });

            Test("API key 环境变量回退：config 有值时优先用 config", () =>
            {
                var file = Path.Combine(Path.GetTempPath(), $"cc_cfg_env2_{Guid.NewGuid():N}.json");
                var oldEnv = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
                try
                {
                    File.WriteAllText(file, "{\"api_key\":\"sk-from-file\"}", new UTF8Encoding(false));
                    Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "sk-from-env");
                    var cfg = ConfigLoader.Load(file);
                    Assert(cfg.ApiKey == "sk-from-file", "文件值优先于环境变量");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", oldEnv);
                    try { if (File.Exists(file)) File.Delete(file); } catch { }
                }
            });

            Test("API key 环境变量回退：两者都为空则返回空", () =>
            {
                var file = Path.Combine(Path.GetTempPath(), $"cc_cfg_env3_{Guid.NewGuid():N}.json");
                var oldEnv1 = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
                var oldEnv2 = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
                try
                {
                    File.WriteAllText(file, "{\"api_key\":\"\"}", new UTF8Encoding(false));
                    Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
                    Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", null);
                    var cfg = ConfigLoader.Load(file);
                    Assert(cfg.ApiKey == "", "都为空则返回空");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", oldEnv1);
                    Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", oldEnv2);
                    try { if (File.Exists(file)) File.Delete(file); } catch { }
                }
            });
        }

        static string FindRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "config.json"))) return dir.FullName;
                dir = dir.Parent;
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
