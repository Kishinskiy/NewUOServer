using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Server
{
    public static class ScriptCompiler
    {
        // Список успешно загруженных динамических сборок
        public static List<Assembly> ScriptsAssemblies { get; } = new List<Assembly>();

        /// <summary>
        /// Сканирует папку, компилирует все файлы .cs на лету и загружает их в память
        /// </summary>
        public static bool CompileDynamicScripts(string scriptsPath)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Компилятор: Сканирование папки '{scriptsPath}'...");
            Console.ResetColor();

            if (!Directory.Exists(scriptsPath))
            {
                Directory.CreateDirectory(scriptsPath);
                Console.WriteLine($"Компилятор: Папка '{scriptsPath}' не найдена и была создана. Скрипты отсутствуют.");
                return true; 
            }

            // 1. Собираем все файлы *.cs в папке CustomScripts и её подпапках
            string[] filePaths = Directory.GetFiles(scriptsPath, "*.cs", SearchOption.AllDirectories);

            if (filePaths.Length == 0)
            {
                Console.WriteLine("Компилятор: В папке кастомных скриптов не найдено файлов .cs.");
                return true;
            }

            Console.WriteLine($"Компилятор: Найдено файлов для компиляции: {filePaths.Length}");

            // 2. Создаем абстрактные синтаксические деревья (Syntax Trees) для C# 10
            var syntaxTrees = filePaths.Select(path => 
                CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), 
                    new CSharpParseOptions(LanguageVersion.CSharp10), 
                    path
                )
            ).ToList();

            // 3. Собираем ссылки на библиотеки (References)
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location), // Наш Server.dll / Server.exe
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),        // System.Private.CoreLib
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),       // System.Console
                MetadataReference.CreateFromFile(typeof(System.IO.File).Assembly.Location) // System.Runtime / IO
            };

            // Автоматически добавляем зависимости, которые загружены в текущий домен приложения
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            // 4. Настраиваем параметры компиляции в памяти
            string assemblyName = $"ServUO.CustomScripts_{Guid.NewGuid():N}";
            var compilationOptions = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, 
                optimizationLevel: OptimizationLevel.Release, 
                allowUnsafe: true 
            );

            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees,
                references.Distinct(), 
                compilationOptions
            );

            // 5. Компилируем сборку напрямую в поток памяти (MemoryStream)
            using var ms = new MemoryStream();
            var result = compilation.Emit(ms);

            if (!result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ Компилятор: Ошибка компиляции кастомных скриптов!");
                
                var failures = result.Diagnostics.Where(diagnostic => 
                    diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);

                foreach (var diagnostic in failures)
                {
                    Console.WriteLine($"\t{diagnostic.Id}: {diagnostic.GetMessage()} (Файл: {diagnostic.Location.GetLineSpan().Path}, Строка: {diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1})");
                }
                Console.ResetColor();
                return false;
            }

            // 6. Загружаем сборку из памяти в контекст сервера
            ms.Seek(0, SeekOrigin.Begin);
            Assembly compiledAssembly = Assembly.Load(ms.ToArray());
            ScriptsAssemblies.Add(compiledAssembly);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✔ Компилятор: Кастомные скрипты успешно скомпилированы и загружены.");
            Console.ResetColor();
            return true;
        }

        /// <summary>
        /// Вызывает определенный метод во всех классах загруженных динамических скриптов
        /// </summary>
        public static void Invoke(string methodName)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Ядро: Вызов глобального этапа '{methodName}' для кастомных скриптов...");
            Console.ResetColor();

            int invokedCount = 0;

            foreach (Assembly assembly in ScriptsAssemblies)
            {
                try
                {
                    Type[] types = assembly.GetTypes();

                    foreach (Type type in types)
                    {
                        MethodInfo? method = type.GetMethod(
                            methodName, 
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic
                        );

                        if (method != null)
                        {
                            if (method.GetParameters().Length == 0)
                            {
                                method.Invoke(null, null);
                                invokedCount++;
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"Внимание: Метод {type.FullName}.{methodName} должен быть без параметров!");
                                Console.ResetColor();
                            }
                        }
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    foreach (var loadError in ex.LoaderExceptions)
                    {
                        if (loadError != null)
                            Console.WriteLine($"Ошибка загрузки типов: {loadError.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Ошибка при выполнении {methodName}: {ex.InnerException?.Message ?? ex.Message}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine($"Ядро: Этап '{methodName}' завершен. Вызвано методов: {invokedCount}");
        }
    }
}
