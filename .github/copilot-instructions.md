# Copilot Instructions for BeingAliveLanguage

## Project Overview

**BeingAliveLanguage** is a multi-project C# solution that creates a Grasshopper plugin for Rhino 3D. The plugin implements tools for translating and visualizing soil-related information, including soil water content, soil-root interaction, soil horizons, and tree/vegetation modeling.

## Solution Structure

This is a **multi-project solution** with the following architecture:

```
BeingAliveLanguageGH/
├── BeingAliveLanguage.slnx      # Visual Studio solution file (VS 2022+)
├── beingAliveLanguage.sln       # Legacy solution file (compatibility)
├── scripts/                     # Build and release scripts
│   ├── prebuild.ps1             # Pre-build script (copies C++ DLL)
│   ├── postbuild.ps1            # Post-build script (copies outputs)
│   └── prepare_yak_pkg.ps1      # Yak package preparation
├── BeingAliveLanguage/          # Main Grasshopper plugin project (C#)
│   ├── BAL*.cs                  # Component definitions
│   ├── BALCore/                 # Core algorithms and data structures
│   ├── Properties/              # Resources (icons, etc.)
│   └── Resources/               # Asset files
├── GeoSharPlusCPP/              # C++ computational library
│   ├── include/GeoSharPlusCPP/
│   │   ├── Core/                # Core headers (Macro.h, MathTypes.h, etc.)
│   │   ├── Serialization/       # Serialization headers
│   │   └── Extensions/bal/      # BAL-specific bridge headers
│   ├── src/
│   │   ├── Core/                # Core implementation
│   │   ├── Serialization/       # Serialization implementation
│   │   └── Extensions/bal/      # BAL-specific bridge implementation
│   └── CMakeLists.txt           # CMake build configuration
├── GeoSharPlusNET/              # .NET wrapper for C++/C# interop
│   ├── Core/                    # Core utilities
│   │   ├── Platform.cs          # OS detection, library loading
│   │   ├── MarshalHelper.cs     # Memory management utilities
│   │   └── Serializer.cs        # Platform-independent serialization
│   ├── Adapters/Rhino/          # Rhino-specific adapters
│   │   └── RhinoAdapter.cs      # Rhino type conversions
│   ├── Extensions/bal/          # BAL-specific extensions
│   │   ├── BalBridge.cs         # P/Invoke declarations
│   │   └── BalUtils.cs          # High-level wrapper functions
│   ├── NativeBridge.cs          # Legacy (deprecated)
│   ├── Wrapper.cs               # Legacy (deprecated)
│   └── BalFuncWrapper.cs        # Legacy (deprecated)
├── _release/                    # Release archives
├── bin/                         # Build output directory
└── cppPrebuild/                 # Pre-built C++ binaries for CI
```

## Project Roles

### BeingAliveLanguage (Main Project)

- **Purpose**: The main Grasshopper plugin containing all GH_Component implementations
- **Language**: C#
- **Framework**: .NET 8.0 targeting Grasshopper/Rhino 8
- **Key Files**:
  - `BALtree.cs` - Tree and forest modeling components
  - `BALsoil.cs` - Soil visualization components
  - `BALroot.cs` - Root system modeling
  - `BALclimate.cs` - Climate-related components
  - `BALCore/` - Core classes like `Tree3D`, `Utils`, `BranchNode3D`

### GeoSharPlusCPP (C++ Library)

- **Purpose**: High-performance computational geometry operations
- **Language**: C++17
- **Build System**: CMake with vcpkg for dependencies
- **Role**: Provides native implementations for performance-critical algorithms
- **Structure**: Uses `Extensions/bal/` for project-specific code

### GeoSharPlusNET (.NET Wrapper)

- **Purpose**: Bridge between C# and C++ code
- **Language**: C#
- **Framework**: .NET 7.0
- **Namespaces**:
  - `GSP.Core` - Platform detection, marshaling, serialization
  - `GSP.Adapters.Rhino` - Rhino type conversions
  - `GSP.Extensions.Bal` - BAL-specific bridge and utilities

## Development Guidelines

### Code Style

- Use C# conventions for the main project
- Components inherit from `GH_Component`
- Each component has:
  - Constructor with name, nickname, description, category, subcategory
  - `Icon` property returning a bitmap
  - `ComponentGuid` property with unique GUID
  - `RegisterInputParams` and `RegisterOutputParams` methods
  - `SolveInstance` method for main logic

### Component Categories

- `BAL` is the main category
- Subcategories include: `01::soil`, `02::root`, `03::plant`, `04::climate`, etc.

### Key Classes

- `Tree3D` - 3D tree representation with growth phases and branch structure
- `Tree3DWrapper` - Grasshopper wrapper for Tree3D objects
- `BranchNode3D` - Individual branch node in tree structure
- `Utils` - Utility functions including geometry helpers

### Tree Growth System

Trees grow in 4 stages controlled by phase parameter:

1. **Stage 1 (phase 1-4)**: Young tree, trunk growth with initial side branches
2. **Stage 2 (phase 5-8)**: Mature tree, branching and crown development
3. **Stage 3 (phase 9-11)**: Aging tree, continued growth
4. **Stage 4 (phase 12)**: Dying tree, branch removal

### Forest Interaction System

- `BALtreeInteraction` component handles tree-to-tree interactions
- Trees scale their crowns based on proximity to neighbors
- Uses KD-tree for nearest neighbor queries
- Scaling affects branches facing toward neighboring trees

## Building

### Prerequisites

- Visual Studio 2022+ with .NET 8.0 support
- Rhino 8+ and Grasshopper SDK
- CMake (for C++ project)
- vcpkg (for C++ dependencies)

### Build Order

1. Build `GeoSharPlusCPP` (CMake) - or use pre-built binaries in `cppPrebuild/`
2. Build `GeoSharPlusNET`
3. Build `BeingAliveLanguage`

### Build Scripts

- `scripts/prebuild.ps1` - Copies C++ DLL to output directory
- `scripts/postbuild.ps1` - Copies build outputs to `bin/` directory
- `scripts/prepare_yak_pkg.ps1` - Creates Yak package for release

## Testing

- Test files and example Grasshopper definitions should be used to verify component behavior
- Pay attention to edge cases like overlapping trees, single tree scenarios, etc.

## Release

- Release scripts are in `scripts/`
- Archives are stored in `_release/archive/`
- Packages are created as `.yak` files for Rhino Package Manager
- Version naming follows: `beingalivelanguage-{version}-rh{rhino_version}-any.yak`

## Adding New C++ Functions

1. Add C++ function in `GeoSharPlusCPP/src/Extensions/bal/BridgeAPI.cpp`
2. Add header in `GeoSharPlusCPP/include/GeoSharPlusCPP/Extensions/bal/BridgeAPI.h`
3. Add P/Invoke in `GeoSharPlusNET/Extensions/bal/BalBridge.cs`
4. Add high-level wrapper in `GeoSharPlusNET/Extensions/bal/BalUtils.cs`
5. Use from BeingAliveLanguage via `GSP.Extensions.Bal.BalUtils`
