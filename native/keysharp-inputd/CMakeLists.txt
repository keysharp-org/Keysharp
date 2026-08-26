cmake_minimum_required(VERSION 3.16)

project(keysharp-inputd
    VERSION 1.2.0
    DESCRIPTION "Privileged input broker daemon for Keysharp"
    LANGUAGES C)

include(CTest)

include(GNUInstallDirs)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

find_package(PkgConfig REQUIRED)
find_package(Threads REQUIRED)
pkg_check_modules(LIBUDEV REQUIRED IMPORTED_TARGET libudev)
pkg_check_modules(LIBEVDEV REQUIRED IMPORTED_TARGET libevdev)

add_subdirectory(
    ${CMAKE_CURRENT_SOURCE_DIR}/../keysharp-trust-lib
    ${CMAKE_CURRENT_BINARY_DIR}/keysharp-trust-lib)

add_executable(keysharp-inputd
    src/active_session.c
    src/connection_ref.c
    src/daemon.c
    src/ipc_unix.c
    src/main.c
    src/pipe_ring.c
    src/synthetic_hooks.c
    src/trust_cli.c
    src/wake_pipe.c
    src/worker_pool.c)

target_include_directories(keysharp-inputd
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include)

target_compile_definitions(keysharp-inputd PRIVATE _GNU_SOURCE _POSIX_C_SOURCE=200809L)
target_link_libraries(keysharp-inputd PRIVATE
    ${CMAKE_DL_LIBS}
    keysharp-trust-lib
    Threads::Threads
    PkgConfig::LIBUDEV
    PkgConfig::LIBEVDEV)

if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    target_sources(keysharp-inputd PRIVATE
        src/platform/linux_devices.c
        src/platform/linux_synth.c
        src/platform/platform_linux.c
        src/platform/vk_evdev.c)
    target_compile_definitions(keysharp-inputd PRIVATE KSI_PLATFORM_LINUX=1)
else()
    message(FATAL_ERROR "keysharp-inputd currently supports Linux only")
endif()

if(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU")
    target_compile_options(keysharp-inputd PRIVATE
        -Wall
        -Wextra
        -Wpedantic
        -Wconversion
        -Wshadow)
endif()

install(TARGETS keysharp-inputd RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR})

set(KEYSHARP_INPUTD_SYSTEMD_UNIT_DIR
    "${CMAKE_INSTALL_LIBDIR}/systemd/system"
    CACHE PATH "systemd system unit install directory")

configure_file(
    systemd/keysharp-inputd.service.in
    ${CMAKE_CURRENT_BINARY_DIR}/keysharp-inputd.service
    @ONLY)

install(FILES
    ${CMAKE_CURRENT_BINARY_DIR}/keysharp-inputd.service
    systemd/keysharp-inputd.socket
    DESTINATION ${KEYSHARP_INPUTD_SYSTEMD_UNIT_DIR})

# Direct root installs finish service setup immediately. Packaging installs
# set DESTDIR and should run the equivalent post-install step themselves.
install(CODE "
    if(\"\$ENV{DESTDIR}\" STREQUAL \"\")
        set(_bin \"\${CMAKE_INSTALL_PREFIX}/${CMAKE_INSTALL_BINDIR}/keysharp-inputd\")
        execute_process(
            COMMAND \"\${_bin}\" --install-input-access
            RESULT_VARIABLE _inputd_setup_result)
        if(NOT _inputd_setup_result EQUAL 0)
            message(WARNING \"keysharp-inputd service setup was not completed; run as root: \${_bin} --install-input-access\")
        endif()
    endif()
")

option(KEYSHARP_INPUTD_BUILD_HOOKTEST "Build the installed-daemon diagnostic tool" ON)
option(KEYSHARP_INPUTD_BUILD_PHYSICAL_TEST_TOOL
    "Build the privileged manual physical-input source (never a CTest)" OFF)

if(KEYSHARP_INPUTD_BUILD_HOOKTEST)
    add_executable(keysharp-inputd-hooktest tools/hooktest.c)
    target_include_directories(keysharp-inputd-hooktest PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include)
    target_compile_definitions(keysharp-inputd-hooktest PRIVATE
        _GNU_SOURCE _POSIX_C_SOURCE=200809L)

    if(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(keysharp-inputd-hooktest PRIVATE
            -Wall -Wextra -Wpedantic -Wconversion -Wshadow)
    endif()
endif()

if(KEYSHARP_INPUTD_BUILD_PHYSICAL_TEST_TOOL)
    add_executable(keysharp-inputd-physical-source tools/physical-source.c)
    target_compile_definitions(keysharp-inputd-physical-source PRIVATE
        _GNU_SOURCE _POSIX_C_SOURCE=200809L)

    if(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(keysharp-inputd-physical-source PRIVATE
            -Wall -Wextra -Wpedantic -Wconversion -Wshadow)
    endif()
endif()

if(BUILD_TESTING)
    add_executable(keysharp-inputd-unit-tests
        tests/daemon_unit_tests.c
        src/active_session.c
        src/connection_ref.c
        src/ipc_unix.c
        src/pipe_ring.c
        src/synthetic_hooks.c
        src/wake_pipe.c
        src/worker_pool.c
        src/platform/linux_devices.c
        src/platform/linux_synth.c
        src/platform/platform_linux.c
        src/platform/vk_evdev.c)

    target_include_directories(keysharp-inputd-unit-tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/src)
    target_compile_definitions(keysharp-inputd-unit-tests PRIVATE
        _GNU_SOURCE _POSIX_C_SOURCE=200809L KSI_PLATFORM_LINUX=1)
    target_link_libraries(keysharp-inputd-unit-tests PRIVATE
        ${CMAKE_DL_LIBS} keysharp-trust-lib Threads::Threads
        PkgConfig::LIBUDEV PkgConfig::LIBEVDEV)

    if(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(keysharp-inputd-unit-tests PRIVATE
            -Wall -Wextra -Wpedantic -Wconversion -Wshadow)
    endif()

    add_test(NAME keysharp-inputd-unit-tests COMMAND keysharp-inputd-unit-tests)

    add_executable(keysharp-inputd-scheduler-tests
        tests/scheduler_adversarial_tests.c
        src/connection_ref.c
        src/ipc_unix.c)
    target_include_directories(keysharp-inputd-scheduler-tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/src)
    target_compile_definitions(keysharp-inputd-scheduler-tests PRIVATE
        _GNU_SOURCE _POSIX_C_SOURCE=200809L)
    target_link_libraries(keysharp-inputd-scheduler-tests PRIVATE Threads::Threads)

    if(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(keysharp-inputd-scheduler-tests PRIVATE
            -Wall -Wextra -Wpedantic -Wconversion -Wshadow)
    endif()

    add_test(NAME keysharp-inputd-scheduler-tests
        COMMAND keysharp-inputd-scheduler-tests)

    add_executable(keysharp-inputd-synthetic-hooks-tests
        tests/synthetic_hooks_tests.c
        src/synthetic_hooks.c)
    target_include_directories(keysharp-inputd-synthetic-hooks-tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include)
    target_compile_definitions(keysharp-inputd-synthetic-hooks-tests PRIVATE
        _GNU_SOURCE _POSIX_C_SOURCE=200809L)

    if(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(keysharp-inputd-synthetic-hooks-tests PRIVATE
            -Wall -Wextra -Wpedantic -Wconversion -Wshadow)
    endif()

    add_test(NAME keysharp-inputd-synthetic-hooks-tests
        COMMAND keysharp-inputd-synthetic-hooks-tests)

    add_executable(keysharp-inputd-pipe-ring-tests
        tests/pipe_ring_tests.c
        src/pipe_ring.c)
    target_include_directories(keysharp-inputd-pipe-ring-tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src)
    target_compile_definitions(keysharp-inputd-pipe-ring-tests PRIVATE
        _GNU_SOURCE _POSIX_C_SOURCE=200809L)
    target_link_libraries(keysharp-inputd-pipe-ring-tests PRIVATE Threads::Threads)

    if(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(keysharp-inputd-pipe-ring-tests PRIVATE
            -Wall -Wextra -Wpedantic -Wconversion -Wshadow)
    endif()

    add_test(NAME keysharp-inputd-pipe-ring-tests
        COMMAND keysharp-inputd-pipe-ring-tests)

    add_executable(keysharp-inputd-wake-pipe-tests
        tests/wake_pipe_tests.c
        src/wake_pipe.c)
    target_include_directories(keysharp-inputd-wake-pipe-tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src)
    target_compile_definitions(keysharp-inputd-wake-pipe-tests PRIVATE
        _GNU_SOURCE _POSIX_C_SOURCE=200809L)

    if(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(keysharp-inputd-wake-pipe-tests PRIVATE
            -Wall -Wextra -Wpedantic -Wconversion -Wshadow)
    endif()

    add_test(NAME keysharp-inputd-wake-pipe-tests
        COMMAND keysharp-inputd-wake-pipe-tests)

    add_executable(keysharp-inputd-active-session-tests
        tests/active_session_tests.c
        src/active_session.c)
    target_include_directories(keysharp-inputd-active-session-tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src)
    target_compile_definitions(keysharp-inputd-active-session-tests PRIVATE
        _GNU_SOURCE _POSIX_C_SOURCE=200809L)
    target_link_libraries(keysharp-inputd-active-session-tests PRIVATE
        ${CMAKE_DL_LIBS})

    if(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(keysharp-inputd-active-session-tests PRIVATE
            -Wall -Wextra -Wpedantic -Wconversion -Wshadow)
    endif()

    add_test(NAME keysharp-inputd-active-session-tests
        COMMAND keysharp-inputd-active-session-tests)

    add_executable(keysharp-inputd-linux-device-filter-tests
        tests/linux_device_filter_tests.c)
    target_include_directories(keysharp-inputd-linux-device-filter-tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src)

    if(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(keysharp-inputd-linux-device-filter-tests PRIVATE
            -Wall -Wextra -Wpedantic -Wconversion -Wshadow)
    endif()

    add_test(NAME keysharp-inputd-linux-device-filter-tests
        COMMAND keysharp-inputd-linux-device-filter-tests)

    add_test(NAME keysharp-inputd-systemd-unit-tests
        COMMAND ${CMAKE_COMMAND}
            -DKSI_SOCKET_UNIT=${CMAKE_CURRENT_SOURCE_DIR}/systemd/keysharp-inputd.socket
            -DKSI_SERVICE_UNIT=${CMAKE_CURRENT_SOURCE_DIR}/systemd/keysharp-inputd.service.in
            -P ${CMAKE_CURRENT_SOURCE_DIR}/tests/systemd_unit_tests.cmake)
endif()
