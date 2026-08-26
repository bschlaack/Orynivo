cmake_minimum_required(VERSION 3.21)

project(OrynivoAirPlay2Bridge VERSION 0.1.0 LANGUAGES CXX)

# The fetched crypto target is static but becomes part of this shared bridge.
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

option(AIRPLAY2BRIDGE_BUILD_TESTS "Build the portable transport tests" ON)
option(AIRPLAY2BRIDGE_BUILD_PROBE "Build the manual receiver probe" ON)

include(FetchContent)
FetchContent_Declare(airplay2_sender_crypto
    GIT_REPOSITORY https://github.com/akustikrausch/airplay2-sender-cpp.git
    GIT_TAG 8c4034263f1c265d25b3cfb88a090624760ad22a
    GIT_SHALLOW FALSE)
FetchContent_MakeAvailable(airplay2_sender_crypto)

add_library(AirPlay2Bridge SHARED
    src/airplay2_bridge.cpp
    src/encrypted_control.cpp
    src/encrypted_rtsp.cpp
    src/transient_pairing.cpp
    src/socket_transport.cpp)

target_compile_features(AirPlay2Bridge PUBLIC cxx_std_20)
target_compile_definitions(AirPlay2Bridge PRIVATE AIRPLAY2BRIDGE_EXPORTS)
target_include_directories(AirPlay2Bridge
    PUBLIC $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
           $<INSTALL_INTERFACE:include>
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)
target_link_libraries(AirPlay2Bridge PRIVATE airplay_crypto)

if(WIN32)
    target_link_libraries(AirPlay2Bridge PRIVATE ws2_32)
endif()

set_target_properties(AirPlay2Bridge PROPERTIES
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES)

if(AIRPLAY2BRIDGE_BUILD_TESTS)
    enable_testing()
    add_executable(AirPlay2BridgeTransportTests
        tests/socket_transport_tests.cpp
        src/socket_transport.cpp)
    target_compile_features(AirPlay2BridgeTransportTests PRIVATE cxx_std_20)
    target_include_directories(AirPlay2BridgeTransportTests PRIVATE src)
    if(WIN32)
        target_link_libraries(AirPlay2BridgeTransportTests PRIVATE ws2_32)
    endif()
    add_test(NAME AirPlay2Bridge.Transport COMMAND AirPlay2BridgeTransportTests)

    add_executable(AirPlay2BridgePairingTests tests/transient_pairing_tests.cpp)
    target_compile_features(AirPlay2BridgePairingTests PRIVATE cxx_std_20)
    target_link_libraries(AirPlay2BridgePairingTests PRIVATE airplay_crypto)
    add_test(NAME AirPlay2Bridge.PairingPrimitives COMMAND AirPlay2BridgePairingTests)

    add_executable(AirPlay2BridgeEncryptedControlTests
        tests/encrypted_control_tests.cpp
        src/encrypted_control.cpp)
    target_compile_features(AirPlay2BridgeEncryptedControlTests PRIVATE cxx_std_20)
    target_include_directories(AirPlay2BridgeEncryptedControlTests PRIVATE src)
    target_link_libraries(AirPlay2BridgeEncryptedControlTests PRIVATE airplay_crypto)
    add_test(NAME AirPlay2Bridge.EncryptedControl COMMAND AirPlay2BridgeEncryptedControlTests)
endif()

install(TARGETS AirPlay2Bridge
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib)
install(FILES include/airplay2_bridge.h DESTINATION include)
install(FILES README.md NOTICE THIRD_PARTY_NOTICES.md DESTINATION share/AirPlay2Bridge)

if(AIRPLAY2BRIDGE_BUILD_PROBE)
    add_executable(AirPlay2BridgeProbe tools/pairing_probe.cpp)
    target_compile_features(AirPlay2BridgeProbe PRIVATE cxx_std_20)
    target_link_libraries(AirPlay2BridgeProbe PRIVATE AirPlay2Bridge)
endif()
