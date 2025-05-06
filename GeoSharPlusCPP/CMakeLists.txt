cmake_minimum_required(VERSION 3.20)
if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type (Release or Debug)" FORCE)
endif()

set(PROJECT_NAME GeoSharPlusCPP)
############################################
# VCPKG 
############################################
# Check for VCPKG_ROOT environment variable
if(DEFINED ENV{VCPKG_ROOT})
    set(CMAKE_TOOLCHAIN_FILE "$ENV{VCPKG_ROOT}/scripts/buildsystems/vcpkg.cmake")
    message(STATUS "Using vcpkg toolchain file: ${CMAKE_TOOLCHAIN_FILE}")
else()
    message(FATAL_ERROR "VCPKG_ROOT environment variable is not defined. Please set it to your vcpkg installation directory.")
endif()

# Explicitly set vcpkg target triplet if not already set
if(NOT DEFINED VCPKG_TARGET_TRIPLET)
    if(WIN32)
        set(VCPKG_TARGET_TRIPLET "x64-windows" CACHE STRING "vcpkg target triplet")
    elseif(APPLE)
        set(VCPKG_TARGET_TRIPLET "x64-osx" CACHE STRING "vcpkg target triplet")
    elseif(UNIX)
        set(VCPKG_TARGET_TRIPLET "x64-linux" CACHE STRING "vcpkg target triplet")
    endif()
    message(STATUS "Setting vcpkg triplet to: ${VCPKG_TARGET_TRIPLET}")
endif()

############################################
# Compiler options
############################################
# Set C++ standard
project(${PROJECT_NAME} LANGUAGES C CXX)

# Set C++ standard
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

message(STATUS "Build type: ${CMAKE_BUILD_TYPE}")
message(STATUS "C++ compiler: ${CMAKE_CXX_COMPILER}")
message(STATUS "Generator: ${CMAKE_GENERATOR}")


# Compiling flags
set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -O3 -Wall -Wextra -pedantic")


# Enable parallel building (equivalent to /MP)
if(MSVC)
    set(CMAKE_C_MP_FLAG ON)
    set(CMAKE_CXX_MP_FLAG ON)
    # Enable faster PDB generation
    add_compile_options("/FS")
    # Set number of parallel jobs for MS compiler
    include(ProcessorCount)
    ProcessorCount(N)
    if(NOT N EQUAL 0)
        set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} /MP${N}")
        set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} /MP${N}")
    endif()
endif()

# Platform-specific compiler flags
if(MSVC)
    # Common MSVC flags
    add_compile_options(
        /fp:precise     # Match original VS precision
        /Zc:inline      # Remove unused code
        /EHsc           # Exception handling
        /Gy             # Function-level linking
        /Oi             # Intrinsic functions
        /Gm-            # Minimal rebuild (disable for faster builds)
    )
    
    # Debug-specific flags
    add_compile_options($<$<CONFIG:Debug>:/Zi>)           # Debug information format
    add_compile_options($<$<CONFIG:Debug>:/Od>)           # Disable optimization
    add_compile_options($<$<CONFIG:Debug>:/RTC1>)         # Run-time error checks
    
    # Release-specific flags
    add_compile_options($<$<CONFIG:Release>:/O2>)         # Optimize for speed
    add_compile_options($<$<CONFIG:Release>:/GL>)         # Whole program optimization
    add_compile_options($<$<CONFIG:Release>:/Ot>)         # Favor speed over size
    
    # Debug linker flags
    set(CMAKE_SHARED_LINKER_FLAGS_DEBUG "${CMAKE_SHARED_LINKER_FLAGS_DEBUG} /DEBUG:FULL")
    set(CMAKE_EXE_LINKER_FLAGS_DEBUG "${CMAKE_EXE_LINKER_FLAGS_DEBUG} /DEBUG:FULL")
    
    # Release linker flags
    set(CMAKE_EXE_LINKER_FLAGS_RELEASE "${CMAKE_EXE_LINKER_FLAGS_RELEASE} /LTCG")
    set(CMAKE_SHARED_LINKER_FLAGS_RELEASE "${CMAKE_SHARED_LINKER_FLAGS_RELEASE} /LTCG")
    set(CMAKE_MODULE_LINKER_FLAGS_RELEASE "${CMAKE_MODULE_LINKER_FLAGS_RELEASE} /LTCG")
elseif(APPLE)
    # Common Apple flags
    add_compile_options(
        -fPIC           # Position-independent code
        -ffp-model=precise
        -fstack-protector-strong
        -pthread
    )
    
    # Debug-specific flags
    add_compile_options($<$<CONFIG:Debug>:-g3>)           # Debug symbols
    add_compile_options($<$<CONFIG:Debug>:-O0>)           # No optimization
    
    # Release-specific flags
    add_compile_options($<$<CONFIG:Release>:-O3>)         # Aggressive optimization
    add_compile_options($<$<CONFIG:Release>:-flto>)       # Link-time optimization
    
    # Set Mac-specific optimization flags
    set(CMAKE_EXE_LINKER_FLAGS_RELEASE "${CMAKE_EXE_LINKER_FLAGS_RELEASE} -flto")
    set(CMAKE_SHARED_LINKER_FLAGS_RELEASE "${CMAKE_SHARED_LINKER_FLAGS_RELEASE} -flto")
endif()
# Cross-platform runtime configuration
if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreadedDLL")
elseif(APPLE)
    set(CMAKE_OSX_ARCHITECTURES "arm64;x86_64" CACHE STRING "Target architectures")
endif()

############################################
# Libraries and dependencies
############################################
# Find dependencies
find_package(Eigen3 CONFIG REQUIRED)
message(STATUS "Found Eigen3: ${EIGEN3_INCLUDE_DIR}")

find_package(libigl CONFIG REQUIRED)
message(STATUS "Found libigl: ${libigl_DIR}")

find_package(Flatbuffers CONFIG REQUIRED)
message(STATUS "Found Flatbuffers: ${Flatbuffers_DIR}")
if(NOT EXISTS "${FLATBUFFERS_FLATC_EXECUTABLE}")
	find_program(flatbuffers_FLATC_EXECUTABLE flatc
		PATHS "${Flatbuffers_DIR}/tools/flatbuffers"
		PATH_SUFFIXES bin
	)
	if(flatbuffers_FLATC_EXECUTABLE)
		message(STATUS "Found flatc by searching: ${flatbuffers_FLATC_EXECUTABLE}")
	else()
		message(FATAL_ERROR "flatc compiler not found. Please ensure Flatbuffers is properly installed.")
	endif()
endif()
message(STATUS "flatc executable path: ${FLATBUFFERS_FLATC_EXECUTABLE}")

###########################################
# FLATBUFFER GENERATION
###########################################
# FlatBuffers code generation setup
file(GLOB FLATBUFFERS_SCHEMAS "${CMAKE_CURRENT_SOURCE_DIR}/schema/*.fbs")
set(FLATBUFFERS_OUTPUT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../generated/GSP_FB")

# Create output directories
file(MAKE_DIRECTORY "${FLATBUFFERS_OUTPUT_DIR}")
file(MAKE_DIRECTORY "${FLATBUFFERS_OUTPUT_DIR}/cpp")
file(MAKE_DIRECTORY "${FLATBUFFERS_OUTPUT_DIR}/csharp")

# Process each schema file
message(STATUS "Found schema files: ${FLATBUFFERS_SCHEMAS}")
foreach(SCHEMA_FILE ${FLATBUFFERS_SCHEMAS})
    get_filename_component(SCHEMA_NAME ${SCHEMA_FILE} NAME_WE)
    message(STATUS "Processing schema: ${SCHEMA_NAME}")
    
    # Generate C++ code
    message(STATUS "Generating Flatbuffers C++ code for ${SCHEMA_NAME}...")
    execute_process(
        COMMAND ${flatbuffers_FLATC_EXECUTABLE}
        --cpp
        --scoped-enums
        -o "${FLATBUFFERS_OUTPUT_DIR}/cpp"
        "${SCHEMA_FILE}"
        RESULT_VARIABLE FLATC_CPP_RESULT
    )

    if(NOT FLATC_CPP_RESULT EQUAL 0)
        message(FATAL_ERROR "Failed to generate C++ Flatbuffers code for ${SCHEMA_NAME}")
    endif()

    # Generate C# code
    message(STATUS "Generating Flatbuffers C# code for ${SCHEMA_NAME}...")
    execute_process(
        COMMAND ${flatbuffers_FLATC_EXECUTABLE}
        --csharp
        --scoped-enums
        --gen-onefile
        --gen-object-api
        -o "${FLATBUFFERS_OUTPUT_DIR}/csharp"
        "${SCHEMA_FILE}"
        RESULT_VARIABLE FLATC_CSHARP_RESULT
    )

    if(NOT FLATC_CSHARP_RESULT EQUAL 0)
        message(FATAL_ERROR "Failed to generate C# Flatbuffers code for ${SCHEMA_NAME}")
    endif()
endforeach()

message(STATUS "Flatbuffers generation complete")


###########################################
# GEOSHARPLUS TARGET
###########################################
# Add header files to the target
file(GLOB_RECURSE HEADERS CONFIGURE_DEPENDS 
    "${CMAKE_CURRENT_SOURCE_DIR}/include/*.h"
)

file(GLOB_RECURSE SOURCES CONFIGURE_DEPENDS 
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp"
)

add_library(${PROJECT_NAME} SHARED ${HEADERS} ${SOURCES})

# Organize VS folders
source_group("Header Files" FILES ${HEADERS})
source_group("Source Files" FILES ${SOURCES})

# Critical include path
target_include_directories(${PROJECT_NAME} PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    ${CMAKE_CURRENT_SOURCE_DIR}/../generated  # For generated FlatBuffers code
    ${CMAKE_CURRENT_SOURCE_DIR}/../external/cyCodeBase 
    ${libigl_INCLUDE_DIRS}
)

# Rest of your CMakeLists.txt remains the same
# Dependencies
target_link_libraries(${PROJECT_NAME} PRIVATE
    Eigen3::Eigen
    igl::igl_core
    flatbuffers::flatbuffers
)

# No need to add dependency since we generate at configure time

# Platform-specific configuration
if(WIN32)
    target_compile_definitions(${PROJECT_NAME} PRIVATE GEOSHARPLUS_EXPORTS)
    target_compile_options(${PROJECT_NAME} PRIVATE /W4)
    
    # Ensure PDB file is generated and copied for Debug builds
    set_target_properties(${PROJECT_NAME} PROPERTIES 
        COMPILE_PDB_NAME "${PROJECT_NAME}" 
        COMPILE_PDB_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}"
    )
else()
    target_compile_options(${PROJECT_NAME} PRIVATE -Wall -Wextra -pedantic)
endif()

############################################
# Post-build steps
############################################
# Create bin directory if it doesn't exist
# file(MAKE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/../bin")
file(MAKE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/../cppPrebuild")

# Add post-build command to copy the DLL to the bin directory
add_custom_command(
    TARGET ${PROJECT_NAME} POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy 
        $<TARGET_FILE:${PROJECT_NAME}> 
        "${CMAKE_CURRENT_SOURCE_DIR}/../cppPrebuild/$<TARGET_FILE_NAME:${PROJECT_NAME}>"
    COMMENT "Copying ${PROJECT_NAME} DLL to bin directory"
)

# For Windows, copy PDB file for debug builds
if(WIN32)
    add_custom_command(
        TARGET ${PROJECT_NAME} POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<$<CONFIG:Debug>:$<TARGET_PDB_FILE:${PROJECT_NAME}>>
            $<$<CONFIG:Debug>:${CMAKE_CURRENT_SOURCE_DIR}/../cppPrebuild/>
        COMMENT "Copying PDB file for debugging"
    )
endif()
