using ConsoleApp2;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// 1. Инициализация БД
var storage = new HardwareStorage("admin.db");
storage.Initialize();
Console.WriteLine("БД готова.\n");

// 2. Определяем путь к JSON
string jsonPath;

if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
{
    // Путь передан аргументом командной строки
    jsonPath = args[0];
    Console.WriteLine($"Путь из аргументов: {jsonPath}");
}
else
{
    // Спрашиваем у пользователя
    Console.Write("Введите путь к JSON-файлу (Enter — по умолчанию hardware.json): ");
    var input = Console.ReadLine() ?? "";
    jsonPath = input.Trim().Trim('"', '\'');

    if (string.IsNullOrWhiteSpace(jsonPath))
        jsonPath = "hardware.json";
}

// Проверка файла
if (!File.Exists(jsonPath))
{
    Console.WriteLine($"❌ Файл не найден: {jsonPath}");
    Console.WriteLine("Нажмите любую клавишу для выхода...");
    Console.ReadKey();
    return;
}

var json = File.ReadAllText(jsonPath);
Console.WriteLine($"✅ Прочитан {jsonPath} ({json.Length} байт)\n");

// 3. Метаданные — их нет в JSON, передаём снаружи
var domain = "stf.corp.local";
var ip = "192.168.1.42";
var platform = "windows";

// 4. Сохранение
string nodeId;
try
{
    nodeId = storage.SaveFromJson(json, domain, ip, platform);
    Console.WriteLine($"✅ Снимок от {domain} сохранён. NodeId = {nodeId}\n");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Ошибка: {ex.Message}");
    Console.WriteLine("Нажмите любую клавишу для выхода...");
    Console.ReadKey();
    return;
}

// 5. Показываем, что в БД
Console.WriteLine("═══ Узлы в БД ═══");
foreach (var n in storage.ListNodes())
{
    Console.WriteLine($"  {n.Domain,-22} {n.Ip,-15} {n.Platform,-8} {n.OsVersion}");
    Console.WriteLine($"  LastSeen: {n.LastSeen:dd.MM.yyyy HH:mm:ss}");
    Console.WriteLine();
}

Console.WriteLine($"═══ Компоненты {domain} ═══");
foreach (var c in storage.GetComponents(domain))
{
    var name = c.Name.Length > 45 ? c.Name.Substring(0, 42) + "..." : c.Name;
    Console.WriteLine($"  [{c.Category,-12}] {name,-45} {c.Specs}");
}

Console.WriteLine("\nГотово. Нажмите любую клавишу для выхода...");
Console.ReadKey();