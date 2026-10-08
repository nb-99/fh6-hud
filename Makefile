PROJECT   := src/Fh6Hud/Fh6Hud.csproj
SOLUTION  := Fh6Hud.slnx
CONFIG    := Release
OUT_DIR   := src/Fh6Hud/bin/$(CONFIG)/net10.0-windows
EXE       := $(OUT_DIR)/Fh6Hud.exe

.PHONY: build run dev test

# Builds the HUD executable (Release).
build:
	dotnet build $(PROJECT) -c $(CONFIG)

# Builds, then starts the HUD detached so the terminal is released.
run: build
	cmd /c start "" /D "$(OUT_DIR)" "$(abspath $(EXE))"

# Debug build with hud.log enabled (--debug), run in the foreground.
dev:
	dotnet run --project $(PROJECT) -c Debug -- --debug

# Runs all unit and WPF tests.
test:
	dotnet test $(SOLUTION) -c $(CONFIG)
