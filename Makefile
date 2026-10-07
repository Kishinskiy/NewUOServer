# Переменные проекта
PROJECT_NAME=Server
CS_PROJ_PATH=src/Server/Server.csproj
BUILD_DIR=bin/Debug/net10.0

# Команда по умолчанию (просто собирает проект под текущую ОС)
all: build

# Сборка проекта (Framework-Dependent)
build:
	@echo "=== Компиляция ядра ServUO (C# 10 / .NET 10) ==="
	dotnet build $(CS_PROJ_PATH)

# Сборка готового автономного релиза под Linux (x64)
build-linux:
	@echo "=== Сборка независимого бинарника под Linux x64 ==="
	dotnet build $(CS_PROJ_PATH) -r linux-x64 --self-contained false
	@chmod +x $(BUILD_DIR)/linux-x64/$(PROJECT_NAME)

# Запуск сервера
run: build
	@echo "=== Запуск сервера ==="
	@cd $(BUILD_DIR) && ./$(PROJECT_NAME)

# Очистка всех собранных файлов и кэша компиляции
clean:
	@echo "=== Очистка временных файлов и папки bin/ ==="
	@rm -rf bin/ obj/ src/Server/obj/ src/Server/bin/
	@echo "Проект очищен."

.PHONY: all build build-linux run clean
