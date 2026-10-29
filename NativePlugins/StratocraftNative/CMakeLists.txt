cmake_minimum_required(VERSION 3.24)
if(POLICY CMP0169)
    cmake_policy(SET CMP0169 OLD)
endif()
project(StratocraftNative VERSION 0.1.0 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

include(FetchContent)

# ==============================================================================
# 1. Ucuncu Parti MIT / BSD / CC0 Acik Kaynak Bagimliliklari (GPL KESIN YASAK)
# ==============================================================================

# 1.1 xxHash v0.8.2 (BSD-2) - Header-only XXH_INLINE_ALL moduyla sifir ek kutuphane yuku
FetchContent_Declare(
    xxhash
    GIT_REPOSITORY https://github.com/Cyan4973/xxHash.git
    GIT_TAG        v0.8.2
    GIT_SHALLOW    TRUE
)
FetchContent_GetProperties(xxhash)
if(NOT xxhash_POPULATED)
    FetchContent_Populate(xxhash)
endif()

# 1.2 LZ4 v1.10.0 (BSD-2) - Dogrudan lz4.c kaynak dosyasi gomulu derlenir
FetchContent_Declare(
    lz4
    GIT_REPOSITORY https://github.com/lz4/lz4.git
    GIT_TAG        v1.10.0
    GIT_SHALLOW    TRUE
)
FetchContent_GetProperties(lz4)
if(NOT lz4_POPULATED)
    FetchContent_Populate(lz4)
endif()

add_library(stratocraft_lz4 STATIC
    "${lz4_SOURCE_DIR}/lib/lz4.c"
    "${lz4_SOURCE_DIR}/lib/lz4hc.c"
)
target_include_directories(stratocraft_lz4 PUBLIC "${lz4_SOURCE_DIR}/lib")

# 1.3 Zstandard v1.5.6 (BSD-3)
set(ZSTD_BUILD_PROGRAMS OFF CACHE BOOL "" FORCE)
set(ZSTD_BUILD_SHARED OFF CACHE BOOL "" FORCE)
set(ZSTD_BUILD_STATIC ON CACHE BOOL "" FORCE)
set(ZSTD_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(ZSTD_LEGACY_SUPPORT OFF CACHE BOOL "" FORCE)
FetchContent_Declare(
    zstd
    GIT_REPOSITORY https://github.com/facebook/zstd.git
    GIT_TAG        v1.5.6
    GIT_SHALLOW    TRUE
    SOURCE_SUBDIR  build/cmake
)
FetchContent_MakeAvailable(zstd)

# 1.4 Monocypher v4.0.2 (CC0 / BSD-2) - Sabit zamanli Ed25519 (RFC 8032 SHA-512 varyanti dahil)
FetchContent_Declare(
    monocypher
    GIT_REPOSITORY https://github.com/LoupVaillant/Monocypher.git
    GIT_TAG        4.0.2
    GIT_SHALLOW    TRUE
)
FetchContent_GetProperties(monocypher)
if(NOT monocypher_POPULATED)
    FetchContent_Populate(monocypher)
endif()

add_library(stratocraft_monocypher STATIC
    "${monocypher_SOURCE_DIR}/src/monocypher.c"
    "${monocypher_SOURCE_DIR}/src/optional/monocypher-ed25519.c"
)
target_include_directories(stratocraft_monocypher PUBLIC
    "${monocypher_SOURCE_DIR}/src"
    "${monocypher_SOURCE_DIR}/src/optional"
)

# 1.5 SQLite3 Amalgamation 3.53.0 (Public Domain / CC0) - Gomulu WAL Telemetri ve WorldState Motoru
FetchContent_Declare(
    sqlite3_amalg
    URL https://www.sqlite.org/2026/sqlite-amalgamation-3530400.zip
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE
)
FetchContent_GetProperties(sqlite3_amalg)
if(NOT sqlite3_amalg_POPULATED)
    FetchContent_Populate(sqlite3_amalg)
endif()

add_library(stratocraft_sqlite3 STATIC
    "${sqlite3_amalg_SOURCE_DIR}/sqlite3.c"
)
target_include_directories(stratocraft_sqlite3 PUBLIC "${sqlite3_amalg_SOURCE_DIR}")
target_compile_definitions(stratocraft_sqlite3 PRIVATE
    SQLITE_THREADSAFE=1
    SQLITE_DEFAULT_WAL_SYNCHRONOUS=1
    SQLITE_OMIT_LOAD_EXTENSION=1
)

# ==============================================================================
# 2. StratocraftNative Paylasimli Kutuphanesi (.dll / .so)
# ==============================================================================
add_library(StratocraftNative SHARED
    src/simd_world_noise.cpp
    src/region_codec.cpp
    src/contract_crypto.cpp
    src/telemetry_sqlite.cpp
)

target_compile_definitions(StratocraftNative PRIVATE
    STRATOCRAFT_NATIVE_EXPORTS
    XXH_INLINE_ALL
)

target_include_directories(StratocraftNative
    PUBLIC
        "${CMAKE_CURRENT_SOURCE_DIR}/include"
    PRIVATE
        "${xxhash_SOURCE_DIR}"
)

target_link_libraries(StratocraftNative PRIVATE
    stratocraft_lz4
    libzstd_static
    stratocraft_monocypher
    stratocraft_sqlite3
)

if(MINGW)
    set_target_properties(StratocraftNative PROPERTIES PREFIX "")
    target_link_options(StratocraftNative PRIVATE -static -static-libgcc -static-libstdc++)
endif()

if(MSVC)
    target_compile_options(StratocraftNative PRIVATE /W4 /WX /arch:AVX2 /fp:fast /EHsc)
else()
    target_compile_options(StratocraftNative PRIVATE -Wall -Wextra -Werror -mavx2 -mfma -O3)
endif()

# ==============================================================================
# 3. GoogleTest v1.14.0 (BSD-3) Birim Testleri (ctest)
# ==============================================================================
enable_testing()

set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
set(BUILD_GMOCK OFF CACHE BOOL "" FORCE)
set(INSTALL_GTEST OFF CACHE BOOL "" FORCE)
FetchContent_Declare(
    googletest
    GIT_REPOSITORY https://github.com/google/googletest.git
    GIT_TAG        v1.14.0
    GIT_SHALLOW    TRUE
)
FetchContent_MakeAvailable(googletest)

add_executable(StratocraftNativeTests
    tests/native_unit_tests.cpp
)
target_link_libraries(StratocraftNativeTests PRIVATE
    StratocraftNative
    GTest::gtest_main
)

if(MSVC)
    target_compile_options(StratocraftNativeTests PRIVATE /W4 /WX /EHsc)
else()
    target_compile_options(StratocraftNativeTests PRIVATE -Wall -Wextra -Werror)
endif()

add_custom_command(TARGET StratocraftNativeTests POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        $<TARGET_FILE:StratocraftNative>
        $<TARGET_FILE_DIR:StratocraftNativeTests>
)

include(GoogleTest)
gtest_discover_tests(StratocraftNativeTests)
