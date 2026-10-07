using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;

namespace Server.Network
{
    public static class MessagePump
    {
        private static Socket? _listener;
        
        // Список всех активных подключений на сервере
        public static ConcurrentBag<NetState> Listeners { get; } = new ConcurrentBag<NetState>();

        /// <summary>
        /// Запуск прослушивания порта
        /// </summary>
        public static void Start(int port = 2593)
        {
            try
            {
                _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                
                // Разрешаем повторное использование порта, чтобы избежать ошибок "Address already in use" при частых перезапусках
                _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                
                _listener.Bind(new IPEndPoint(IPAddress.Any, port));
                _listener.Listen(100); // Очередь ожидающих подключений

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Служба сети] Сервер слушает порт {port}...");
                Console.ResetColor();

                // Начинаем асинхронно принимать входящие подключения
                _listener.BeginAccept(OnAccept, null);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Служба сети] КРИТИЧЕСКАЯ ОШИБКА: Не удалось запустить порт {port}. Причина: {ex.Message}");
                Console.ResetColor();
                throw;
            }
        }

        private static void OnAccept(IAsyncResult ar)
        {
            if (_listener == null) return;

            try
            {
                // Завершаем операцию принятия подключения и получаем сокет клиента
                Socket clientSocket = _listener.EndAccept(ar);
                
                NetState ns = new NetState(clientSocket);
                Listeners.Add(ns);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[Служба сети] Новое подключение со стороны: {ns.Address}");
                Console.ResetColor();

                // Запускаем у этого клиента цикл чтения данных
                ns.StartReceive();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Служба сети] Ошибка при обработке входящего подключения: {ex.Message}");
            }
            finally
            {
                // Важно! Продолжаем слушать порт дальше для следующих клиентов
                try
                {
                    _listener.BeginAccept(OnAccept, null);
                }
                catch { }
            }
        }
    }
}
