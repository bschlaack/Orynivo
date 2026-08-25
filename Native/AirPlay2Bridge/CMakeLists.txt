cmake_minimum_required(VERSION 3.21)

project(OrynivoAirPlay2Bridge VERSION 0.1.0 LANGUAGES CXX)

option(AIRPLAY2BRIDGE_BUILD_TESTS "Build the portable transport tests" ON)

add_library(AirPlay2Bridge SHARED
    src/airplay2_bridge.cpp
    src/socket_transport.cpp)

target_compile_features(AirPlay2Bridge PUBLIC cxx_std_20)
target_compile_definitions(AirPlay2Bridge PRIVATE AIRPLAY2BRIDGE_EXPORTS)
target_include_directories(AirPlay2Bridge
    PUBLIC $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
           $<INSTALL_INTERFACE:include>
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)

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
endif()

install(TARGETS AirPlay2Bridge
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib)
install(FILES include/airplay2_bridge.h DESTINATION include)
