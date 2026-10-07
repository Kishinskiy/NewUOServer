using System;
using System.IO;
using System.Runtime;
using System.Threading;
using Server.Network;

namespace Server
{
    public static class Core
    {
        public static bool IsDebug { get; private set; }
        public static bool IsService { get; private set; }
        public static string BaseDirectory { get; private set; } = string.Empty;

        // Точка входа в программу (классический метод Main)
        public static void Main(string[] args)
        {
            // 1. Настройка рабочего каталога сервера
            string exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь к EXE.");
            BaseDirectory = Path.GetDirectoryName(exePath) ?? Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(BaseDirectory);

            // Настройка режима отладки по умолчанию из директив компилятора
#if DEBUG
            IsDebug = true;
#endif

            // 2. Глобальный перехват критических ошибок (чтобы сервер не падал молча)
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Критическая ошибка] {(e.IsTerminating ? "Сервер падает:" : "Внимание:")}");
                Console.WriteLine(e.ExceptionObject);
                Console.ResetColor();

                if (e.IsTerminating)
                {
                    // В будущем: Безопасное сохранение мира перед закрытием (World.Save())
                    Environment.Exit(1);
                }
            };

            // 3. Парсинг аргументов командной строки
            foreach (string arg in args)
            {
                if (string.Equals(arg, "-debug", StringComparison.OrdinalIgnoreCase)) IsDebug = true;
                if (string.Equals(arg, "-service", StringComparison.OrdinalIgnoreCase)) IsService = true;
            }

            // 4. Вывод системной информации
            PrintSystemInfo();

            // 5. Загрузка конфигурации
            Console.WriteLine("Ядро: Загрузка конфигурации...");
            // Config.Load(); 

            // 6. Динамическая компиляция кастомных скриптов через Roslyn
            string customScriptsPath = Path.Combine(BaseDirectory, "../../CustomScripts");
            if (!ScriptCompiler.CompileDynamicScripts(customScriptsPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Ядро: Запуск остановлен из-за ошибок компиляции в кастомных скриптах.");
                Console.ResetColor();
                Console.ReadLine();
                return;
            }

            // 7. Этап первоначальной настройки скриптов
            ScriptCompiler.Invoke("Configure");

            // 8. Инициализация игровых систем и мира
            Console.WriteLine("Ядро: Инициализация игровых подсистем...");
            // Region.Load();
            // World.Load();

            // 9. Этап постобработки скриптов (когда мир уже готов)
            ScriptCompiler.Invoke("Initialize");

            // 10. Запуск сетевой подсистемы
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Ядро: Инициализация сети...");
            Console.ResetColor();
            
            MessagePump.Start(2593); // Слушаем порт 2593

            // 11. Главный игровой цикл (World Loop)
            RunWorldLoop();
        }

        private static void PrintSystemInfo()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== Модернизированный ServUO (C# 10 / .NET 6) ===");
            Console.WriteLine($"Режим: {(IsDebug ? "Отладка (Debug)" : "Релиз (Release)")}");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"ОС: {Environment.OSVersion}");
            Console.WriteLine($"Процессоры: {Environment.ProcessorCount}x ({(Environment.Is64BitProcess ? "64-bit" : "32-bit")})");
            Console.WriteLine($"Режим GC: {(GCSettings.IsServerGC ? "Server GC (Оптимизировано)" : "Workstation GC")}");
            Console.ResetColor();
        }

        private static void RunWorldLoop()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Ядро: Сервер успешно запущен и готов к работе.");
            Console.ResetColor();

            bool serverRunning = true;
            var signal = new AutoResetEvent(true);

            while (serverRunning)
            {
                signal.WaitOne(1); // Защита от 100% утилизации процессора

                // Сюда будут привязаны тики движка:
                // Mobile.ProcessDeltaQueue();
                // Item.ProcessDeltaQueue();
                // Timer.Slice();
            }
        }
    }
}
