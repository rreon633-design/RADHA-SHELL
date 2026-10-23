cmake_minimum_required(VERSION 3.24)
project(RadhaNative LANGUAGES CXX)

# Native bridge for the pieces the managed shell cannot do on its own:
#   · application-desktop-toolbar (AppBar) docking for the bar
#   · DWM blur / Mica fallbacks on older builds
#   · global hotkeys (RegisterHotKey on an out-of-process message window)
#   · virtual-desktop COM interop (IVirtualDesktopManagerInternal, undocumented)
#
# Build (Windows, VS 2022):  cmake -B build -A x64 && cmake --build build --config Release

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(RadhaNative SHARED
    src/Internal.cpp
    src/AppBar.cpp
    src/Hotkeys.cpp
    src/WindowFx.cpp
    src/Workspaces.cpp
)

target_include_directories(RadhaNative PUBLIC include)
target_compile_definitions(RadhaNative PRIVATE
    RADHANATIVE_EXPORTS
    UNICODE _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX)

target_link_libraries(RadhaNative PRIVATE
    user32
    shell32
    dwmapi
    ole32
    shcore
)

set_target_properties(RadhaNative PROPERTIES OUTPUT_NAME "RadhaNative")
