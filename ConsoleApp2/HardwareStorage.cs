using Microsoft.Data.Sqlite;
using agent.Hardware;

namespace ConsoleApp2;

public class HardwareStorage
{
    private readonly string _connectionString;

    public HardwareStorage(string dbPath = "hardware.db")
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var sql = @"
            CREATE TABLE IF NOT EXISTS Cpu (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT, Cores INTEGER, Threads INTEGER, MaxClockGhz REAL
            );
            CREATE TABLE IF NOT EXISTS Ram (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Manufacturer TEXT, SizeBytes INTEGER, SpeedMhz INTEGER, Type TEXT
            );
            CREATE TABLE IF NOT EXISTS Disks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Model TEXT, SizeBytes INTEGER, InterfaceType TEXT, MediaType TEXT
            );
            CREATE TABLE IF NOT EXISTS Gpus (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT, VramBytes INTEGER, DriverVersion TEXT
            );
            CREATE TABLE IF NOT EXISTS Motherboard (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Manufacturer TEXT, Product TEXT, SerialNumber TEXT
            );
            CREATE TABLE IF NOT EXISTS Os (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Caption TEXT, Version TEXT, Architecture TEXT,
                MachineName TEXT, UptimeTicks INTEGER
            );";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    public void Save(HardwareInfo hw)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        Exec(conn, tx,
            "INSERT INTO Cpu (Name, Cores, Threads, MaxClockGhz) VALUES ($n, $c, $t, $f)",
            ("$n", hw.Cpu.Name), ("$c", hw.Cpu.Cores),
            ("$t", hw.Cpu.Threads), ("$f", hw.Cpu.MaxClockGhz));

        foreach (var r in hw.RamModules)
            Exec(conn, tx,
                "INSERT INTO Ram (Manufacturer, SizeBytes, SpeedMhz, Type) VALUES ($m, $s, $sp, $t)",
                ("$m", r.Manufacturer), ("$s", r.SizeBytes),
                ("$sp", r.SpeedMhz), ("$t", r.Type));

        foreach (var d in hw.Disks)
            Exec(conn, tx,
                "INSERT INTO Disks (Model, SizeBytes, InterfaceType, MediaType) VALUES ($m, $s, $i, $mt)",
                ("$m", d.Model), ("$s", d.SizeBytes),
                ("$i", d.InterfaceType), ("$mt", d.MediaType));

        foreach (var g in hw.Gpus)
            Exec(conn, tx,
                "INSERT INTO Gpus (Name, VramBytes, DriverVersion) VALUES ($n, $v, $d)",
                ("$n", g.Name), ("$v", (object?)g.VramBytes ?? DBNull.Value), ("$d", g.DriverVersion));

        Exec(conn, tx,
            "INSERT INTO Motherboard (Manufacturer, Product, SerialNumber) VALUES ($m, $p, $s)",
            ("$m", hw.Motherboard.Manufacturer),
            ("$p", hw.Motherboard.Product),
            ("$s", hw.Motherboard.SerialNumber));

        Exec(conn, tx,
            "INSERT INTO Os (Caption, Version, Architecture, MachineName, UptimeTicks) VALUES ($c, $v, $a, $mn, $u)",
            ("$c", hw.Os.Caption), ("$v", hw.Os.Version),
            ("$a", hw.Os.Architecture), ("$mn", hw.Os.MachineName),
            ("$u", hw.Os.Uptime.Ticks));

        tx.Commit();
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