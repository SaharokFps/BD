using System.Text.Json;
using System.Text.Json.Serialization;
using ConsoleApp2;
using agent.Hardware;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// ============================================================
// 1. Инициализация БД
// ============================================================
var storage = new HardwareStorage("admin.db");
storage.Initialize();
Console.WriteLine("БД готова.\n");

// ============================================================
// 2. Определяем путь к JSON
// ============================================================
string jsonPath;

if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
{
    jsonPath = args[0];
    Console.WriteLine($"Путь из аргументов: {jsonPath}");
}
else
{
    Console.Write("Введите путь к JSON-файлу (Enter — по умолчанию hardware.json): ");
    var input = Console.ReadLine() ?? "";
    jsonPath = input.Trim().Trim('"', '\'');

    if (string.IsNullOrWhiteSpace(jsonPath))
        jsonPath = "hardware.json";
}

if (!File.Exists(jsonPath))
{
    Console.WriteLine($"❌ Файл не найден: {jsonPath}");
    Console.WriteLine("\nНажмите любую клавишу для выхода...");
    Console.ReadKey();
    return;
}

var json = File.ReadAllText(jsonPath);
Console.WriteLine($"✅ Прочитан {jsonPath} ({json.Length} байт)\n");

// ============================================================
// 3. Достаём MachineName из JSON — чтобы понять, ЧЕЙ это ПК
// ============================================================
var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
HardwareInfo? hw;
try
{
    hw = JsonSerializer.Deserialize<HardwareInfo>(json, opts);
}
catch (JsonException ex)
{
    Console.WriteLine($"❌ Ошибка парсинга JSON (строка {ex.LineNumber}): {ex.Message}");
    Console.ReadKey();
    return;
}

if (hw is null)
{
    Console.WriteLine("❌ Не удалось распарсить JSON в HardwareInfo.");
    Console.ReadKey();
    return;
}

// Формируем домен из MachineName
var machineName = hw.Os.MachineName?.Trim() ?? "";
if (string.IsNullOrWhiteSpace(machineName))
{
    Console.WriteLine("⚠️ В JSON нет Os.MachineName — используйте другой файл.");
    Console.ReadKey();
    return;
}

var domain = machineName.ToLower() + ".corp.local";
var ip = "192.168.1.42";        // по-хорошему — из сетевого запроса
var platform = "windows";

Console.WriteLine($"Обнаружен ПК: {machineName}");
Console.WriteLine($"Домен:        {domain}");
Console.WriteLine($"IP:           {ip}\n");

// ============================================================
// 4. Сохранение — создаётся новый снимок под ЭТИМ узлом
// ============================================================
string nodeId;
try
{
    nodeId = storage.SaveFromJson(json, domain, ip, platform);
    Console.WriteLine($"✅ Снимок от {domain} сохранён. NodeId = {nodeId}\n");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Ошибка: {ex.Message}");
    Console.WriteLine("\nНажмите любую клавишу для выхода...");
    Console.ReadKey();
    return;
}

// ============================================================
// 5. Показываем всё, что в БД
// ============================================================
Console.WriteLine("═══ Все узлы в БД ═══");
foreach (var n in storage.ListNodes())
{
    Console.WriteLine($"  {n.Domain,-25} {n.Ip,-15} {n.Platform,-8} {n.OsVersion}");
    Console.WriteLine($"  LastSeen: {n.LastSeen:dd.MM.yyyy HH:mm:ss}\n");
}

Console.WriteLine($"═══ История снимков {domain} ═══");
foreach (var s in storage.ListSnapshots(domain))
    Console.WriteLine($"  Снимок #{s.SnapshotId}  от {s.CapturedAt}");

Console.WriteLine($"\n═══ Компоненты (последний снимок) {domain} ═══");
foreach (var c in storage.GetComponents(domain))
{
    var name = c.Name.Length > 45 ? c.Name.Substring(0, 42) + "..." : c.Name;
    Console.WriteLine($"  [{c.Category,-12}] {name,-45} {c.Specs}");
}

Console.WriteLine("\nГотово. Нажмите любую клавишу для выхода...");
Console.ReadKey();