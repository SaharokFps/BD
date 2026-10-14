using ConsoleApp2;
using agent.Hardware;

namespace ConsoleApp2
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var storage = new HardwareStorage("hardware.db");
            storage.Initialize();

            Console.WriteLine("Сбор данных о железе...\n");
            var provider = new WindowsHardwareProvider();
            var hw = provider.Collect();

            storage.Save(hw);
            Console.WriteLine("Данные сохранены в hardware.db");
        }
    }
}