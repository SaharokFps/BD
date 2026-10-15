using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using agent.Hardware;

namespace ConsoleApp2;

public class HardwareStorage
{
    private readonly string _connectionString;
    private const string DateFmt = "yyyy-MM-dd HH:mm:ss";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HardwareStorage(string dbPath = "admin.db")
    {
        _connectionString = $"Data Source={dbPath}";
    }

    // ============================================================
    // 1. ИНИЦИАЛИЗАЦИЯ — читает схему из schema.sql
    // ============================================================
    public void Initialize()
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");

        if (!File.Exists(schemaPath))
            throw new FileNotFoundException(
                $"Не найден файл схемы: {schemaPath}. Проверь свойство 'Копировать более позднюю версию' у schema.sql.",
                schemaPath);

        var sql = File.ReadAllText(schemaPath);
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException($"Файл {schemaPath} пустой.");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = new SqliteCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    // ============================================================
    // 2. ГЛАВНЫЙ МЕТОД — принять JSON-строку → сохранить
    // ============================================================
    public string SaveFromJson(string json, string domainName, string ipAddress, string platform = "windows")
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON пустой", nameof(json));

        HardwareInfo? hw;
        try
        {
            hw = JsonSerializer.Deserialize<HardwareInfo>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Ошибка парсинга JSON (строка {ex.LineNumber}): {ex.Message}", ex);
        }

        if (hw is null)
            throw new InvalidOperationException("JSON не соответствует схеме HardwareInfo.");

        return Save(hw, domainName, ipAddress, platform);
    }

    // ============================================================
    // 3. СОХРАНЕНИЕ — создаёт НОВЫЙ снимок, старые не трогает
    // ============================================================
    public string Save(HardwareInfo hw, string domainName, string ipAddress, string platform = "windows")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        var now = DateTime.Now;

        // 3.1 Найти или создать узел
        string nodeId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT NodeId FROM Nodes WHERE DomainName = $dn";
            cmd.Parameters.AddWithValue("$dn", domainName);
            nodeId = cmd.ExecuteScalar()?.ToString() ?? Guid.NewGuid().ToString();
        }

        // 3.2 UPSERT узла
        Exec(conn, tx, @"
            INSERT INTO Nodes (NodeId, DomainName, IpAddress, Platform, OsVersion, IsMaster, LastSeen)
            VALUES ($id, $dn, $ip, $pl, $osv, 0, $ls)
            ON CONFLICT(DomainName) DO UPDATE SET
                IpAddress = excluded.IpAddress,
                Platform  = excluded.Platform,
                OsVersion = excluded.OsVersion,
                LastSeen  = excluded.LastSeen;",
            ("$id", nodeId),
            ("$dn", domainName),
            ("$ip", ipAddress),
            ("$pl", platform),
            ("$osv", $"{hw.Os.Caption} {hw.Os.Version}".Trim()),
            ("$ls", now.ToString(DateFmt)));

        // 3.3 Создать НОВЫЙ снимок (не удаляя старые!)
        int snapshotId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO Snapshots (NodeId, CapturedAt)
                                VALUES ($nid, $cap);
                                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$nid", nodeId);
            cmd.Parameters.AddWithValue("$cap", now.ToString(DateFmt));
            snapshotId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // 3.4 Компоненты
        var source = platform == "windows" ? "wmi" : "sysfs";

        // CPU
        Exec(conn, tx, @"
            INSERT INTO Components (SnapshotId, NodeId, Category, Name, Vendor, SerialNumber, Specs, Source, CapturedAt)
            VALUES ($sid, $nid, 'CPU', $name, $vend, NULL, $specs, $src, $cap)",
            ("$sid", snapshotId),
            ("$nid", nodeId),
            ("$name", hw.Cpu.Name),
            ("$vend", ExtractVendor(hw.Cpu.Name)),
            ("$specs", JsonSerializer.Serialize(new
            {
                hw.Cpu.Cores,
                hw.Cpu.Threads,
                hw.Cpu.MaxClockGhz
            }, JsonOpts)),
            ("$src", source),
            ("$cap", now.ToString(DateFmt)));

        // RAM — каждая планка своей строкой
        foreach (var r in hw.RamModules)
            Exec(conn, tx, @"
                INSERT INTO Components (SnapshotId, NodeId, Category, Name, Vendor, SerialNumber, Specs, Source, CapturedAt)
                VALUES ($sid, $nid, 'RAM', $name, $vend, NULL, $specs, $src, $cap)",
                ("$sid", snapshotId),
                ("$nid", nodeId),
                ("$name", $"{r.Type} {r.SizeBytes / 1024 / 1024 / 1024}GB"),
                ("$vend", r.Manufacturer),
                ("$specs", JsonSerializer.Serialize(new
                {
                    r.SizeBytes,
                    r.SpeedMhz,
                    r.Type
                }, JsonOpts)),
                ("$src", source),
                ("$cap", now.ToString(DateFmt)));

        // Disks
        foreach (var d in hw.Disks)
            Exec(conn, tx, @"
                INSERT INTO Components (SnapshotId, NodeId, Category, Name, Vendor, SerialNumber, Specs, Source, CapturedAt)
                VALUES ($sid, $nid, 'Disk', $name, $vend, NULL, $specs, $src, $cap)",
                ("$sid", snapshotId),
                ("$nid", nodeId),
                ("$name", d.Model),
                ("$vend", ExtractVendor(d.Model)),
                ("$specs", JsonSerializer.Serialize(new
                {
                    d.SizeBytes,
                    d.InterfaceType,
                    d.MediaType
                }, JsonOpts)),
                ("$src", source),
                ("$cap", now.ToString(DateFmt)));

        // GPU
        foreach (var g in hw.Gpus)
            Exec(conn, tx, @"
                INSERT INTO Components (SnapshotId, NodeId, Category, Name, Vendor, SerialNumber, Specs, Source, CapturedAt)
                VALUES ($sid, $nid, 'GPU', $name, $vend, NULL, $specs, $src, $cap)",
                ("$sid", snapshotId),
                ("$nid", nodeId),
                ("$name", g.Name),
                ("$vend", ExtractVendor(g.Name)),
                ("$specs", JsonSerializer.Serialize(new
                {
                    g.VramBytes,
                    g.DriverVersion
                }, JsonOpts)),
                ("$src", source),
                ("$cap", now.ToString(DateFmt)));

        // Motherboard
        Exec(conn, tx, @"
            INSERT INTO Components (SnapshotId, NodeId, Category, Name, Vendor, SerialNumber, Specs, Source, CapturedAt)
            VALUES ($sid, $nid, 'Motherboard', $name, $vend, $sn, '{}', $src, $cap)",
            ("$sid", snapshotId),
            ("$nid", nodeId),
            ("$name", hw.Motherboard.Product),
            ("$vend", hw.Motherboard.Manufacturer),
            ("$sn", (object?)hw.Motherboard.SerialNumber ?? DBNull.Value),
            ("$src", source),
            ("$cap", now.ToString(DateFmt)));

        // 3.5 OsInfo — привязана к снимку
        Exec(conn, tx, @"
            INSERT INTO OsInfo (SnapshotId, NodeId, Caption, Version, BuildNumber, Kernel, Architecture, InstallDate, CapturedAt)
            VALUES ($sid, $nid, $cap, $ver, NULL, NULL, $arch, NULL, $ca)",
            ("$sid", snapshotId),
            ("$nid", nodeId),
            ("$cap", hw.Os.Caption),
            ("$ver", hw.Os.Version),
            ("$arch", hw.Os.Architecture),
            ("$ca", now.ToString(DateFmt)));

        tx.Commit();
        return nodeId;
    }

    // ============================================================
    // 4. ЧТЕНИЕ ДЛЯ АДМИНКИ
    // ============================================================

    /// <summary>Все узлы.</summary>
    public List<(string NodeId, string Domain, string Ip, string Platform, string OsVersion, DateTime LastSeen)> ListNodes()
    {
        var list = new List<(string, string, string, string, string, DateTime)>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT NodeId, DomainName, IpAddress, Platform, OsVersion, LastSeen
                            FROM Nodes ORDER BY LastSeen DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add((
                r.GetString(0),
                r.GetString(1),
                r.IsDBNull(2) ? "" : r.GetString(2),
                r.IsDBNull(3) ? "" : r.GetString(3),
                r.IsDBNull(4) ? "" : r.GetString(4),
                DateTime.ParseExact(r.GetString(5), DateFmt, CultureInfo.InvariantCulture)));
        }
        return list;
    }

    /// <summary>Комплектующие ТОЛЬКО последнего снимка узла.</summary>
    public List<(string Category, string Name, string Vendor, string Specs)> GetComponents(string domainName)
    {
        var list = new List<(string, string, string, string)>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var nodeId = GetNodeId(conn, domainName);
        if (nodeId is null) return list;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Category, Name, Vendor, Specs FROM Components
            WHERE SnapshotId = (
                SELECT SnapshotId FROM Snapshots
                WHERE NodeId = $id
                ORDER BY SnapshotId DESC LIMIT 1
            )
            ORDER BY Category, Id";
        cmd.Parameters.AddWithValue("$id", nodeId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((
                r.GetString(0),
                r.IsDBNull(1) ? "" : r.GetString(1),
                r.IsDBNull(2) ? "" : r.GetString(2),
                r.IsDBNull(3) ? "{}" : r.GetString(3)));
        return list;
    }

    /// <summary>Все снимки узла (для истории).</summary>
    public List<(int SnapshotId, string CapturedAt)> ListSnapshots(string domainName)
    {
        var list = new List<(int, string)>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var nodeId = GetNodeId(conn, domainName);
        if (nodeId is null) return list;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT SnapshotId, CapturedAt FROM Snapshots
                            WHERE NodeId = $id ORDER BY SnapshotId DESC";
        cmd.Parameters.AddWithValue("$id", nodeId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.GetString(1)));
        return list;
    }

    // ============================================================
    // 5. ХЕЛПЕРЫ
    // ============================================================
    private static string? GetNodeId(SqliteConnection conn, string domainName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT NodeId FROM Nodes WHERE DomainName = $dn";
        cmd.Parameters.AddWithValue("$dn", domainName);
        return cmd.ExecuteScalar()?.ToString();
    }

    private static string ExtractVendor(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown";
        return name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Unknown";
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx,
        string sql, params (string name, object value)[] pars)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in pars)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}