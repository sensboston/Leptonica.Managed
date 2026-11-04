# Native counterpart of benchmarks/Leptonica.Managed.Benchmarks: the same cases,
# run on the C library.
#
# Windows, against the build of tools/native/build.ps1:
#   cmake -S benchmarks/native -B artifacts/native/bench -A x64 -DLEPTONICA_BUILD=artifacts/native/build
#   cmake --build artifacts/native/bench --config Release
#   artifacts/native/bench/Release/bench_native.exe artifacts/native/leptonica/prog --out results.csv
#
# Linux, against an installed Leptonica 1.87.0 (pkg-config lept):
#   cmake -S benchmarks/native -B build -DCMAKE_BUILD_TYPE=Release
#   cmake --build build && ./build/bench_native <leptonica>/prog --out results.csv
cmake_minimum_required(VERSION 3.16)
project(bench_native LANGUAGES C)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

set(LEPTONICA_BUILD "" CACHE PATH "Leptonica CMake build tree (tools/native/build.ps1 output)")

add_executable(bench_native bench_native.c)

if(LEPTONICA_BUILD)
    # Build tree of tools/native/build.ps1: headers from the source and the build,
    # import library and DLLs from the build.
    get_filename_component(_lept_build "${LEPTONICA_BUILD}" ABSOLUTE)
    get_filename_component(_lept_src "${_lept_build}/../leptonica/src" ABSOLUTE)
    file(GLOB _lept_lib "${_lept_build}/src/Release/leptonica-*.lib")
    if(NOT _lept_lib)
        message(FATAL_ERROR "leptonica import library not found under ${_lept_build}/src/Release")
    endif()
    target_include_directories(bench_native PRIVATE "${_lept_src}" "${_lept_build}/src")
    target_link_libraries(bench_native PRIVATE ${_lept_lib})
    if(WIN32)
        # Copy leptonica and its codec DLLs next to the executable.
        file(GLOB _lept_dlls "${_lept_build}/bin/Release/*.dll")
        add_custom_command(TARGET bench_native POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_if_different ${_lept_dlls} "$<TARGET_FILE_DIR:bench_native>")
    endif()
else()
    find_package(PkgConfig REQUIRED)
    pkg_check_modules(LEPT REQUIRED IMPORTED_TARGET lept)
    target_link_libraries(bench_native PRIVATE PkgConfig::LEPT)
endif()

if(MSVC)
    target_compile_options(bench_native PRIVATE /W4)
    target_compile_definitions(bench_native PRIVATE _CRT_SECURE_NO_WARNINGS)
else()
    target_compile_options(bench_native PRIVATE -Wall -Wextra)
    target_link_libraries(bench_native PRIVATE m)
endif()
