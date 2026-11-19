using DrawAudit.Extraction;
using Microsoft.Data.Sqlite;

namespace DrawAudit.Pipeline;

// Persists extracted drawings + BOM rows with provenance so the hierarchy builder can query within
// a single Sub. Re-saving replaces the rows for each file (keyed by file_path).
public static class DrawingStore
{
    public static void Save(string dbPath, IEnumerable<DrawingResult> drawings)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        Init(conn);

        using var tx = conn.BeginTransaction();
        foreach (var d in drawings)
        {
            Delete(conn, tx, d.FilePath);
            InsertDrawing(conn, tx, d);
            foreach (var b in d.Bom)
                InsertBom(conn, tx, d, b);
        }
        tx.Commit();
    }

    static void Init(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS drawings (
                file_path      TEXT PRIMARY KEY,
                file           TEXT,
                sub            TEXT,
                file_hash      TEXT,
                extracted_at   TEXT,
                drawing_no     TEXT,
                revision       TEXT,
                page           TEXT,
                title          TEXT,
                tot_weight     TEXT,
                iso5817        TEXT,
                mentions_ndt   INTEGER,
                mentions_seal  INTEGER
            );
            CREATE TABLE IF NOT EXISTS bom_items (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path         TEXT,
                parent_drawing_no TEXT,
                parent_sub        TEXT,
                amount            TEXT,
                det_no            TEXT,
                child_drawing_no  TEXT,
                description       TEXT,
                specification     TEXT,
                net_weight        TEXT,
                is_sub_drawing    INTEGER
            );
            CREATE INDEX IF NOT EXISTS ix_drawings_sub ON drawings(sub, drawing_no);
            CREATE INDEX IF NOT EXISTS ix_bom_sub ON bom_items(parent_sub, child_drawing_no);
            """;
        cmd.ExecuteNonQuery();
    }

    static void Delete(SqliteConnection conn, SqliteTransaction tx, string filePath)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM drawings WHERE file_path=$p; DELETE FROM bom_items WHERE file_path=$p;";
        cmd.Parameters.AddWithValue("$p", filePath);
        cmd.ExecuteNonQuery();
    }

    static void InsertDrawing(SqliteConnection conn, SqliteTransaction tx, DrawingResult d)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO drawings
              (file_path, file, sub, file_hash, extracted_at, drawing_no, revision, page, title, tot_weight, iso5817, mentions_ndt, mentions_seal)
            VALUES
              ($file_path, $file, $sub, $hash, $at, $dno, $rev, $page, $title, $wt, $iso, $ndt, $seal);
            """;
        cmd.Parameters.AddWithValue("$file_path", d.FilePath);
        cmd.Parameters.AddWithValue("$file", d.File);
        cmd.Parameters.AddWithValue("$sub", (object?)d.Sub ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", d.FileHash);
        cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$dno", (object?)d.DrawingNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rev", (object?)d.Revision ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$page", (object?)d.Page ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", (object?)d.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$wt", (object?)d.TotalWeight ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$iso", (object?)d.Iso5817Class ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ndt", d.MentionsNdt ? 1 : 0);
        cmd.Parameters.AddWithValue("$seal", d.MentionsSealWeld ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    static void InsertBom(SqliteConnection conn, SqliteTransaction tx, DrawingResult d, BomRow b)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO bom_items
              (file_path, parent_drawing_no, parent_sub, amount, det_no, child_drawing_no, description, specification, net_weight, is_sub_drawing)
            VALUES
              ($file_path, $pdno, $psub, $amt, $det, $cdno, $desc, $spec, $wt, $issub);
            """;
        cmd.Parameters.AddWithValue("$file_path", d.FilePath);
        cmd.Parameters.AddWithValue("$pdno", (object?)d.DrawingNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$psub", (object?)d.Sub ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$amt", b.Amount);
        cmd.Parameters.AddWithValue("$det", b.DetNo);
        cmd.Parameters.AddWithValue("$cdno", b.DrawingNo);
        cmd.Parameters.AddWithValue("$desc", b.Description);
        cmd.Parameters.AddWithValue("$spec", b.Specification);
        cmd.Parameters.AddWithValue("$wt", b.NetWeight);
        cmd.Parameters.AddWithValue("$issub", b.IsSubDrawing ? 1 : 0);
        cmd.ExecuteNonQuery();
    }
}
