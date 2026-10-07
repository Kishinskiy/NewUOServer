using System;
using System.Net.Sockets;

namespace Server.Network
{
    public class NetState
    {
        public Socket Socket { get; }
        public string Address { get; }
        
        private readonly byte[] _buffer = new byte[2048];
        
        // Флаг: получили ли мы первые 4 байта инициализации от клиента?
        private bool _hasSeed; 

        public NetState(Socket socket)
        {
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            Address = socket.RemoteEndPoint?.ToString() ?? "Unknown";
        }

        public void StartReceive()
        {
            try
            {
                Socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, OnReceive, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Сеть] Ошибка начала приема данных от {Address}: {ex.Message}");
                Dispose();
            }
        }

        private void OnReceive(IAsyncResult ar)
        {
            try
            {
                int bytesRead = Socket.EndReceive(ar);

                if (bytesRead > 0)
                {
                    // Логируем сырые данные
                    string hexData = BitConverter.ToString(_buffer, 0, bytesRead);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Сеть] Получено {bytesRead} байт от {Address}: {hexData}");
                    Console.ResetColor();

                    // Создаем ридер для обработки пришедшего массива байт
                    var reader = new PacketReader(_buffer);

                    // 1. Если Seed еще не обрабатывали, проверяем пакет инициализации
                    if (!_hasSeed)
                    {
                        if (bytesRead >= 4)
                        {
                            byte firstByte = _buffer[0];

                            // Если первый байт 0xEF — это современный комбинированный пакет инициализации (83 байта)
                            if (firstByte == 0xEF && bytesRead >= 83)
                            {
                                _hasSeed = true;
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"[Сеть] Клиент {Address} прислал комбинированный пакет 0xEF. Парсим авторизацию...");
                                Console.ResetColor();

                                // Согласно структуре пакета 0xEF:
                                // Пропускаем первые 21 байт служебных данных шифрования и Seed
                                for (int i = 0; i < 21; i++) reader.ReadByte();

                                // 22-й байт — это начало пакета авторизации (должен быть 0x80)
                                byte innerPacketId = reader.ReadByte();
                                if (innerPacketId == 0x80)
                                {
                                    ProcessLoginPacket(reader);
                                }
                            }
                            // Если клиент старый и прислал чистый Seed (4 байта)
                            else
                            {
                                _hasSeed = true;
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"[Сеть] Клиент {Address} прислал чистый Seed. Ожидаем следующий пакет...");
                                Console.ResetColor();
                            }
                        }
                    }
                    // 2. Если Seed уже был получен ранее, обрабатываем как обычный игровой пакет
                    else
                    {
                        byte packetId = reader.ReadByte();
                        if (packetId == 0x80)
                        {
                            ProcessLoginPacket(reader);
                        }
                    }

                    // Продолжаем слушать сокет
                    Socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, OnReceive, null);
                }
                else
                {
                    Console.WriteLine($"[Сеть] Клиент {Address} отключился.");
                    Dispose();
                }
            }
            catch (Exception)
            {
                Console.WriteLine($"[Сеть] Клиент {Address} принудительно разорвал соединение.");
                Dispose();
            }
        }

        // Выносим чтение логина и пароля в отдельный чистый метод
        private void ProcessLoginPacket(PacketReader reader)
        {
            // Считываем фиксированные строки по 30 байт
            string accountName = reader.ReadString(30);
            string password = reader.ReadString(30);

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"[Авторизация] Успешно расшифровано из потока!");
            Console.WriteLine($"\tЛогин: '{accountName}'");
            Console.WriteLine($"\tПароль: '{password}'");
            Console.ResetColor();

            // СЮДА мы на следующем шаге вставим отправку ответа:
            // SendLoginResponse();
        }
        public void Dispose()
        {
            try
            {
                if (Socket.Connected)
                {
                    Socket.Shutdown(SocketShutdown.Both);
                    Socket.Close();
                }
            }
            catch { }
        }
    }
}
