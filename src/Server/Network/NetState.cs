using System;
using System.IO;
using System.Net.Sockets;

namespace Server.Network
{
    public class NetState
    {
        public Socket Socket { get; }
        public string Address { get; }

        private readonly byte[] _buffer = new byte[2048];
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
                    string hexData = BitConverter.ToString(_buffer, 0, bytesRead);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Сеть] Получено {bytesRead} байт от {Address}: {hexData}");
                    Console.ResetColor();

                    var reader = new PacketReader(_buffer);
                    byte firstByte = _buffer[0];

                    // ФАЗА 1: Клиент только подключился (проверяем Seed или склеенный Логин 0xEF)
                    // ФАЗА 1: Клиент только открыл сокет (проверяем Seed, логин 0xEF или игровой реконнект)
                    if (!_hasSeed)
                    {
                        if (bytesRead >= 3) // Уменьшаем до 3 байт, чтобы ловить пакет 0xA0 (3 байта)
                        {
                            // КЕЙС А: Повторный игровой коннект (TazUO прислал Seed + 0x91, либо сразу игровой пакет)
                            if ((bytesRead >= 69 && _buffer[4] == 0x91) || firstByte == 0x91)
                            {
                                _hasSeed = true;
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"[Сеть] Клиент {Address} перешел на игровой поток (0x91)!");
                                Console.ResetColor();

                                int skipBytes = (firstByte == 0x91) ? 1 : 5;
                                for (int i = 0; i < skipBytes; i++) reader.ReadByte();
                                
                                ProcessGameLogin(reader);
                            }
                            // КЕЙС Б: Уникальный случай TazUO/Enhanced Razor — при реконнекте они дублируют пакет 0xA0 (3 байта) 
                            // прямо в новый сокет, чтобы подтвердить стейт гейм-сервера!
                            else if (firstByte == 0xA0 && bytesRead == 3)
                            {
                                _hasSeed = true;
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"[Сеть] Перехвачен повторный верификационный пакет 0xA0 на игровом сокете!");
                                Console.ResetColor();

                                // Сразу же активируем гейм-поток для этого сокета, высылая фичи и чар-лист!
                                SendSupportedFeatures();
                                SendCharacterList();
                            }
                            // КЕЙС В: Первый вход на Логин-Сервер (Пакет 0xEF)
                            else if (firstByte == 0xEF && bytesRead >= 83)
                            {
                                _hasSeed = true;
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"[Сеть] Клиент {Address} подключился к Логин-Серверу (0xEF).");
                                Console.ResetColor();

                                for (int i = 0; i < 21; i++) reader.ReadByte();

                                byte innerPacketId = reader.ReadByte();
                                if (innerPacketId == 0x80)
                                {
                                    ProcessLoginPacket(reader);
                                }
                            }
                            else
                            {
                                _hasSeed = true;
                                Console.WriteLine($"[Сеть] Клиент {Address} прислал технический заголовок.");
                            }
                        }
                    }

                    // Фаза 2: Seed уже получен, обрабатываем стандартные команды на этом сокете
                    else
                    {
                        // Клиент кликнул на сервер в списке
                        if (firstByte == 0xA0)
                        {
                            ushort serverIndex = (ushort)((_buffer[1] << 8) | _buffer[2]);

                            Console.ForegroundColor = ConsoleColor.Magenta;
                            Console.WriteLine($"[Выбор сервера] Игрок выбрал шард {serverIndex}. Переводим стейт без дисконнекта!");
                            Console.ResetColor();

                            // Отправляем пакет 0x86 (Server Redirect), чтобы переключить TazUO в режим игры
                            using var writer = new PacketWriter();
                            writer.WriteByte(0x86);
                            for (int i = 0; i < 29; i++) writer.WriteByte(0);
                            Send(writer.ToArray());

                            // МГНОВЕННО вслед за 0x86 шлем пакет поддерживаемых фич 0xB9
                            SendSupportedFeatures();

                            // ШЛЕМ СПИСОК ПЕРСОНАЖЕЙ (0xA9), в котором теперь лежит персонаж 'Admin'!
                            SendCharacterList();
                        }
                        // Если TazUO шлет пакет 0x5D (Выбор персонажа из списка)
                        else if (firstByte == 0x5D)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[Мир] Клиент выбрал персонажа 'Admin' из списка! Впускаем в мир пакетом 0x1B.");
                            Console.ResetColor();
                            
                            // Вот ТЕПЕРЬ, когда у TazUO есть имя профиля, шлем Login Confirm!
                            SendLoginConfirm();
                        }
                    }

                    // Продолжаем слушать сокет
                    if (Socket != null && Socket.Connected)
                    {
                        Socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, OnReceive, null);
                    }

                }
                else
                {
                    Console.WriteLine($"[Сеть] Клиент {Address} отключился.");
                    Dispose();
                }
            }
            catch (Exception)
            {
                Dispose();
            }
        }

        private void ProcessLoginPacket(PacketReader reader)
        {
            string accountName = reader.ReadString(30);
            string password = reader.ReadString(30); // ИСПРАВЛЕНО: 'r' заменено на 'reader'

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"[Авторизация] Успешно расшифровано: Логин='{accountName}'");
            Console.ResetColor();

            SendServerList();
        }

        private void ProcessGameLogin(PacketReader reader)
        {
            uint authKey = reader.ReadUInt32();
            string username = reader.ReadString(30);
            string password = reader.ReadString(30);

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"[Игровой поток] TazUO успешно вернулся! Запускаем фичи и шлем слоты персонажей.");
            Console.ResetColor();

            // 1. Шлем пакет фич 0xB9, который требует TazUO версии 7.x
            SendSupportedFeatures();

            // 2. Шлем пакет 0xA9 (Список персонажей)
            SendCharacterList();
        }

        private void SendServerList()
        {
            using var writer = new PacketWriter();
            writer.WriteByte(0xA8);
            writer.WriteUInt16(0x0000);
            writer.WriteByte(0x5D);
            writer.WriteUInt16(1);

            writer.WriteUInt16(0);
            writer.WriteStringFixed("My New ServUO 10", 32);
            writer.WriteByte(0);
            writer.WriteByte(0);

            writer.WriteByte(127); writer.WriteByte(0); writer.WriteByte(0); writer.WriteByte(1); // IP 127.0.0.1
            writer.WriteUInt16(2593); // Порт

            writer.SeekAndWriteLength();
            Send(writer.ToArray());
        }

        private void SendSupportedFeatures()
        {
            using var writer = new PacketWriter();
            writer.WriteByte(0xB9);
            writer.WriteUInt32(0x8017); // Флаги расширений (AOS, SE, ML)
            Send(writer.ToArray());
        }

        private void SendCharacterList()
        {
            using var writer = new PacketWriter();
            writer.WriteByte(0xA9);             // 1. ID пакета (1 байт)
            writer.WriteUInt16(0);              // 2. Место под общую длину (2 байта)

            int count = 5; 
            writer.WriteByte((byte)count);       // 3. Количество слотов персонажей (1 байт)

            // --- СЛОТ 1: Наш готовый персонаж ---
            writer.WriteStringFixed("Admin", 30); // Имя персонажа (30 байт)
            for (int i = 0; i < 30; i++) writer.WriteByte(0); // Пароль персонажа (30 байт нулей)

            // --- СЛОТЫ 2-5: Полностью пустые (4 слота по 60 байт нулей каждый) ---
            // Это жестко гарантирует, что структура пакета не сместится!
            for (int i = 1; i < count; ++i)
            {
                for (int j = 0; j < 60; j++)
                {
                    writer.WriteByte(0);
                }
            }

            // --- СТАРТОВЫЙ ГОРОД (Строго 89 байт структуры) ---
            writer.WriteByte(1); // Количество городов = 1
            writer.WriteByte(0); // Индекс города = 0
            writer.WriteStringFixed("Britain", 32);         // Название города (32 байта)
            writer.WriteStringFixed("The Castle of Lord British", 32); // Локация (32 байта)
            
            // ИСПРАВЛЕНИЕ: Клиент 7.x ожидает увидеть координаты и флаги в формате Little-Endian!
            // Записываем байты напрямую, используя стандартный порядок байт вашей ОС Linux x64
            byte[] xBytes = BitConverter.GetBytes((uint)1323); writer.WriteByte(xBytes[0]); writer.WriteByte(xBytes[1]); writer.WriteByte(xBytes[2]); writer.WriteByte(xBytes[3]);
            byte[] yBytes = BitConverter.GetBytes((uint)1624); writer.WriteByte(yBytes[0]); writer.WriteByte(yBytes[1]); writer.WriteByte(yBytes[2]); writer.WriteByte(yBytes[3]);
            byte[] zBytes = BitConverter.GetBytes((uint)0);    writer.WriteByte(zBytes[0]); writer.WriteByte(zBytes[1]); writer.WriteByte(zBytes[2]); writer.WriteByte(zBytes[3]);
            byte[] mapBytes = BitConverter.GetBytes((uint)0);  writer.WriteByte(mapBytes[0]); writer.WriteByte(mapBytes[1]); writer.WriteByte(mapBytes[2]); writer.WriteByte(mapBytes[3]);
            
            // Забиваем Cliloc ID и Резерв (по 4 байта нулей)
            for (int i = 0; i < 8; i++) writer.WriteByte(0);

            // Флаги интерфейса слотов (Тоже должны быть в формате Little-Endian!)
            uint characterListFlags = 0x14 | 0x200 | 0x400; 
            byte[] flagBytes = BitConverter.GetBytes(characterListFlags);
            writer.WriteByte(flagBytes[0]); writer.WriteByte(flagBytes[1]); writer.WriteByte(flagBytes[2]); writer.WriteByte(flagBytes[3]);
            
            // Обязательный финальный терминатор (-1) для современных клиентов
            writer.WriteUInt16(0xFFFF); 

            // Рассчитываем общую длину и пишем во 2-й и 3-й байты пакета
            writer.SeekAndWriteLength();
            
            byte[] outBuffer = writer.ToArray();
            Console.WriteLine($"[Сеть] Отправлен бинарно выверенный Character List (0xA9). Длина: {outBuffer.Length} байт.");
            
            Send(outBuffer);
        }

        private void SendLoginConfirm()
        {
            using var writer = new PacketWriter();
            writer.WriteByte(0x1B);
            writer.WriteUInt32(1); // Serial = 1
            writer.WriteUInt32(0);
            writer.WriteUInt16(400); // Скин человека
            writer.WriteUInt16(1323); writer.WriteUInt16(1624); writer.WriteUInt16(0); // Britain
            writer.WriteByte(0); writer.WriteByte(0);
            writer.WriteUInt32(0xFFFFFFFF);
            writer.WriteUInt16(0); writer.WriteUInt16(0);
            writer.WriteUInt16(6144); writer.WriteUInt16(4096); // Границы Felucca
            writer.WriteUInt32(0); writer.WriteUInt32(0); writer.WriteByte(0);

            Send(writer.ToArray());
            Console.WriteLine($"[Сеть] Вход утвержден пакетом 0x1B! Персонаж Admin успешно загружен в мир.");
        }
        public void Send(byte[] data)
        {
            try { Socket.BeginSend(data, 0, data.Length, SocketFlags.None, null, null); } catch { }
        }
        public void Dispose()
        {
            try { if (Socket.Connected) { Socket.Shutdown(SocketShutdown.Both); Socket.Close(); } } catch { }
        }
    }
}