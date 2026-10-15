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

    
    public void Initialize()
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");

        if (!File.Exists(schemaPath))
            throw new FileNotFoundException(
                $"Не найден файл схемы: {schemaPath}. Проверь свойство 'Копировать, если новее' у schema.sql.",
                schemaPath);

        var sql = File.ReadAllText(schemaPath);
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException($"Файл {schemaPath} пустой.");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = new SqliteCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    
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

    
    public string Save(HardwareInfo hw, string domainName, string ipAddress, string platform = "windows")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        var now = DateTime.Now;

        
        string nodeId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT NodeId FROM Nodes WHERE DomainName = $dn";
            cmd.Parameters.AddWithValue("$dn", domainName);
            nodeId = cmd.ExecuteScalar()?.ToString() ?? Guid.NewGuid().ToString();
        }

        
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

        
        Exec(conn, tx, "DELETE FROM Components WHERE NodeId = $id", ("$id", nodeId));
        Exec(conn, tx, "DELETE FROM OsInfo     WHERE NodeId = $id", ("$id", nodeId));

        
        var source = platform == "windows" ? "wmi" : "sysfs";

        
        Exec(conn, tx, @"
            INSERT INTO Components (NodeId, Category, Name, Vendor, SerialNumber, Specs, Source, CapturedAt)
            VALUES ($nid, 'CPU', $name, $vend, NULL, $specs, $src, $cap)",
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

        
        foreach (var r in hw.RamModules)
            Exec(conn, tx, @"
                INSERT INTO Components (NodeId, Category, Name, Vendor, SerialNumber, Specs, Source, CapturedAt)
                VALUES ($nid, 'RAM', $name, $vend, NULL, $specs, $src, $cap)",
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

        
        foreach (var d in hw.Disks)
            Exec(conn, tx, @"
                INSERT INTO Components (NodeId, Category, Name, Vendor, SerialNumber, Specs, Source, CapturedAt)
                VALUES ($nid, 'Disk', $name, $vend, NULL, $specs, $src, $cap)",
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

        
        foreach (var g in hw.Gpus)
            Exec(conn, tx, @"
                INSERT INTO Components (NodeId, Category, Name, Vendor, SerialNumber, Specs, Source, CapturedAt)
                VALUES ($nid, 'GPU', $name, $vend, NULL, $specs, $src, $cap)",
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

        
        Exec(conn, tx, @"
            INSERT INTO Components (NodeId, Category, Name, Vendor, SerialNumber, Specs, Source, CapturedAt)
            VALUES ($nid, 'Motherboard', $name, $vend, $sn, '{}', $src, $cap)",
            ("$nid", nodeId),
            ("$name", hw.Motherboard.Product),
            ("$vend", hw.Motherboard.Manufacturer),
            ("$sn", (object?)hw.Motherboard.SerialNumber ?? DBNull.Value),
            ("$src", source),
            ("$cap", now.ToString(DateFmt)));

        
        Exec(conn, tx, @"
            INSERT INTO OsInfo (NodeId, Caption, Version, BuildNumber, Kernel, Architecture, InstallDate, CapturedAt)
            VALUES ($nid, $cap, $ver, NULL, NULL, $arch, NULL, $ca)",
            ("$nid", nodeId),
            ("$cap", hw.Os.Caption),
            ("$ver", hw.Os.Version),
            ("$arch", hw.Os.Architecture),
            ("$ca", now.ToString(DateFmt)));

        tx.Commit();
        return nodeId;
    }

    
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
                r.GetString(0), r.GetString(1),
                r.IsDBNull(2) ? "" : r.GetString(2),
                r.IsDBNull(3) ? "" : r.GetString(3),
                r.IsDBNull(4) ? "" : r.GetString(4),
                DateTime.ParseExact(r.GetString(5), DateFmt, CultureInfo.InvariantCulture)));
        }
        return list;
    }

    public List<(string Category, string Name, string Vendor, string Specs)> GetComponents(string domainName)
    {
        var list = new List<(string, string, string, string)>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var nodeId = GetNodeId(conn, domainName);
        if (nodeId is null) return list;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Category, Name, Vendor, Specs FROM Components
                            WHERE NodeId = $id ORDER BY Category, Id";
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