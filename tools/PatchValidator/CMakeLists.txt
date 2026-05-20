cmake_minimum_required(VERSION 3.16)
project(PatchValidator VERSION 1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)

# Works with either Qt 6 (preferred) or Qt 5.
find_package(QT NAMES Qt6 Qt5 REQUIRED COMPONENTS Widgets Network)
find_package(Qt${QT_VERSION_MAJOR} REQUIRED COMPONENTS Widgets Network)

add_executable(PatchValidator
    main.cpp
    mainwindow.cpp
    mainwindow.h
    githubclient.cpp
    githubclient.h
)

target_link_libraries(PatchValidator PRIVATE
    Qt${QT_VERSION_MAJOR}::Widgets
    Qt${QT_VERSION_MAJOR}::Network
)
