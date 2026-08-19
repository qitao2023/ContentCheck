using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using ContentCheck.Core.Models;

namespace ContentCheck.Core.Storage
{
    /// <summary>
    /// 规范条文的 SQLite 存储。插件运行时唯一的数据通路。
    /// ReplaceAll 整体重写（重导幂等）；meta 表记录导入来源与时间。
    /// </summary>
    public class SqliteProvisionStore
    {
        const string META_IMPORTED_FROM = "imported_from";
        const string META_IMPORTED_AT = "imported_at";

        public string DbPath { get; }

        public SqliteProvisionStore(string dbPath)
        {
            DbPath = dbPath;
        }

        static string ConnString(string path) =>
            "Data Source=" + path + ";Version=3;";

        public void Init()
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS provisions (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    discipline    TEXT NOT NULL,
    code_name     TEXT NOT NULL,
    clause_number TEXT,
    clause_text   TEXT NOT NULL,
    drawing_types TEXT
);
CREATE INDEX IF NOT EXISTS idx_prov_disc  ON provisions(discipline);
CREATE INDEX IF NOT EXISTS idx_prov_dtypes ON provisions(drawing_types);
CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value TEXT
);";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>整体重写条文数据（单事务，重导幂等），并记录导入来源/时间。</summary>
        public void ReplaceAll(IEnumerable<Provision> provisions, string importedFrom = null)
        {
            Init();
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM provisions;";
                    del.ExecuteNonQuery();
                }

                using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = @"INSERT INTO provisions
                        (discipline, code_name, clause_number, clause_text, drawing_types)
                        VALUES (@discipline, @code_name, @clause_number, @clause_text, @drawing_types);";
                    var pDisc = ins.Parameters.Add("@discipline", System.Data.DbType.String);
                    var pCode = ins.Parameters.Add("@code_name", System.Data.DbType.String);
                    var pNum = ins.Parameters.Add("@clause_number", System.Data.DbType.String);
                    var pText = ins.Parameters.Add("@clause_text", System.Data.DbType.String);
                    var pTypes = ins.Parameters.Add("@drawing_types", System.Data.DbType.String);

                    foreach (var p in provisions)
                    {
                        pDisc.Value = p.Discipline ?? "";
                        pCode.Value = p.CodeName ?? "";
                        pNum.Value = (object)p.ClauseNumber ?? DBNull.Value;
                        pText.Value = p.ClauseText ?? "";
                        pTypes.Value = p.DrawingTypesRaw ?? "";
                        ins.ExecuteNonQuery();
                    }
                }

                using (var stamp = conn.CreateCommand())
                {
                    stamp.Transaction = tx;
                    stamp.CommandText = @"INSERT OR REPLACE INTO meta(key, value) VALUES(@k, @v);";
                    var pk = stamp.Parameters.Add("@k", System.Data.DbType.String);
                    var pv = stamp.Parameters.Add("@v", System.Data.DbType.String);
                    pk.Value = META_IMPORTED_FROM; pv.Value = importedFrom ?? "";
                    stamp.ExecuteNonQuery();
                    pk.Value = META_IMPORTED_AT; pv.Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    stamp.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }

        /// <summary>按专业（可多选）+ 图纸类型子串过滤查询，按专业/规范/序号排序。</summary>
        public List<Provision> QueryByDisciplines(string[] disciplines, string typeFilter = "设计说明")
        {
            var list = new List<Provision>();
            if (disciplines == null || disciplines.Length == 0) return list;

            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                var places = string.Join(",", Enumerable.Range(0, disciplines.Length).Select(i => "@d" + i));
                cmd.CommandText = $@"SELECT id, discipline, code_name, clause_number, clause_text, drawing_types
                    FROM provisions
                    WHERE discipline IN ({places})
                    {LikeClause(typeFilter, cmd)}
                    ORDER BY discipline, code_name, id;";
                for (int i = 0; i < disciplines.Length; i++)
                    cmd.Parameters.AddWithValue("@d" + i, disciplines[i]);

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(ReadProvision(r));
                }
            }
            return list;
        }

        /// <summary>库里现有的全部专业（DISTINCT）。GUI 据此动态生成勾选框。</summary>
        public string[] GetDistinctDisciplines()
        {
            var list = new List<string>();
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT discipline FROM provisions ORDER BY discipline;";
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(r.GetString(0));
            }
            return list.ToArray();
        }

        public DateTime? GetLastImport()
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT value FROM meta WHERE key=@k;";
                cmd.Parameters.AddWithValue("@k", META_IMPORTED_AT);
                var v = cmd.ExecuteScalar() as string;
                return DateTime.TryParse(v, out var dt) ? (DateTime?)dt : null;
            }
        }

        /// <summary>库文件完整性自检（SQLite.Interop 部署问题会在 StartTransaction 前暴露）。</summary>
        public string CheckIntegrity()
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA integrity_check;";
                return cmd.ExecuteScalar() as string ?? "error";
            }
        }

        SQLiteConnection Open()
        {
            var conn = new SQLiteConnection(ConnString(DbPath));
            conn.Open();
            return conn;
        }

        static Provision ReadProvision(SQLiteDataReader r)
        {
            var types = r.IsDBNull(5) ? "" : r.GetString(5);
            return new Provision
            {
                Id = r.GetInt64(0),
                Discipline = r.GetString(1),
                CodeName = r.GetString(2),
                ClauseNumber = r.IsDBNull(3) ? null : r.GetString(3),
                ClauseText = r.GetString(4),
                DrawingTypesRaw = types,
                DrawingTypes = Excel.ExcelParser.SplitTypes(types),
            };
        }

        static string LikeClause(string typeFilter, SQLiteCommand cmd)
        {
            if (string.IsNullOrWhiteSpace(typeFilter)) return "";
            cmd.Parameters.AddWithValue("@typeFilter", "%" + typeFilter + "%");
            return "AND drawing_types LIKE @typeFilter";
        }
    }
}
